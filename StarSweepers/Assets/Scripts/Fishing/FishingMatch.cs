using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>試合がどう終わるか。**片方に差し替えられるように enum にしている。**</summary>
public enum MatchEndRule
{
    /// <summary>制限時間で終わる（時間切れの時点で点が多いチームの勝ち）</summary>
    TimeLimit,

    /// <summary>目標点に先に届いたチームの勝ち</summary>
    TargetScore
}

/// <summary>試合がいまどの段階か。</summary>
public enum MatchPhase
{
    /// <summary>対戦中</summary>
    Playing,

    /// <summary>決着がついて結果を出している</summary>
    Result
}

/// <summary>
/// **オンラインの試合のまとめ役。** 釣りのオンラインシーンに1つ置く。
///
/// ホストだけが中身を決めて、結果を全員へ配る
/// （`ネットワークの制作工程.md` の「動きは本人、勝敗と点数はホスト」の形）。
///
/// 持っているもの：
///   ・**チーム数**（人数から自動で決まる。<see cref="FishingTeams"/> のルール）
///   ・**チームごとの点数**
///   ・**4つのゴールを、どのチームのものにするか**（人数が変わると割り当て直す）
///
/// 点を足すのは <see cref="FishingNetPocket"/> から。表示は <see cref="FishingStatusUI"/>。
/// </summary>
public class FishingMatch : NetworkBehaviour
{
    /// <summary>シーンにある試合のまとめ役。ゴールや表示から探すために持っている。</summary>
    public static FishingMatch Current { get; private set; }

    /// <summary>
    /// いま遊んでよいか。**結果が出たあとは動かせなくする**ために使う。
    /// 1人用のシーンには試合のまとめ役がいないので、そのときは常に true。
    /// </summary>
    public static bool PlayAllowed => Current == null || Current.IsPlaying;

    [Header("決着のつけ方")]
    [Tooltip("試合がどう終わるか。**制限時間と目標点を切り替えられる**")]
    [SerializeField] private MatchEndRule endRule = MatchEndRule.TimeLimit;

    [Tooltip("制限時間（秒）。End Rule が TimeLimit のときに使う")]
    [SerializeField] private float timeLimitSeconds = 180f;

    [Tooltip("目標点。End Rule が TargetScore のときに使う")]
    [SerializeField] private int targetScore = 10;

    /// <summary>いま何チームに分かれているか。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<int> teamCount = new NetworkVariable<int>(1);

    /// <summary>
    /// チームごとの点数。**書き換えられるのはホストだけ**（初期設定のまま）。
    /// 長さは常に <see cref="FishingTeams.MaxTeams"/>。
    /// </summary>
    private readonly NetworkList<int> teamScores = new NetworkList<int>();

    /// <summary>
    /// 4つのゴールの持ち主。中身はチーム番号か <see cref="FishingTeams.NeutralTeam"/>。
    /// 並び順はゴールの Pocket Index に対応する。
    /// </summary>
    private readonly NetworkList<int> pocketOwners = new NetworkList<int>();

    /// <summary>直前に起きたこと（「北のゴールに 青チーム +1」など）。表示用。</summary>
    private readonly NetworkVariable<FixedString128Bytes> lastEvent =
        new NetworkVariable<FixedString128Bytes>();

    /// <summary>いまの段階（対戦中／結果）。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<MatchPhase> phase = new NetworkVariable<MatchPhase>(MatchPhase.Playing);

    /// <summary>
    /// 試合が終わる時刻。**全員で同期された時計（ServerTime）で持つ**ので、
    /// どのPCでも同じ残り時間が出る。
    /// </summary>
    private readonly NetworkVariable<double> endServerTime = new NetworkVariable<double>(0d);

    /// <summary>勝ったチーム。-1 なら引き分け。</summary>
    private readonly NetworkVariable<int> winnerTeam = new NetworkVariable<int>(-1);

    /// <summary>いま何チームに分かれているか。</summary>
    public int TeamCount => teamCount.Value;

    /// <summary>いまの段階。</summary>
    public MatchPhase Phase => phase.Value;

