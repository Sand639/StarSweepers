using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// **新しい体（プレイヤーの本実装）を動かすボットの頭。**（2026/11/5・小野田さんの依頼）
///
/// 今の体（フック）のボット <see cref="SpaceJunkBot"/> と同じく、**物を直接動かさず、見えないコントローラー
/// （<see cref="VirtualPlayerInput"/>）を操作するだけ**。歩ける床の地図（<see cref="SpaceJunkBotNavMesh"/>）も同じものを使う。
/// 違うのは「どこに立って撃つか」で、新しい体の投げ方に合わせてある。
///
/// ## 新しい体の投げ方と、立つ場所
///
/// 新しい体は、物資を手前へ引き寄せている途中で押すと、**体の真後ろの、だいたい決まった距離**へ落ちる。
/// 押すタイミングで距離は変わらない。そこでボットは、**素材 → 自分 → ゴール が一直線に並び、自分からゴールまでがちょうどその距離になる場所**に立つ。
///
/// | 素材とゴールの距離 | どうする |
/// | --- | --- |
/// | クリティカルの距離より遠い | 何割か（Critical Chance）は**クリティカルを狙う**。受付の真ん中で押す（Critical Success の割合で成功。失敗すると受付のあとに押して、ふつうの距離に落ちる） |
/// | ふつうの距離より遠い | クリティカルの受付が終わった直後に押して、**後ろへ投げる**（物資が手前に来てから押すので、毎回だいたい同じ距離に落ちる） |
/// | 1回では届かないほど遠い | 届くいちばんゴール寄りから後ろへ投げて、**近づける**（中継） |
/// | ふつうの距離より近い | 後ろへ投げると飛びすぎるので、**素材の向こう側に立ち、押さずに前へ投げる**（ゴールのほうへ転がす） |
///
/// ### 落ちる距離は、実際に投げて覚える
///
/// 宇宙ごみの物資は空気の抵抗（Linear Damping）があり、重力も時間で変わり、落ちてから転がる。
/// そのため、インスペクターの Back Distance（6m）どおりには飛ばない（試合で確かめると、後ろへ投げた物は体から 2〜4m で止まっていた）。
/// そこでボットは、**投げた物が止まった所を見届けて、その距離を覚える**（後ろ・クリティカル・前で別々。ボットみんなで共有）。
/// まだ投げていないうちは、Back Distance × First Guess Ratio などの見積もりを使う。
/// 後ろへ投げるときは、引き寄せている間ずっと素材を見たまま（向きを変えると素材が回り込み、その勢いで横へそれる）。
/// 前へ投げるときだけ、引き寄せている間にゴールのほうへ体を向ける。
///
/// ## そのほか
///
/// - 爆弾を引っ掛けてしまったら、押さずに前へ投げる（後ろ＝自分のゴールへ投げないため）
/// - **重いデブリは狙わない**（引きずるだけなので、まだボットの作戦に入れていない）
/// - 自分が投げた素材は、しばらく（Ignore Own Throw Seconds）狙わない（投げた物をすぐ拾い直し続けないため）
/// - 考えるのは、その体を操作するPC（ボットはホストのPC）だけ
///
/// 試合にボットを出すのは今までどおりロビーの小窓（<see cref="SpaceJunkSession.ServerSetBotCount"/>）。
/// 体が新しい体（<see cref="SweeperShooter"/> が付いている）なら、こちらの頭が付く。
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(VirtualPlayerInput))]
public class SweeperBot : MonoBehaviour
{
    [Header("チーム")]
    [Tooltip("どのチームのゴールへ運ぶか（0=青 / 1=赤 / 2=緑 / 3=黄）。オンラインではロビーのチーム分けに従う")]
    [SerializeField] private int team = 0;

    [Tooltip("ゴールを直接決める（検証シーン用）。入っていれば、チームに関係なくここへ運ぶ")]
    [SerializeField] private Transform goalOverride;

    [Header("撃つ")]
    [Tooltip("光線の届く長さ（最大）のうち、どこまでを使うか（0〜1）。小さいほど近づいてから撃つ")]
    [Range(0.3f, 1f)]
    [SerializeField] private float reachUse = 0.8f;

    [Tooltip("素材からこの距離（m）は離れて撃つ（手前で止まる位置 Stop Distance より少し遠く）")]
    [Min(0.5f)]
    [SerializeField] private float minPullDistance = 1.8f;

    [Tooltip("同じ素材を続けて外したら、その素材をしばらくあきらめる回数")]
    [Min(1)]
    [SerializeField] private int missesBeforeGiveUp = 2;

    [Header("投げる")]
    [Tooltip("クリティカルの距離で届く素材のうち、クリティカルを狙う割合（0〜1）")]
    [Range(0f, 1f)]
    [SerializeField] private float criticalChance = 0.5f;

    [Tooltip("クリティカルを狙ったとき、成功する割合（0〜1）。失敗すると受付のあとに押して、ふつうの距離に落ちる")]
    [Range(0f, 1f)]
    [SerializeField] private float criticalSuccess = 0.7f;

