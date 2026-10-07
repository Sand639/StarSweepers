using Unity.Netcode;
using UnityEngine;

/// <summary>ラウンドがいまどの段階か。</summary>
public enum SpaceJunkRoundPhase
{
    /// <summary>集めている最中</summary>
    Playing,

    /// <summary>決着がついて結果を出している</summary>
    Result
}

/// <summary>ラウンドの勝ち方。</summary>
public enum SpaceJunkWinRule
{
    /// <summary>**時間いっぱい得点を競う**（2026/9/22 から。いまのルール）</summary>
    Score,

    /// <summary>素材3種類をそろえたチームが、その場で勝つ（2026/9/17 の最初のルール。コードだけ残してある）</summary>
    ThreeKinds
}

/// <summary>
/// **1ラウンドぶんの進行役。** マップ（ステージ）のシーンに1つ置く。
///
/// 勝ち方は2つあり、<see cref="SpaceJunkSession"/> の Win Rule で切り替える（ロビーには出していない）。
///
/// ## 得点制（Score・いまのルール。2026/9/22・大槻さん）
///
/// - **時間いっぱい遊び、得点の高いチームがラウンドを取る**（途中で決着はつかない）
/// - 素材を自陣のゴールに入れると **1点**。同じ種類を2個目以降入れても1点ずつ入る
/// - **ラウンドの途中でイベントが起きる**（初期値は20秒後。<c>SpaceJunkRoundEvents.cs</c>）。
///   いまあるのは「お題セット」で、お題の素材を全部入れるとボーナスが入る
///   （2026/9/29。それまでの「同じ種類を3回続けると +5」はこれに置き換えた）
/// - **1位が同点なら引き分け**で、誰も取らずに次へ行く（全チーム0点も引き分け）
///
/// ## 3種類そろえたら勝ち（ThreeKinds・最初のルール。2026/9/17・大槻さん）
///
/// - **自陣のゴールに素材3種類を1個ずつ入れたチームが、その場でラウンド勝利**
/// - 誰もそろえられないまま最大時間に達したら、**集めた種類が多いチームがラウンドを取る**
/// - **1位が同数（引き分け）なら、そのラウンドは誰も取らずに次へ行く**
/// - 同じ種類を2個目入れても**何も起きない**（消えるだけ）
///
/// ## 誰が判定するか
///
/// **ホストだけ。** 参加者のPCは結果を受け取って出すだけで、一切数えていない
/// （それぞれが数えると、同じ素材で何度も数えてしまう）。
///
/// ## 勝ち数はここに持たない
///
/// ラウンドの勝ち数と設定は <see cref="SpaceJunkSession"/> が持っている。
/// こちらは**シーンと一緒に消える**ので、覚えておきたいものは置かない。
/// </summary>
public partial class SpaceJunkRound : NetworkBehaviour
{
    /// <summary>いま動いているラウンド。ゴールや画面表示から探すために持っている。</summary>
    public static SpaceJunkRound Current { get; private set; }

    /// <summary>
    /// いま動いてよいか。**結果が出たあとは動かせなくする**ために使う。
    /// ラウンドの進行役がいないシーン（釣りなど）では常に true。
    /// </summary>
    public static bool PlayAllowed => Current == null || Current.IsPlaying;

    [Header("最大時間（係がいないときの予備）")]
    [Tooltip("SpaceJunkSession が見つからないときに使う秒数。" +
             "**ふだんはホストがロビーで決めた値が使われる**ので、ここは効かない")]
    [SerializeField] private float fallbackSeconds = 60f;

    [Header("始まりの演出（2026/10/7。スプラトゥーンのようにマップを見せてから 3・2・1・START）")]
    [Tooltip("OFF にすると演出をせず、すぐに始まる")]
    [SerializeField] private bool playIntro = true;

    [Tooltip("全員の読み込みを待つ秒数（演出の前）。遅いPCでも最初から見られるように")]
    [Min(0f)]
    [SerializeField] private float introLeadSeconds = 1.5f;

    [Tooltip("マップをぐるっと見せる秒数")]
    [Min(0f)]
    [SerializeField] private float introFlyoverSeconds = 4f;

    [Tooltip("自分のプレイヤーへカメラが寄る秒数")]
    [Min(0f)]
    [SerializeField] private float introZoomSeconds = 1.5f;

