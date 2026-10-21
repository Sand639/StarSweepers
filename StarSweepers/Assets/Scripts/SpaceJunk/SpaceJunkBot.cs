using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// **宇宙ごみを集めるボット（NPC）の頭。** 試作・2026/10/21（大槻さんの依頼）
///
/// ## プレイヤーと同じ操作しかしない
///
/// ボットは物を直接動かさない。**プレイヤーの体（移動・狙い・フック・投げるの部品が付いた体）に乗って、
/// 見えないコントローラー（<see cref="VirtualPlayerInput"/>）を操作するだけ。**
/// 操作は**操作タイプA・投げ方「左右で使い分け」**と同じ（PCの設定がタイプBやZRワンボタンでも、ボットはこの操作）。
///
/// | ボットがすること | プレイヤーで言うと |
/// | --- | --- |
/// | 素材の近くの「投げればゴールに届く場所」まで歩く | 左スティック |
/// | 素材にカーソルを合わせる | 右スティック（マウス） |
/// | RT を押して溜め、届く長さになったら離す | RT（右クリック）長押し → 離す |
/// | 持ち上げている間に、カーソルをゴールへ向ける | 右スティック |
/// | 投げるゲージが、ゴールまでちょうど届く位置に来たら RT | RT |
///
/// ボットが読むのは、**プレイヤーが画面で見ているものと同じ情報だけ**
/// （素材とゴールの場所、自分のフックの溜め具合、投げるゲージの位置）。
///
/// ## 届かないときは中継する
///
/// 素材がゴールから遠すぎて1回では届かないときは、**ゴールに向けて思いきり投げて近づける**。
///
/// ## 自分が投げた物は拾わない
///
/// 投げた物をすぐ拾い直して投げ続けないよう、**自分が投げた素材は狙わない**（ほかの人が引っ掛けたら、また狙ってよい）。
/// 中継で近づけた物も自分では拾い直さないので、続きはほかの人・ほかのボットに任せる形になる。
///
/// ## 壁は回り込み、違う段の素材には上がってから撃つ（2026/10/21・大槻さんの報告：STAGE_07）
///
/// マップの当たり判定から**歩ける床の地図**を作り（<see cref="SpaceJunkBotNavMesh"/>）、
/// 「素材と同じ高さの床」「素材との間に壁が無い」「歩いて行ける」場所を選んで、道の曲がり角を順にたどって歩く。
/// そういう場所が無い素材（上の段へ上がる道が無いなど）は、しばらくあきらめて別の素材を探す。
///
/// ## 考えるのは、その体を操作するPCだけ
///
/// オンラインの体（<see cref="FishingNetPlayer"/>）なら、持ち主のPCだけで考える。つないでいなければそのPCで考える。
///
/// ## 試合に出す（デバッグ用のボット）
///
/// ロビーの「このPCで遊ぶ人数」の小窓で、**ホストがボットの数を決める**（<see cref="SpaceJunkSession.ServerSetBotCount"/>）。
/// ボットはホストのPCが持ち主のプレイヤーとして出て、ホストのPCでだけ <see cref="AttachTo"/> でこの頭が付く。
/// チームはロビーのチーム分けに従う（<see cref="Team"/>）。
///
/// 入力を書き込んでから操作の部品が読むよう、**ほかの部品より先に動く**（DefaultExecutionOrder）。
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(VirtualPlayerInput))]
public class SpaceJunkBot : MonoBehaviour
{
    [Header("チーム")]
    [Tooltip("どのチームのゴールへ運ぶか（0=青 / 1=赤 / 2=緑 / 3=黄）。そのチームのゴールが無いマップでは何もしない")]
    [SerializeField] private int team = 0;

    [Tooltip("ゴールを直接決める（検証シーン用）。入っていれば、チームに関係なくここへ運ぶ")]
    [SerializeField] private Transform goalOverride;

    [Header("参照（空なら同じ体から自動で探す）")]
    [SerializeField] private HookController hook;
    [SerializeField] private ThrowController thrower;

    [Tooltip("カーソルの位置を決めるときに使うカメラ。空なら Camera.main（プレイヤーの狙いと同じカメラ）")]
    [SerializeField] private Camera viewCamera;

    [Header("フックを撃つ")]
    [Tooltip("フックの届く長さ（最大）のうち、どこまでを使うか（0〜1）。小さいほど近づいてから撃つ")]
    [Range(0.3f, 1f)]
    [SerializeField] private float hookReachUse = 0.8f;

