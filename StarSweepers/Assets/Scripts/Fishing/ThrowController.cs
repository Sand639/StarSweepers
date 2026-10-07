using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>投げの強さの段階。スキルチェックの正確さで決まる（仕様5）。</summary>
public enum ThrowTier
{
    Normal,   // 通常成功 … 普通の力
    Good,     // 良いタイミング … 強い力
    Perfect   // 最適なタイミング … 非常に強い力
}

/// <summary>投げる方向の決め方（仕様6・7）。あとから種類を足せるよう enum にしている。</summary>
public enum ThrowDirectionMode
{
    Backward,     // プレイヤーの後方（いま向いている方向の反対）
    Forward,      // プレイヤーの前方（いま向いている方向＝進行方向）
    WorldCustom   // 下の Custom Direction で指定した固定方向
}

/// <summary>
/// **引っ掛けたあとの操作のしかた。** プレイヤーのプレハブの ThrowController の Style で切り替える。
/// </summary>
public enum ThrowStyle
{
    /// <summary>
    /// **釣り式（もとの仕様）。** 引き寄せながらゲージが1回だけ動き、
    /// 前半の枠で押す＝後ろへ、後半の枠で押す＝前へ投げる。
    /// </summary>
    SweepOnce,

    /// <summary>
    /// **宇宙ごみ式（2026/9/22・大槻さんの依頼）。** 撃つボタンで流れが分かれる。
    /// 左クリック（LT）で撃つ → くっついた場所で止まってゲージ → 左で引っ張る（頭上を越えて後ろへ飛ぶ）。
    /// 右クリック（RT）で撃つ → 頭上まで来て止まってゲージ → 右で投げる。
    /// ゲージは往復し、真ん中に近いほど強い。
    /// </summary>
    TwoButtons
}

/// <summary>
/// **スキルチェックの枠1つぶんの設定。**
///
/// ゲージの上のどこにあるか、どれくらいの広さか、成功したらどっちへ投げるか、をまとめて持つ。
/// **枠を増やしたいときは、ThrowController の Skill Check Zones に足すだけでよい。**
/// </summary>
[System.Serializable]
public class SkillCheckZone
{
    [Tooltip("画面とログに出す名前")]
    public string label = "前半";

    [Tooltip("ゲージ上の中心。0で左端、0.5で真ん中、1で右端")]
    [Range(0f, 1f)]
    public float center = 0.27f;

    [Tooltip("『通常成功』になる範囲（中心からの片側の広さ）。ここを外すとミス")]
    public float hitWindow = 0.11f;

    [Tooltip("『強い力』になる範囲（中心からの片側の広さ）")]
    public float goodWindow = 0.06f;

    [Tooltip("『非常に強い力』になる範囲（中心からの片側の広さ）")]
    public float perfectWindow = 0.022f;

    [Tooltip("この枠で成功したときに物資を飛ばす方向")]
    public ThrowDirectionMode direction = ThrowDirectionMode.Backward;
}

