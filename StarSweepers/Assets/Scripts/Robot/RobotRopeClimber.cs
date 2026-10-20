using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **ロープにつかまって、上下に移動する。**
/// （VALORANT のマップにある、上下に移動できる紐と同じ遊び方）
///
/// 遊び方：
///   ロープに近づくと、ロープの**色が変わる**（つかまれる合図）
///   F を押すと**つかまる**
///   **縦のロープ**なら W / S で**上下に移動する**
///   **横のロープ**なら、**行きたい方へ倒すとその方向へ伝っていく**（体は真下にぶら下がる）
///   もう一度 F を押すと**手を離す**（そのまま落ちる）
///   Space を押すと**見ている方向へ飛び降りる**
///   **Q を押すと、脚をその場に落として上半身だけが上り続ける**
///   Tab で下半身に移っても、**上半身はロープにつかまったまま残る**
///
/// **つかまれるのは「手のある体」だけ。**
/// 合体中と上半身はつかまれるが、**下半身だけのときはつかまれない。**
/// 物を持つとき（<see cref="RobotGrabber"/>）と同じ決まりで、
/// **体に「手の位置」が入っているかどうか**で決まる（<see cref="RobotBody.HoldPoint"/>）。
///
/// ## 動かし方
///
/// つかまっている間は、その体を <see cref="RobotController.SuspendedBody"/> に入れて
/// **普段の移動から外し、この部品が直接動かしている。**
/// 両方が同時に動かすと、引っ張り合いになってガタつくため。
///
/// 外すのは**操作していない体でもよい。**
/// そのおかげで、**上半身をロープに残したまま、下半身を操作しに行ける。**
///
/// ## F キーはロープが優先
///
/// 物を持つのと同じ F キーを使っている。
/// **ロープを狙っているとき・つかまっているときはロープが優先**され、
/// それ以外のときは <see cref="RobotGrabber"/> が受け取る。
/// （ただし、すでに物を持っているときは、物を離すほうが優先）
///
/// ## 流れ（毎フレーム）
///
/// 1. つかまっていなければ、**狙えるロープを探して色を変える**（<see cref="UpdateAim"/>）
/// 2. つかまっていれば、**入力に合わせて上り下りし、体をロープの位置へ寄せる**（<see cref="UpdateAttached"/>）
/// 3. F が押されたら、**つかまる／手を離す**（<see cref="ToggleHold"/>）
///
/// 体が切り替わったとき（Q で切り離す・合体する・Tab で操作を移す）は、
/// <see cref="HandleSplit"/> と <see cref="HandleControlSwitched"/> が、
/// **つかまっている体を引き継ぐか、手を離すか**を決める。
///
/// 使っているシーン：ロボット分離の検証シーン（`RobotSplitTest`）など、`RobotRig` を置いたシーン。
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotRopeClimber : MonoBehaviour
{
    [Header("届く範囲")]
    [Tooltip("**手からこの距離まで近づくとつかまれる**（メートル）")]
    [Range(0.5f, 6f)]
    [SerializeField] private float reach = 1.5f;

    [Header("狙い方")]
    [Tooltip("**画面の中央から、この角度以内に見えていればつかめる（度）。**" +
             "カメラから見た角度なので、**画面のどのあたりに映っているか**とほぼ同じ意味になる。" +
             "15度で、だいたい画面の中央あたり")]
    [Range(1f, 90f)]
    [SerializeField] private float maxAngle = 15f;

    [Header("上り下り")]
    [Tooltip("上り下りする速さ（1秒あたりのメートル）")]
    [Range(0.5f, 12f)]
    [SerializeField] private float climbSpeed = 3.5f;

    [Tooltip("ロープの位置まで引き寄せられる速さ。大きいほど一瞬でくっつく")]
    [Range(1f, 30f)]
    [SerializeField] private float snapSpeed = 8f;

    [Tooltip("ONにすると、一番下まで下りたときに自動で手を離す")]
    [SerializeField] private bool releaseAtBottom = true;

    [Header("飛び降りる")]
    [Tooltip("Space で飛び降りるときに、前へ飛ぶ勢い。0にすると真下に落ちる")]
    [Range(0f, 15f)]
    [SerializeField] private float jumpOffSpeed = 4f;

    [Tooltip("Space で飛び降りるときに、上へ跳ねる勢い")]
    [Range(0f, 15f)]
    [SerializeField] private float jumpOffUp = 3f;

    [Header("キーの割り当て")]
    [Tooltip("つかまる／手を離すキー。物を持つキーと同じにしてよい")]
    [SerializeField] private Key grabKey = Key.F;

    [Header("困ったとき")]
    [Tooltip("ONにすると、**つかめなかったときに理由をConsoleに出す**（何mだったか・何度ずれていたか）。" +
             "調整が終わったらOFFにしてよい")]
    [SerializeField] private bool logWhenFailed = true;

    [Header("見た目")]
    [Tooltip("画面中央のレティクル。つかまれるときに色が変わる")]
    [SerializeField] private Reticle reticle;

    [Tooltip("つかまれるロープを、この色に近づける")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.35f);

    [Tooltip("どれくらい色を混ぜるか")]
    [Range(0f, 1f)]
    [SerializeField] private float highlightStrength = 0.6f;

    /// <summary>
    /// ロープを調べるときの点の間隔（メートル）。
    /// ロープは長い1本の線なので、この間隔で点を取り、手との距離と狙いを調べる。
    /// </summary>
    private const float SampleSpacing = 0.25f;

    /// <summary>1本のロープを何区間まで分けて調べるか。とても長いロープでも重くならないよう、ここで止める。</summary>
    private const int MaxSampleSteps = 64;

    private RobotController controller;

    /// <summary>物を持つ側。F キーをどちらが受け取るか決めるために見ている（無くてもよい）。</summary>
    private RobotGrabber grabber;

    /// <summary>狙えているロープの色を変える道具。物を持つ・鍵を使う側と同じ仕組みを使っている。</summary>
    private readonly InteractHighlight highlight = new InteractHighlight();

    /// <summary>いまつかまっているロープ。つかまっていなければ null。</summary>
    public RobotRope AttachedRope { get; private set; }

    /// <summary>いま狙えているロープ。無ければ null。</summary>
    public RobotRope AimedRope { get; private set; }

    /// <summary>いまロープにつかまっているか。</summary>
    public bool IsAttached => AttachedRope != null;

    /// <summary>
    /// **いま操作している体が、ロープにつかまっているか。**
    ///
    /// <see cref="IsAttached"/> との違いは、
    /// **上半身をロープに残したまま下半身を操作している**ときに false になること。
    /// そのときの操作（F・W/S・Space）は、ロープではなく下半身のものになる。
    /// </summary>
    public bool IsClimbing => IsAttached && attachedBody == controller.ActiveBody;

    /// <summary>
    /// **このフレームの F キーを、ロープ側が受け取るか。**
    /// <see cref="RobotGrabber"/> がこれを見て、物を持つ処理を止める。
    /// </summary>
    public bool WantsInteract => IsClimbing || (AimedRope != null && !GrabberIsHolding);

    /// <summary>いま操作している体が、ロープにつかまれるか（手のある体か）。</summary>
    public bool CanClimbNow =>
        controller != null && controller.ActiveBody != null && controller.ActiveBody.CanHold;

    private bool GrabberIsHolding => grabber != null && grabber.Held != null;

    /// <summary>つかまっている体。</summary>
    private RobotBody attachedBody;

    /// <summary>いまつかまっている高さ（ロープの下端からの距離）。体の足元の高さ。</summary>
    private float holdHeight;

    /// <summary>
    /// **手がロープのどの高さを握っているか**（下端からの距離）。
    ///
    /// 切り離しで体が入れ替わったときに、**手の位置がずれないように**引き継ぐために使う。
    /// 足元の高さをそのまま渡すと、上半身の背丈のぶんだけ体がずり落ちてしまう。
    /// </summary>
    private float handHeight;

    /// <summary>ロープのどちら側にぶら下がっているか（水平の向き）。反対側へ回り込まないように覚えておく。</summary>
    private Vector3 hangSide;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        grabber = GetComponent<RobotGrabber>();
    }

    private void OnEnable()
    {
        if (controller != null)
        {
            // **切り離しは、体の入れ替えより先に受け取る必要がある**（順番が大事）
            controller.Split += HandleSplit;
            controller.ControlSwitched += HandleControlSwitched;
        }
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.Split -= HandleSplit;
            controller.ControlSwitched -= HandleControlSwitched;
        }

        Detach();
    }

    private void Update()
    {
        // つかまっているかどうかで、毎フレームの仕事が変わる
        if (IsAttached)
        {
            UpdateAttached();
        }
        else
        {
            UpdateAim();
        }

        // F の処理は最後。先に狙い（AimedRope）を新しくしてから、その結果でつかまる
        if (WasGrabKeyPressed())
        {
            ToggleHold();
        }
    }

    // ------------------------------------------------------------
    // 狙う
    // ------------------------------------------------------------

    /// <summary>正面の近くにあるロープを探して、色を変える。</summary>
    private void UpdateAim()
    {
        RobotRope found = CanClimbNow ? FindNearestRope() : null;

        if (found == AimedRope)
        {
            return;
        }

        AimedRope = found;

        highlight.Clear();

        if (AimedRope != null)
        {
            highlight.Apply(AimedRope.gameObject, highlightColor, highlightStrength);
        }

        if (reticle != null)
        {
            reticle.SetHighlight(AimedRope != null ? highlightColor : (Color?)null);
        }
    }

    /// <summary>
    /// **つかまれるロープ**を返す。次の2つを**両方とも**満たすものだけが対象。
    ///
    /// 1. **手からの距離**が `reach` 以内
    /// 2. **画面の中央から `maxAngle` 度以内に見えている**
    ///
    /// 距離は**ロープ全体との距離ではなく、一番近い点との距離**で測る。
    /// 長いロープでも、目の前の部分を見ていればつかまれるようにするため。
    /// </summary>
    private RobotRope FindNearestRope()
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        RobotRope nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (RobotRope rope in RobotRope.All)
        {
            if (rope == null)
            {
                continue;
            }

            if (!TryFindHold(rope, hand, body, out float distance))
            {
                continue;
            }

            if (distance >= nearestDistance)
            {
                continue;
            }

            nearest = rope;
            nearestDistance = distance;
        }

        return nearest;
    }

    /// <summary>
    /// そのロープをつかめるかを調べる。**2つの条件を別々に見る。**
    ///
    /// | 条件 | 見る場所 |
    /// | 手が届くか | **ロープの中で、手に一番近い場所** |
    /// | 狙えているか | **ロープの中で、レティクルに一番近い場所** |
    ///
    /// **同じ場所で両方を満たす必要はない。**
    /// 斜めに走っているロープでは、**手に一番近い所と、レティクルが乗っている所が別**になる。
    /// 同じ場所を求めると、**画面では明らかに狙えているのにつかめない**という状態になってしまう。
    ///
    /// 「近くにロープがあり、そのロープを見ている」なら届く、という考え方にしてある。
    ///
    /// ロープは長い1本の線なので、**細かく分けて調べている**（端の1点だけでは足りない）。
    /// </summary>
    private bool TryFindHold(RobotRope rope, Transform hand, RobotBody body, out float distance)
    {
        distance = float.MaxValue;
        bool aimed = false;

        float min = rope.MinHold;
        float span = rope.MaxHold - min;

        // だいたい25cmおきに調べる。長いロープでも数十回で済む
        int steps = SampleSteps(span);

        // 区間の数より1つ多い点を調べる（`<=` なので、両端とも含まれる）
        for (int i = 0; i <= steps; i++)
        {
            Vector3 point = rope.PointAt(min + span * i / steps);

            distance = Mathf.Min(distance, Vector3.Distance(point, hand.position));

            if (!aimed && IsAimedAt(point, body))
            {
                aimed = true;
            }
        }

        return aimed && distance <= reach;
    }

    /// <summary>
    /// 長さ `span`（メートル）のロープを、**何区間に分けて調べるか**。
    /// だいたい <see cref="SampleSpacing"/> おきになるようにし、1〜<see cref="MaxSampleSteps"/> の間に収める。
    /// 狙いの判定（<see cref="TryFindHold"/>）と、つかめなかった理由の表示（<see cref="LogWhyNotGrabbed"/>）で
    /// **同じ分け方にそろえる**ために、ここにまとめてある。
    /// </summary>
    private static int SampleSteps(float span)
    {
        return Mathf.Clamp(Mathf.CeilToInt(span / SampleSpacing), 1, MaxSampleSteps);
    }

    /// <summary>
    /// **その場所を狙えているか。**
    ///
    /// **カメラから見て、画面の中央から何度ずれているか**で判断する。
    /// カメラを中心にしているので、**画面のどのあたりに映っているか**とほぼ同じ意味になる。
    ///
    /// **プレイヤーの頭を中心にしていないのが要点。**
    /// 頭を中心にすると、**自分の体の裏を通っているロープ**や、
    /// **真上を通っているロープ**が「大きく外れている」と判断されてしまう。
    /// 画面では狙えているのにつかめない、という状態になる。
    ///
    /// 物を持つ・扉を開けるほうは、**手を伸ばして触る**動きなので、
    /// これまでどおり頭を中心にしている（<see cref="AimCheck.IsAimed"/>）。
    /// </summary>
    private bool IsAimedAt(Vector3 worldPoint, RobotBody body)
    {
        return AimCheck.IsAimedFromCamera(
            worldPoint, maxAngle, body.HeadPosition, body.transform.forward);
    }

    // ------------------------------------------------------------
    // つかまる・手を離す
    // ------------------------------------------------------------

    /// <summary>つかまっていなければつかまり、つかまっていれば手を離す。</summary>
    public void ToggleHold()
    {
        if (IsAttached)
        {
            Detach();
            return;
        }

        if (AimedRope != null && CanClimbNow)
        {
            Attach(AimedRope);
            return;
        }

        if (logWhenFailed)
        {
            LogWhyNotGrabbed();
        }
    }

    /// <summary>
    /// **つかめなかったとき、なぜ届かなかったかをConsoleに出す。**
    /// 「近すぎ／遠すぎ」「狙いがずれている」のどちらなのかが、数字で分かる。
    /// </summary>
    private void LogWhyNotGrabbed()
    {
        if (!CanClimbNow)
        {
            Debug.Log("[ROBOT] いまの体ではロープにつかまれません（手のある体だけ）");
            return;
        }

        if (RobotRope.All.Count == 0)
        {
            Debug.Log("[ROBOT] このシーンにロープがありません");
            return;
        }

        Transform hand = controller.ActiveBody.HoldPoint;

        foreach (RobotRope rope in RobotRope.All)
        {
            if (rope == null)
            {
                continue;
            }

            float nearestHand = float.MaxValue;
            float nearestAngle = float.MaxValue;

            // 調べ方は TryFindHold と同じ（同じ点で測らないと、表示した数字と判定が食い違う）
            float min = rope.MinHold;
            float span = rope.MaxHold - min;
            int steps = SampleSteps(span);

            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = rope.PointAt(min + span * i / steps);

                nearestHand = Mathf.Min(nearestHand, Vector3.Distance(point, hand.position));
                nearestAngle = Mathf.Min(nearestAngle, AimCheck.AngleFromCamera(point));
            }

            Debug.Log($"[ROBOT] {rope.name}：手からの距離 {nearestHand:0.00}m（許容 {reach}m） / " +
                      $"画面中央からのずれ {nearestAngle:0.0}度（許容 {maxAngle}度）", rope);
        }
    }

    /// <summary>ロープにつかまる。</summary>
    public void Attach(RobotRope rope)
    {
        RobotBody body = controller.ActiveBody;

        if (rope == null || body == null || !body.CanHold)
        {
            return;
        }

        AttachedRope = rope;
        attachedBody = body;

        // いまの足元の高さでつかまる（つかまれる範囲の外なら、端に寄せる）
        holdHeight = Mathf.Clamp(rope.HeightOf(body.transform.position), rope.MinHold, rope.MaxHold);

        // holdHeight を決めたあとに呼ぶ（その高さのロープの点から測るため）
        hangSide = DecideHangSide(rope, body);

        highlight.Clear();
        AimedRope = null;

        handHeight = holdHeight + HandOffset(body);

        // 普段の移動から外す。ここから先はこの部品が体を動かす
        controller.SuspendedBody = body;

        Debug.Log($"[ROBOT] {body.name} が {rope.name} につかまりました");
    }

    /// <summary>
    /// つかまった瞬間に、**ロープのどちら側にぶら下がるか**（水平の向き）を決める。
    /// <see cref="holdHeight"/> を先に決めてから呼ぶこと。
    /// </summary>
    private Vector3 DecideHangSide(RobotRope rope, RobotBody body)
    {
        if (rope.IsHorizontal)
        {
            // 横のロープは**真下にぶら下がる**ので、どちら側かを覚える必要がない
            return Vector3.down;
        }

        // **近づいた側にぶら下がる。** 反対側へ勝手に回り込むと、
        // 足場から落ちたように見えてしまうため
        Vector3 side = body.transform.position - rope.PointAt(holdHeight);

        // ロープに沿った成分を取り除き、「ロープから見てどちらの横にいるか」だけを残す
        side -= Vector3.Project(side, rope.Direction);

        // ロープとほぼ重なっていて（1cm未満）横の向きが決まらないときは、
        // 体の正面の反対側にぶら下がる（＝ロープを正面に見る位置になる）
        return side.sqrMagnitude > 0.0001f
            ? side.normalized
            : -Flat(body.transform.forward);
    }

    /// <summary>手を離す。そのまま落ちる。</summary>
    public void Detach()
    {
        if (!IsAttached)
        {
            return;
        }

        RobotRope rope = AttachedRope;

        AttachedRope = null;
        attachedBody = null;

        if (controller != null)
        {
            controller.SuspendedBody = null;
        }

        if (reticle != null)
        {
            reticle.SetHighlight(null);
        }

        Debug.Log($"[ROBOT] {(rope != null ? rope.name : "ロープ")} から手を離しました");
    }

    /// <summary>見ている方向へ飛び降りる。</summary>
    private void JumpOff()
    {
        RobotBody body = attachedBody;

        Vector3 forward = Flat(controller.LookForward);

        Detach();

        if (body != null)
        {
            body.Launch(forward * jumpOffSpeed + Vector3.up * jumpOffUp);
        }
    }

    // ------------------------------------------------------------
    // つかまっている間
    // ------------------------------------------------------------

    /// <summary>
    /// つかまっている間の1フレーム分。
    /// **飛び降り → 上り下りの入力 → 一番下での自動の手離し → 体をロープへ寄せる**、の順に見る。
    /// </summary>
    private void UpdateAttached()
    {
        RobotRope rope = AttachedRope;
        RobotBody body = attachedBody;

        // ロープが消えた・体が引っ込んだ場合の保険
        if (rope == null || body == null || !body.gameObject.activeInHierarchy)
        {
            Detach();
            return;
        }

        // **操作しているのが、つかまっている体のときだけ入力を受け取る。**
        // 下半身を操作しに行っている間、残した上半身は
        // 「その場でつかまったまま」になるだけ（勝手に上り下りしない）
        bool controlling = attachedBody == controller.ActiveBody;

        if (controlling && controller.JumpPressedThisFrame)
        {
            JumpOff();
            return;
        }

        float input = controlling ? ReadMoveInput(rope) : 0f;

        // 入力の向き（＋で上・先端へ、−で下・根元へ）に、1秒あたり climbSpeed メートル進む
        holdHeight = Mathf.Clamp(
            holdHeight + input * climbSpeed * Time.deltaTime, rope.MinHold, rope.MaxHold);

        // **縦のロープだけ**、一番下まで下りたら自動で手を離して立たせる。
        // 横のロープでは端に着いても落ちない（伝っている途中で落ちると事故になる）
        // （`+ 0.01f` は 1cm ぶんの余裕。下へ押しているときだけ離す）
        if (releaseAtBottom && !rope.IsHorizontal
            && input < 0f && holdHeight <= rope.MinHold + 0.01f)
        {
            Detach();
            return;
        }

        PullBodyToHold(rope, body);
    }

    /// <summary>
    /// 体を、いまの <see cref="holdHeight"/> に合った場所（<see cref="TargetPosition"/>）へ寄せる。
    ///
    /// **1フレームに動ける距離に上限がある**（`snapSpeed` × 経過時間）。
    /// 離れていれば少しずつ引き寄せられ、近ければぴったり合う。
    /// 動かしたあとは、**実際にたどり着いた位置から高さを測り直す。**
    /// </summary>
    private void PullBodyToHold(RobotRope rope, RobotBody body)
    {
        Vector3 target = TargetPosition(rope, body);
        Vector3 delta = target - body.transform.position;

        // 一度に動ける距離を決めておく。
        // つかまった瞬間の引き寄せも、上り下りも、この1本で足りる
        float step = snapSpeed * Time.deltaTime;

        // 長さの2乗どうしで比べている（平方根を取らずに「step より遠いか」が分かる）
        if (delta.sqrMagnitude > step * step)
        {
            delta = delta.normalized * step;
        }

        body.MoveWithoutGravity(delta, GetFacing());

        // 壁に阻まれて動けなかったぶんを、つかまっている高さにも反映する。
        // ずれたままにすると、あとで一気に引き寄せられてしまう
        holdHeight = Mathf.Clamp(rope.HeightOf(body.transform.position), rope.MinHold, rope.MaxHold);

        // 手が握っている高さも控えておく。切り離しで体が入れ替わるときに使う
        handHeight = holdHeight + HandOffset(body);
    }

    /// <summary>
    /// **ロープに沿って進みたい量**を読む（＋で先端へ、−で根元へ）。
    ///
    /// | ロープ | 読み方 |
    /// | 縦 | **W で上、S で下。** カメラの向きは関係しない |
    /// | 横 | **押した方向を、ロープの向きに当てはめる。** ロープが伸びている方へ押せば進む |
    ///
    /// 横のロープで W/S だけを見ると、**カメラの向きしだいで進む向きが逆になる。**
    /// 「行きたい方へ倒せば、その方向へ進む」ほうが迷わない。
    /// </summary>
    private float ReadMoveInput(RobotRope rope)
    {
        if (!rope.IsHorizontal)
        {
            return controller.MoveInput.y;
        }

        return Vector3.Dot(controller.MoveWorldDirection, rope.Direction);
    }

    /// <summary>
    /// つかまっているときに、体を置きたい場所。
    ///
    /// - **縦のロープ**… ロープの横に、近づいた側からぶら下がる
    /// - **横のロープ**… **ロープの真下にぶら下がる。**
    ///   足元ではなく**手がロープに届く高さ**に合わせるので、背丈が違っても同じ見た目になる
    /// </summary>
    private Vector3 TargetPosition(RobotRope rope, RobotBody body)
    {
        Vector3 point = rope.PointAt(holdHeight);

        if (!rope.IsHorizontal)
        {
            return point + hangSide * rope.HangDistance;
        }

        return point - Vector3.up * (HandHeightAboveFeet(body) + rope.HangBelow);
    }

    /// <summary>その体の**足元から手までの高さ**（まっすぐ上に測る）。</summary>
    private static float HandHeightAboveFeet(RobotBody body)
    {
        if (body == null || body.HoldPoint == null)
        {
            return 0f;
        }

        return body.HoldPoint.position.y - body.transform.position.y;
    }

    /// <summary>
    /// その体の**足元から手までの高さ**（ロープに沿って測る）。
    /// 体によって背丈が違うので、手の位置を合わせるのに使う。
    /// </summary>
    private float HandOffset(RobotBody body)
    {
        if (body == null || body.HoldPoint == null || AttachedRope == null)
        {
            return 0f;
        }

        return Vector3.Dot(body.HoldPoint.position - body.transform.position, AttachedRope.Direction);
    }

    /// <summary>
    /// つかまっている間、体をどちらへ向けるか。
    ///
    /// - 見ている方向を向く作り（一人称／フォートナイト型）なら、カメラの向き
    /// - そうでなければ、**縦のロープならロープのほう、横のロープならロープに沿った向き**
    /// </summary>
    private Vector3? GetFacing()
    {
        if (controller.FaceLookDirection)
        {
            return Flat(controller.LookForward);
        }

        if (AttachedRope != null && AttachedRope.IsHorizontal)
        {
            return Flat(AttachedRope.Direction);
        }

        return -hangSide;
    }

    // ------------------------------------------------------------
    // 体が切り替わったとき
    // ------------------------------------------------------------

    /// <summary>
    /// **ロープにつかまったまま Q を押したとき。**
    ///
    /// **脚をその場に落として、上半身だけがロープに残る。**
    /// 落とした脚は重力で下まで落ちる。上半身はそのまま上り続けられる。
    ///
    /// 切り離しの処理は、上半身を離れた位置へ飛ばしてしまうので、
    /// **そのすぐあとにロープの位置へ戻している。**
    /// 同じフレームのうちに戻すため、画面上は動いていないように見える。
    ///
    /// ※ この処理は <see cref="HandleControlSwitched"/> より**先に**呼ばれる。
    ///    先に引き継いでおかないと、そちらで手を離す扱いになってしまう
    /// </summary>
    private void HandleSplit()
    {
        if (!IsAttached)
        {
            return;
        }

        // 切り離した直後は、上半身を操作している
        RobotBody next = controller.ActiveBody;

        if (next == null || !next.CanHold)
        {
            Detach();
            return;
        }

        TransferTo(next);
    }

    /// <summary>
    /// つかまっている体を入れ替える。**手の高さを合わせたまま**引き継ぐ。
    /// </summary>
    private void TransferTo(RobotBody next)
    {
        RobotRope rope = AttachedRope;

        attachedBody = next;
        controller.SuspendedBody = next;

        // **足元ではなく、手の高さを合わせる。**
        // 足元をそのまま渡すと、背丈の差のぶんだけ体がずり落ちる。
        // 横のロープは、ぶら下がる高さを毎回手から計算しているので、そのままでよい
        if (!rope.IsHorizontal)
        {
            holdHeight = Mathf.Clamp(handHeight - HandOffset(next), rope.MinHold, rope.MaxHold);
        }

        Vector3 target = TargetPosition(rope, next);
        Vector3 facing = GetFacing() ?? next.transform.forward;

        // 切り離しで与えられた勢いも、ここで消える（Teleport が打ち消す）
        next.Teleport(target, Quaternion.LookRotation(facing, Vector3.up));

        Debug.Log($"[ROBOT] ロープを {next.name} に引き継ぎました（脚はその場に落ちます）");
    }

    /// <summary>
    /// 合体・操作の切り替えが起きたとき。
    ///
    /// - **つかまっている体がまだ出ているなら、そのままつかまらせておく。**
    ///   （上半身をロープに残したまま、下半身を操作しに行ける）
    /// - **つかまっている体が消えたなら**（合体で入れ替わったとき）、手を離す。
    ///   放っておくと、消えた体につかまったままになってしまう
    ///
    /// 切り離しのときは <see cref="HandleSplit"/> が先に上半身へ引き継いでいるので、
    /// ここでは「まだ出ている」と判断されて何もしない。
    /// </summary>
    private void HandleControlSwitched(RobotController.RobotState state)
    {
        if (!IsAttached)
        {
            return;
        }

        if (attachedBody != null && attachedBody.gameObject.activeInHierarchy)
        {
            return;
        }

        Detach();
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>
    /// 上下の成分を落として、水平の向きにする。
    /// ほぼ真上・真下を向いていて水平の成分が残らない（長さ0.01未満）ときは、ワールドの前（Z+）を返す。
    /// </summary>
    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    /// <summary>このフレームに、つかまる／手を離すキー（`grabKey`）が押されたか。</summary>
    private bool WasGrabKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[grabKey].wasPressedThisFrame;
    }
}