    [Tooltip("素材にこれより近いと撃ちにくいので、この距離（m）は離れて撃つ")]
    [SerializeField] private float minHookDistance = 2.5f;

    [Tooltip("同じ素材を続けて外したら、その素材をしばらくあきらめる回数")]
    [SerializeField] private int missesBeforeGiveUp = 2;

    [Header("投げる")]
    [Tooltip("投げて届く最大の距離のうち、どこまでを使うか（0〜1）。小さいほどゴールの近くまで寄ってから投げる")]
    [Range(0.3f, 1f)]
    [SerializeField] private float throwRangeUse = 0.85f;

    [Tooltip("飛ぶ距離の見積もりの補正（1 で計算どおり）。いつも手前に落ちるなら大きく、奥へ行きすぎるなら小さくする")]
    [SerializeField] private float throwDistanceScale = 1f;

    [Tooltip("ゲージを押す位置のぶれ（ゲージ全体を1とした幅）。大きいほど下手になる。0 で毎回ぴったり")]
    [Range(0f, 0.3f)]
    [SerializeField] private float gaugeError = 0.04f;

    [Tooltip("ゴールからこの距離（m）より近い素材は拾わない（もうすぐ点になる物を持ち出さないため）")]
    [SerializeField] private float ignoreNearGoalDistance = 2.5f;

    [Header("間合い")]
    [Tooltip("撃つ場所に着いてから、撃ち始めるまでの秒数（考えている時間）")]
    [SerializeField] private float thinkSeconds = 0.25f;

    [Tooltip("投げたあと、次の素材を探し始めるまでの秒数")]
    [SerializeField] private float afterThrowPause = 0.5f;

    [Header("困ったとき")]
    [Tooltip("この秒数のあいだ目的地へ近づけなかったら、その素材をあきらめる")]
    [SerializeField] private float stuckSeconds = 3f;

    [Tooltip("あきらめた素材を、また狙うまでの秒数")]
    [SerializeField] private float giveUpSeconds = 6f;

    /// <summary>どのボットかが狙っている素材。**2体が同じ素材を取り合わない**ために使う。</summary>
    private static readonly HashSet<HookableObject> Reserved = new HashSet<HookableObject>();

    private VirtualPlayerInput pad;
    private FishingNetPlayer netPlayer;

    /// <summary>狙っている素材。</summary>
    private HookableObject target;

    /// <summary>あきらめた素材と、また狙ってよくなる時刻。</summary>
    private readonly Dictionary<HookableObject, float> givenUp = new Dictionary<HookableObject, float>();

    /// <summary>
    /// **自分が投げた素材。** これは狙わない（投げた物をすぐ拾い直して、投げ続けてしまうのを防ぐ。2026/10/21・大槻さんの報告）。
    /// ほかの人が引っ掛けたら、もう「自分が投げた物」ではないので外す。
    /// </summary>
    private readonly HashSet<HookableObject> thrownByMe = new HashSet<HookableObject>();

    /// <summary>今の素材を続けて外した回数。</summary>
    private int misses;

    /// <summary>撃ってから、何かに引っ掛かったか（フックが戻ってきたとき、外したかどうかの判断に使う）。</summary>
    private bool hookedSinceFire;

    /// <summary>直前のフレームのフックの段階。</summary>
    private HookController.HookPhase lastPhase;

    /// <summary>この時刻までは何もしない（考えている・投げたあとの一息）。</summary>
    private float pauseUntil;

    /// <summary>撃つ場所に着いた時刻（着いてから <see cref="thinkSeconds"/> 待ってから撃つ）。着いていなければ -1。</summary>
    private float arrivedAt = -1f;

    /// <summary>今回の投げでゲージを押す位置。投げるゲージが出た瞬間に決める。-1 ならまだ決めていない。</summary>
    private float pressAt = -1f;

    // 進めなくなったかを見るための、いちばん縮んだ「目的地までの距離」と、その時刻
    private float bestDistance = float.MaxValue;
    private float bestDistanceTime;

    // ---- 立つ場所と道（Plan で決める。2026/10/21・壁を回り込む・高さを見る） ----

    private CharacterController body;

    /// <summary>素材を撃つために立つ場所（歩ける床の上）。</summary>
    private Vector3 standPoint;

    /// <summary>立つ場所までの道の曲がり角（最初は出発点）と、いま向かっている角の番号。</summary>
    private readonly List<Vector3> pathCorners = new List<Vector3>();
    private int cornerIndex;

