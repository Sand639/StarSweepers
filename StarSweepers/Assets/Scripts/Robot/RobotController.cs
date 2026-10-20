using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットの上半身と下半身を、切り離したり合体させたりする。
///
/// 遊び方：
///   W / A / S / D … 今操作している体を動かす
///   マウス        … カメラを回す
///   Q             … 切り離す
///   Tab           … 操作する方を切り替える（上半身 ⇔ 下半身）
///   E             … 近づいていれば合体する
///   Space         … ジャンプする（合体中と、分離中の下半身だけ）
///   F             … 目の前の物を持つ／離す（合体中と、分離中の上半身だけ）
///                    目の前がロープなら、つかまる／手を離す
///   V             … 一人称と三人称を切り替える
///
/// **合体しているときは1体、分けたときは2体**という作りにしている。
/// 2つを物理的に繋いで動かすとガタつくため、繋ぐのをやめて
/// **「合体した姿の体」と「上半身」「下半身」を入れ替える**方式にした。
///
/// ## 流れ（毎フレーム）
///
/// 1. Q / Tab / E を見て、**切り離す・操作を移す・合体する**（<see cref="ReadStateInput"/>）
/// 2. いまの状態に合わせて、**出ている体を動かす**（<see cref="MoveBodies"/>）。
///    操作していない体も、重力だけは効かせる
/// 3. **カメラが追いかける体**を決める（<see cref="UpdateCameraTarget"/>）
///
/// 状態が変わると、`Split`・`Combined`・`ControlSwitched` のイベントで周りの部品に知らせる。
/// 物を持つ（<see cref="RobotGrabber"/>）・ロープ（<see cref="RobotRopeClimber"/>）・視点（<see cref="RobotViewSwitcher"/>）は、
/// これを受けて「持っている物・つかまっている体・隠す体」を新しい体へ移す。
///
/// 使っているシーン：ロボット分離の検証シーン（`RobotSplitTest`）や、ロボットで走るレースなど、
/// `RobotRig` プレハブを置いたシーン。
/// </summary>
public class RobotController : MonoBehaviour
{
    /// <summary>いまの状態。</summary>
    public enum RobotState
    {
        /// <summary>合体中。1体として動く</summary>
        Combined,

        /// <summary>分離中。上半身を操作している</summary>
        SplitUpper,

        /// <summary>分離中。下半身を操作している</summary>
        SplitLower,
    }

    [Header("つなぐもの")]
    [Tooltip("合体した姿の体。合体中だけ出てくる")]
    [SerializeField] private RobotBody combinedBody;

    [Tooltip("上半身。分離中だけ出てくる")]
    [SerializeField] private RobotBody upperBody;

    [Tooltip("下半身。分離中だけ出てくる")]
    [SerializeField] private RobotBody lowerBody;