    [Tooltip("3・2・1 のカウントダウンの秒数")]
    [Min(0f)]
    [SerializeField] private float introCountdownSeconds = 3f;

    public float IntroFlyoverSeconds => introFlyoverSeconds;
    public float IntroZoomSeconds => introZoomSeconds;
    public float IntroCountdownSeconds => introCountdownSeconds;

    /// <summary>
    /// **動き出してよい時刻**（全員で同期された時計 ServerTime）。これより前は始まりの演出中で、誰も動けない。
    /// 0 なら演出なし。
    /// </summary>
    private readonly NetworkVariable<double> playStartServerTime = new NetworkVariable<double>(0d);

    /// <summary>動き出してよい時刻（ServerTime）。0 なら演出なし。</summary>
    public double PlayStartServerTime => playStartServerTime.Value;

    /// <summary>始まりの演出が始まる時刻（ServerTime）。</summary>
    public double IntroStartServerTime =>
        playStartServerTime.Value - introFlyoverSeconds - introZoomSeconds - introCountdownSeconds;

    /// <summary>いまの時刻（全員で同期された時計）。</summary>
    public double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

    /// <summary>**始まりの演出中か**（マップの紹介〜カウントダウン）。この間は誰も動けず、素材も出ない。</summary>
    public bool IsIntro =>
        phase.Value == SpaceJunkRoundPhase.Playing && playStartServerTime.Value > 0d && ServerNow < playStartServerTime.Value;

    /// <summary>前のフレームで演出中だったか（演出が終わった瞬間に操作を戻すため）。</summary>
    private bool wasIntro;

    /// <summary>
    /// チームごとの、集めた素材の種類。**1ビットが1種類**（装甲板=1 / 回路基板=2 / 燃料タンク=4）。
    /// 長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。
    /// </summary>
    private readonly NetworkList<int> collected = new NetworkList<int>();

    /// <summary>いまの段階。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<SpaceJunkRoundPhase> phase =
        new NetworkVariable<SpaceJunkRoundPhase>(SpaceJunkRoundPhase.Playing);

    /// <summary>
    /// ラウンドが終わる時刻。**全員で同期された時計（ServerTime）で持つ**ので、
    /// どのPCでも同じ残り時間が出る。
    /// </summary>
    private readonly NetworkVariable<double> endServerTime = new NetworkVariable<double>(0d);

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    private readonly NetworkVariable<int> roundWinner = new NetworkVariable<int>(-1);

    /// <summary>このラウンドの勝ち方。**ホストが開始時に決めて全員に配る。**</summary>
    private readonly NetworkVariable<SpaceJunkWinRule> rule =
        new NetworkVariable<SpaceJunkWinRule>(SpaceJunkWinRule.Score);

    /// <summary>チームごとの得点（得点制のとき）。長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。</summary>
    private readonly NetworkList<int> scores = new NetworkList<int>();

    /// <summary>素材1個を入れたときの得点。</summary>
    public const int PointPerItem = 1;

    /// <summary>このラウンドの勝ち方。</summary>
    public SpaceJunkWinRule Rule => rule.Value;

    /// <summary>そのチームの得点（得点制のとき）。</summary>
    public int ScoreOf(int team)
    {
        return team >= 0 && team < scores.Count ? scores[team] : 0;
    }

    /// <summary>集めている最中か（始まりの演出中は false）。</summary>
    public bool IsPlaying => phase.Value == SpaceJunkRoundPhase.Playing && !IsIntro;

    /// <summary>いまの段階。</summary>
    public SpaceJunkRoundPhase Phase => phase.Value;

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    public int RoundWinner => roundWinner.Value;