    /// <summary>道を決めたときの素材の場所と、次に道を引き直す時刻。</summary>
    private Vector3 plannedItemPosition;
    private float replanAt;

    /// <summary>同じ素材で、進めなくなって道を引き直した回数。</summary>
    private int stuckCount;

    /// <summary>道を調べるときの入れ物（使い回す。Awake で作る）。</summary>
    private NavMeshPath navPath;
    private readonly List<HookableObject> candidates = new List<HookableObject>();
    private static readonly RaycastHit[] rayHits = new RaycastHit[16];

    /// <summary>素材がこれ以上動いたら、道を引き直す（m）。</summary>
    private const float ReplanMoveDistance = 1f;

    /// <summary>この秒数ごとに、道を引き直す（ほかの人が動かした・地形が動いたときのため）。</summary>
    private const float ReplanSeconds = 1.5f;

    /// <summary>立つ場所にこれだけ近づいたら「着いた」（m）。</summary>
    private const float ArriveDistance = 0.7f;

    /// <summary>道の曲がり角にこれだけ近づいたら、次の角へ（m）。</summary>
    private const float CornerReachDistance = 0.5f;

    /// <summary>1フレームで道を調べる素材の数（多いと重くなる）。</summary>
    private const int MaxPlanTriesPerFrame = 4;

    /// <summary>立つ床と素材の高さの差がこれより大きければ「違う段」とみなす（m）。</summary>
    private const float SameLevelHeight = 1f;

    /// <summary>手元（フックが出る所）の、足元からの高さ（m）。</summary>
    private const float HandHeight = 1f;

    /// <summary>投げるときの頭上の高さ（m。ThrowController の Overhead Height の初期値）。</summary>
    private const float OverheadHeight = 2.6f;

    /// <summary>
    /// どのチームのボットか。**試合の中（オンラインの体）では、ロビーのチーム分けに従う**
    /// （ホストがチームを変えたら、ボットの運ぶ先も変わる）。つないでいない体では Team の値。
    /// </summary>
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

    /// <summary>
    /// **プレイヤーの体に、ボットの頭を付ける**（見えないコントローラーも一緒に付く）。
    /// 試合にボットを足すとき、ホストのPCでだけ呼ぶ（<see cref="SpaceJunkSession.ServerSetBotCount"/>）。
    /// </summary>
    public static SpaceJunkBot AttachTo(GameObject body)
    {
        if (body.GetComponent<VirtualPlayerInput>() == null)
        {
            body.AddComponent<VirtualPlayerInput>();
        }

        SpaceJunkBot bot = body.GetComponent<SpaceJunkBot>();
        return bot != null ? bot : body.AddComponent<SpaceJunkBot>();
    }