/// <summary>
/// **フックした物資を引き寄せて、スキルチェックの入力で投げる係。**
///
/// 引き寄せの動き（仕様改善1）：
///   物資はプレイヤーへ向かって弧を描いて上がり、**ゲージの真ん中（0.5）でプレイヤーの真上を通過**し、
///   そのまま後方へ抜けていく。「釣り竿で引っこ抜く」動きになる。
///
/// スキルチェック（仕様追加1）：
///   ゲージは**1往復だけ**動く。枠は2つあり、
///     ・前半の枠（真上を通る前）で押す → **後方**へ飛ばす
///     ・後半の枠（真上を通った後）で押す → **進行方向（前方）**へ飛ばす
///   枠の中心に近いほど強く投げる（通常／強い／非常に強い）。
///
/// ミス（仕様改善2）：
///   **ゲージが通り過ぎた**か、**枠の外で押した**場合はミス。
///   ほんの少しだけプレイヤー側へ引き寄せるだけで、引っ張りを終了する。
///
/// 投げる方向は、**投げた瞬間にプレイヤーが向いている方向**を基準にする（仕様改善3）。
/// 釣り上げたときの向きではないので、引っ張っている間に振り向けば行き先も変わる。
///
/// ## 宇宙ごみ式（Style が TwoButtons のとき。2026/9/22）
///
/// 上は釣り式（SweepOnce）の説明。宇宙ごみのプレイヤーは **TwoButtons** にしてあり、流れが違う：
///
///   **フックを撃つボタンで、引っ掛けたあとの流れが決まる**（ゲージは往復し、真ん中に1つの枠。真ん中ほど強い）
///
///   ・左クリック（LT）で撃つ … くっついた場所で**止まって**ゲージ → 左で押すと、釣り式と同じ軌道で
///     **頭上を越えて後ろへ抜け**、そのまま後ろへ飛ぶ（強いほど遠く）
///   ・右クリック（RT）で撃つ … 頭上まで来て**止まって**ゲージ → 右で押すと、
///     **投げた瞬間にプレイヤーが向いている方向**へ**投げる**（強いほど遠く。2026/10/1 から。Throw Toward Facing を OFF にすると、くっついていた場所の向きへ投げ返す）
///
/// 押すまでずっと待つ（時間切れなし）。**ほかの人のフックが当たる**か、爆風を受けると外れる。
/// 釣り式に戻したいときは、プレハブの Style を SweepOnce にするだけでよい。
///
/// ## 重い物（<see cref="HeavyHookable"/> が付いた物。宇宙ごみ式のときだけ。2026/9/29）
///
///   ・**投げられない。** 右クリック（RT）で撃ったフックは引っ掛からない（<see cref="RefusesHook"/>）
///   ・左で引っ掛けると、その場で止まってゲージ。**ゲージは往復せず、右端まで行ったら終わり**（重いほど速い）
///   ・左で押すと、**頭上は越えずに地面を引きずる。** 距離（m）＝ 引っ張る強さ ÷ 重さ ×（1 − 真ん中からのずれ）。
///     右端まで押さなかったときや端で押したときも、少しだけ引きずる（2026/9/29 に「中間まで」から固定の距離に変えた）
///
///   **釣りでは重い物を出していないので、釣りの動きは変わらない。**
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("同じプレイヤーの HookController")]
    [SerializeField] private HookController hook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("操作のしかた")]
    [Tooltip("SweepOnce＝釣り式（もとの仕様）。TwoButtons＝宇宙ごみ式（左で撃つと引っ張る・右で撃つと投げる）。\n" +
             "**いつでもここを切り替えれば、もとの仕様に戻せる**")]
    [SerializeField] private ThrowStyle style = ThrowStyle.SweepOnce;

    [Header("宇宙ごみ式（Style が TwoButtons のとき）")]
    [Tooltip("ゲージのマーカーが、1秒間に端から端まで何回動くか。大きいほど速くて難しい")]
    [Min(0.1f)]
    [SerializeField] private float barSweepsPerSecond = 1.2f;

    [Tooltip("引っ張るとき、真ん中から一番遠い（端で押した）ときの力")]
    [SerializeField] private float pullForceMin = 4f;

    [Tooltip("引っ張るとき、真ん中ちょうどで押したときの力。頭上を越えたあと、この力で後ろへ飛ぶ")]
    [SerializeField] private float pullForceMax = 16f;

    [Tooltip("引っ張るときに少し上向きに加える力")]
    [SerializeField] private float pullLift = 3f;

    [Tooltip("引っ張ってから、頭上を越えて後ろへ抜けるまでの秒数")]
    [Min(0.05f)]
    [SerializeField] private float pullTravelSeconds = 0.6f;

    [Tooltip("右クリックで撃って引っ掛けてから、頭上まで持ち上がるまでの秒数")]
    [Min(0.05f)]
    [SerializeField] private float liftSeconds = 0.45f;

    [Tooltip("ON＝**投げた瞬間にプレイヤーが向いている方向**へ投げる（2026/10/1 から。持ち上げている間に向きを変えれば、投げる方向も変わる）。\n" +
             "OFF＝物資がくっついていた場所の方向へ投げ返す（前の動き）")]
    [SerializeField] private bool throwTowardFacing = true;

    [Tooltip("投げるとき、真ん中から一番遠い（端で押した）ときの力")]
    [SerializeField] private float twoButtonThrowForceMin = 6f;

    [Tooltip("投げるとき、真ん中ちょうどで押したときの力")]
    [SerializeField] private float twoButtonThrowForceMax = 22f;

    [Header("重い物（宇宙ごみ式のとき。HeavyHookable が付いた物）")]
    [Tooltip("重さ1のとき、ゲージのマーカーが1秒間に進む量（1で1秒かけて左端→右端）。**往復せず、右端まで行ったら終わる**")]
    [Min(0.1f)]
    [SerializeField] private float heavyBarSweepsPerSecond = 0.8f;

    [Tooltip("重さが1増えるごとに、ゲージがどれだけ速くなるか（0.3 なら重さ2で1.3倍、重さ3で1.6倍）")]
    [Min(0f)]
    [SerializeField] private float heavyBarSpeedUpPerWeight = 0.3f;

    [Tooltip("**引っ張る強さ（メートル）。** 引きずれる距離 ＝ 引っ張る強さ ÷ 重さ ×（1 − ゲージの真ん中からのずれ）。\n" +
             "重さ1でど真ん中なら、この距離だけ引きずる")]
    [Min(0f)]
    [SerializeField] private float heavyDragStrength = 6f;

    [Tooltip("いちばん短く引きずる距離（メートル。重さで割る）。端で押した・押さずに右端まで行ったときでも、これだけは動く（少し引っ張る）")]
    [Min(0f)]
    [SerializeField] private float heavyDragMinDistance = 0.8f;

    [Tooltip("プレイヤーにこれ以上近づけない距離（メートル）。引きずりすぎてプレイヤーに重ならない・追い越さないように")]
    [Min(0f)]
    [SerializeField] private float heavyDragStopDistance = 1.5f;

    [Tooltip("引きずるのにかける秒数")]
    [Min(0.05f)]
    [SerializeField] private float heavyDragSeconds = 0.6f;

    [Header("引き寄せの動き")]
    [Tooltip("引き寄せ〜スキルチェックが終わるまでの秒数。短いほど難しい")]
    [SerializeField] private float skillCheckDuration = 1.6f;

    [Tooltip("プレイヤーの足元から、物資が通り越す高さ（メートル）")]
    [SerializeField] private float overheadHeight = 2.6f;

    [Tooltip("真上を通ったあと、後方どれだけまで抜けていくか（メートル）")]
    [SerializeField] private float passDistance = 3.5f;

    [Tooltip("弧の膨らみ。大きいほど高く跳ね上げてから来る")]
    [SerializeField] private float arcLift = 0.8f;

    [Tooltip("引き寄せ中に物資を回す速さ（1秒あたりの度）。0で回さない")]
    [SerializeField] private float reelSpinSpeed = 200f;

    [Header("スキルチェックの枠")]
    [Tooltip("枠は上から順に判定される。**増やせばそのまま増える**")]
    [SerializeField]
    private SkillCheckZone[] skillCheckZones =
    {
        new SkillCheckZone
        {
            label = "前半",
            center = 0.27f,
            hitWindow = 0.11f,
            goodWindow = 0.06f,
            perfectWindow = 0.022f,
            direction = ThrowDirectionMode.Backward
        },
        new SkillCheckZone
        {
            label = "後半",
            center = 0.73f,
            hitWindow = 0.11f,
            goodWindow = 0.06f,
            perfectWindow = 0.022f,
            direction = ThrowDirectionMode.Forward
        }
    };

    [Header("投げる力")]
    [Tooltip("通常成功のときの力")]
    [SerializeField] private float throwForceNormal = 8f;

    [Tooltip("良いタイミングのときの力")]
    [SerializeField] private float throwForceGood = 14f;

    [Tooltip("最適なタイミングのときの力")]
    [SerializeField] private float throwForcePerfect = 22f;

    [Tooltip("投げるときに少し上向きに加える力（0で真横に飛ぶ）")]
    [SerializeField] private float throwLift = 2f;

    [Tooltip("方向が WorldCustom の枠で使う固定方向")]
    [SerializeField] private Vector3 customDirection = Vector3.zero;

    [Header("ミスしたとき")]
    [Tooltip("ミスしたときに、プレイヤー側へほんの少し引き寄せる力")]
    [SerializeField] private float missPullForce = 2.5f;

    [Header("タイプBの物理引き寄せ")]
    [Tooltip("タイプBでオブジェクトをプレイヤーへ引く力（加速度として加える）")]
    [Min(0f)]
    [SerializeField] private float pullForceB = 28f;

    [Tooltip("タイプBで引きずっている間の重力倍率。0で重力なし、1で通常の重力")]
    [Range(0f, 1f)]
    [SerializeField] private float dragGravityScaleB = 0.35f;

    [Tooltip("タイプBで投げるときに狙う、プレイヤー足元からの高さ")]
    [Min(0f)]
    [SerializeField] private float throwAimHeightB = 2f;

    [Tooltip("タイプBの投げ先を横へずらす量。斜めの軌道を作る")]
    [Min(0f)]
    [SerializeField] private float throwSideOffsetB = 0.5f;

    private InputActionMap playerMap;
    private InputAction attackAction;
    private PlayerInputSource input;

    private HookableObject target;
    private IHookPullable pullableTarget;
    private bool active;
    private bool distanceBasedPullActive;
    private float armDelay;   // Begin 直後の1入力を誤爆しないための短い待ち
    private float timer;
    private float distancePullElapsed;
    private Vector3 reelStart;
    private bool targetGravityWas;
    private bool targetKinematicWas;
    private AnchorGimmick anchorTarget;
    private CharacterController pullingPlayer;
    private FishingPlayerController pullingPlayerMovement;

    // ---- 宇宙ごみ式の途中経過 ----

    /// <summary>宇宙ごみ式の段階。</summary>
    private enum TwoButtonStage
    {
        AimPull,     // 【左で撃った】くっついた場所で止まってゲージ。左＝引っ張る
        PullTravel,  // 【左で撃った】引っ張って、頭上を越えて後ろへ抜けている途中（ゲージは出さない）
        Lifting,     // 【右で撃った】頭上へ持ち上げている途中（ゲージは出さない）
        AimThrow,    // 【右で撃った】頭上で止まってゲージ。右＝投げる
        HeavyDrag    // 【重い物】地面を引きずっている途中（ゲージは出さない）
    }

    private TwoButtonStage stage;
    private float barTimer;
    private float liftTimer;
    private float pullAccuracy;

    // ---- 重い物の途中経過 ----

    /// <summary>いま引っ張っている物が重い物か（宇宙ごみ式のときだけ true になる）。</summary>
    private bool heavy;

    /// <summary>重い物の重さ。</summary>
    private float heavyWeight = 1f;

    private Vector3 dragFrom;
    private Vector3 dragTo;

    /// <summary>次に引っ掛けたときに「投げる」ほうか（右で撃った）。false なら「引っ張る」ほう（左で撃った）。</summary>
    private bool nextIsThrow;

    /// <summary>いま引き寄せ中か。UI などが参照する。</summary>
    public bool IsPulling => active;

    /// <summary>操作タイプBで距離ベースの引き抜きを使うか。入力方式と軌道処理から独立させる。</summary>
    private bool UsesDistanceBasedPull => GameSettings.ControllerOperation == ControllerOperationType.TypeB;

    /// <summary>いまの操作のしかた。</summary>
    public ThrowStyle Style => style;

    /// <summary>
    /// **ほかの人のフックが当たったら、その人の引っ掛けを外すか。**
    /// 宇宙ごみ式だけ（2026/9/22・大槻さん「他の人のフックが当たったら失敗」）。釣りでは今までどおり素通りする。
    /// </summary>
    public bool BreaksOthersGrab => style == ThrowStyle.TwoButtons;

    /// <summary>
    /// **宇宙ごみ式で、どちらのボタンでフックを撃ったかを伝える。** <see cref="HookController"/> が撃つときに呼ぶ。
    /// true＝右（投げる）、false＝左（引っ張る）。
    /// </summary>
    public void SetNextIsThrow(bool isThrow)
    {
        nextIsThrow = isThrow;
    }

    /// <summary>
    /// **このフックで、その物資を引っ掛けてはいけないか。** <see cref="HookController"/> が当たったときに聞く。
    /// 宇宙ごみ式で、**右クリック（投げる）で撃ったフックは、重い物には引っ掛からない**（2026/9/29・大槻さん。重い物は投げられない）。
    /// </summary>
    public bool RefusesHook(HookableObject hookable)
    {
        return style == ThrowStyle.TwoButtons && nextIsThrow && HeavyHookable.TryGetWeight(hookable, out _);
    }

    private void Awake()
    {
        if (hook == null)
        {
            hook = GetComponent<HookController>();
        }

        // 操作の読み口（単体操作か、1台のPCで複数人の席か）
        input = PlayerInputSource.Get(this);

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、この体専用の複製を使う**（理由は FishingPlayerController.Awake と同じ。
        // 共有したままだと、他の人のぶんを止めたときに自分の入力まで止まる）
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        attackAction = playerMap.FindAction("Attack", true);
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
        SetAnchorGravitySuppressed(false);
    }

    /// <summary>HookController から呼ばれる。引き寄せとスキルチェックを開始する。</summary>
    public void Begin(HookableObject hookable)
    {
        distanceBasedPullActive = false;
        target = hookable;
        pullableTarget = null;
        active = true;
        timer = 0f;
        armDelay = 0.12f;
        heavy = false;
        heavyWeight = 1f;

        // アンカーは物資を引き寄せない。フックを固定したまま、プレイヤー自身を寄せる。
        // AnchorGimmick が無い従来の物資は、これまでどおり下の引き寄せ・投げ処理へ進む。
        anchorTarget = target != null ? target.GetComponent<AnchorGimmick>() : null;
        if (anchorTarget != null)
        {
            anchorTarget.BeginPull(hook != null ? hook.Charge : 0f);
            pullingPlayer = hook != null ? hook.PlayerRoot.GetComponent<CharacterController>() : null;
            pullingPlayerMovement = hook != null ? hook.PlayerRoot.GetComponent<FishingPlayerController>() : null;
            SetAnchorGravitySuppressed(true);
            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            return;
        }

        // **重い物（宇宙ごみの特殊デブリ）は、操作タイプBでも「引きずるだけ」の決まりを使う**（2026/10/1・PR #10 との合流時）。
        // タイプBの物理の引き寄せを使うと、重い物も軽く引っ張れて投げられてしまうため
        bool heavyTwoButtons = style == ThrowStyle.TwoButtons && HeavyHookable.TryGetWeight(hookable, out _);
        distanceBasedPullActive = UsesDistanceBasedPull && !heavyTwoButtons;

        if (target != null)
        {
            // **持っている間は、自分の体に当たらないようにする**（2026/9/30）。
            // 頭上や体のそばを通すので、当たると歩いたときに体が押されたり引っかかったりする。
            // 手を離したら、体から離れたところで自動で元に戻る（ThrowPassThrough）
            ThrowPassThrough.Hold(target.gameObject, hook.PlayerRoot.GetComponent<CharacterController>());

            reelStart = target.transform.position;
            distancePullElapsed = 0f;

            targetGravityWas = target.Body.useGravity;
            targetKinematicWas = target.Body.isKinematic;

            if (distanceBasedPullActive)
            {
                // タイプBはRigidbodyを生かし、現在位置・速度・回転を保持して引きずる。
                target.Body.isKinematic = false;
                target.Body.useGravity = targetGravityWas;
            }
            else
            {
                // タイプAは従来どおり、決めたレール上を通すため物理を止める。
                target.Body.linearVelocity = Vector3.zero;
                target.Body.angularVelocity = Vector3.zero;
                target.Body.useGravity = false;
                target.Body.isKinematic = true;
            }
        }

        if (distanceBasedPullActive)
        {
            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            return;
        }

        if (style == ThrowStyle.TwoButtons)
        {
            BeginTwoButtons();
            return;
        }

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(true);
            ApplyZonesToUI();
        }
    }

    /// <summary>
    /// HookController から呼ばれる。大型物件はその場で待ち、
    /// プレイヤーがもう一度左入力を押した時だけ <see cref="IHookPullable"/> を呼ぶ。
    /// </summary>
    public void BeginPullable(IHookPullable pullable)
    {
        distanceBasedPullActive = false;
        target = null;
        anchorTarget = null;
        pullableTarget = pullable;
        active = pullable != null;
        timer = 0f;
        armDelay = 0.12f;
        stage = TwoButtonStage.AimPull;

        // 直前に重い物（特殊デブリ）を引きずっていても、大型物件のゲージは往復させる
        heavy = false;
        heavyWeight = 1f;

        if (active)
        {
            StartBar();
        }
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        if (pullableTarget != null)
        {
            UpdatePullableTarget();
            return;
        }

        // 途中で物資が消えた（爆発した・ポケットに入った）場合は、物理をいじらずに終わる
        if (target == null || target.IsVanished)
        {
            AbandonPull();
            return;
        }

        if (GamePause.BlocksInput)
        {
            return;
        }

        if (anchorTarget != null)
        {
            // プレイヤーがアンカー位置まで着くまで、フックと糸をつないだままにする。
            if (anchorTarget.PullPlayer(pullingPlayer))
            {
                EndPull();
            }
            return;
        }

        // スタンさせられたら引っ張りは続けられない。ミス扱いで終了する
        if (hook != null && hook.IsStunned)
        {
            FinishAsMiss("スタンしたため");
            return;
        }

        if (distanceBasedPullActive)
        {
            UpdateDistanceBasedPull();
            return;
        }

        timer += Time.deltaTime;
        if (armDelay > 0f)
        {
            armDelay -= Time.deltaTime;
        }

        if (style == ThrowStyle.TwoButtons)
        {
            UpdateTwoButtons();
            return;
        }

        float t =Mathf.Clamp01(timer / Mathf.Max(0.1f, skillCheckDuration));

        UpdateReelPosition(t);

        if (hook != null && hook.UI != null)
        {
            hook.UI.SetMarker(t);
        }

        if (armDelay <= 0f && input.AttackPressed(attackAction))
        {
            Resolve(t);
            return;
        }

        // ゲージが1往復して通り過ぎたらミス（仕様改善2）
        if (timer >= skillCheckDuration)
        {
            FinishAsMiss("ゲージが通り過ぎた");
        }
    }

    private void FixedUpdate()
    {
        if (!active || target == null || target.IsVanished || anchorTarget != null ||
            !distanceBasedPullActive || GamePause.BlocksInput)
        {
            return;
        }

        ApplyDistanceBasedPullForces();
    }

    /// <summary>
    /// タイプBの引き寄せ入力を処理する。
    /// 入力の判定、距離から強さを決める計算、FixedUpdate内の物理的な引き寄せを分けている。
    /// </summary>
    private void UpdateDistanceBasedPull()
    {
        distancePullElapsed += Time.deltaTime;

        float distanceRatio = GetDistanceRatio();
        if (IsDistancePullInputPressed())
        {
            FinishDistanceBasedPull(distanceRatio, false);
            return;
        }

        // 目の前まで引ききったら、再発動できなかった扱いで弱く後ろへ落とす。
        if (distancePullElapsed >= skillCheckDuration || distanceRatio <= 0.03f)
        {
            FinishDistanceBasedPull(distanceRatio, true);
        }
    }

    /// <summary>現在のプレイヤー位置に向けて、引力と重力補正を物理ステップごとに加える。</summary>
    private void ApplyDistanceBasedPullForces()
    {
        Rigidbody body = target.Body;
        if (body.isKinematic)
        {
            return;
        }

        Vector3 pullPoint = hook.PlayerRoot.position + Vector3.up * 0.45f;
        Vector3 toPlayer = pullPoint - body.worldCenterOfMass;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            body.AddForce(toPlayer.normalized * pullForceB, ForceMode.Acceleration);
        }

        if (targetGravityWas && body.useGravity && Physics.gravity.y < 0f && dragGravityScaleB < 1f)
        {
            float upwardAcceleration = -Physics.gravity.y * (1f - dragGravityScaleB);
            body.AddForce(Vector3.up * upwardAcceleration, ForceMode.Acceleration);
        }
    }

    /// <summary>引き抜き入力。現在はフックボタン（Attack）またはLT。</summary>
    private bool IsDistancePullInputPressed()
    {
        return input.AttackPressed(attackAction) || PullPressed();
    }

    /// <summary>残り距離をフックの最大飛距離に対する割合（0〜1）で返す。</summary>
    private float GetDistanceRatio()
    {
        if (hook == null || hook.MaxRange <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(Vector3.Distance(target.transform.position, hook.PlayerRoot.position) / hook.MaxRange);
    }

    /// <summary>距離割合から従来のクリティカル力に対する倍率を計算する。</summary>
    private static float CalculateDistancePullStrength(float distanceRatio)
    {
        if (distanceRatio <= 0.15f)
        {
            return 1f;
        }
        if (distanceRatio <= 0.5f)
        {
            return Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(0.15f, 0.5f, distanceRatio));
        }
        return Mathf.Lerp(0.7f, 0.05f, Mathf.InverseLerp(0.5f, 1f, distanceRatio));
    }

    /// <summary>距離判定後の引き抜き。強さ計算と飛ばす方向を物理実行から分ける。</summary>
    private void FinishDistanceBasedPull(float distanceRatio, bool missed)
    {
        Vector3 direction = ResolveDistancePullDirection(missed);
        float strength = missed ? 0.1f : CalculateDistancePullStrength(distanceRatio);
        float force = throwForcePerfect * strength;

        Debug.Log($"距離ベース引き抜き：残り距離 {distanceRatio:P0} / 力 {force:0.0}" +
                  (missed ? "（引ききりミス）" : ""));
        LaunchDistanceBasedThrow(direction, force);
    }

    /// <summary>成功時はプレイヤー頭上へ斜めに抜け、時間切れ時は後方へ転がす方向を作る。</summary>
    private Vector3 ResolveDistancePullDirection(bool missed)
    {
        Vector3 forward = hook.CurrentAimDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = hook.PlayerRoot.forward;
            forward.y = 0f;
        }
        forward.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        if (missed)
        {
            return (-forward + side * 0.35f).normalized;
        }

        Vector3 overheadPoint = hook.PlayerRoot.position + Vector3.up * throwAimHeightB + side * throwSideOffsetB;
        Vector3 towardOverhead = overheadPoint - target.Body.worldCenterOfMass;
        return towardOverhead.sqrMagnitude > 0.0001f ? towardOverhead.normalized : Vector3.up;
    }

    /// <summary>
    /// タイプB専用の投げ。タイプAのLaunchとは分け、接近時も頭上へ向かうImpulseを加える。
    /// 引きずり中に得た速度も残して、物理的な慣性を保つ。
    /// </summary>
    private void LaunchDistanceBasedThrow(Vector3 direction, float force)
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        FishingNetSupply netSupply = GetNetSupply();
        if (netSupply != null)
        {
            netSupply.RequestThrow(direction, force, 0f, hook.LocalPlayerIndex,
                preserveVelocity: true,
                inheritedVelocity: body.linearVelocity,
                inheritedAngularVelocity: body.angularVelocity);
        }
        else
        {
            body.AddForce(direction * force, ForceMode.Impulse);
        }

        EndPull();
    }

    private void UpdatePullableTarget()
    {
        Component component = pullableTarget.HookComponent;
        if (component == null || !pullableTarget.IsHooked)
        {
            AbandonPull();
            return;
        }

        if (GamePause.BlocksInput)
        {
            return;
        }

        if (hook != null && hook.IsStunned)
        {
            pullableTarget.SetHooked(false);
            AbandonPull();
            return;
        }

        if (armDelay > 0f)
        {
            armDelay -= Time.deltaTime;
        }

        TickBar();
        if (armDelay <= 0f && PullPressed())
        {
            FinishPullable(Accuracy(BarPosition()));
        }
    }

    private void FinishPullable(float accuracy)
    {
        Vector3 playerPosition = hook != null ? hook.PlayerRoot.position : transform.position;
        CompletePullable(pullableTarget, playerPosition, accuracy);
        pullableTarget = null;
        active = false;

        if (hook != null)
        {
            if (hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            hook.NotifyThrowFinished();
        }
    }

    /// <summary>拉扯ゲージが確定した時だけ、対象の介面を呼ぶ。</summary>
    public static void CompletePullable(IHookPullable pullable, Vector3 playerPosition, float accuracy)
    {
        pullable?.CompletePull(new HookPullContext(playerPosition, accuracy));
    }

    // ------------------------------------------------------------
    // 引き寄せの軌道
    // ------------------------------------------------------------

    /// <summary>
    /// ゲージの進み具合 <paramref name="t"/>（0〜1）から、物資の位置を決める。
    ///
    /// 0〜0.5 … 釣り上げた場所から、プレイヤーの真上へ弧を描いて上がってくる
    /// 0.5    … **プレイヤーの真上**
    /// 0.5〜1 … そのまま後方へ抜けていく
    ///
    /// プレイヤーは動くので、行き先は毎フレーム取り直している。
    /// </summary>
    /// <returns>
    /// 宇宙ごみ式（TwoButtons）で、**途中で壁に当たって止まったら false**。
    /// 釣り式では壁を調べない（これまでどおり。釣りの動きは変えない）ので、常に true。
    /// </returns>
    private bool UpdateReelPosition(float t)
    {
        Vector3 root = hook.PlayerRoot.position;
        Vector3 aimDirection = hook.CurrentAimDirection;

        Vector3 overhead = root + Vector3.up * overheadHeight;
        Vector3 behind = root - aimDirection * passDistance + Vector3.up * (overheadHeight * 0.45f);

        Vector3 position;

        if (t <= 0.5f)
        {
            float u = Mathf.Clamp01(t / 0.5f);
            position = Vector3.Lerp(reelStart, overhead, Mathf.SmoothStep(0f, 1f, u));
            position.y += Mathf.Sin(u * Mathf.PI) * arcLift;
        }
        else
        {
            float u = Mathf.Clamp01((t - 0.5f) / 0.5f);
            position = Vector3.Lerp(overhead, behind, u);
            position.y += Mathf.Sin(u * Mathf.PI) * (arcLift * 0.4f);
        }

        bool reached = true;
        if (style == ThrowStyle.TwoButtons)
        {
            reached = MoveHeldTo(position);
        }
        else
        {
            target.Body.position = position;
            target.transform.position = position;
        }

        if (reelSpinSpeed != 0f)
        {
            target.transform.Rotate(Vector3.right, reelSpinSpeed * Time.deltaTime, Space.Self);
        }

        return reached;
    }

    // ------------------------------------------------------------
    // 壁に当たる（宇宙ごみ式。2026/10/6・大槻さん）
    // ------------------------------------------------------------

    /// <summary>壁の手前で止めるときに、壁から空けておくすき間（m）。</summary>
    private const float WallSkin = 0.05f;

    /// <summary>
    /// **持っている物を <paramref name="desired"/> へ動かす。途中に壁があれば、壁の手前で止める。**
    /// 止まったら false。
    ///
    /// 引き寄せの間は物理を止めて（Is Kinematic）決めた道筋の上へ置いているので、
    /// そのままだと**壁を突き抜けてしまう**。動かす前に、物の形のまま通り道を調べる（SweepTest）。
    ///
    /// 壁とみなさない物：床（上向きの面）・プレイヤー・ほかの物資。
    /// 床まで壁とみなすと、床の上を引きずるだけで止まってしまうため。
    /// </summary>
    private bool MoveHeldTo(Vector3 desired)
    {
        Rigidbody body = target.Body;
        Vector3 from = body.position;
        Vector3 delta = desired - from;
        float distance = delta.magnitude;

        if (distance < 0.0001f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        float nearest = distance;
        bool blocked = false;

        foreach (RaycastHit hit in body.SweepTestAll(direction, distance, QueryTriggerInteraction.Ignore))
        {
            if (!IsWall(hit))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                blocked = true;
            }
        }

        Vector3 position = from + direction * Mathf.Max(0f, nearest - WallSkin);
        body.position = position;
        target.transform.position = position;

        return !blocked;
    }

    /// <summary>当たった物が「壁」か（床・プレイヤー・ほかの物資・自分の持ち物は壁ではない）。</summary>
    private bool IsWall(RaycastHit hit)
    {
        Collider other = hit.collider;
        if (other == null || other.isTrigger)
        {
            return false;
        }

        // 上向きの面（床・台の上）は壁ではない
        if (hit.normal.y > 0.7f)
        {
            return false;
        }

        if (other.attachedRigidbody != null && other.attachedRigidbody == target.Body)
        {
            return false;
        }

        if (other.GetComponentInParent<CharacterController>() != null ||
            other.GetComponentInParent<HookableObject>() != null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// **壁に当たって持ってこられなかった。フックを外して、物をその場に落とす。**
    /// 引っ張る途中・投げる前に頭上へ持ち上げる途中・頭上で持っている間に、壁に当たったとき。
    /// </summary>
    private void FinishAsBlocked()
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        Debug.Log("壁に当たって持ってこられなかったので、フックを外しました");

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestRelease(Vector3.zero, 0f, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        EndPull();
    }

    // ------------------------------------------------------------
    // 宇宙ごみ式（Style が TwoButtons のとき）
    // ------------------------------------------------------------

    /// <summary>
    /// 引っ掛けた直後。**どちらのボタンで撃ったかで流れが分かれる。**
    /// 左＝くっついた場所で止まってゲージ。右＝まず頭上へ持ち上げる。
    /// </summary>
    private void BeginTwoButtons()
    {
        // **重い物は投げられない。** どちらで撃っていても、その場で止まってゲージを出し、引きずるだけ
        heavy = HeavyHookable.TryGetWeight(target, out heavyWeight);

        if (heavy)
        {
            stage = TwoButtonStage.AimPull;
            StartBar();
            return;
        }

        if (nextIsThrow)
        {
            stage = TwoButtonStage.Lifting;
            liftTimer = 0f;

            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
        }
        else
        {
            stage = TwoButtonStage.AimPull;
            StartBar();
        }
    }

    /// <summary>ゲージを左端から動かし始める。枠は真ん中に1つだけ。</summary>
    private void StartBar()
    {
        barTimer = 0f;
        armDelay = Mathf.Max(armDelay, 0.12f);

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(true);

            // 真ん中に1つ。見た目は「外側＝通常・内側＝強い・中心＝最も強い」の3段だが、
            // 実際の強さは**真ん中からの近さでなめらかに**変わる（段ではない）
            hook.UI.SetZoneCount(1);
            hook.UI.SetZone(0, 0.5f, 0.5f, 0.28f, 0.07f);
            hook.UI.SetMarker(0f);
        }
    }

    /// <summary>
    /// **ゲージのマーカーの位置（0〜1）。端まで行ったら跳ね返って往復する。**
    /// </summary>
    private float BarPosition()
    {
        if (heavy)
        {
            // 重い物は**往復しない**。左端から右端へ1回だけ進み、重いほど速い
            float speed = heavyBarSweepsPerSecond * (1f + heavyBarSpeedUpPerWeight * Mathf.Max(0f, heavyWeight - 1f));
            return Mathf.Clamp01(barTimer * speed);
        }

        return Mathf.PingPong(barTimer * barSweepsPerSecond, 1f);
    }

    /// <summary>
    /// **真ん中にどれだけ近いか（0〜1）。** 真ん中ちょうどで 1、端で 0。
    /// </summary>
    private static float Accuracy(float barPosition)
    {
        return 1f - Mathf.Clamp01(Mathf.Abs(barPosition - 0.5f) / 0.5f);
    }

    private void UpdateTwoButtons()
    {
        switch (stage)
        {
            case TwoButtonStage.AimPull when heavy:
                TickBar();

                if (armDelay <= 0f && PullPressed())
                {
                    // 引きずる距離 ＝ 引っ張る強さ ÷ 重さ ×（1 − 真ん中からのずれ）。真ん中に近いほど遠くまで
                    StartHeavyDrag(heavyDragStrength * Accuracy(BarPosition()));
                }
                else if (BarPosition() >= 1f)
                {
                    // 右端まで押さなかった。**少しだけ引っ張って終わる**
                    StartHeavyDrag(0f);
                }
                break;

            case TwoButtonStage.HeavyDrag:
                liftTimer += Time.deltaTime;
                float drag = Mathf.Clamp01(liftTimer / heavyDragSeconds);

                Vector3 dragged = Vector3.Lerp(dragFrom, dragTo, Mathf.SmoothStep(0f, 1f, drag));

                // 壁に当たったら、そこで引きずるのをやめる（壁の手前で止まる）
                if (!MoveHeldTo(dragged) || drag >= 1f)
                {
                    FinishHeavyDrag();
                }
                break;

            case TwoButtonStage.AimPull:
                TickBar();

                if (armDelay <= 0f && PullPressed())
                {
                    // 強さを覚えて、釣り式と同じく**頭上を越えて後ろへ抜ける**動きを始める
                    pullAccuracy = Accuracy(BarPosition());
                    stage = TwoButtonStage.PullTravel;
                    liftTimer = 0f;

                    if (hook != null && hook.UI != null)
                    {
                        hook.UI.ShowTiming(false);
                    }
                }
                break;

            case TwoButtonStage.PullTravel:
                liftTimer += Time.deltaTime;
                float travel = Mathf.Clamp01(liftTimer / pullTravelSeconds);

                // 釣り式と同じ軌道（ゲージ 0〜1 ぶん）：弧を描いて頭上を通り、後ろへ抜ける。
                // 途中で壁に当たったら、フックを外してその場に落とす
                if (!UpdateReelPosition(travel))
                {
                    FinishAsBlocked();
                    break;
                }

                if (travel >= 1f)
                {
                    FinishAsPull(pullAccuracy);
                }
                break;

            case TwoButtonStage.Lifting:
                liftTimer += Time.deltaTime;
                float u = Mathf.Clamp01(liftTimer / liftSeconds);

                // 釣り式の前半と同じ、弧を描いて頭上へ上がる動き（ゲージ 0〜0.5 の部分）。
                // **壁に当たって持ってこられなかったら、フックを外す**
                if (!UpdateReelPosition(u * 0.5f))
                {
                    FinishAsBlocked();
                    break;
                }

                if (u >= 1f)
                {
                    stage = TwoButtonStage.AimThrow;
                    StartBar();
                }
                break;

            case TwoButtonStage.AimThrow:
                // 頭上で止めておく（プレイヤーが動いたらついてくる）。
                // 持ったまま壁の向こうへ歩いて、物が壁に引っかかったら外す
                if (!UpdateReelPosition(0.5f))
                {
                    FinishAsBlocked();
                    break;
                }
                TickBar();

                if (armDelay <= 0f && ThrowPressed())
                {
                    FinishAsTwoButtonThrow(Accuracy(BarPosition()));
                }
                break;
        }
    }

    private void TickBar()
    {
        barTimer += Time.deltaTime;

        if (hook != null && hook.UI != null)
        {
            hook.UI.SetMarker(BarPosition());
        }
    }

    /// <summary>「引っ張る」ボタンが押された瞬間か。左クリックか、コントローラーの LT。</summary>
    public bool PullPressed() => input.PullPressed;

    /// <summary>「引っ張る」ボタンが離された瞬間か。</summary>
    public bool PullReleased() => input.PullReleased;

    /// <summary>「投げる」ボタンが離された瞬間か。</summary>
    public bool ThrowReleased() => input.ThrowReleased;

    /// <summary>「投げる」ボタンが押された瞬間か。右クリックか、コントローラーの RT。</summary>
    public bool ThrowPressed() => input.ThrowPressed;

    /// <summary>
    /// **引っ張る（の仕上げ）。** 釣り式と同じ軌道で頭上を越え、後ろへ抜けたところで手を離し、
    /// **そのまま後ろへ飛ばす**（プレイヤー→物資の向き＝物資が来た方の反対）。
    /// 真ん中に近いほど強く、強いほど後ろの遠くまで飛んでいく。
    /// </summary>
    private void FinishAsPull(float accuracy)
    {
        Vector3 away = target.transform.position - hook.PlayerRoot.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = -hook.CurrentAimDirection;
        }

        float force = Mathf.Lerp(pullForceMin, pullForceMax, accuracy);

        Debug.Log($"引っ張った：真ん中への近さ {accuracy:0.00}（力 {force:0.0}）");

        Launch(away.normalized, force, pullLift);
    }

    /// <summary>
    /// **投げる。** 投げた瞬間にプレイヤーが向いている方向へ飛ばす（Throw Toward Facing が OFF なら、物資がくっついていた場所へ投げ返す）。
    /// 真ん中に近いほど強い。
    /// </summary>
    private void FinishAsTwoButtonThrow(float accuracy)
    {
        Vector3 direction;

        if (throwTowardFacing)
        {
            // **投げた瞬間にプレイヤーが向いている方向**（マウス・右スティックで向けた方向）へ投げる（2026/10/1・大槻さん）
            direction = hook.CurrentAimDirection;
        }
        else
        {
            // 前の動き：物資がくっついていた場所の方向へ投げ返す
            direction = reelStart - hook.PlayerRoot.position;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = hook.PlayerRoot.forward;
            direction.y = 0f;
        }

        float force = Mathf.Lerp(twoButtonThrowForceMin, twoButtonThrowForceMax, accuracy);

        Debug.Log($"投げた：真ん中への近さ {accuracy:0.00}（力 {force:0.0}）／{(throwTowardFacing ? "向いている方向" : "くっついていた場所の方向")}へ");

        Launch(direction.normalized, force, throwLift);
    }

    /// <summary>
    /// **重い物を引きずり始める。** プレイヤーへ向かって、<paramref name="strength"/> ÷ 重さ（メートル）だけ、
    /// 地面の高さのまま動かす。頭上は越えない。
    /// いちばん短くても <see cref="heavyDragMinDistance"/> ÷ 重さ は動き、プレイヤーの手前 <see cref="heavyDragStopDistance"/> で止まる。
    /// </summary>
    private void StartHeavyDrag(float strength)
    {
        float weight = Mathf.Max(0.1f, heavyWeight);
        float distance = Mathf.Max(strength, heavyDragMinDistance) / weight;

        dragFrom = target.transform.position;

        Vector3 toPlayer = hook.PlayerRoot.position - dragFrom;
        toPlayer.y = 0f;

        // プレイヤーに重ならない・追い越さないように、手前で止める
        float room = Mathf.Max(0f, toPlayer.magnitude - heavyDragStopDistance);
        distance = Mathf.Min(distance, room);

        dragTo = dragFrom + (toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.zero) * distance;

        Debug.Log($"重い物を引きずった：重さ {heavyWeight:0.#}／{distance:0.0}m");

        stage = TwoButtonStage.HeavyDrag;
        liftTimer = 0f;

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }
    }

    /// <summary>引きずり終わった。その場で手を離す（飛ばさない）。</summary>
    private void FinishHeavyDrag()
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestRelease(Vector3.zero, 0f, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        EndPull();
    }

    /// <summary>
    /// 物資に力を加えて手を離す。**オンラインではホストが力を加える**（どこへ飛んだかを全員でそろえるため）。
    /// </summary>
    private void Launch(Vector3 direction, float force, float lift)
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        // **頭上から投げるので、投げた直後は自分の体に当たらないようにする**（頭にぶつかって跳ねるのを防ぐ。2026/9/30）
        ThrowPassThrough.Apply(target.gameObject, hook.PlayerRoot.GetComponent<CharacterController>());

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestThrow(direction, force, lift, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * force + Vector3.up * lift, ForceMode.Impulse);
        }

        EndPull();
    }

    /// <summary>宇宙ごみ式のとき、いま押せるボタンを案内する（ゲージの少し上）。</summary>
    private void OnGUI()
    {
        if (!active || style != ThrowStyle.TwoButtons || stage == TwoButtonStage.Lifting || stage == TwoButtonStage.PullTravel ||
            stage == TwoButtonStage.HeavyDrag || GamePause.IsPaused)
        {
            return;
        }

        string text = heavy
            ? $"重い！（重さ {heavyWeight:0.#}）　左クリック（LT）：引きずる　真ん中ほど遠くまで"
            : stage == TwoButtonStage.AimPull
                ? "左クリック（LT）：引っ張る　真ん中ほど強い"
                : "右クリック（RT）：向いている方向へ投げる　真ん中ほど強い";

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f) * 22f),
            fontStyle = FontStyle.Bold
        };

        Rect rect = new Rect(0f, Screen.height * 0.70f, Screen.width, labelStyle.fontSize * 1.6f);

        Color saved = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, labelStyle);
        GUI.color = Color.white;
        GUI.Label(rect, text, labelStyle);
        GUI.color = saved;
    }

    // ------------------------------------------------------------
    // スキルチェックの判定
    // ------------------------------------------------------------

    /// <summary>押された瞬間のゲージ位置を、どの枠に入ったかで判定する。</summary>
    private void Resolve(float t)
    {
        if (skillCheckZones != null)
        {
            foreach (SkillCheckZone zone in skillCheckZones)
            {
                if (zone == null)
                {
                    continue;
                }

                float distance = Mathf.Abs(t - zone.center);

                if (distance <= zone.perfectWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Perfect);
                    return;
                }
                if (distance <= zone.goodWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Good);
                    return;
                }
                if (distance <= zone.hitWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Normal);
                    return;
                }
            }
        }

        FinishAsMiss("枠の外で押した");
    }

    private void FinishAsThrow(SkillCheckZone zone, ThrowTier tier)
    {
        Vector3 direction = ResolveDirection(zone.direction, tier);
        float force = ForceOf(tier);

        Rigidbody body = target.Body;
        RestorePhysics(body);

        Debug.Log($"スキルチェック成功：{zone.label} / {tier} / {zone.direction} へ（力 {force}）");

        // **オンラインでは、力を加えるのはホスト。**
        // 手元で加えると、どこへ飛んだかが人によって変わってしまう
        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestThrow(direction, force, throwLift, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * force + Vector3.up * throwLift, ForceMode.Impulse);
        }

        EndPull();
    }

    private void FinishAsMiss(string reason)
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        // ほんの少しだけプレイヤー側へ引き寄せて終わる（仕様改善2）
        Vector3 toPlayer = hook.PlayerRoot.position - target.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
        {
            toPlayer = -hook.CurrentAimDirection;
        }

        Debug.Log($"スキルチェック失敗（{reason}）：少しだけ引き寄せて終了");

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestRelease(toPlayer.normalized, missPullForce, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(toPlayer.normalized * missPullForce, ForceMode.Impulse);
        }

        EndPull();
    }

    /// <summary>
    /// オンラインで、引っ掛けが**ホストに認められなかった**とき。
    /// 物理には触らず（持ち主はホストのまま）、引っ張りだけをやめる。
    /// </summary>
    public void AbandonBecauseLost()
    {
        if (!active)
        {
            return;
        }

        SetAnchorGravitySuppressed(false);
        target = null;
        anchorTarget = null;
        pullingPlayer = null;
        pullingPlayerMovement = null;
        active = false;

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }
    }

    /// <summary>
    /// **つかんでいる物資を、力を加えずにその場で離す。** <see cref="HookController.ResetHook"/> から呼ばれる。
    /// 出てくる場所へ戻ったとき・シーンが切り替わったときに、つかんだままにならないようにするため（2026/9/22）。
    /// </summary>
    public void ForceRelease()
    {
        if (!active)
        {
            return;
        }

        if (pullableTarget != null)
        {
            pullableTarget.SetHooked(false);
            pullableTarget = null;
            active = false;

            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            return;
        }

        // **アンカーは物資を引き寄せていない**（フックを固定して、プレイヤーのほうが寄っていく）ので、
        // 物理には触らずにやめるだけでよい（2026/9/24・アンカーとの合流時に追加）
        if (anchorTarget != null)
        {
            SetAnchorGravitySuppressed(false);
            anchorTarget = null;
            target = null;
            active = false;
            pullingPlayer = null;
            pullingPlayerMovement = null;

            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }

            return;
        }

        if (target != null && !target.IsVanished)
        {
            FishingNetSupply netSupply = GetNetSupply();

            // 物理を戻すのは、いまの持ち主のときだけ（持ち主でないPCは、ホストから配られる位置に従う）
            if (netSupply == null || !netSupply.IsSpawned || netSupply.IsOwner)
            {
                RestorePhysics(target.Body);
            }

            if (netSupply != null)
            {
                netSupply.RequestRelease(Vector3.zero, 0f, hook.LocalPlayerIndex);
            }

            target.SetHooked(false);
        }

        target = null;
        active = false;

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }
    }

    /// <summary>
    /// いま引っ張っている物資のオンライン部品。
    /// **オンラインでないとき、または部品が無いときは null**（そのときは手元で処理する）。
    /// </summary>
    private FishingNetSupply GetNetSupply()
    {
        if (hook == null || !hook.IsOnline || target == null)
        {
            return null;
        }

        return target.GetComponent<FishingNetSupply>();
    }

    /// <summary>引き寄せ中に物資が消えた場合。物理には触らず、フックだけ戻す。</summary>
    private void AbandonPull()
    {
        // **オンラインでは、ホストにも「離した」と知らせる**（2026/10/6）。
        // このPCだけが「消えた」と判断した場合（爆発の巻き込みを、ホストと少し違う位置で判断した など）、
        // ホストではまだ消えておらず「この人が引っ掛け中」のまま残る。そうなると、その物は**誰もつかめなくなる。**
        // ホストで本当に消えていれば、この知らせは届かないか、引っ掛け中でないので何も起きない
        if (target != null && anchorTarget == null)
        {
            FishingNetSupply netSupply = GetNetSupply();
            if (netSupply != null && netSupply.IsSpawned)
            {
                netSupply.RequestRelease(Vector3.zero, 0f, hook.LocalPlayerIndex);
            }
        }

        if (pullableTarget != null)
        {
            pullableTarget.SetHooked(false);
            pullableTarget = null;
        }
        SetAnchorGravitySuppressed(false);
        target = null;
        anchorTarget = null;
        pullingPlayer = null;
        pullingPlayerMovement = null;
        active = false;
        distanceBasedPullActive = false;

        if (hook != null)
        {
            if (hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            hook.NotifyThrowFinished();
        }
    }

    /// <summary>爆風で物資を引き離す。投げ・ミスの通信や力を追加せず、糸とゲージを戻す。</summary>
    public static void ReleaseTargetForExplosion(HookableObject item)
    {
        foreach (ThrowController puller in FindObjectsByType<ThrowController>(FindObjectsSortMode.None))
        {
            if (!puller.active || puller.target != item)
            {
                continue;
            }

            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            // 持ち主を失ったPCでは物理を再開しない（ホストが吹き飛ばす）。
            if (netSupply == null || !netSupply.IsSpawned || netSupply.IsOwner)
            {
                puller.RestorePhysics(item.Body);
            }
            puller.EndPull();
        }
        item.SetHooked(false);
    }

    private void EndPull()
    {
        SetAnchorGravitySuppressed(false);
        target.SetHooked(false);
        target = null;
        anchorTarget = null;
        pullingPlayer = null;
        pullingPlayerMovement = null;
        active = false;
        distanceBasedPullActive = false;
        heavy = false;
        heavyWeight = 1f;

        if (hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }

        hook.NotifyThrowFinished();
    }

    private void SetAnchorGravitySuppressed(bool suppressed)
    {
        if (pullingPlayerMovement != null)
        {
            pullingPlayerMovement.SetAnchorPulling(suppressed);
        }
    }

    /// <summary>引き寄せのために止めていた物理を元に戻す。</summary>
    private void RestorePhysics(Rigidbody body)
    {
        body.isKinematic = targetKinematicWas;
        body.useGravity = targetGravityWas;
    }

    private float ForceOf(ThrowTier tier)
    {
        switch (tier)
        {
            case ThrowTier.Perfect: return throwForcePerfect;
            case ThrowTier.Good: return throwForceGood;
            default: return throwForceNormal;
        }
    }

    /// <summary>
    /// 投げる方向を決める。
    ///
    /// **基準は「投げた瞬間にプレイヤーが向いている方向」**（仕様改善3）。
    /// 釣り上げたときの向きを使っていると、引っ張っている間に振り向いたときに
    /// 思っていた方向と逆へ飛んでしまうため。
    ///
    /// いまは <paramref name="tier"/> は方向に影響しないが、
    /// 「タイミングの良さで方向も変える」を足すときに、ここで分岐できるよう受け取っている。
    /// </summary>
    private Vector3 ResolveDirection(ThrowDirectionMode mode, ThrowTier tier)
    {
        Vector3 facing = hook.CurrentAimDirection;
        Vector3 direction;

        switch (mode)
        {
            case ThrowDirectionMode.Forward:
                direction = facing;
                break;
            case ThrowDirectionMode.WorldCustom:
                direction = customDirection.sqrMagnitude > 0.001f
                    ? customDirection.normalized
                    : -facing;
                break;
            case ThrowDirectionMode.Backward:
            default:
                direction = -facing;
                break;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = -facing;
        }
        return direction.normalized;
    }

    /// <summary>枠の位置と広さを UI に伝える。開始時に1回だけ呼ぶ。</summary>
    private void ApplyZonesToUI()
    {
        if (skillCheckZones == null)
        {
            return;
        }

        hook.UI.SetZoneCount(skillCheckZones.Length);

        for (int i = 0; i < skillCheckZones.Length; i++)
        {
            SkillCheckZone zone = skillCheckZones[i];
            if (zone == null)
            {
                continue;
            }
            hook.UI.SetZone(i, zone.center, zone.hitWindow, zone.goodWindow, zone.perfectWindow);
        }
    }
}
