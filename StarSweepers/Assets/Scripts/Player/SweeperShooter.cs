using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **プレイヤー（本実装）の「狙う → 光線を伸ばす → 当たったら巻き取りへ」。**（2026/10/27・小野田さんの仕様）
///
/// | 操作 | どうなる |
/// | --- | --- |
/// | RT／左クリックを押す | 狙い始める。リングの縁から、届く長さの矢印の帯（<see cref="SweeperAimArea"/>）が出る |
/// | 押している間 | 帯が最短から最長まで伸びていく（Charge Seconds で最長）。**歩ける**。体は狙う方向を向く |
/// | 離す | 光線がその長さまで伸びる（<see cref="extendSpeed"/>）。**ここから投げ終わるまで歩けない** |
/// | 光線が物資に当たる | 自動で巻き取りへ（<see cref="SweeperReel"/>）。巻き取り中に押すと後ろへ投げる |
/// | 何にも当たらない・壁に当たる | 光線が戻ってくる。戻りきったら歩ける |
///
/// 光線の見た目は液体のビーム（<see cref="LiquidBeam"/>）。何もつかんでいない間は薄いチームの色、つかんだら濃くなる。
/// 撃った瞬間に <see cref="SweeperAnimation.PlayShoot"/>（A_SHOOT → A_AIM_01）を呼ぶ。
/// </summary>
public class SweeperShooter : MonoBehaviour
{
    /// <summary>光線の段階。オンラインでは、ほかの人の画面にこれを配って同じ見た目を出す（<see cref="SweeperNetSync"/>）。</summary>
    public enum ShotPhase
    {
        Idle,       // 何もしていない
        Charging,   // 押して狙っている（歩ける）
        Extending,  // 光線が伸びている
        Retracting, // 何にも当たらず、光線が戻っている
        Reeling     // 物資を巻き取っている
    }

    [Header("参照（空なら同じ体から探す）")]
    [SerializeField] private SweeperAim aim;
    [SerializeField] private SweeperMover mover;
    [SerializeField] private SweeperReel reel;
    [SerializeField] private SweeperAimArea aimArea;
    [SerializeField] private SweeperRing ring;
    [SerializeField] private SweeperAnimation animationPlayer;

    [Tooltip("Assets/InputSystem_Actions を入れる（Attack＝RT・左クリックを使う）")]
    [SerializeField] private InputActionAsset inputActions;

    [Tooltip("光線の見た目（Assets/Prefabs/Effect/LiquidBeam.prefab）。空でも動く")]
    [SerializeField] private LiquidBeam beamPrefab;

    [Header("伸ばせる長さ（プレイヤーの中心から m）")]
    [Tooltip("押した瞬間の長さ")]
    [Min(0.5f)]
    [SerializeField] private float minReach = 3f;

    [Tooltip("いちばん長く伸ばせる長さ")]
    [Min(0.5f)]
    [SerializeField] private float maxReach = 12f;

    [Tooltip("押しっぱなしで、いちばん長くなるまでの秒数")]
    [Min(0.01f)]
    [SerializeField] private float chargeSeconds = 1f;

    [Header("光線の速さ（m/秒）")]
    [Tooltip("光線が伸びる速さ")]
    [Min(0.1f)]
    [SerializeField] private float extendSpeed = 30f;

    [Tooltip("何にも当たらなかったとき、光線が戻る速さ")]
    [Min(0.1f)]
    [SerializeField] private float retractSpeed = 40f;

    [Header("当たり判定")]
    [Tooltip("光線の先の、当たりを見る球の半径（m）。大きいほど当てやすい")]
    [Min(0.05f)]
    [SerializeField] private float hitRadius = 0.45f;

    [Tooltip("当たりを見る高さ（足元から m）。低い物資にも当たるように、腰より低めにしてある")]
    [Min(0f)]
    [SerializeField] private float castHeight = 0.6f;

    [Tooltip("光線が当たる相手のレイヤー")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("ON：引っ掛けられない物（壁など）に当たったら、そこで戻り始める。OFF：すり抜けて最後まで伸びる")]
    [SerializeField] private bool stopAtWalls = true;