    private void Awake()
    {
        pad = GetComponent<VirtualPlayerInput>();
        netPlayer = GetComponent<FishingNetPlayer>();
        body = GetComponent<CharacterController>();
        navPath = new NavMeshPath();

        if (hook == null)
        {
            hook = GetComponent<HookController>();
        }

        if (thrower == null)
        {
            thrower = GetComponent<ThrowController>();
        }

        if (hook == null || thrower == null)
        {
            Debug.LogError($"{name}: ボットはプレイヤーの体（HookController と ThrowController が付いた物）に付けてください。", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        SetTarget(null);
    }

    private void OnDestroy()
    {
        // シーンを移ると素材ごと消えるので、消えた物の印を片付けておく
        Reserved.RemoveWhere(item => item == null);
    }

    /// <summary>
    /// **このPCでボットを考えさせてよいか。**
    /// オンラインの体なら持ち主のPCだけ。つないでいない体なら、このPCで動かす。
    /// </summary>
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

        // ボタンは「押した瞬間」だけ意味があるので、押したら次のフレームで必ず離す
        // （溜めている間だけは、下の HandleCharging が押し続ける）
        pad.SetPull(false);
        if (hook.Phase != HookController.HookPhase.Charging)
        {
            pad.SetThrow(false);
        }

        // ラウンドの始まりの演出中・ラウンドの合間・試合の外は、何もしない
        if (!SpaceJunkRound.PlayAllowed || !FishingMatch.PlayAllowed || GamePause.BlocksInput)
        {
            pad.Move = Vector2.zero;
            pad.SetThrow(false);
            return;
        }

        TrackHookResult();
        ForgetThrownTakenByOthers();

        // ---- 画面にゲージが出ていたら、それに応える ----
        if (thrower.IsThrowGaugeShowing)
        {
            pad.Move = Vector2.zero;
            HandleThrowGauge();
            return;
        }

        pressAt = -1f;

        if (thrower.IsPullGaugeShowing)
        {
            // 横取りの待ちなどで「引っ張る」ゲージが出た。真ん中あたりで LT を押す
            pad.Move = Vector2.zero;
            float gauge = thrower.GaugePosition;
            if (gauge > 0.45f && gauge < 0.55f)
            {
                pad.SetPull(true);
            }
            return;
        }

        switch (hook.Phase)
        {
            case HookController.HookPhase.Charging:
                HandleCharging();
                break;

            case HookController.HookPhase.Idle:
                if (Time.time >= pauseUntil)
                {
                    HandleSeek();
                }
                else
                {
                    pad.Move = Vector2.zero;
                }
                break;

            default:
                // 飛んでいる・戻っている・持ち上げている途中。止まって、持ち上げ中ならゴールを向いておく
                pad.Move = Vector2.zero;
                if (hook.Phase == HookController.HookPhase.Attached && GoalPosition().HasValue)
                {
                    PointAt(GoalPosition().Value);
                }
                break;
        }
    }

    // ------------------------------------------------------------
    // 撃った結果を見る（外したら数える）
    // ------------------------------------------------------------

    private void TrackHookResult()
    {
        HookController.HookPhase phase = hook.Phase;

        if (phase == HookController.HookPhase.Attached)
        {
            hookedSinceFire = true;
        }

        if (lastPhase == HookController.HookPhase.Charging && phase == HookController.HookPhase.Flying)
        {
            hookedSinceFire = false;
        }

        // 戻ってきた。一度も引っ掛からなかったなら「外した」
        if (lastPhase != HookController.HookPhase.Idle && phase == HookController.HookPhase.Idle && !hookedSinceFire)
        {
            misses++;
            if (target != null && misses >= missesBeforeGiveUp)
            {
                givenUp[target] = Time.time + giveUpSeconds;
                SetTarget(null);
            }
        }

        lastPhase = phase;
    }

    /// <summary>
    /// 自分が投げた素材のうち、消えた物と、**ほかの人が引っ掛けた物**を忘れる
    /// （ほかの人が触ったら、もう自分が投げた物ではないので、また狙ってよい）。
    /// </summary>
    private void ForgetThrownTakenByOthers()
    {
        if (thrownByMe.Count == 0)
        {
            return;
        }

        HookableObject mine = hook.AttachedItem;
        thrownByMe.RemoveWhere(item =>
        {
            if (item == null || item.IsVanished)
            {
                return true;
            }

            if (item == mine)
            {
                return false;
            }

            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            bool claimedOnline = netSupply != null && netSupply.IsSpawned && netSupply.IsClaimed;
            return item.IsHooked || claimedOnline;
        });
    }

    // ------------------------------------------------------------
    // 素材を探して、撃つ場所まで歩く
    // ------------------------------------------------------------

    private void HandleSeek()
    {
        Vector3? goal = GoalPosition();
        if (!goal.HasValue)
        {
            // 自分のチームのゴールが無い（チーム数が足りないマップなど）。何もしない
            pad.Move = Vector2.zero;
            return;
        }

        // 歩ける床の地図（マップごとに1回作る）。作れなければ、まっすぐ歩く簡易版になる
        SpaceJunkBotNavMesh.EnsureBuilt(body);

        if (!IsPickable(target))
        {
            ChooseTarget(goal.Value);
        }
        else if (Time.time >= replanAt ||
                 FlatDistance(target.transform.position, plannedItemPosition) > ReplanMoveDistance)
        {
            // 素材が動いた・しばらくたった。立つ場所と道を引き直す（行けなくなっていたらあきらめる）
            if (!Plan(target, goal.Value))
            {
                GiveUp(target);
            }
        }

        if (target == null)
        {
            pad.Move = Vector2.zero;
            return;
        }

        Vector3 itemPosition = target.transform.position;
        float toStand = FlatDistance(transform.position, standPoint);

        // **撃つ場所に着いた。** 止まって素材にカーソルを合わせ、少し待ってから撃つ
        if (toStand < ArriveDistance)
        {
            pad.Move = Vector2.zero;
            PointAt(itemPosition);

            if (arrivedAt < 0f)
            {
                arrivedAt = Time.time;
            }

            // 着いてすぐには撃たない（体の向きがそろうのを待つ）
            if (Time.time - arrivedAt >= thinkSeconds)
            {
                pad.SetThrow(true); // RT を押して溜め始める
            }
            return;
        }

        arrivedAt = -1f;

        // 進めなくなったら、まず道を引き直す。それでも進めなければ、その素材をあきらめる
        if (IsStuck(RemainingPathLength()))
        {
            stuckCount++;
            if (stuckCount >= 2 || !Plan(target, goal.Value))
            {
                GiveUp(target);
                pad.Move = Vector2.zero;
                return;
            }
        }

        FollowPath();
    }

    /// <summary>
    /// **次に狙う素材を決める。** 近い順に試し、立つ場所と道が見つかった最初の素材にする
    /// （壁の向こうで回り込めない物・上の段で行けない物は、しばらくあきらめる）。
    /// </summary>
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

        // 1フレームで調べすぎないよう、近い順に数個まで。だめだった物はしばらく外す
        int tries = Mathf.Min(candidates.Count, MaxPlanTriesPerFrame);
        for (int i = 0; i < tries; i++)
        {
            HookableObject item = candidates[i];
            if (Plan(item, goal))
            {
                SetTarget(item);
                return;
            }

            givenUp[item] = Time.time + giveUpSeconds;
        }
    }

