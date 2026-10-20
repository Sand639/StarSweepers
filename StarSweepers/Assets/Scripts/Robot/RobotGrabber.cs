using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットが**目の前の物を持ったり離したりする**。
///
/// 遊び方：
///   目の前の持てる物に近づくと、その物の**色が変わる**（狙えている合図）
///   F を押すと**持つ**
///   もう一度 F を押すと**離す**
///
/// **持てるのは「手のある体」だけ。**
/// 合体中と上半身は持てるが、**下半身だけのときは持てない。**
/// これは状態を見て分岐しているのではなく、
/// **体に「手の位置」が入っているかどうか**で決まる（<see cref="RobotBody.HoldPoint"/>）。
///
/// ## 探し方は「距離と向き」
///
/// 細い物や小さい物でも拾えるように、**線を飛ばして当てる**のではなく、
/// **近くにある持てる物の中から、正面に近くて一番近いもの**を選んでいる。
/// （通信の実装でも、当たり判定より距離のほうが確実だという教訓があった）
///
/// ## 流れ
///
/// 1. 毎フレーム、何も持っていなければ**狙える物を探して色を変える**（<see cref="UpdateAim"/>）
/// 2. F が押されたら、**持つ／離す**（<see cref="ToggleGrab"/>）。
///    ただし、ロープや鍵が F を受け取るときは譲る
/// 3. 持っている間は、物を**手の子にして**一緒に動かす。物理は止めておき、離すときに元へ戻す
/// 4. 合体・分離で体が入れ替わったら、**新しい体の手へ持ち替えるか、その場に落とす**（<see cref="HandleControlSwitched"/>）
///
/// 使っているシーン：ロボット分離の検証シーン（`RobotSplitTest`）など、`RobotRig` を置いたシーン。
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotGrabber : MonoBehaviour
{
    [Header("届く範囲")]
    [Tooltip("この距離まで近づくと持てる（メートル）")]
    [Range(0.5f, 5f)]
    [SerializeField] private float reach = 2f;

    [Tooltip("**見ている方向からどれだけずれていても持てるか**（度）。" +
             "頭を中心に、カメラの向きと、その水平の向きの2本で判断する")]
    [Range(10f, 180f)]
    [SerializeField] private float maxAngle = 70f;

    [Tooltip("一度に調べる物の数の上限。増やすと重くなる")]
    [SerializeField] private int maxCandidates = 16;

    [Tooltip("ONにすると、**壁の向こうの物は持てなくなる**（間に何かあるかを調べる）。" +
             "ガラスのように透ける物は、間にあっても持てる。OFFにすると壁越しに持てる（前の動き）")]
    [SerializeField] private bool requireClearPath = true;

    [Header("キーの割り当て")]
    [Tooltip("持つ／離すを切り替えるキー")]
    [SerializeField] private Key grabKey = Key.F;

    [Header("見た目")]
    [Tooltip("画面中央のレティクル。狙えているときに色が変わる")]
    [SerializeField] private Reticle reticle;

    [Tooltip("狙えている物を、この色に近づける")]
    [SerializeField] private Color highlightColor = new Color(0.4f, 0.9f, 1f);

    [Tooltip("どれくらい色を混ぜるか")]
    [Range(0f, 1f)]
    [SerializeField] private float highlightStrength = 0.5f;

    [Header("離すとき")]
    [Tooltip("離すときに前へ押し出す強さ。0なら、その場に落とすだけ")]
    [Range(0f, 10f)]
    [SerializeField] private float releasePush = 0f;

    private RobotController controller;

    /// <summary>ロープ側。同じ F キーをどちらが受け取るか決めるために見ている（無くてもよい）。</summary>
    private RobotRopeClimber ropeClimber;

    /// <summary>鍵を使う側。こちらも同じ F キーを使う（無くてもよい）。</summary>
    private RobotKeyUser keyUser;

    /// <summary>
    /// 手の周りの当たり判定を一度に受け取れる数。
    /// 床や壁など「持てない物」もここに入るので、`maxCandidates`（調べる「持てる物」の数。初期値16）より多めにしてある。
    /// `maxCandidates` を32より大きくしても、一度に調べられるのはここまで。
    /// </summary>
    private const int OverlapBufferSize = 32;

    /// <summary>
    /// 手の周りにある当たり判定を受け取る入れ物。
    /// 毎フレーム新しく作ると無駄なゴミが出るので、1つを使い回している。
    /// </summary>
    private readonly Collider[] overlapBuffer = new Collider[OverlapBufferSize];

    /// <summary>狙えている物の色を変える道具。ロープ・鍵を使う側と同じ仕組みを使っている。</summary>
    private readonly InteractHighlight highlight = new InteractHighlight();

    /// <summary>いま狙えている物。無ければ null。</summary>
    public Grabbable Aimed { get; private set; }

    /// <summary>いま持っている物。無ければ null。</summary>
    public Grabbable Held { get; private set; }

    /// <summary>
    /// いま物を持てる状態か（手のある体を操作しているか）。
    /// **ロープにつかまっている間は持てない。**
    /// </summary>
    public bool CanGrabNow =>
        controller != null && controller.ActiveBody != null && controller.ActiveBody.CanHold
        && (ropeClimber == null || !ropeClimber.IsClimbing);

    // 持つ前の状態。離すときに元へ戻すために控えておく
    private Transform heldOriginalParent;
    private bool heldOriginalKinematic;
    private bool heldOriginalGravity;
    private Rigidbody heldBody;
    private Collider heldCollider;

    /// <summary>持った物とぶつからないようにしている体の当たり判定。戻すときに使う（<see cref="IgnoreCollisionWithBody"/>）。</summary>
    private Collider ignoredBodyCollider;

    /// <summary>いまどの体が持っているか。切り替えのときに、持ち替えるか判断するのに使う。</summary>
    private RobotBody heldByBody;

    /// <summary>いま操作している体が、物を持っているか。</summary>
    private bool ActiveBodyIsHolding =>
        Held != null && heldByBody != null && heldByBody == controller.ActiveBody;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        ropeClimber = GetComponent<RobotRopeClimber>();
        keyUser = GetComponent<RobotKeyUser>();
    }

    private void OnEnable()
    {
        if (controller != null)
        {
            controller.ControlSwitched += HandleControlSwitched;
        }
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.ControlSwitched -= HandleControlSwitched;
        }
    }

    private void Update()
    {
        // いま操作している体が何も持っていなければ、狙える物を探す
        if (ActiveBodyIsHolding)
        {
            // 持っている間は、狙いの色を消しておく
            if (Aimed != null)
            {
                ClearHighlight();
                Aimed = null;
                UpdateReticle();
            }
        }
        else
        {
            UpdateAim();
        }

        // ロープや鍵が受け取るときは、F キーをそちらに譲る
        if (WasGrabKeyPressed() && !OtherHandlesInteract)
        {
            ToggleGrab();
        }
    }

    /// <summary>
    /// このフレームの F キーを、**ロープか鍵のほうが受け取るか。**
    ///
    /// - ロープ … つかまっている、またはロープを狙っている（物を持っていないとき）
    /// - 鍵 … **鍵を持っていて、開けられる扉を狙っている**
    /// </summary>
    private bool OtherHandlesInteract =>
        (ropeClimber != null && ropeClimber.WantsInteract)
        || (keyUser != null && keyUser.WantsInteract);

    // ------------------------------------------------------------
    // 狙う
    // ------------------------------------------------------------

    /// <summary>正面の近くにある「持てる物」を探して、色を変える。</summary>
    private void UpdateAim()
    {
        Grabbable found = CanGrabNow ? FindNearestGrabbable() : null;

        if (found == Aimed)
        {
            return;
        }

        ClearHighlight();
        Aimed = found;

        if (Aimed != null)
        {
            ApplyHighlight(Aimed.gameObject);
        }

        // レティクルの色も変えて、狙えていることを画面中央でも伝える
        UpdateReticle();
    }

    /// <summary>レティクルの見た目を、いまの状況に合わせる。</summary>
    private void UpdateReticle()
    {
        if (reticle == null)
        {
            return;
        }

        reticle.SetHighlight(Aimed != null ? highlightColor : (Color?)null);
    }

    /// <summary>正面に近くて一番近い「持てる物」を返す。</summary>
    private Grabbable FindNearestGrabbable()
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        // 手から reach メートル以内の当たり判定を集める。
        // `~0` は「すべてのレイヤー」、トリガー（すり抜け用の当たり判定）は数えない
        int count = Physics.OverlapSphereNonAlloc(
            hand.position, reach, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);

        Grabbable nearest = null;
        float nearestDistance = float.MaxValue;

        // 調べた「持てる物」の数。床や壁は数えない。maxCandidates 個まで調べたら打ち切る
        int checkedCount = 0;

        for (int i = 0; i < count && checkedCount < maxCandidates; i++)
        {
            Collider hit = overlapBuffer[i];

            if (hit == null)
            {
                continue;
            }

            Grabbable grabbable = hit.GetComponentInParent<Grabbable>();

            // 持てない設定の物と、すでに誰かが持っている物は対象外
            if (grabbable == null || grabbable.IsHeld || !grabbable.CanBeCarried)
            {
                continue;
            }

            // 自分の体は対象外
            if (grabbable.transform.IsChildOf(transform))
            {
                continue;
            }

            checkedCount++;

            if (!IsInSight(body, grabbable))
            {
                continue;
            }

            // 一番近いかどうかは、**手から物の中心まで**の距離で比べる
            float distance = Vector3.Distance(grabbable.transform.position, hand.position);

            if (distance < nearestDistance)
            {
                nearest = grabbable;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// その物が、**いまの体から狙えていて、間に壁が無いか。**
    /// 距離（届くか）はここでは見ない。呼ぶ側で測る。
    /// </summary>
    private bool IsInSight(RobotBody body, Grabbable grabbable)
    {
        // **頭を中心に、見ている方向から大きく外れているものは無視する**
        // （判断のしかたは AimCheck にまとめてある）
        if (!AimCheck.IsAimed(
            body.HeadPosition, grabbable.transform.position, maxAngle, body.transform.forward))
        {
            return false;
        }

        // **壁の向こうの物は持てない**（ガラスなど透ける物は通す）
        if (requireClearPath && !AimCheck.HasClearPath(
            body.HeadPosition, grabbable.transform.position, transform.root, grabbable.transform))
        {
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------
    // 持つ・離す
    // ------------------------------------------------------------

    /// <summary>
    /// 持っていなければ持ち、持っていれば離す。
    ///
    /// **離せるのは、自分で持っている体だけ。**
    /// 上半身が持っている物を、下半身を操作しながら離すことはできない。
    /// </summary>
    public void ToggleGrab()
    {
        if (ActiveBodyIsHolding)
        {
            Release();
            return;
        }

        if (Aimed != null && CanGrabNow)
        {
            Grab(Aimed);
        }
    }

    /// <summary>物を持つ。</summary>
    private void Grab(Grabbable target)
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        heldBody = target.GetComponent<Rigidbody>();
        heldCollider = target.GetComponentInChildren<Collider>();

        if (heldBody == null)
        {
            Debug.LogWarning($"{target.name}: Rigidbody が無いので持てません。", target);
            return;
        }

        // 離すときに元へ戻せるよう、いまの状態を控えておく
        heldOriginalParent = target.transform.parent;
        heldOriginalKinematic = heldBody.isKinematic;
        heldOriginalGravity = heldBody.useGravity;

        // 持っている間は物理を止める。動かすのは手の位置なので、
        // 物理に任せると引っ張り合いになる
        heldBody.isKinematic = true;
        heldBody.useGravity = false;

        // 持った物が自分の体を押して、動けなくなるのを防ぐ
        IgnoreCollisionWithBody(body, true);

        ClearHighlight();

        AttachToHand(target, hand);

        target.SetHeld(true);
        Held = target;
        heldByBody = body;
        Aimed = null;

        Debug.Log($"[ROBOT] {body.name} が {target.name} を持ちました");
    }

    /// <summary>
    /// 物を**手の子にして**、手の位置に置く。
    /// 手の子にしておくと、体が歩いても向きを変えても、物が手と一緒に付いてくる。
    ///
    /// 持ったとき（<see cref="Grab"/>）と、体が入れ替わって持ち替えるとき
    /// （<see cref="HandleControlSwitched"/>）の両方で使う。
    /// </summary>
    private static void AttachToHand(Grabbable target, Transform hand)
    {
        // false … いまの見た目の位置を保たず、手から見た位置として置き直す
        target.transform.SetParent(hand, false);
        target.transform.localPosition = target.HoldOffset;

        if (target.StraightenWhenHeld)
        {
            target.transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>持っている物を離す。</summary>
    public void Release()
    {
        if (Held == null)
        {
            return;
        }

        Grabbable target = Held;

        // true … いまの見た目の位置・向きのまま、元の親へ戻す（手を離したその場に残る）
        target.transform.SetParent(heldOriginalParent, true);

        if (heldBody != null)
        {
            heldBody.isKinematic = heldOriginalKinematic;
            heldBody.useGravity = heldOriginalGravity;

            if (!heldBody.isKinematic && releasePush > 0f)
            {
                Vector3 forward = controller.ActiveBody != null
                    ? controller.ActiveBody.transform.forward
                    : transform.forward;

                heldBody.AddForce(forward * releasePush, ForceMode.Impulse);
            }
        }

        IgnoreCollisionWithBody(null, false);

        target.SetHeld(false);
        Held = null;
        heldByBody = null;
        heldBody = null;
        heldCollider = null;

        Debug.Log($"[ROBOT] {target.name} を離しました");
    }

    /// <summary>
    /// **持っている物を使い切って、消す。** 鍵を扉に使ったときなどに呼ぶ。
    ///
    /// いきなり消さずに**いったん離してから消している。**
    /// 持ったまま消すと、当たり判定を無視する設定などが後始末されずに残ってしまう。
    /// </summary>
    public void ConsumeHeld()
    {
        if (Held == null)
        {
            return;
        }

        GameObject target = Held.gameObject;

        Release();
        Destroy(target);
    }

    /// <summary>
    /// 持った物と、体の当たり判定をぶつけないようにする（または戻す）。
    ///
    /// | 呼び方 | すること |
    /// | `ignore = true` | `body` の当たり判定を覚えて、持った物とぶつからないようにする |
    /// | `ignore = false` | **前に覚えた体**とのぶつからない設定を元に戻し、覚えた体を忘れる（`body` は使わない） |
    ///
    /// 戻すときに `body` を見ないのは、体が入れ替わったあとでも、
    /// **設定したときの体**を確実に元へ戻すため。
    /// </summary>
    private void IgnoreCollisionWithBody(RobotBody body, bool ignore)
    {
        if (heldCollider == null)
        {
            return;
        }

        if (ignore)
        {
            ignoredBodyCollider = body != null ? body.BodyCollider : null;
        }

        if (ignoredBodyCollider != null)
        {
            Physics.IgnoreCollision(heldCollider, ignoredBodyCollider, ignore);
        }

        if (!ignore)
        {
            ignoredBodyCollider = null;
        }
    }

    // ------------------------------------------------------------
    // 体が切り替わったとき
    // ------------------------------------------------------------

    /// <summary>
    /// 分離・合体・操作の切り替えが起きたときに呼ばれる。
    ///
    /// 判断は2通り。
    ///
    /// - **持っている体がまだ出ているなら、そのまま持たせておく。**
    ///   （上半身に持たせたまま、下半身を操作しに行ける）
    /// - **持っている体が消えたなら**（合体・分離で入れ替わったとき）、
    ///   新しい体に手があれば持ち替え、無ければその場に落とす。
    ///   放っておくと、消えた体の手に付いたまま**物まで見えなくなる**ため
    /// </summary>
    private void HandleControlSwitched(RobotController.RobotState state)
    {
        if (Held == null)
        {
            return;
        }

        // 持っている体がまだ出ているなら、何もしなくてよい
        if (heldByBody != null && heldByBody.gameObject.activeInHierarchy)
        {
            return;
        }

        RobotBody body = controller.ActiveBody;

        if (body == null || !body.CanHold)
        {
            Debug.Log("[ROBOT] 持てる体がいなくなったので、持っていた物を離しました");
            Release();
            return;
        }

        // 新しい体の手に持ち替える。
        // 前の体とのぶつからない設定を戻してから、新しい体とぶつからないようにする
        IgnoreCollisionWithBody(null, false);
        IgnoreCollisionWithBody(body, true);

        AttachToHand(Held, body.HoldPoint);

        heldByBody = body;

        Debug.Log($"[ROBOT] 持っていた物を {body.name} に持ち替えました");
    }

    // ------------------------------------------------------------
    // 色を変える
    //
    // 中身は <see cref="InteractHighlight"/> にまとめてある（ロープと共通）
    // ------------------------------------------------------------

    private void ApplyHighlight(GameObject target)
    {
        highlight.Apply(target, highlightColor, highlightStrength);
    }

    private void ClearHighlight()
    {
        highlight.Clear();
    }

    /// <summary>このフレームに、持つ／離すキー（`grabKey`）が押されたか。</summary>
    private bool WasGrabKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[grabKey].wasPressedThisFrame;
    }
}