    [Tooltip("ふつうに後ろへ投げるとき、クリティカルの受付が終わってから押すまでの秒数。" +
             "物資が手前まで来てから押すので、毎回だいたい同じ距離に落ちる（空気の抵抗があると、遠くで押すほど手前に落ちるため）")]
    [Min(0f)]
    [SerializeField] private float pressAfterWindow = 0.05f;

    [Tooltip("まだ一度も投げていないとき、後ろへ投げた物が「Back Distance の何倍」の所で止まると見積もるか。" +
             "投げたあとは、実際に止まった所を覚えて、それを使う（物資の空気の抵抗・重力の変化・滑りで、計算どおりには飛ばないため）")]
    [Range(0.2f, 2f)]
    [SerializeField] private float firstGuessRatio = 0.7f;

    [Tooltip("まだ一度も投げていないとき、前へ投げた物が体からどれだけ離れた所で止まると見積もるか（m）")]
    [Min(1f)]
    [SerializeField] private float firstGuessForward = 5f;

    [Tooltip("実際に止まった所を、どれだけ見積もりに取り入れるか（0〜1）。大きいほど直前の1回に合わせる")]
    [Range(0.05f, 1f)]
    [SerializeField] private float learnRate = 0.4f;

    [Tooltip("ゴールからこの距離（m）より近い素材は拾わない（もうすぐ点になる物を持ち出さないため）")]
    [Min(0f)]
    [SerializeField] private float ignoreNearGoalDistance = 2.5f;

    [Header("間合い")]
    [Tooltip("撃つ場所に着いてから、撃ち始めるまでの秒数（考えている時間）")]
    [Min(0f)]
    [SerializeField] private float thinkSeconds = 0.25f;