    [Tooltip("カメラ。操作している体を追いかける")]
    [SerializeField] private RobotCameraLook cameraLook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("切り離したときの飛び方")]
    [Tooltip("ONにすると、上半身が出てくる高さを**体の大きさから自動で計算する**。" +
             "合体していたときに上半身があった位置から、そのまま出てくる")]
    [SerializeField] private bool autoSplitHeight = true;

    [Tooltip("自動計算を使わないときの高さ（合体した体の足元から）")]
    [Range(0f, 3f)]
    [SerializeField] private float upperSplitHeight = 1.0f;

    [Tooltip("上半身が離れる距離（メートル）。何も押していないときは使わない")]
    [Range(0f, 3f)]
    [SerializeField] private float upperSplitDistance = 0.9f;

    [Tooltip("上半身が飛んでいく勢い（1秒あたりのメートル）。0にすると置くだけ")]
    [Range(0f, 15f)]
    [SerializeField] private float upperLaunchSpeed = 5f;

    [Tooltip("キーを押しているときに、上へ跳ねる勢い。0にすると水平に飛ぶ")]
    [Range(0f, 10f)]
    [SerializeField] private float upperLaunchUp = 2.5f;

    [Tooltip("**キーを何も押していないときに、真上へ飛ぶ勢い。**横には動かない。" +
             "4なら約0.4m、6なら約0.9m上がる")]
    [Range(0f, 15f)]
    [SerializeField] private float upperLaunchUpOnly = 4f;

    [Tooltip("合体した体の足元から見た、下半身の出てくる位置")]
    [SerializeField] private Vector3 lowerSplitOffset = Vector3.zero;

    [Header("合体")]
    [Tooltip("**真上から見たときの距離**が、これ以内なら合体できる（メートル）。高さは含まない")]
    [Range(0.5f, 10f)]
    [SerializeField] private float combineDistance = 2.5f;

    [Tooltip("**高さの差**が、これ以内なら合体できる（メートル）。" +
             "大きくすると、高い足場の上と下でも合体できるようになる")]
    [Range(0.1f, 10f)]
    [SerializeField] private float combineHeightGap = 1.5f;

    [Header("カメラ")]
    [Tooltip("ONにすると、下半身を操作中もカメラは上半身のまま。OFFなら操作している方を追いかける")]
    [SerializeField] private bool cameraAlwaysOnUpper = false;

    [Header("ジャンプ")]
    [Tooltip("ONにすると、分離中の上半身もジャンプできるようになる。OFFなら足（下半身）だけ")]
    [SerializeField] private bool upperCanJump = false;

    [Header("キーの割り当て")]
    [Tooltip("切り離すキー")]
    [SerializeField] private Key splitKey = Key.Q;

    [Tooltip("合体させるキー")]
    [SerializeField] private Key combineKey = Key.E;

    [Tooltip("操作する方を切り替えるキー")]
    [SerializeField] private Key switchKey = Key.Tab;

    // ------------------------------------------------------------
    // 拡張ポイント：エフェクトや音を足したいときはここを購読する
    // ------------------------------------------------------------

    /// <summary>切り離した瞬間に呼ばれる。</summary>
    public event Action Split;

    /// <summary>合体した瞬間に呼ばれる。</summary>
    public event Action Combined;

    /// <summary>操作する方が切り替わったときに呼ばれる。</summary>
    public event Action<RobotState> ControlSwitched;

    /// <summary>いまの状態。UI表示などから見たいときのために公開している。</summary>
    public RobotState State { get; private set; } = RobotState.Combined;

    /// <summary>
    /// **true の間、操作を受け付けない。**
    ///
    /// レースのカウントダウン中やゴール後に、外から止めるためのもの
    /// （`Assets/Scripts/Race/RobotRacerLink.cs`）。
    /// **重力は効いたまま**なので、その場に立って待つ形になる。
    /// </summary>
    public bool ControlSuspended { get; set; }

    /// <summary>合体した姿の体。**やり直しでまとめて動かしたいとき**に使う。</summary>
    public RobotBody CombinedBodyPart => combinedBody;

    /// <summary>上半身。</summary>
    public RobotBody UpperBodyPart => upperBody;

    /// <summary>下半身。</summary>
    public RobotBody LowerBodyPart => lowerBody;

    /// <summary>いま操作している体。</summary>
    public RobotBody ActiveBody { get; private set; }

    /// <summary>
    /// いま合体できるか。**2つの条件を別々に見ている。**
    ///
    /// 1. **真上から見たときの距離**（XZ平面）が `combineDistance` 以内
    /// 2. **高さの差**（Y）が `combineHeightGap` 以内
    ///
    /// まっすぐな距離ひとつで見ると、
    /// **「真横で遠い」と「真上で近い」が同じ扱いになってしまう。**
    /// 別々にすると、**「足元は近いが、高い足場の上にいる」を弾ける。**
    ///
    /// UIの表示にも使える。
    /// </summary>
    public bool CanCombine
    {
        get
        {
            if (State == RobotState.Combined || upperBody == null || lowerBody == null)
            {
                return false;
            }

            return FlatDistance <= combineDistance && HeightGap <= combineHeightGap;
        }
    }

    /// <summary>上半身と下半身の、**真上から見たときの距離**（高さを含まない）。</summary>
    public float FlatDistance
    {
        get
        {
            if (upperBody == null || lowerBody == null)
            {
                return float.MaxValue;
            }

            Vector3 gap = upperBody.transform.position - lowerBody.transform.position;
            gap.y = 0f;

            return gap.magnitude;
        }
    }

    /// <summary>上半身と下半身の、**高さの差**（上下どちらでも正の数になる）。</summary>
    public float HeightGap
    {
        get
        {
            if (upperBody == null || lowerBody == null)
            {
                return float.MaxValue;
            }

            return Mathf.Abs(upperBody.transform.position.y - lowerBody.transform.position.y);
        }
    }

    /// <summary>
    /// いまジャンプが許されている状態か。**分離中の上半身は跳べない**。
    /// （地面に付いているかどうかは見ていない。それは体の側で判断する）
    /// </summary>
    public bool CanJump => State != RobotState.SplitUpper || upperCanJump;

    private InputActionMap playerMap;
    private InputAction moveAction;
    private InputAction jumpAction;

    // ------------------------------------------------------------
    // 外から使うための窓口
    //
    // ロープなど「体を別の動かし方で動かす部品」が、
    // 入力やカメラの向きを自分で読み直さずに済むように公開している
    // ------------------------------------------------------------

    /// <summary>
    /// **この体だけ、普段の移動から外す。**
    /// ここに入っている体は <see cref="RobotBody.Tick"/> で動かされなくなる（重力も効かない）。
    ///
    /// ロープにつかまっている間のように、
    /// **別の部品が体を動かしたいとき**に入れる（<see cref="RobotRopeClimber"/>）。
    ///
    /// **操作していない体でも指定できる。**
    /// そうしないと、**上半身をロープに残したまま下半身を操作しに行く**と、
    /// 残した上半身が重力で落ちてしまう。
    /// </summary>
    public RobotBody SuspendedBody { get; set; }

    /// <summary>スティック・WASDの入力そのもの。y が前後、x が左右。</summary>
    public Vector2 MoveInput =>
        moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    /// <summary>
    /// **進みたい方向**（カメラの向きを基準にした、実際の向き）。
    /// 横向きのロープのように、**「押した方向がどちらを向いているか」を知りたいとき**に使う。
    /// </summary>
    public Vector3 MoveWorldDirection => GetMoveDirection();

    /// <summary>このフレームにジャンプが押されたか。</summary>
    public bool JumpPressedThisFrame => jumpAction != null && jumpAction.WasPressedThisFrame();

    /// <summary>カメラが向いている水平方向。ロープから飛び降りる向きなどに使う。</summary>
    public Vector3 LookForward =>
        cameraLook != null ? cameraLook.FlatForward : transform.forward;

    private void Awake()
    {
        if (combinedBody == null || upperBody == null || lowerBody == null)
        {
            Debug.LogError($"{name}: 体（合体・上半身・下半身）が揃っていません。", this);
            enabled = false;
            return;
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、この体専用の複製を使う。**
        // 元のアセットをそのまま使うと、1つのシーンに複数の体が出たとき
        // （オンラインや画面分割）に、**他の体の操作を止めると自分の入力まで一緒に止まる**
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        jumpAction = playerMap.FindAction("Jump", true);

        // 最初は合体した姿から始める（上半身・下半身は隠しておく）
        ApplyState(RobotState.Combined);
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
    }

    private void Update()
    {
        ReadStateInput();
        MoveBodies();
        UpdateCameraTarget();
    }

    // ------------------------------------------------------------
    // 入力
    // ------------------------------------------------------------

    /// <summary>
    /// 切り離す（Q）・操作を移す（Tab）・合体する（E）のキーを見る。
    /// それぞれ、**その状態のときだけ**効く（合体中に Tab や E を押しても何も起きない）。
    /// </summary>
    private void ReadStateInput()
    {
        // レース中など、外から止められている間は何も受け付けない
        if (ControlSuspended)
        {
            return;
        }

        if (WasKeyPressed(splitKey) && State == RobotState.Combined)
        {
            DoSplit();
        }

        if (WasKeyPressed(switchKey) && State != RobotState.Combined)
        {
            SwitchControl();
        }

        if (WasKeyPressed(combineKey) && State != RobotState.Combined)
        {
            TryCombine();
        }
    }

    // ------------------------------------------------------------
    // 切り離す
    // ------------------------------------------------------------

    /// <summary>合体した体を、上半身と下半身に分ける。</summary>
    public void DoSplit()
    {
        if (State != RobotState.Combined)
        {
            return;
        }

        Vector3 basePosition = combinedBody.transform.position;
        Quaternion baseRotation = combinedBody.transform.rotation;

        // **押しているキーの方向へ飛ばす。**
        // W なら前、S なら後ろ。何も押していなければ、横には動かず真上へ飛ぶ
        Vector3 launchDirection = GetSplitDirection();

        // GetSplitDirection は「長さ1の向き」か「ゼロ」しか返さないので、ゼロでないかを見ているだけ
        bool hasInput = launchDirection.sqrMagnitude > 0.0001f;

        // 合体した体を隠してから、2つを置く
        combinedBody.gameObject.SetActive(false);

        PlaceBody(lowerBody, basePosition + baseRotation * lowerSplitOffset, baseRotation);

        Vector3 upperPosition = basePosition
            + launchDirection * upperSplitDistance
            + Vector3.up * GetSplitHeight();

        PlaceBody(upperBody, upperPosition, baseRotation);

        // 置いたあとに勢いを与える（置く処理が勢いを消すため、順番が大事）
        float upSpeed = hasInput ? upperLaunchUp : upperLaunchUpOnly;
        upperBody.Launch(launchDirection * upperLaunchSpeed + Vector3.up * upSpeed);

        ApplyState(RobotState.SplitUpper);

        // 拡張ポイント：ここで切り離しのエフェクトや音を鳴らせる
        // **Split → ControlSwitched の順番を変えないこと。**
        // ロープ側（RobotRopeClimber）は、Split で上半身へ引き継いでから ControlSwitched を受け取る前提で作られている。
        // 逆にすると、ロープにつかまったまま切り離したときに手を離してしまう
        Split?.Invoke();
        ControlSwitched?.Invoke(State);
    }

    /// <summary>
    /// 切り離したときに、上半身が飛んでいく**横方向**を決める。
    ///
    /// **押しているキーの方向へ飛ぶ。**
    /// W なら前、S なら後ろ、A / D なら横。斜めもそのまま反映される。
    ///
    /// **何も押していないときは、横には動かない**（ゼロを返す）。
    /// その場合は真上へ飛ぶ。
    /// </summary>
    private Vector3 GetSplitDirection()
    {
        Vector3 flatDirection = GetMoveDirection();
        flatDirection.y = 0f;

        // スティックをほんの少し（長さ0.1未満）倒しただけなら、押していない扱いにする
        return flatDirection.sqrMagnitude > 0.01f ? flatDirection.normalized : Vector3.zero;
    }

    /// <summary>
    /// 切り離したときに、上半身が出てくる高さを決める。
    ///
    /// **決め打ちの数字を書かず、体の大きさから計算する。**
    /// 合体した体の高さから上半身の高さを引くと、
    /// **合体していたときに上半身の足元があった高さ**になる。
    ///
    /// 例：合体2.0m − 上半身0.9m ＝ 1.1m
    ///
    /// こうしておくと、**体の大きさを変えても位置がずれない。**
    /// 自動計算を使いたくない場合は `Auto Split Height` を OFF にする。
    /// </summary>
    private float GetSplitHeight()
    {
        if (!autoSplitHeight)
        {
            return upperSplitHeight;
        }

        float height = combinedBody.Height - upperBody.Height;

        // 大きさが取れなかった場合の保険
        return height > 0f ? height : upperSplitHeight;
    }

    // ------------------------------------------------------------
    // 操作を切り替える
    // ------------------------------------------------------------

    /// <summary>操作する方を入れ替える。</summary>
    public void SwitchControl()
    {
        if (State == RobotState.Combined)
        {
            return;
        }

        ApplyState(State == RobotState.SplitUpper ? RobotState.SplitLower : RobotState.SplitUpper);

        // 拡張ポイント：ここで「いまどちらを操作中か」のUI表示を切り替えられる
        ControlSwitched?.Invoke(State);
    }

    // ------------------------------------------------------------
    // 合体する
    // ------------------------------------------------------------

    /// <summary>近づいていれば合体する。合体できたら true。</summary>
    public bool TryCombine()
    {
        if (!CanCombine)
        {
            return false;
        }

        // 足の位置に合体した体を作る。向きは今操作している方に合わせる
        Vector3 basePosition = lowerBody.transform.position - lowerSplitOffset;
        Quaternion baseRotation = ActiveBody != null
            ? ActiveBody.transform.rotation
            : lowerBody.transform.rotation;

        upperBody.gameObject.SetActive(false);
        lowerBody.gameObject.SetActive(false);

        PlaceBody(combinedBody, basePosition, baseRotation);

        ApplyState(RobotState.Combined);

        // 拡張ポイント：ここで合体のエフェクトや音を鳴らせる
        Combined?.Invoke();
        ControlSwitched?.Invoke(State);

        return true;
    }

    // ------------------------------------------------------------
    // 状態を反映する
    // ------------------------------------------------------------

    /// <summary>状態に合わせて、出す体と操作する体を決める。</summary>
    private void ApplyState(RobotState next)
    {
        State = next;

        bool isCombined = next == RobotState.Combined;

        combinedBody.gameObject.SetActive(isCombined);
        upperBody.gameObject.SetActive(!isCombined);
        lowerBody.gameObject.SetActive(!isCombined);

        ActiveBody = next switch
        {
            RobotState.Combined => combinedBody,
            RobotState.SplitUpper => upperBody,
            _ => lowerBody,
        };
    }

    /// <summary>体を出しつつ、指定した場所へ移す。</summary>
    private static void PlaceBody(RobotBody body, Vector3 position, Quaternion rotation)
    {
        body.gameObject.SetActive(true);
        body.Teleport(position, rotation);
    }

    // ------------------------------------------------------------
    // 動かす
    // ------------------------------------------------------------

    /// <summary>
    /// **体を「見ている方向」に向けるか。**
    ///
    /// ONにすると、**一人称でも三人称でも**カメラの向きに体が揃う（フォートナイト型）。
    /// 向きが固定されるぶん、**横歩き・後ろ歩きが表現できる**ようになる
    /// （速さの違いは <see cref="RobotBody"/> 側で付けている）。
    ///
    /// OFFにすると、進む方向へ向き直る昔の作りに戻る。
    ///
    /// 切り替えるのは <see cref="RobotViewSwitcher"/>。
    /// </summary>
    public bool FaceLookDirection { get; set; }

    /// <summary>
    /// 出ている体を、1フレーム分動かす。
    /// 操作している体には入力を渡し、**操作していない体には「止まっている」を渡して重力だけ効かせる。**
    /// </summary>
    private void MoveBodies()
    {
        // 止められている間は、動かない（重力だけは下で効く）
        Vector3 direction = ControlSuspended ? Vector3.zero : GetMoveDirection();

        // ジャンプできるのは「合体中」と「分離中の下半身」だけ
        bool jump = !ControlSuspended && jumpAction.WasPressedThisFrame() && CanJump;

        // 見ている方向を向かせる（一人称・三人称とも）
        Vector3? facing = FaceLookDirection && cameraLook != null
            ? cameraLook.FlatForward
            : (Vector3?)null;

        // ロープにつかまっている体は、ここでは動かさない。
        // **動かすのはロープ側**（二重に動かすと引っ張り合いになる）

        if (State == RobotState.Combined)
        {
            TickBody(combinedBody, direction, jump, facing);
            return;
        }

        // 操作していない方も、重力だけは効かせる（勝手には動かない）
        if (State == RobotState.SplitUpper)
        {
            TickBody(upperBody, direction, jump, facing);
            TickBody(lowerBody, Vector3.zero, false, null);
        }
        else
        {
            TickBody(lowerBody, direction, jump, facing);
            TickBody(upperBody, Vector3.zero, false, null);
        }
    }

    /// <summary>
    /// 体を1つ動かす。
    /// **ロープにつかまっている体だけは、ここでは触らない。**
    /// </summary>
    private void TickBody(RobotBody body, Vector3 direction, bool jump, Vector3? facing)
    {
        if (body == null || body == SuspendedBody)
        {
            return;
        }

        body.Tick(direction, jump, facing);
    }

    /// <summary>カメラの向きを基準に、進みたい方向を求める。</summary>
    private Vector3 GetMoveDirection()
    {
        if (cameraLook == null)
        {
            return Vector3.zero;
        }

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 direction = cameraLook.FlatForward * input.y + cameraLook.FlatRight * input.x;

        // 長さが1を超えたときだけ1に縮める（W と D を同時に押した斜めで、速くなりすぎないように）。
        // スティックを半分だけ倒したときの「ゆっくり歩き」は、縮めずにそのまま残す
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    // ------------------------------------------------------------
    // カメラ
    // ------------------------------------------------------------

    /// <summary>カメラが追いかける体を、いまの状態に合わせて決める。</summary>
    private void UpdateCameraTarget()
    {
        if (cameraLook == null)
        {
            return;
        }

        // 設定によって、常に上半身を見るか、操作している方を見るかが変わる
        RobotBody target = cameraAlwaysOnUpper && State != RobotState.Combined
            ? upperBody
            : ActiveBody;

        cameraLook.SetTarget(target != null ? target.transform : null);
    }

    private static bool WasKeyPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }
}