    [Header("光線の見た目")]
    [Tooltip("光線が出てくる手元の高さ（足元から m）")]
    [Min(0f)]
    [SerializeField] private float handHeight = 1.0f;

    [Tooltip("光線が出てくる手元の、体の前への出っぱり（m）")]
    [Min(0f)]
    [SerializeField] private float handForward = 0.35f;

    [Tooltip("何もつかんでいない間の色の薄さ（0 でチームの色そのまま、1 で白）")]
    [Range(0f, 1f)]
    [SerializeField] private float idleWhiten = 0.45f;

    private PlayerInputSource input;
    private PlayerStun stun;
    private InputActionMap playerMap;
    private InputAction attackAction;
    private LiquidBeam beam;

    private ShotPhase phase = ShotPhase.Idle;
    private float charge;
    private float targetDistance;
    private float traveled;
    private Vector3 launchOrigin;
    private Vector3 launchDirection = Vector3.forward;
    private Color teamColor = new Color(0.3f, 0.6f, 0.95f);

    /// <summary>押して狙っている最中か。</summary>
    public bool IsCharging => phase == ShotPhase.Charging;

    /// <summary>光線が出ている（伸びている・戻っている・巻き取っている）か。この間は歩けない。</summary>
    public bool IsBeamOut => phase == ShotPhase.Extending || phase == ShotPhase.Retracting || phase == ShotPhase.Reeling;

    /// <summary>いま離したら、光線がどこまで伸びるか（プレイヤーの中心から m）。</summary>
    public float CurrentReach => Mathf.Lerp(minReach, Mathf.Max(minReach, maxReach), charge);

    /// <summary>いまの段階の名前（検証用の表示に使う）。</summary>
    public string PhaseName => phase.ToString();

    /// <summary>いまの段階。</summary>
    public ShotPhase Phase => phase;

    /// <summary>狙っている向き（狙っている間）／光線を撃った向き（撃ったあと）。</summary>
    public Vector3 ShotDirection => phase == ShotPhase.Charging && aim != null ? aim.Direction : launchDirection;

    /// <summary>光線の先の位置（巻き取り中は物資の引っ掛けた所）。</summary>
    public Vector3 BeamTip => phase == ShotPhase.Reeling && reel != null && reel.Item != null
        ? reel.Item.AnchorPoint
        : launchOrigin + launchDirection * Mathf.Max(0f, traveled);

    /// <summary>巻き取っている物資。巻き取っていなければ null。</summary>
    public HookableObject ReelItem => phase == ShotPhase.Reeling && reel != null ? reel.Item : null;

    /// <summary>
    /// **撃っている光線をしまい、巻き取りもやめる**（物資は投げずに離す）。
    /// オンラインで、出てくる場所へ戻されたときなどに呼ぶ（<see cref="SweeperNetSync"/>）。
    /// </summary>
    public void CancelShot()
    {
        if (phase == ShotPhase.Idle)
        {
            return;
        }

        reel?.Cancel();
        EndShot();
    }

    private void Awake()
    {
        if (aim == null) aim = GetComponent<SweeperAim>();
        if (mover == null) mover = GetComponent<SweeperMover>();
        if (reel == null) reel = GetComponent<SweeperReel>();
        if (aimArea == null) aimArea = GetComponentInChildren<SweeperAimArea>();
        if (ring == null) ring = GetComponent<SweeperRing>();
        if (animationPlayer == null) animationPlayer = GetComponent<SweeperAnimation>();

        stun = GetComponent<PlayerStun>();
        input = PlayerInputSource.Get(this);

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // 入力の設定は、この体専用の複製を使う（ほかの体が止めたときに、自分の入力まで止まらないように）
        inputActions = Instantiate(inputActions);
        playerMap = inputActions.FindActionMap("Player", true);
        attackAction = playerMap.FindAction("Attack", true);

        beam = CreateBeam();
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
        if (phase != ShotPhase.Idle)
        {
            reel?.Cancel();
            EndShot();
        }
    }