    [Tooltip("投げたあと、次の素材を探し始めるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float afterThrowPause = 0.5f;

    [Tooltip("自分が投げた素材を、また狙うまでの秒数")]
    [Min(0f)]
    [SerializeField] private float ignoreOwnThrowSeconds = 4f;

    [Header("困ったとき")]
    [Tooltip("この秒数のあいだ目的地へ近づけなかったら、その素材をあきらめる")]
    [Min(0.5f)]
    [SerializeField] private float stuckSeconds = 3f;

    [Tooltip("あきらめた素材を、また狙うまでの秒数")]
    [Min(0f)]
    [SerializeField] private float giveUpSeconds = 6f;

    /// <summary>どう投げるつもりか。</summary>
    private enum ThrowPlan
    {
        Back,     // 後ろへ投げる（ふつうの距離）
        Critical, // クリティカルを狙う
        Forward   // 押さずに前へ投げる（ゴールに近い素材）
    }

    /// <summary>どの新しい体のボットかが狙っている素材（2体が同じ素材を取り合わないため）。</summary>
    private static readonly HashSet<HookableObject> Reserved = new HashSet<HookableObject>();

    /// <summary>
    /// **実際に投げて覚えた「投げた物が体からどれだけ離れた所で止まるか」（m）。** まだ覚えていなければ -1。
    /// 体の作り（プレハブ）はボットみんな同じなので、ボットみんなで共有する（1体が投げれば、ほかのボットも賢くなる）。
    /// 並びは <see cref="ThrowPlan"/> と同じ（後ろ・クリティカル・前）。
    /// </summary>
    private static readonly float[] Learned = { -1f, -1f, -1f };

    /// <summary>投げたあと、止まった所を見届けるための記録。</summary>
    private struct ThrowRecord
    {
        public HookableObject item;
        public ThrowPlan kind;
        public Vector3 from;
        public float time;
    }

    private readonly List<ThrowRecord> watching = new List<ThrowRecord>();

    /// <summary>投げてからこの秒数たったら、止まっていなくても、その場所で覚える。</summary>
    private const float WatchSeconds = 4f;

    /// <summary>投げてからこの秒数は、止まっていても覚えない（投げた直後は、まだ速さが出ていないことがあるため）。</summary>
    private const float WatchMinSeconds = 0.6f;

    private VirtualPlayerInput pad;
    private FishingNetPlayer netPlayer;
    private CharacterController body;
    private SweeperShooter shooter;
    private SweeperReel reel;
    private SweeperAim aim;

    private HookableObject target;
    private ThrowPlan plan;

    /// <summary>今の素材でクリティカルを狙うか（素材を決めたときに1回だけ決める。道を引き直すたびに変わらないように）。</summary>
    private bool wantsCritical;

    private readonly Dictionary<HookableObject, float> givenUp = new Dictionary<HookableObject, float>();

    /// <summary>自分が投げた素材と、また狙ってよくなる時刻。</summary>
    private readonly Dictionary<HookableObject, float> thrownByMe = new Dictionary<HookableObject, float>();

    private int misses;
    private float pauseUntil;
    private float arrivedAt = -1f;
    private SweeperShooter.ShotPhase lastPhase;

    // ---- 巻き取り中（Reeling に入った瞬間に決める） ----
    private HookableObject reelingItem;
    private float pressAt = -1f;
    private bool pressedThisReel;

    // ---- 立つ場所と道 ----
    private Vector3 standPoint;
    private readonly List<Vector3> pathCorners = new List<Vector3>();
    private int cornerIndex;
    private Vector3 plannedItemPosition;
    private float replanAt;
    private int stuckCount;
    private float itemGroundHeight;
    private float bestDistance = float.MaxValue;
    private float bestDistanceTime;
    private NavMeshPath navPath;
    private readonly List<HookableObject> candidates = new List<HookableObject>();
    private static readonly RaycastHit[] rayHits = new RaycastHit[16];

    private const float MovingSpeed = 1f;
    private const float PlanRetrySeconds = 1f;
    private const float GroundSearchDepth = 20f;
    private const float ReplanMoveDistance = 1f;
    private const float ReplanSeconds = 1.5f;
    /// <summary>
    /// 立つ場所にこれだけ近づいたら「着いた」（m）。今の体のボット（0.7m）より狭い。
    /// 新しい体は立つ場所のずれがそのまま落ちる所のずれになるため（テストで 0.7m だとゴールを 2.5m 越えた）。
    /// </summary>
    private const float ArriveDistance = 0.3f;
    private const float CornerReachDistance = 0.5f;
    private const int MaxPlanTriesPerFrame = 4;
    private const float SameLevelHeight = 1f;

    /// <summary>光線が出る手元の高さ（足元から m。<see cref="SweeperShooter"/> の Hand Height の初期値）。</summary>
    private const float HandHeight = 1f;

    /// <summary>後ろへ投げたときの放物線の高さの目安（足元から m。<see cref="SweeperReel"/> の Back Arc Height の初期値）。</summary>
    private const float ArcHeight = 3.2f;

    /// <summary>立つ場所を、理想の場所から素材のまわりに回してずらしてみる角度（度）。ずらすほど、落ちる所もゴールからずれる。</summary>
    private static readonly float[] SwingTries = { 0f, 8f, -8f, 16f, -16f };

    /// <summary>どのチームのボットか。オンラインではロビーのチーム分けに従う。</summary>
    public int Team
    {
        get
        {
            SpaceJunkSession session = SpaceJunkSession.Current;
            if (netPlayer != null && netPlayer.IsSpawned && session != null && session.IsSpawned)
            {
                return session.TeamOf(netPlayer.PlayerKey);
            }
            return team;
        }
    }

    /// <summary>新しい体に、ボットの頭を付ける（見えないコントローラーも一緒に付く）。</summary>
    public static SweeperBot AttachTo(GameObject bodyObject)
    {
        if (bodyObject.GetComponent<VirtualPlayerInput>() == null)
        {
            bodyObject.AddComponent<VirtualPlayerInput>();
        }

        SweeperBot bot = bodyObject.GetComponent<SweeperBot>();
        return bot != null ? bot : bodyObject.AddComponent<SweeperBot>();
    }

    private void Awake()
    {
        pad = GetComponent<VirtualPlayerInput>();
        netPlayer = GetComponent<FishingNetPlayer>();
        body = GetComponent<CharacterController>();
        shooter = GetComponent<SweeperShooter>();
        reel = GetComponent<SweeperReel>();
        aim = GetComponent<SweeperAim>();
        navPath = new NavMeshPath();

        if (shooter == null || reel == null)
        {
            Debug.LogError($"{name}: このボットは新しい体（SweeperShooter と SweeperReel が付いた物）に付けてください。", this);
            enabled = false;
        }
    }

    private void Start()
    {
        // ボットの狙いのカーソルは、ホストの画面に出さない
        if (aim != null)
        {
            aim.SetCursorVisible(false);
        }
    }

    private void OnDisable()
    {
        SetTarget(null);
    }

    private void OnDestroy()
    {
        Reserved.RemoveWhere(item => item == null);
    }

    private bool CanThink()
    {
        return netPlayer == null || !netPlayer.IsSpawned || netPlayer.IsOwner;
    }

    private void Update()
    {
        if (!CanThink())
        {
            return;
        }

        if (!SpaceJunkRound.PlayAllowed || !FishingMatch.PlayAllowed || GamePause.BlocksInput || !shooter.enabled)
        {
            pad.Move = Vector2.zero;
            pad.SetPull(false);
            lastPhase = shooter.Phase;
            return;
        }

        SweeperShooter.ShotPhase phase = shooter.Phase;
        TrackShotResult(phase);
        UpdateWatching();
        ForgetOldThrows();

        bool hold = false;

        switch (phase)
        {
            case SweeperShooter.ShotPhase.Idle:
                if (Time.time >= pauseUntil)
                {
                    // 押したのに狙い始めなかった（スタン中など）ときは、いったん離す（押した瞬間しか効かないため）
                    hold = HandleSeek() && !pad.PullHeld;
                }
                else
                {
                    pad.Move = Vector2.zero;
                }
                break;

            case SweeperShooter.ShotPhase.Charging:
                hold = HandleCharging();
                break;

            case SweeperShooter.ShotPhase.Reeling:
                hold = HandleReel();
                break;

            default:
                // 光線が伸びている・戻っている。止まって、素材を見ておく
                pad.Move = Vector2.zero;
                if (target != null)
                {
                    PointAt(target.transform.position);
                }
                break;
        }

        // ボタンは Attack（引っ張る）の1つだけ。溜めている間は押し続け、巻き取り中は1フレームだけ押す
        pad.SetPull(hold);
        lastPhase = phase;
    }

    // ------------------------------------------------------------
    // 撃った結果を見る
    // ------------------------------------------------------------

    private void TrackShotResult(SweeperShooter.ShotPhase phase)
    {
        // 伸びた光線が何にも当たらずに戻り始めた＝外した
        if (lastPhase == SweeperShooter.ShotPhase.Extending && phase == SweeperShooter.ShotPhase.Retracting)
        {
            misses++;
            if (target != null && misses >= missesBeforeGiveUp)
            {
                GiveUp(target);
            }
        }

        // 巻き取りが終わった（投げた・離した・取られた）
        if (lastPhase == SweeperShooter.ShotPhase.Reeling && phase != SweeperShooter.ShotPhase.Reeling)
        {
            if (reelingItem != null)
            {
                thrownByMe[reelingItem] = Time.time + ignoreOwnThrowSeconds;
                StartWatching(reelingItem, reel.LastResult);
            }

            reelingItem = null;
            misses = 0;
            pauseUntil = Time.time + afterThrowPause;
            SetTarget(null);
        }
    }

    // ------------------------------------------------------------
    // 投げた物が止まった所を覚える
    // ------------------------------------------------------------

    /// <summary>投げた物を見届け始める（結果が「後ろ」「クリティカル」「前」のときだけ。離した・取られたは見ない）。</summary>
    private void StartWatching(HookableObject item, string result)
    {
        ThrowPlan kind;
        switch (result)
        {
            case "後ろ": kind = ThrowPlan.Back; break;
            case "クリティカル": kind = ThrowPlan.Critical; break;
            case "前": kind = ThrowPlan.Forward; break;
            default: return;
        }

        watching.Add(new ThrowRecord { item = item, kind = kind, from = transform.position, time = Time.time });
    }

    /// <summary>
    /// 見届けている物が止まったら（または <see cref="WatchSeconds"/> たったら）、投げた所からの距離を覚える。
    /// ゴールに入って消えた物・誰かが引っ掛けた物は、覚えずにやめる。
    /// </summary>
    private void UpdateWatching()
    {
        for (int i = watching.Count - 1; i >= 0; i--)
        {
            ThrowRecord record = watching[i];
            HookableObject item = record.item;
            float age = Time.time - record.time;

            if (item == null || item.IsVanished || !item.gameObject.activeInHierarchy || item.IsHooked)
            {
                watching.RemoveAt(i);
                continue;
            }

            bool stopped = age >= WatchMinSeconds && !IsMoving(item, 0.3f);
            if (!stopped && age < WatchSeconds)
            {
                continue;
            }

            float measured = FlatDistance(record.from, item.transform.position);
            int index = (int)record.kind;
            Learned[index] = Learned[index] < 0f ? measured : Mathf.Lerp(Learned[index], measured, learnRate);
            Learned[index] = Mathf.Clamp(Learned[index], 1f, 40f);
            watching.RemoveAt(i);
        }
    }

    /// <summary>その投げ方で、投げた物が体からどれだけ離れた所で止まるかの見積もり（m）。覚えていなければ最初の見積もり。</summary>
    private float ReachOf(ThrowPlan kind)
    {
        float learned = Learned[(int)kind];
        if (learned > 0f)
        {
            return learned;
        }

        return kind switch
        {
            ThrowPlan.Critical => reel.CriticalBackDistance * firstGuessRatio,
            ThrowPlan.Forward => firstGuessForward,
            _ => reel.BackDistance * firstGuessRatio,
        };
    }

    /// <summary>覚えた距離（後ろ・クリティカル・前）。検証用の表示に使う。覚えていなければ -1。</summary>
    public static float LearnedBack => Learned[(int)ThrowPlan.Back];
    public static float LearnedCritical => Learned[(int)ThrowPlan.Critical];
    public static float LearnedForward => Learned[(int)ThrowPlan.Forward];

    /// <summary>投げてからしばらくたった素材・消えた素材を「自分が投げた物」から外す。</summary>
    private void ForgetOldThrows()
    {
        if (thrownByMe.Count == 0)
        {
            return;
        }

        List<HookableObject> expired = null;
        foreach (KeyValuePair<HookableObject, float> entry in thrownByMe)
        {
            if (entry.Key == null || entry.Key.IsVanished || Time.time >= entry.Value)
            {
                (expired ??= new List<HookableObject>()).Add(entry.Key);
            }
        }

        if (expired != null)
        {
            foreach (HookableObject item in expired)
            {
                thrownByMe.Remove(item);
            }
        }
    }

    // ------------------------------------------------------------
    // 素材を探して、撃つ場所まで歩く（撃ち始めるなら true）
    // ------------------------------------------------------------

    private bool HandleSeek()
    {
        Vector3? goal = GoalPosition();
        if (!goal.HasValue)
        {
            pad.Move = Vector2.zero;
            return false;
        }

        SpaceJunkBotNavMesh.EnsureBuilt(body);

        if (!IsPickable(target))
        {
            ChooseTarget(goal.Value);
        }
        else if (!IsMoving(target) &&
                 (Time.time >= replanAt || FlatDistance(target.transform.position, plannedItemPosition) > ReplanMoveDistance))
        {
            if (!Plan(target, goal.Value))
            {
                GiveUp(target);
            }
        }

        if (target == null)
        {
            pad.Move = Vector2.zero;
            return false;
        }

        Vector3 itemPosition = target.transform.position;

        // 撃つ場所に着いた。止まって素材にカーソルを合わせ、少し待ってから撃ち始める
        if (FlatDistance(transform.position, standPoint) < ArriveDistance)
        {
            pad.Move = Vector2.zero;
            PointAt(itemPosition);

            if (arrivedAt < 0f)
            {
                arrivedAt = Time.time;
            }

            return Time.time - arrivedAt >= thinkSeconds;
        }

        arrivedAt = -1f;

        if (IsStuck(RemainingPathLength()))
        {
            stuckCount++;
            if (stuckCount >= 2 || !Plan(target, goal.Value))
            {
                GiveUp(target);
                pad.Move = Vector2.zero;
                return false;
            }
        }

        FollowPath();
        return false;
    }

    /// <summary>次に狙う素材を決める。近い順に試し、立つ場所と道が見つかった最初の素材にする。</summary>
    private void ChooseTarget(Vector3 goal)
    {
        SetTarget(null);

        candidates.Clear();
        foreach (HookableObject item in HookableObject.All)
        {
            if (IsCandidate(item, goal))
            {
                candidates.Add(item);
            }
        }

        Vector3 me = transform.position;
        candidates.Sort((a, b) =>
            FlatDistance(me, a.transform.position).CompareTo(FlatDistance(me, b.transform.position)));

        int tries = Mathf.Min(candidates.Count, MaxPlanTriesPerFrame);
        for (int i = 0; i < tries; i++)
        {
            HookableObject item = candidates[i];
            wantsCritical = Random.value < criticalChance;
            if (Plan(item, goal))
            {
                SetTarget(item);
                return;
            }

            givenUp[item] = Time.time + PlanRetrySeconds;
        }
    }

    private bool IsCandidate(HookableObject item, Vector3 goal)
    {
        if (!IsPickable(item) || Reserved.Contains(item) || thrownByMe.ContainsKey(item))
        {
            return false;
        }

        if (givenUp.TryGetValue(item, out float until) && Time.time < until)
        {
            return false;
        }

        if (IsMoving(item))
        {
            return false;
        }

        return FlatDistance(item.transform.position, goal) >= ignoreNearGoalDistance;
    }

    /// <summary>狙ってよい素材か（素材・消えていない・誰も引いていない・重いデブリでない）。</summary>
    private static bool IsPickable(HookableObject item)
    {
        if (item == null || item.IsVanished || item.IsHooked)
        {
            return false;
        }

        if (item.GetComponent<SpaceJunkMaterial>() == null || HeavyHookable.TryGetWeight(item, out _))
        {
            return false;
        }

        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        return netSupply == null || !netSupply.IsSpawned || !netSupply.IsClaimed;
    }

    /// <summary>
    /// **素材を撃つための、立つ場所と道と投げ方を決める。** 見つからなければ false。
    /// 理想の場所（素材 → 自分 → ゴールが一直線）から試し、だめなら素材のまわりに少し回した場所・少し近い/遠い場所を試す。
    /// </summary>
    private bool Plan(HookableObject item, Vector3 goal)
    {
        Vector3 itemPosition = item.transform.position;
        plannedItemPosition = itemPosition;
        replanAt = Time.time + ReplanSeconds;
        stuckCount = 0;
        ResetStuck();

        Vector3 fromGoal = itemPosition - goal;
        fromGoal.y = 0f;
        float itemToGoal = fromGoal.magnitude;
        Vector3 away = itemToGoal > 0.01f ? fromGoal / itemToGoal : -Flat(transform.forward);

        float reach = shooter.MaxReach * reachUse;
        float pullMin = Mathf.Min(minPullDistance, reach);

        // 素材から、どちら向きにどれだけ離れて立つか（away の向きを＋とする）。
        // 自分からゴールまでが「投げた物が止まる距離」（実際に投げて覚えた値。ReachOf）になる所に立つ
        float criticalReach = ReachOf(ThrowPlan.Critical);
        float backReach = ReachOf(ThrowPlan.Back);
        float forwardReach = ReachOf(ThrowPlan.Forward);
        float standOffset;
        if (wantsCritical && itemToGoal - criticalReach >= pullMin)
        {
            // 素材 → 自分 → ゴール。1回で届かなければ、届くいちばんゴール寄りから（中継）
            plan = ThrowPlan.Critical;
            standOffset = -Mathf.Clamp(itemToGoal - criticalReach, pullMin, reach);
        }
        else
        {
            // 後ろへ投げる（素材 → 自分 → ゴール）と、前へ投げる（自分 → 素材 → ゴール）の、ずれが小さいほう。
            // どちらも素材に近づきすぎないように Min Pull Distance は離れるので、その分ゴールを越えることがある
            float backStand = itemToGoal - backReach;     // 素材から、ゴールのほうへ何m寄って立つか
            float forwardStand = forwardReach - itemToGoal; // 素材から、ゴールと反対へ何m離れて立つか
            float backError = Mathf.Max(0f, pullMin - backStand);
            float forwardError = Mathf.Max(0f, pullMin - forwardStand) + Mathf.Max(0f, forwardStand - reach);

            if (backError <= forwardError)
            {
                plan = ThrowPlan.Back;
                standOffset = -Mathf.Clamp(backStand, pullMin, reach);
            }
            else
            {
                plan = ThrowPlan.Forward;
                standOffset = Mathf.Clamp(forwardStand, pullMin, reach);
            }
        }

        Vector3 ideal = itemPosition + away * standOffset;

        if (!SpaceJunkBotNavMesh.Ready)
        {
            standPoint = new Vector3(ideal.x, transform.position.y, ideal.z);
            pathCorners.Clear();
            pathCorners.Add(standPoint);
            cornerIndex = 0;
            return true;
        }

        if (!SpaceJunkBotNavMesh.TryFindFloor(transform.position, 2f, out Vector3 myFloor))
        {
            return false;
        }

        itemGroundHeight = GroundHeightUnder(itemPosition);

        float bestScore = float.MaxValue;
        bool found = false;
        float[] offsets = { 0f, 1.5f, -1.5f };

        foreach (float swing in SwingTries)
        {
            foreach (float extra in offsets)
            {
                float offset = standOffset + Mathf.Sign(standOffset) * extra;
                if (Mathf.Abs(offset) < pullMin * 0.8f || Mathf.Abs(offset) > reach)
                {
                    continue;
                }

                Vector3 candidate = itemPosition + Quaternion.Euler(0f, swing, 0f) * away * offset;
                candidate.y = itemGroundHeight + 0.2f;

                if (!TryScoreStandPoint(candidate, itemPosition, goal, myFloor, reach, pullMin, out Vector3 floor, out float score))
                {
                    continue;
                }

                // 理想の場所からずれるほど、落ちる所がゴールからずれるので減点
                score += Mathf.Abs(swing) * 0.6f + Mathf.Abs(extra) * 2f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                standPoint = floor;
                pathCorners.Clear();
                pathCorners.AddRange(navPath.corners);
                found = true;
            }
        }

        cornerIndex = pathCorners.Count > 1 ? 1 : 0;
        return found;
    }

    private bool TryScoreStandPoint(Vector3 candidate, Vector3 itemPosition, Vector3 goal, Vector3 myFloor,
        float reach, float pullMin, out Vector3 floor, out float score)
    {
        score = float.MaxValue;

        if (!SpaceJunkBotNavMesh.TryFindFloor(candidate, 1.5f, out floor) ||
            Mathf.Abs(floor.y - itemGroundHeight) > SameLevelHeight)
        {
            return false;
        }

        float toItem = FlatDistance(floor, itemPosition);
        if (toItem > reach || toItem < pullMin * 0.8f)
        {
            return false;
        }

        if (IsBlocked(floor + Vector3.up * HandHeight, itemPosition))
        {
            return false;
        }

        if (!SpaceJunkBotNavMesh.TryFindPath(myFloor, floor, navPath))
        {
            return false;
        }

        score = PathLength(navPath.corners);

        // 後ろへ投げるなら、頭の上からゴールまでの間に壁があれば減点（前へ転がすなら、床の上を見る）
        float height = plan == ThrowPlan.Forward ? 0.5f : ArcHeight;
        if (IsBlocked(floor + Vector3.up * height, goal + Vector3.up * 1f))
        {
            score += 15f;
        }

        return true;
    }

    // ------------------------------------------------------------
    // 溜めて撃つ（押し続けるなら true）
    // ------------------------------------------------------------

    private bool HandleCharging()
    {
        pad.Move = Vector2.zero;

        if (target == null)
        {
            return false; // 狙う物が無くなった。その場で撃ってしまう
        }

        Vector3 itemPosition = target.transform.position;
        PointAt(itemPosition);

        float need = FlatDistance(transform.position, itemPosition) + 0.3f;
        return shooter.CurrentReach < need && shooter.CurrentReach < shooter.MaxReach;
    }

    // ------------------------------------------------------------
    // 巻き取り中（押すフレームだけ true）
    // ------------------------------------------------------------

    private bool HandleReel()
    {
        pad.Move = Vector2.zero;

        HookableObject item = shooter.ReelItem;
        if (item == null)
        {
            return false;
        }

        // 巻き取りに入った瞬間に、押すタイミングを決める
        if (item != reelingItem)
        {
            reelingItem = item;
            pressedThisReel = false;
            pressAt = ChoosePressTime(item);
        }

        // 前へ投げる・爆弾のときは、投げる向きに体を向ける（前へ投げる向きは体の向きそのまま）。
        // 後ろへ投げるときは素材を見たまま（体を回すと素材が回り込み、その勢いが投げる向きに混ざって横へそれるため。テストで分かった）
        bool facingReady = true;
        if (TryChooseFacing(item, out Vector3 facing))
        {
            PointAt(transform.position + facing * 3f);
            facingReady = Vector3.Angle(Flat(transform.forward), facing) < 4f;
        }
        else
        {
            PointAt(item.transform.position);
        }

        if (pressedThisReel || pressAt < 0f || reel.IsWaitingForClaim || reel.Elapsed < pressAt)
        {
            return false;
        }

        // まだ投げる向きを向いていなければ、少し待つ（クリティカルの受付と前へ投げる直前は待たない）
        if (!facingReady && !reel.InCriticalWindow && reel.Elapsed < reel.ReelSeconds - 0.15f)
        {
            return false;
        }

        pressedThisReel = true;
        return true;
    }

    /// <summary>
    /// 巻き取り中に体を向ける向き（真上から見た向き・長さ1）。決められなければ false（素材を見たまま）。
    /// ・前へ投げる … ゴールのほう
    /// ・爆弾 … 自分のゴールと反対（押さないので前へ飛ぶ＝ゴールから離れる）
    /// ・後ろへ投げる … false（素材を見たまま。立つ場所で、素材 → 自分 → ゴールが並ぶようにしてある）
    /// </summary>
    private bool TryChooseFacing(HookableObject item, out Vector3 facing)
    {
        facing = Vector3.forward;
        Vector3? goal = GoalPosition();
        if (!goal.HasValue || reel.IsHeavyDrag)
        {
            return false;
        }

        bool bomb = item.GetComponent<ExplosiveObject>() != null;
        bool forward = !bomb && item == target && plan == ThrowPlan.Forward;
        if (!bomb && !forward)
        {
            return false;
        }

        Vector3 fromGoal = transform.position - goal.Value;
        fromGoal.y = 0f;
        if (fromGoal.sqrMagnitude < 0.25f)
        {
            return false;
        }
        fromGoal.Normalize();
        facing = forward ? -fromGoal : fromGoal;
        return true;
    }

    /// <summary>引き寄せ始めてから何秒で押すか。押さない（前へ投げる）なら -1。</summary>
    private float ChoosePressTime(HookableObject item)
    {
        // 重い物は投げられないので、すぐ離す（ボットは狙わないが、通り道で当たることがある）
        if (reel.IsHeavyDrag)
        {
            return 0f;
        }

        // 爆弾は後ろ（自分のゴールの側）へ投げない
        if (item.GetComponent<ExplosiveObject>() != null)
        {
            return -1f;
        }

        // 狙っていた素材と違う物に当たったときは、ふつうに後ろへ投げる
        ThrowPlan current = item == target ? plan : ThrowPlan.Back;

        // ふつうに後ろへ投げるのは、クリティカルの受付が終わった直後（物資がもう手前に来ている）。
        // クリティカルを狙って失敗したときも同じ時に押すので、落ちる所はふつうと同じになる
        float latest = Mathf.Max(0f, reel.ReelSeconds - 0.1f);
        float afterWindow = Mathf.Min(reel.CriticalWindowEnd + pressAfterWindow, latest);
        switch (current)
        {
            case ThrowPlan.Forward:
                return -1f;

            case ThrowPlan.Critical:
                return Random.value < criticalSuccess
                    ? Mathf.Lerp(reel.CriticalWindowStart, reel.CriticalWindowEnd, Random.Range(0.3f, 0.7f))
                    : afterWindow;

            default:
                return afterWindow;
        }
    }

    // ------------------------------------------------------------
    // ゴール
    // ------------------------------------------------------------

    private Vector3? GoalPosition()
    {
        if (goalOverride != null)
        {
            return goalOverride.position;
        }

        int myTeam = Team;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && !goal.IsClosed && goal.OwnerTeam == myTeam)
            {
                Collider area = goal.GetComponent<Collider>();
                return area != null ? area.bounds.center : goal.transform.position;
            }
        }

        return null;
    }