    /// <summary>対戦中か。</summary>
    public bool IsPlaying => phase.Value == MatchPhase.Playing;

    /// <summary>決着のつけ方。</summary>
    public MatchEndRule EndRule => endRule;

    /// <summary>目標点（End Rule が TargetScore のとき）。</summary>
    public int TargetScore => targetScore;

    /// <summary>勝ったチーム。-1 なら引き分け。</summary>
    public int WinnerTeam => winnerTeam.Value;

    /// <summary>
    /// 残り秒数。**制限時間のときだけ意味を持つ。**
    /// 全員で同期された時計から計算するので、どのPCでも同じ値になる。
    /// </summary>
    public float RemainingSeconds
    {
        get
        {
            if (endRule != MatchEndRule.TimeLimit || NetworkManager == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(endServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>直前に起きたことの文章。</summary>
    public string LastEvent => lastEvent.Value.ToString();

    /// <summary>
    /// 直前のことが起きた時刻（このPCの時計）。少しの間だけ表示するために使う。
    /// 始まった直後に表示されないよう、はじめは「ずっと前」（-999 秒）にしておく。
    /// </summary>
    public float LastEventTime { get; private set; } = -999f;

    /// <summary>前回チーム分けをしたときの人数。人の出入りに気づくために控える。-1 はまだ一度も分けていない。</summary>
    private int lastKnownPlayerCount = -1;

    /// <summary>
    /// 全員のPCで呼ばれる。ホストはここで点数の枠とゴールの割り当てを用意して試合を始める。
    /// 参加者もホストも、「ゴールの持ち主が変わったら色を塗り直す」ための購読をする。
    /// </summary>
    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            // 点数の枠をチームの最大数ぶん用意する（0で埋める）
            teamScores.Clear();
            for (int i = 0; i < FishingTeams.MaxTeams; i++)
            {
                teamScores.Add(0);
            }

            // ゴールの持ち主の枠。いったん仮に 0 番チームで埋め、すぐ下の RebuildTeams で正しい持ち主に差し替える
            pocketOwners.Clear();
            for (int i = 0; i < FishingTeams.PocketCount; i++)
            {
                pocketOwners.Add(0);
            }

            RebuildTeams();

            // 試合開始。**このシーンに来た時点から数え始める**
            phase.Value = MatchPhase.Playing;
            winnerTeam.Value = -1;
            endServerTime.Value = NetworkManager.ServerTime.Time + timeLimitSeconds;
        }

        lastEvent.OnValueChanged += OnLastEventChanged;
        pocketOwners.OnListChanged += OnPocketOwnersChanged;

        ApplyPocketOwnersToScene();
    }

    public override void OnNetworkDespawn()
    {
        lastEvent.OnValueChanged -= OnLastEventChanged;
        pocketOwners.OnListChanged -= OnPocketOwnersChanged;

        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>ホストだけが動かす。人数の変化を見張り、決着がついたかを毎フレーム確かめる。</summary>
    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        // 人が入ったり抜けたりしたら、チーム数とゴールの割り当てを作り直す
        int playerCount = FishingNetPlayer.All.Count;
        if (playerCount != lastKnownPlayerCount)
        {
            RebuildTeams();
        }

        CheckMatchEnd();
    }

    // ------------------------------------------------------------
    // 決着（ホストだけが判定する）
    // ------------------------------------------------------------

    /// <summary>
    /// 試合が終わったかを見る。**決着のつけ方はここ1か所だけ。**
    /// 制限時間と目標点を入れ替えたいときも、このメソッドを直せばよい。
    /// </summary>
    private void CheckMatchEnd()
    {
        if (phase.Value != MatchPhase.Playing)
        {
            return;
        }

        switch (endRule)
        {
            case MatchEndRule.TargetScore:
                for (int team = 0; team < teamCount.Value; team++)
                {
                    if (ScoreOf(team) >= targetScore)
                    {
                        FinishMatch();
                        return;
                    }
                }
                break;

            case MatchEndRule.TimeLimit:
            default:
                if (NetworkManager.ServerTime.Time >= endServerTime.Value)
                {
                    FinishMatch();
                }
                break;
        }
    }

    /// <summary>決着をつける。点が一番多いチームの勝ち。同点なら引き分け。</summary>
    private void FinishMatch()
    {
        int bestTeam = -1;
        int bestScore = int.MinValue;
        bool tied = false;

        // 一番多い点を探す。同じ点が並んだら tied にするが、そのあと更に多い点が出たら引き分けでなくなる
        // （1位どうしが同点のときだけ引き分け。2位以下の同点は関係ない）
        for (int team = 0; team < teamCount.Value; team++)
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

        winnerTeam.Value = tied ? -1 : bestTeam;
        phase.Value = MatchPhase.Result;

        Debug.Log(winnerTeam.Value < 0
            ? $"[FISH] 試合終了。引き分け（{bestScore} 点）"
            : $"[FISH] 試合終了。{FishingTeams.TeamName(winnerTeam.Value)} の勝ち（{bestScore} 点）");
    }

    // ------------------------------------------------------------
    // チーム数とゴールの割り当て（ホストだけが行う）
    // ------------------------------------------------------------

    /// <summary>いまの人数から、チーム数とゴールの持ち主を決め直す。</summary>
    private void RebuildTeams()
    {
        int playerCount = FishingNetPlayer.All.Count;
        lastKnownPlayerCount = playerCount;

        int teams = FishingTeams.TeamCountFor(playerCount);
        teamCount.Value = teams;

        int[] owners = FishingTeams.PocketOwners(teams);
        for (int i = 0; i < pocketOwners.Count && i < owners.Length; i++)
        {
            pocketOwners[i] = owners[i];
        }

        Debug.Log($"[FISH] 人数 {playerCount} 人 → {teams} チーム。" +
                  $"ゴールの持ち主：{string.Join(" / ", owners)}");
    }

    /// <summary>ホストが割り当てを変えると全員のPCで呼ばれる。参加者の画面でもゴールの色が変わるように。</summary>
    private void OnPocketOwnersChanged(NetworkListEvent<int> changeEvent)
    {
        ApplyPocketOwnersToScene();
    }

    /// <summary>ゴールの持ち主を、シーンにあるゴールへ伝える（色を変えるため）。</summary>
    private void ApplyPocketOwnersToScene()
    {
        foreach (FishingNetPocket pocket in FishingNetPocket.All)
        {
            pocket.ApplyOwnerTeam(OwnerTeamOfPocket(pocket.PocketIndex));
        }
    }

    /// <summary>そのゴールを持っているチーム。共通ゴールなら <see cref="FishingTeams.NeutralTeam"/>。</summary>
    public int OwnerTeamOfPocket(int pocketIndex)
    {
        if (pocketIndex < 0 || pocketIndex >= pocketOwners.Count)
        {
            return FishingTeams.NeutralTeam;
        }
        return pocketOwners[pocketIndex];
    }

    // ------------------------------------------------------------
    // 点数
    // ------------------------------------------------------------

    /// <summary>チームの点数。</summary>
    public int ScoreOf(int team)
    {
        if (team < 0 || team >= teamScores.Count)
        {
            return 0;
        }
        return teamScores[team];
    }

    /// <summary>
    /// 点を足す。**ホストだけが呼べる。**
    /// <see cref="FishingNetPocket"/> のホスト側の判定から呼ばれる。
    /// </summary>
    public void AddTeamScore(int team, int amount, string where)
    {
        if (!IsServer || team < 0 || team >= teamScores.Count)
        {
            return;
        }

        teamScores[team] += amount;
        lastEvent.Value = $"{where} のゴールに投入！　{FishingTeams.TeamName(team)} +{amount}";
        LastEventTime = Time.time;
    }

    /// <summary>ホストが文章を変えると全員のPCで呼ばれる（ホスト自身も含む）。</summary>
    private void OnLastEventChanged(FixedString128Bytes before, FixedString128Bytes after)
    {
        // 参加者側でも「いま入った」と分かるように、受け取った時刻を控える
        LastEventTime = Time.time;
    }
}