    /// <summary>残り秒数。どのPCでも同じ値になる。</summary>
    public float RemainingSeconds
    {
        get
        {
            if (NetworkManager == null)
            {
                return 0f;
            }

            // **始まりの演出中（3・2・1 の間も）は、決めた制限時間のまま止めておく。** START から減り始める（2026/10/7）。
            // そのまま引き算すると、演出の残りのぶん多く出る（60秒なのに 63秒 など）
            if (IsIntro)
            {
                return Mathf.Max(0f, (float)(endServerTime.Value - playStartServerTime.Value));
            }

            return Mathf.Max(0f, (float)(endServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>そのチームが、その種類をもう持っているか。</summary>
    public bool HasKind(int team, SpaceJunkMaterialKind kind)
    {
        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        return (collected[team] & KindBit(kind)) != 0;
    }

    /// <summary>そのチームが集めた種類の数（0〜3）。</summary>
    public int KindCountOf(int team)
    {
        if (team < 0 || team >= collected.Count)
        {
            return 0;
        }

        int mask = collected[team];
        int count = 0;

        for (int i = 0; i < SpaceJunkMaterials.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>種類をビットに直す。</summary>
    private static int KindBit(SpaceJunkMaterialKind kind)
    {
        return 1 << (int)kind;
    }

    /// <summary>そのマップで出る種類が全部そろった状態のビット（3種類のマップなら 0b111）。</summary>
    private static int FullMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < SpaceJunkMaterials.Count; i++)
            {
                if (SpaceJunkMaterials.IsUsed(SpaceJunkMaterials.FromIndex(i)))
                {
                    mask |= 1 << i;
                }
            }
            return mask;
        }
    }

    // ------------------------------------------------------------
    // 出入り
    // ------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            collected.Clear();
            scores.Clear();

            for (int i = 0; i < SpaceJunkTeams.MaxTeams; i++)
            {
                collected.Add(0);
                scores.Add(0);
            }

            rule.Value = SpaceJunkSession.Current != null
                ? SpaceJunkSession.Current.WinRule
                : SpaceJunkWinRule.Score;

            phase.Value = SpaceJunkRoundPhase.Playing;
            roundWinner.Value = -1;

            float seconds = SpaceJunkSession.Current != null
                ? SpaceJunkSession.Current.RoundSeconds
                : fallbackSeconds;

            // **始まりの演出のぶん、動き出す時刻を後ろにずらす。** 残り時間とイベントは、動き出してから数える
            double now = NetworkManager.ServerTime.Time;
            double playStart = playIntro
                ? now + introLeadSeconds + introFlyoverSeconds + introZoomSeconds + introCountdownSeconds
                : 0d;
            playStartServerTime.Value = playStart;

            endServerTime.Value = (playStart > 0d ? playStart : now) + seconds;

            ServerInitEvents(seconds);
        }

        phase.OnValueChanged += OnPhaseChanged;

        // **前のラウンドで止めた操作を、ここで戻す。**（演出中なら、まだ止めておく。演出が終わったら Update で戻す）
        // プレイヤーはシーンをまたいで生き続けるので、止めたままだと次のラウンドで動けなくなる
        wasIntro = IsIntro;
        SetLocalPlayerControlEnabled(!wasIntro);

        // 始まりの演出（カメラとカウントダウン）を、全員のPCで出す
        if (GetComponent<SpaceJunkRoundIntro>() == null)
        {
            gameObject.AddComponent<SpaceJunkRoundIntro>();
        }

        ApplyGoalOwners();

        if (IsServer)
        {
            CheckGoalsArePlayable();
        }
    }

    /// <summary>
    /// **このマップで、全チームがちゃんと戦えるかを見る。**
    ///
    /// ゴールが足りないマップだと、**あるチームは素材を入れる場所が無く、
    /// 絶対にラウンドを取れない**。遊んでいる最中は気づきにくいので、開始時に知らせる。
    ///
    /// （2026/9/20：`SpaceJunkMap03` はコピー元の `FishingMap03` にゴールが1つしか
    /// 置かれておらず、2チーム以上だと片方が何もできない状態だった）
    /// </summary>
    private void CheckGoalsArePlayable()
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

        // ゴールの数そのものは見ない（1対1のマップはゴールが2つ。2026/9/30）。
        // 大事なのは「遊んでいる全チームが自分のゴールを持てるか」なので、下でそれを確かめる。
        // （ロビーでも、遊べないマップはチーム数に合わせて候補から外している）

        // 遊んでいるチームに、持ち場のゴールがあるかを1つずつ見る
        for (int team = 0; team < teamCount; team++)
        {
            bool hasGoal = false;

            foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
            {
                if (goal != null && goal.OwnerTeam == team)
                {
                    hasGoal = true;
                    break;
                }
            }

            if (!hasGoal)
            {
                Debug.LogError(
                    $"[JUNK] {SpaceJunkTeams.TeamName(team)} のゴールが、このマップにありません。" +
                    "**このチームは素材を入れる場所が無く、ラウンドを取れません。**");
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;

        if (Current == this)
        {
            Current = null;
        }
    }

    private void OnPhaseChanged(SpaceJunkRoundPhase before, SpaceJunkRoundPhase after)
    {
        // 結果が出たら動けなくする（結果を見る時間になる）
        SetLocalPlayerControlEnabled(after == SpaceJunkRoundPhase.Playing);
    }

    private void Update()
    {
        // 重いデブリの見た目は、全員のPCで付け外しする
        UpdateHeavyMarks();

        // **始まりの演出が終わった瞬間に、操作を戻す**（全員のPC。START! の合図と同時に動ける）
        bool intro = IsIntro;
        if (wasIntro && !intro && phase.Value == SpaceJunkRoundPhase.Playing)
        {
            SetLocalPlayerControlEnabled(true);
        }
        wasIntro = intro;

        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing || intro)
        {
            return;
        }

        ServerUpdateEvents();

        // 最大時間に達した
        if (NetworkManager.ServerTime.Time >= endServerTime.Value)
        {
            if (rule.Value == SpaceJunkWinRule.Score)
            {
                FinishByScore();
            }
            else
            {
                FinishByCount();
            }
        }
    }

    // ------------------------------------------------------------
    // 素材を受け取る（**ホストだけが呼ぶ**）
    // ------------------------------------------------------------

    /// <summary>
    /// **ゴールに素材が入ったことを受け取る。** <see cref="SpaceJunkGoal"/> から呼ばれる。
    ///
    /// 得点制なら点を足す。3種類ルールなら、すでに持っている種類のときは何もしない（素材は消えるだけ）。
    /// </summary>
    /// <returns>数えたら true（ゴールの床が光る）。</returns>
    public bool ServerCollect(int team, SpaceJunkMaterialKind kind, NetworkObject item = null)
    {
        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return false;
        }

        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        // **誰もいないチームのぶんは数えない。**
        // 数えてしまうと、投げ間違いが積み重なるだけで
        // 「誰もいないチーム」がラウンドを取ってしまう
        if (SpaceJunkSession.Current != null && !SpaceJunkSession.Current.HasPlayers(team))
        {
            return false;
        }

        if (rule.Value == SpaceJunkWinRule.Score)
        {
            ServerAddScore(team, kind, item);
            return true;
        }

        int bit = KindBit(kind);

        if ((collected[team] & bit) != 0)
        {
            // もう持っている種類。**何も起きない**
            return false;
        }

        collected[team] = collected[team] | bit;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を集めました" +
                  $"（{KindCountOf(team)} / {SpaceJunkMaterials.Count} 種類）。");