    // ------------------------------------------------------------
    // 歩く・カーソル（SpaceJunkBot と同じ）
    // ------------------------------------------------------------

    private void FollowPath()
    {
        while (cornerIndex < pathCorners.Count - 1 &&
               FlatDistance(transform.position, pathCorners[cornerIndex]) < CornerReachDistance)
        {
            cornerIndex++;
        }

        Vector3 next = pathCorners.Count > 0 ? pathCorners[cornerIndex] : standPoint;
        WalkTo(next);
    }

    private float RemainingPathLength()
    {
        if (pathCorners.Count == 0)
        {
            return FlatDistance(transform.position, standPoint);
        }

        float length = FlatDistance(transform.position, pathCorners[cornerIndex]);
        for (int i = cornerIndex; i < pathCorners.Count - 1; i++)
        {
            length += FlatDistance(pathCorners[i], pathCorners[i + 1]);
        }
        return length;
    }

    /// <summary>行き先へ左スティックを倒す（スティックは画面の上下左右なので、カメラの向きのぶん逆に回す）。</summary>
    private void WalkTo(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            pad.Move = Vector2.zero;
            return;
        }

        Camera view = Camera.main;
        float yaw = view != null ? view.transform.eulerAngles.y : 0f;
        Vector3 stick = Quaternion.Euler(0f, -yaw, 0f) * direction.normalized;