    /// <summary>色を変える（<see cref="SweeperPlayer"/> がチームの色を入れる）。</summary>
    public void SetColor(Color color)
    {
        teamColor = color;
        if (beam != null && phase != ShotPhase.Reeling)
        {
            beam.SetColor(IdleColor);
        }
    }

    private Color IdleColor => Color.Lerp(teamColor, Color.white, idleWhiten);

    private void Update()
    {
        bool blocked = GamePause.BlocksInput;
        bool stunned = stun != null && stun.IsStunned;
        bool pressed = !blocked && input.AttackPressed(attackAction);
        bool released = !blocked && input.AttackReleased(attackAction);

        switch (phase)
        {
            case ShotPhase.Idle:
                if (pressed && !stunned)
                {
                    BeginCharge();
                }
                break;

            case ShotPhase.Charging:
                UpdateCharge(released, stunned);
                break;

            case ShotPhase.Extending:
                UpdateExtend();
                break;

            case ShotPhase.Retracting:
                traveled -= retractSpeed * Time.deltaTime;
                if (traveled <= 0f)
                {
                    EndShot();
                }
                break;

            case ShotPhase.Reeling:
                if (pressed)
                {
                    reel.Press();
                }
                if (!reel.IsActive)
                {
                    EndShot();
                }
                break;
        }
    }

    private void LateUpdate()
    {
        if (beam == null || !IsBeamOut)
        {
            return;
        }

        beam.Follow(HandPosition, BeamTip);
    }

    // ------------------------------------------------------------
    // オンラインのほかの人の体（2026/10/28）
    // ------------------------------------------------------------

    /// <summary>ほかの人の体で、最後に見せた段階。段階が変わった瞬間だけ色やアニメーションを切り替えるために持つ。</summary>
    private ShotPhase remotePhase = ShotPhase.Idle;

    /// <summary>
    /// **ほかの人の体に、本人から届いた光線の様子をそのまま見せる。**（<see cref="SweeperNetSync"/> が毎フレーム呼ぶ）
    /// ほかの人の体ではこの部品は止まっている（操作はしない）ので、見た目だけをここで出す。
    /// </summary>
    public void ShowRemote(ShotPhase newPhase, Vector3 direction, float reach, Vector3 tip, HookableObject item)
    {
        if (newPhase != remotePhase)
        {
            OnRemotePhaseChanged(remotePhase, newPhase);
            remotePhase = newPhase;
        }

        if (newPhase == ShotPhase.Charging)
        {
            float ringRadius = ring != null ? ring.Radius : 0.5f;
            aimArea?.Show(transform.position, direction, ringRadius, reach);
            return;
        }

        aimArea?.Hide();

        if (beam != null && newPhase != ShotPhase.Idle)
        {
            beam.Follow(HandPosition, newPhase == ShotPhase.Reeling && item != null ? item.AnchorPoint : tip);
        }
    }

    private void OnRemotePhaseChanged(ShotPhase before, ShotPhase after)
    {
        switch (after)
        {
            case ShotPhase.Idle:
                beam?.Stop();
                beam?.SetColor(IdleColor);
                aimArea?.Hide();
                animationPlayer?.PlayIdle();
                break;

            case ShotPhase.Charging:
                animationPlayer?.PlayAim();
                break;

            case ShotPhase.Extending:
                beam?.SetColor(IdleColor);
                animationPlayer?.PlayShoot();
                break;

            case ShotPhase.Reeling:
                beam?.ChangeColorFromTip(teamColor);
                break;
        }
    }