        // そのマップで出る種類が全部そろったら、その場でラウンド勝利
        if ((collected[team] & FullMask) == FullMask)
        {
            FinishRound(team);
        }

        return true;
    }

    /// <summary>
    /// **得点制の点の足し方。** 1個で1点。イベントが起きていれば、そのぶんのボーナスも足す
    /// （お題セットなら、お題がそろった1個でボーナスが入る）。
    /// </summary>
    private void ServerAddScore(int team, SpaceJunkMaterialKind kind, NetworkObject item)
    {
        // 集めた種類の印も付けておく（画面の表示に使う）
        collected[team] = collected[team] | KindBit(kind);

        int bonus = ServerEventBonusOnCollect(team, kind, item);

        // 真ん中の特殊デブリ（SpaceJunkBonusDebris）なら、その点数も足す
        if (item != null && item.TryGetComponent(out SpaceJunkBonusDebris bonusDebris))
        {
            bonus += bonusDebris.BonusPoints;
        }

        int gained = PointPerItem + bonus;

        scores[team] = scores[team] + gained;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を入れました" +
                  $"（+{gained} 点{(bonus > 0 ? $"。ボーナス {bonus} 点込み" : string.Empty)}／" +
                  $"合計 {scores[team]} 点）。");
    }

    // ------------------------------------------------------------
    // 決着（**ホストだけが判定する**）
    // ------------------------------------------------------------

    /// <summary>
    /// 得点制で、最大時間に達したときの決着。**得点の高いチームがラウンドを取る。**
    /// 1位が同点なら引き分けで、誰も取らない（全チーム0点も引き分け）。
    /// </summary>
    private void FinishByScore()
    {
        int bestTeam = -1;
        int bestScore = -1;
        bool tied = false;

        int teams = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.TeamCount
            : SpaceJunkTeams.MaxTeams;

        for (int team = 0; team < teams; team++)
        {
            int score = ScoreOf(team);

            if (score > bestScore)
            {
                bestScore = score;
                bestTeam = team;
                tied = false;
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }

        FinishRound(tied || bestScore <= 0 ? -1 : bestTeam);
    }

    /// <summary>
    /// 最大時間に達したときの決着。**集めた種類が多いチームがラウンドを取る。**
    /// 1位が同数なら引き分けで、誰も取らない。
    /// </summary>
    private void FinishByCount()
    {
        int bestTeam = -1;
        int bestCount = -1;
        bool tied = false;

        int teams = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.TeamCount
            : SpaceJunkTeams.MaxTeams;

        for (int team = 0; team < teams; team++)
        {
            int count = KindCountOf(team);

            if (count > bestCount)
            {
                bestCount = count;
                bestTeam = team;
                tied = false;
            }
            else if (count == bestCount)
            {
                tied = true;
            }
        }

        // 1位が同数なら引き分け。**誰も集めていない（全員0種類）ときも引き分け**
        FinishRound(tied || bestCount <= 0 ? -1 : bestTeam);
    }

    /// <summary>ラウンドを終わらせて、試合の係へ結果を渡す。</summary>
    private void FinishRound(int winnerTeam)
    {
        if (phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return;
        }

        roundWinner.Value = winnerTeam;
        phase.Value = SpaceJunkRoundPhase.Result;

        Debug.Log(winnerTeam < 0
            ? "[JUNK] ラウンド終了。引き分け（誰もラウンドを取りません）。"
            : $"[JUNK] ラウンド終了。{SpaceJunkTeams.TeamName(winnerTeam)} がラウンドを取りました。");

        if (SpaceJunkSession.Current != null)
        {
            SpaceJunkSession.Current.ServerReportRoundResult(winnerTeam);
        }
        else
        {
            Debug.LogWarning("[JUNK] 試合の係（SpaceJunkSession）がいないので、次のラウンドへ進めません。");
        }
    }

    // ------------------------------------------------------------
    // ゴールの持ち主と、操作の止め方
    // ------------------------------------------------------------

    /// <summary>
    /// **そのゴールが、どのチームのものか。**
    ///
    /// 全員が同じ計算（チーム数 → 割り当て）をするので、通信で送る必要はない。
    /// ゴールの側からも呼べるようにしてある（**生まれる順番がどちらでも取りこぼさないため**）。
    /// </summary>
    public int OwnerTeamOfGoal(int goalIndex)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

        // **このマップにあるゴールも見て決める**（東西だけのマップなら、2チームは東西を使う。2026/9/30）
        int[] owners = SpaceJunkTeams.GoalOwners(teamCount, GoalMaskOfThisMap());

        return owners[Mathf.Clamp(goalIndex, 0, owners.Length - 1)];
    }

    /// <summary>
    /// **このマップにあるゴールの番号**（北=1 / 東=2 / 南=4 / 西=8 を足したもの）。
    /// シーンの切り替え途中に前のマップのゴールが残っていることがあるので、**このシーンのゴールだけ**数える。
    /// </summary>
    private int GoalMaskOfThisMap()
    {
        int mask = 0;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == gameObject.scene &&
                goal.GoalIndex >= 0 && goal.GoalIndex < SpaceJunkTeams.GoalCount)
            {
                mask |= 1 << goal.GoalIndex;
            }
        }

        return mask;
    }

    /// <summary>シーンにあるゴールへ、どのチームのものかをまとめて配る。</summary>
    private void ApplyGoalOwners()
    {
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null)
            {
                goal.ApplyOwnerTeam(OwnerTeamOfGoal(goal.GoalIndex));
            }
        }
    }

    /// <summary>
    /// **このPCで操作しているプレイヤーの、移動とフックを止める／戻す。**
    ///
    /// 釣りでは <c>FishingMatch.PlayAllowed</c> がこの役目をしているが、
    /// 宇宙ごみのシーンには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）。
    /// **釣り側のファイルを一切変えずに済ませるために、ここで直接止めている。**
    /// </summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }

            // **オートエイムも一緒に戻す。** ロビーの設定端末は、開いている間これを止める。
            // ホストは設定を開いたまま「ゲーム開始」を押すので、ここで戻さないと
            // **ホストだけマップでオートエイムが効かない**ままになる
            HookAimAssist aimAssist = player.GetComponent<HookAimAssist>();
            if (aimAssist != null)
            {
                aimAssist.enabled = enabledState;
            }
        }
    }
}