        pad.Move = new Vector2(stick.x, stick.z);
        PointAt(destination);
    }

    /// <summary>カーソルを、地面の <paramref name="worldPoint"/> が映っている画面の位置に置く（新しい体は、それを足元の円の中に映す）。</summary>
    private void PointAt(Vector3 worldPoint)
    {
        Camera view = Camera.main;
        if (view == null)
        {
            return;
        }

        // 新しい体は、画面の位置を足元の高さの面に映して向きを決めるので、足元の高さの点を渡す
        worldPoint.y = transform.position.y;
        Vector3 screen = view.WorldToScreenPoint(worldPoint);
        if (screen.z > 0f)
        {
            pad.SetPointer(screen);
        }
    }

    // ------------------------------------------------------------
    // 小さな部品
    // ------------------------------------------------------------

    private void SetTarget(HookableObject item)
    {
        if (target == item)
        {
            return;
        }

        if (target != null)
        {
            Reserved.Remove(target);
        }

        target = item;
        misses = 0;
        arrivedAt = -1f;

        if (target != null)
        {
            Reserved.Add(target);
        }

        ResetStuck();
    }

    private void GiveUp(HookableObject item)
    {
        if (item != null)
        {
            givenUp[item] = Time.time + giveUpSeconds;
        }
        SetTarget(null);
    }

    private bool IsStuck(float distance)
    {
        if (distance < bestDistance - 0.3f)
        {
            bestDistance = distance;
            bestDistanceTime = Time.time;
            return false;
        }

        return Time.time - bestDistanceTime > stuckSeconds;
    }

    private void ResetStuck()
    {
        bestDistance = float.MaxValue;
        bestDistanceTime = Time.time;
    }

    private static bool IsMoving(HookableObject item, float speed = MovingSpeed)
    {
        Rigidbody itemBody = item.Body;
        return itemBody != null && !itemBody.isKinematic && itemBody.linearVelocity.sqrMagnitude > speed * speed;
    }

    /// <summary><paramref name="from"/> から <paramref name="to"/> の間に、動かない壁や床があるか（素材・体・転がる物は数えない）。</summary>
    private static bool IsBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, rayHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = rayHits[i].collider;
            if (hit == null || hit is CharacterController)
            {
                continue;
            }

            if (hit.GetComponentInParent<HookableObject>() != null || hit.GetComponentInParent<CharacterController>() != null)
            {
                continue;
            }

            Rigidbody hitBody = hit.attachedRigidbody;
            if (hitBody != null && !hitBody.isKinematic)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary><paramref name="point"/> の真下の、動かない床の高さ。見つからなければ <paramref name="point"/> の高さ。</summary>
    private static float GroundHeightUnder(Vector3 point)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.5f, Vector3.down, rayHits, GroundSearchDepth, ~0,
            QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        float height = point.y;

        for (int i = 0; i < count; i++)
        {
            Collider hit = rayHits[i].collider;
            if (hit == null || hit is CharacterController ||
                hit.GetComponentInParent<HookableObject>() != null || hit.GetComponentInParent<CharacterController>() != null)
            {
                continue;
            }

            Rigidbody hitBody = hit.attachedRigidbody;
            if (hitBody != null && !hitBody.isKinematic)
            {
                continue;
            }

            if (rayHits[i].distance < best)
            {
                best = rayHits[i].distance;
                height = rayHits[i].point.y;
            }
        }

        return height;
    }

    private static float PathLength(Vector3[] corners)
    {
        float length = 0f;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            length += Vector3.Distance(corners[i], corners[i + 1]);
        }
        return length;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