    /// <summary>狙う候補にしてよい素材か（拾える・ほかのボットが狙っていない・あきらめていない・自分が投げていない・ゴールのすぐ近くでない）。</summary>
    private bool IsCandidate(HookableObject item, Vector3 goal)
    {
        if (!IsPickable(item) || Reserved.Contains(item) || thrownByMe.Contains(item))
        {
            return false;
        }

        if (givenUp.TryGetValue(item, out float until) && Time.time < until)
        {
            return false;
        }

        // もうゴールの近くにある物は、そのまま点になるので触らない
        return FlatDistance(item.transform.position, goal) >= ignoreNearGoalDistance;
    }

    /// <summary>
    /// **素材を撃つための、立つ場所と道を決める。** 見つからなければ false。
    ///
    /// 素材のまわりに立つ場所の候補をいくつも作り、次の条件で選ぶ。
    ///
    /// | 条件 | なぜ |
    /// | --- | --- |
    /// | 歩ける床の上で、**素材と同じ高さ** | 上の段・下の段にある素材は、そこへ上がらないとフックが当たらない |
    /// | 自分からそこまで**歩いて行ける**（壁は回り込む） | 壁に向かって押し続けないように |
    /// | そこから素材まで、**間に壁が無い** | 壁があるとフックが当たらない |
    /// | そこからゴールへ**ちょうど届く距離**・投げる道に壁が無い | 満たさなくても選べるが、点が悪くなる |
    ///
    /// 歩ける床の地図が無いときは、素材とゴールを結ぶ線の上の1点だけを使い、まっすぐ歩く（前の簡易版の動き）。
    /// </summary>
    private bool Plan(HookableObject item, Vector3 goal)
    {
        Vector3 itemPosition = item.transform.position;
        Vector3 ideal = StandPoint(item, itemPosition, goal);

        plannedItemPosition = itemPosition;
        replanAt = Time.time + ReplanSeconds;
        stuckCount = 0;
        ResetStuck();

        if (!SpaceJunkBotNavMesh.Ready)
        {
            standPoint = ideal;
            pathCorners.Clear();
            pathCorners.Add(ideal);
            cornerIndex = 0;
            return true;
        }

        if (!SpaceJunkBotNavMesh.TryFindFloor(transform.position, 2f, out Vector3 myFloor))
        {
            return false;
        }

        float reach = HookReach();
        float throwMax = MaxThrowDistance(item) * throwRangeUse;
        float throwMin = MinThrowDistance(item);
        float idealRadius = Mathf.Clamp(FlatDistance(ideal, itemPosition), minHookDistance, reach);

        float bestScore = float.MaxValue;
        bool found = false;

        // 候補：まず線の上の理想の1点、次に素材のまわりの輪（3つの半径 × 12方向）
        for (int ring = -1; ring < 3; ring++)
        {
            int directions = ring < 0 ? 1 : 12;
            float radius = ring switch
            {
                0 => idealRadius,
                1 => Mathf.Lerp(minHookDistance, reach, 0.6f),
                _ => minHookDistance + 0.5f,
            };

            for (int d = 0; d < directions; d++)
            {
                Vector3 candidate = ring < 0
                    ? new Vector3(ideal.x, itemPosition.y, ideal.z)
                    : itemPosition + Quaternion.Euler(0f, d * 30f, 0f) * Vector3.forward * radius;

                if (!TryScoreStandPoint(candidate, itemPosition, item, goal, myFloor, reach, throwMin, throwMax,
                        out Vector3 floor, out float score) || score >= bestScore)
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

        // 道の最初の点は出発点なので、その次から歩く
        cornerIndex = pathCorners.Count > 1 ? 1 : 0;
        return found;
    }

    /// <summary>
    /// 立つ場所の候補 <paramref name="candidate"/> を調べ、使えるなら点（小さいほどよい）をつける。
    /// 使えれば true で、<see cref="navPath"/> にそこまでの道が入る。
    /// </summary>
    private bool TryScoreStandPoint(Vector3 candidate, Vector3 itemPosition, HookableObject item, Vector3 goal,
        Vector3 myFloor, float reach, float throwMin, float throwMax, out Vector3 floor, out float score)
    {
        score = float.MaxValue;

        // 歩ける床の上で、素材と同じ高さか（上の段・下の段ではないか）
        if (!SpaceJunkBotNavMesh.TryFindFloor(candidate, 1.5f, out floor) ||
            Mathf.Abs(floor.y - itemPosition.y) > SameLevelHeight)
        {
            return false;
        }

        // フックが届く距離か
        float toItem = FlatDistance(floor, itemPosition);
        if (toItem > reach || toItem < minHookDistance * 0.8f)
        {
            return false;
        }

        // 素材との間に壁が無いか（手元の高さから見る）
        if (IsBlocked(floor + Vector3.up * HandHeight, itemPosition))
        {
            return false;
        }

        // 歩いて行けるか（壁は回り込む）
        if (!SpaceJunkBotNavMesh.TryFindPath(myFloor, floor, navPath))
        {
            return false;
        }

        score = PathLength(navPath.corners);

        // ゴールへちょうど届く距離でなければ減点（中継になる・飛びすぎる）
        float toGoal = FlatDistance(floor, goal);
        if (toGoal > throwMax || toGoal < throwMin)
        {
            score += 30f;
        }

        // 頭上からゴールへの間に壁があれば減点
        if (IsBlocked(floor + Vector3.up * OverheadHeight, goal + Vector3.up * 1.5f))
        {
            score += 15f;
        }

        return true;
    }

    /// <summary>
    /// <paramref name="from"/> から <paramref name="to"/> の間に、**壁や床（動かない物）**があるか。
    /// 素材（狙っている物もほかの物も）・プレイヤーの体・すり抜ける判定・転がる物は数えない。
    /// </summary>
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

            Rigidbody body = hit.attachedRigidbody;
            if (body != null && !body.isKinematic)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>道の曲がり角を順にたどって歩く。曲がり角に着いたら次へ。</summary>
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

    /// <summary>いまの場所から、道の残り（次の曲がり角 → … → 立つ場所）の長さ。</summary>
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

    private static float PathLength(Vector3[] corners)
    {
        float length = 0f;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            length += Vector3.Distance(corners[i], corners[i + 1]);
        }
        return length;
    }

    /// <summary>その素材をしばらくあきらめる。</summary>
    private void GiveUp(HookableObject item)
    {
        if (item != null)
        {
            givenUp[item] = Time.time + giveUpSeconds;
        }
        SetTarget(null);
    }

    /// <summary>
    /// **どこに立って撃つか。** 素材とゴールを結ぶ線の上で、「素材にフックが届き」「投げればゴールにちょうど届く」場所。
    ///
    /// - ふつう … 素材からゴールのほうへ寄った所（素材を背にしてゴールへ投げる）
    /// - 素材がゴールに近すぎる … いちばん弱く投げても飛びすぎるので、**素材の向こう側**（ゴールから離れる側）に立つ
    /// - 1回では届かないほど遠い … フックが届くいちばんゴール寄りの場所（そこから思いきり投げて中継する）
    ///
    /// 壁や高さは見ていない。**理想の1点**として <see cref="Plan"/> が最初に試す（だめなら、まわりの候補を探す）。
    /// </summary>
    private Vector3 StandPoint(HookableObject material, Vector3 item, Vector3 goal)
    {
        Vector3 toGoal = goal - item;
        toGoal.y = 0f;
        float itemToGoal = toGoal.magnitude;

        float reach = HookReach();
        float throwMax = MaxThrowDistance(material) * throwRangeUse;
        float throwMin = MinThrowDistance(material);

        // 素材からゴールのほうへ、どれだけ寄った所に立つか（マイナスなら素材の向こう側）。
        // 立った場所からゴールまでは「itemToGoal − along」なので、それが throwMin〜throwMax に入るようにする
        float nearest = itemToGoal - throwMax;  // これより手前（素材寄り）に立つと届かない
        float farthest = itemToGoal - throwMin; // これよりゴール寄りに立つと飛びすぎる

        float along;

        if (Mathf.Max(minHookDistance, nearest) <= farthest)
        {
            // 1回では届かないときは、届くいちばんゴール寄りから中継する
            along = Mathf.Min(Mathf.Max(minHookDistance, nearest), reach);
        }
        else
        {
            // ゴールに近すぎる。素材の向こう側に回る
            along = Mathf.Clamp(Mathf.Min(farthest, -minHookDistance), -reach, -minHookDistance);
        }

        Vector3 direction = itemToGoal > 0.01f ? toGoal / itemToGoal : Vector3.forward;
        Vector3 point = item + direction * along;
        point.y = transform.position.y;
        return point;
    }

    /// <summary>
    /// **狙ってよい素材か。** 素材であること・消えていないこと・誰も引っ掛けていないこと・
    /// 重いデブリでないこと（右で撃つと引っ掛からないため）。
    /// </summary>
    private static bool IsPickable(HookableObject item)
    {
        if (item == null || item.IsVanished || item.IsHooked)
        {
            return false;
        }

        if (item.GetComponent<SpaceJunkMaterial>() == null || item.GetComponent<HeavyHookable>() != null)
        {
            return false;
        }

        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        return netSupply == null || !netSupply.IsSpawned || !netSupply.IsClaimed;
    }

    // ------------------------------------------------------------
    // フックを溜めて撃つ
    // ------------------------------------------------------------

    /// <summary>RT を押したまま、素材まで届く長さになったら離す。</summary>
    private void HandleCharging()
    {
        pad.Move = Vector2.zero;

        if (target == null)
        {
            pad.SetThrow(false); // 狙う物が無くなった。その場で撃ってしまう
            return;
        }

        Vector3 itemPosition = target.transform.position;
        PointAt(itemPosition);

        float need = FlatDistance(hook.HandPoint != null ? hook.HandPoint.position : transform.position, itemPosition) + 0.5f;
        bool longEnough = hook.ReachAtCurrentCharge >= need || hook.Charge >= 1f;

        pad.SetThrow(!longEnough);
    }

    // ------------------------------------------------------------
    // 頭上に持って、ゲージを見て投げる
    // ------------------------------------------------------------

    /// <summary>
    /// 投げるゲージが出ている間。カーソルをゴールへ向け、**ゴールまでちょうど届く強さになる位置**で RT を押す。
    /// ゲージは左端から右端へ1回だけ動き、真ん中ほど強い。ボットはいつも**真ん中より右側**で押す
    /// （出た直後は押しても効かない時間があるので、左側は使わない）。
    /// </summary>
    private void HandleThrowGauge()
    {
        Vector3? goal = GoalPosition();
        if (!goal.HasValue)
        {
            return; // 押さなければ、右端で後ろへ落とす
        }

        PointAt(goal.Value);

        if (pressAt < 0f)
        {
            HookableObject held = hook.AttachedItem;
            float distance = FlatDistance(transform.position, goal.Value);
            float accuracy = AccuracyFor(distance, held, goal.Value.y);

            // 真ん中（0.5）から、強さを落とすぶんだけ右へずらす。押し損ねて右端（後ろへ落とす）にならないよう 0.95 まで
            pressAt = Mathf.Clamp(0.5f + (1f - accuracy) * 0.5f + Random.Range(-gaugeError, gaugeError), 0.5f, 0.95f);
        }

        if (thrower.GaugePosition >= pressAt)
        {
            // いま頭上に持っている物を「自分が投げた物」として覚える（このフレームで投げるので、まだ持っている）
            HookableObject thrown = hook.AttachedItem;
            if (thrown != null)
            {
                thrownByMe.Add(thrown);
            }

            pad.SetThrow(true);
            pressAt = -1f;
            pauseUntil = Time.time + afterThrowPause;
            misses = 0;
            SetTarget(null);
        }
    }

    /// <summary>ゴールまで <paramref name="distance"/> m 投げるには、ゲージの真ん中にどれだけ近づければよいか（0〜1）。</summary>
    private float AccuracyFor(float distance, HookableObject held, float goalHeight)
    {
        float best = 1f;
        float bestError = float.MaxValue;

        for (int i = 0; i <= 50; i++)
        {
            float accuracy = i / 50f;
            float error = Mathf.Abs(EstimateThrowDistance(accuracy, held, goalHeight) - distance);
            if (error < bestError)
            {
                bestError = error;
                best = accuracy;
            }
        }

        return best;
    }

    /// <summary>いちばん強く投げたときに届く距離（m）の見積もり。</summary>
    private float MaxThrowDistance(HookableObject item)
    {
        return EstimateThrowDistance(1f, item, transform.position.y);
    }

    /// <summary>いちばん弱く投げた（ゲージの端で押した）ときに届く距離（m）の見積もり。少し余裕を足してある。</summary>
    private float MinThrowDistance(HookableObject item)
    {
        return EstimateThrowDistance(0f, item, transform.position.y) * 1.1f;
    }

    /// <summary>
    /// **ゲージを押した位置（真ん中への近さ）から、投げた物が飛ぶ水平の距離を見積もる。**
    /// 投げる力（<see cref="ThrowController.ThrowForceAtEdge"/>〜<see cref="ThrowController.ThrowForceAtCenter"/>）を
    /// 物の重さで割って速さにし、頭上の高さから放物線で落ちるまでの距離を出す（空気の抵抗も大まかに入れる）。
    /// </summary>
    private float EstimateThrowDistance(float accuracy, HookableObject item, float landHeight)
    {
        float mass = 1f;
        float damping = 0f;
        float startHeight = transform.position.y + 2.6f; // 頭上の高さ（ThrowController の Overhead Height の初期値）

        if (item != null && item.Body != null)
        {
            mass = Mathf.Max(0.01f, item.Body.mass);
            damping = item.Body.linearDamping;
            if (hook.Phase == HookController.HookPhase.Attached)
            {
                startHeight = item.transform.position.y;
            }
        }

        float force = Mathf.Lerp(thrower.ThrowForceAtEdge, thrower.ThrowForceAtCenter, accuracy);
        float horizontal = force / mass;
        float vertical = thrower.ThrowLift / mass;
        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        float height = Mathf.Max(0f, startHeight - landHeight);

        // height + vertical*t - gravity*t*t/2 = 0 の、正のほうの t（落ちるまでの秒数）
        float time = (vertical + Mathf.Sqrt(vertical * vertical + 2f * gravity * height)) / gravity;

        float distance = damping > 0.001f
            ? horizontal * (1f - Mathf.Exp(-damping * time)) / damping
            : horizontal * time;

        return distance * throwDistanceScale;
    }

    // ------------------------------------------------------------
    // ゴール
    // ------------------------------------------------------------

    /// <summary>
    /// 運ぶ先（ゴールの真ん中）。Goal Override があればそこ、無ければ**自分のチームが持っているゴール**。
    /// 見つからなければ null。
    /// </summary>
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
    // コントローラーの操作
    // ------------------------------------------------------------

    /// <summary>
    /// <paramref name="destination"/> へ向かって左スティックを倒す。カーソルも行き先に置く（体が進む方を向く）。
    /// スティックは**画面の上下左右**なので、カメラの水平の向きのぶんだけ逆に回してから入れる。
    /// </summary>
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

    /// <summary>カーソルを、地面の <paramref name="worldPoint"/> が映っている画面の位置に置く。</summary>
    private void PointAt(Vector3 worldPoint)
    {
        Camera view = viewCamera != null ? viewCamera : Camera.main;
        if (view == null)
        {
            return;
        }

        Vector3 screen = view.WorldToScreenPoint(worldPoint);
        if (screen.z > 0f)
        {
            pad.SetPointer(screen);
        }
    }

    // ------------------------------------------------------------
    // 小さな部品
    // ------------------------------------------------------------

    /// <summary>フックが届く長さ（使うぶんだけ）。</summary>
    private float HookReach()
    {
        return hook.MaxRange * hookReachUse;
    }

    /// <summary>狙う素材を変える（ほかのボットに「狙っている」と知らせる印も付け替える）。</summary>
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

    /// <summary>
    /// **進めなくなったか。** 目的地までの距離が <see cref="stuckSeconds"/> のあいだ 0.3m も縮まなければ true。
    /// </summary>
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

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