    /// <summary>光線が出てくる手元の位置。</summary>
    private Vector3 HandPosition
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return transform.position + Vector3.up * handHeight + forward * handForward;
        }
    }

    private void BeginCharge()
    {
        phase = ShotPhase.Charging;
        charge = 0f;
        if (mover != null) mover.FaceAim = true;
        animationPlayer?.PlayAim();
    }

    private void UpdateCharge(bool released, bool stunned)
    {
        if (stunned)
        {
            // スタンしたら、撃たずにやめる
            aimArea?.Hide();
            EndShot();
            return;
        }

        charge = Mathf.Clamp01(charge + Time.deltaTime / chargeSeconds);

        float ringRadius = ring != null ? ring.Radius : 0.5f;
        aimArea?.Show(transform.position, aim != null ? aim.Direction : transform.forward, ringRadius, CurrentReach);

        if (released)
        {
            Fire();
        }
    }

    private void Fire()
    {
        aimArea?.Hide();

        launchDirection = aim != null ? aim.Direction : transform.forward;
        launchDirection.y = 0f;
        launchDirection = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : Vector3.forward;
        launchOrigin = transform.position + Vector3.up * castHeight;
        targetDistance = CurrentReach;
        traveled = 0f;
        phase = ShotPhase.Extending;

        // 撃った瞬間から投げ終わるまで歩けない（向きは狙いで変えられる）
        if (mover != null) mover.MoveLocked = true;

        if (beam != null)
        {
            beam.SetColor(IdleColor);
        }

        animationPlayer?.PlayShoot();
    }

    private void UpdateExtend()
    {
        float step = extendSpeed * Time.deltaTime;
        float from = traveled;
        float to = Mathf.Min(targetDistance, traveled + step);

        if (TryHit(from, to, out RaycastHit hit, out HookableObject hookable))
        {
            traveled = hit.distance + from;

            if (hookable != null)
            {
                phase = ShotPhase.Reeling;
                reel.Begin(hookable);
                beam?.ChangeColorFromTip(teamColor);
                return;
            }

            // 壁などに当たった：そこから戻る
            phase = ShotPhase.Retracting;
            return;
        }

        traveled = to;
        if (traveled >= targetDistance)
        {
            phase = ShotPhase.Retracting;
        }
    }

    /// <summary>
    /// 光線の先が <paramref name="from"/> から <paramref name="to"/> まで進む間に、何かに当たるか。
    /// 物資（まだ誰も引っ掛けていない物）に当たれば <paramref name="hookable"/> に入る。
    /// </summary>
    private bool TryHit(float from, float to, out RaycastHit nearest, out HookableObject hookable)
    {
        nearest = default;
        hookable = null;

        float distance = to - from;
        if (distance <= 0f)
        {
            return false;
        }

        Vector3 start = launchOrigin + launchDirection * from;
        RaycastHit[] hits = Physics.SphereCastAll(start, hitRadius, launchDirection, distance, hitMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            // 自分の体には当たらない。最初から重なっている物（distance 0）は、場所が分からないので物資のときだけ使う
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            HookableObject candidate = hit.collider.GetComponentInParent<HookableObject>();

            // 重いデブリは、いまは引っ張れない（壁と同じ扱い。2026/10/28）
            if (candidate != null && HeavyHookable.TryGetWeight(candidate, out _))
            {
                candidate = null;
            }

            bool usable = candidate != null && !candidate.IsHooked && !candidate.IsVanished && reel.CanHook(candidate);
            if (!usable && (!stopAtWalls || candidate != null || hit.distance <= 0f))
            {
                // 引っ掛けられない物資（誰かが引いている。オンラインではほかの人が引いている物も）はすり抜ける。壁で止まらない設定のときも同じ
                continue;
            }

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                hookable = usable ? candidate : null;
                found = true;
            }
        }

        return found;
    }

    /// <summary>光線をしまい、歩けるように戻す。</summary>
    private void EndShot()
    {
        phase = ShotPhase.Idle;
        traveled = 0f;
        charge = 0f;

        if (mover != null)
        {
            mover.MoveLocked = false;
            mover.FaceAim = false;
        }

        if (beam != null)
        {
            beam.Stop();
            beam.SetColor(IdleColor);
        }

        aimArea?.Hide();
        animationPlayer?.PlayIdle();
    }

    private LiquidBeam CreateBeam()
    {
        LiquidBeam made;
        if (beamPrefab != null)
        {
            made = Instantiate(beamPrefab, transform);
        }
        else
        {
            // プレハブが入っていないとき。マテリアルは LiquidBeam が自分で作る
            GameObject child = new GameObject("SweeperBeam");
            child.transform.SetParent(transform, false);
            made = child.AddComponent<LiquidBeam>();
        }

        made.name = "SweeperBeam";
        made.HoldUntilStopped = true;
        return made;
    }
}
