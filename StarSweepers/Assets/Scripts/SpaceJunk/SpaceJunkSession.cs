using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>試合がいまどの段階か。</summary>
public enum SpaceJunkMatchState
{
    /// <summary>ロビーで待っている（ホストが設定中）</summary>
    Lobby,

    /// <summary>ラウンドを戦っている最中</summary>
    Playing,

    /// <summary>何本先取に届いて、試合そのものが終わった</summary>
    MatchOver
}

/// <summary>
/// 参加者1人ぶんの席。**誰が・どのチームか**だけを持つ。
/// <see cref="NetworkList{T}"/> に入れるため、構造体にして比較できるようにしてある。
/// </summary>
public struct SpaceJunkPlayerSlot : INetworkSerializable, IEquatable<SpaceJunkPlayerSlot>
{
    /// <summary>その人の接続番号。</summary>
    public ulong ClientId;

    /// <summary>所属チーム（0から）。</summary>
    public int Team;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Team);
    }

    public bool Equals(SpaceJunkPlayerSlot other)
    {
        return ClientId == other.ClientId && Team == other.Team;
    }
}

/// <summary>
/// **宇宙ごみ集めの「試合そのもの」を覚えておく係。**
///
/// ## なぜこれが要るのか
///
/// この遊びは**ラウンドごとにマップ（シーン）が切り替わる。**
/// シーンを切り替えると、そのシーンに置いてあった物は全部消えてしまうので、
/// **ラウンドをまたいで覚えておきたいもの**を、ここに集めてある。
///
///   ・ホストが決めた設定（何本先取・1ラウンドの最大時間・使うマップ）
///   ・誰がどのチームか
///   ・チームごとの**ラウンドの勝ち数**
///
/// ## どうやってシーンをまたぐのか
///
/// **プレハブから生まれた通信オブジェクトは、シーンを切り替えても生き続ける**
/// （Netcode が「どのシーンにも属さない場所」へ移してくれる）。
/// 釣りのプレイヤーがロビーから会場へそのまま付いてくるのと同じ仕組み。
///
/// そのため、これは**シーンに置くのではなく、ホストがプレハブから1つだけ出す。**
/// 出す役は SpaceJunkSessionSpawner。
///
/// ## 書き換えられるのはホストだけ
///
/// 設定もチーム分けも勝ち数も、**すべてホストが決めて全員へ配る**
/// （ネットワークの制作工程.md の「動きは本人、勝敗と点数はホスト」の形）。
/// 参加者のPCからは読むだけ。
/// </summary>
public class SpaceJunkSession : NetworkBehaviour
{
    /// <summary>いま動いている試合の係。どこからでも参照できるように持っている。</summary>
    public static SpaceJunkSession Current { get; private set; }

    [Header("戻り先")]
    [Tooltip("試合が終わったあとに全員が戻るシーンの名前。**ビルドの一覧に入っていること**")]
    [SerializeField] private string lobbySceneName = "SpaceJunkLobby";

    [Header("待ち時間")]
    [Tooltip("ラウンドの結果を見せてから、次のマップへ移るまでの秒数")]
    [SerializeField] private float roundResultSeconds = 5f;

    [Tooltip("試合の結果を見せてから、ロビーへ戻るまでの秒数")]
    [SerializeField] private float matchResultSeconds = 8f;

    [Header("ラウンドの勝ち方")]
    [Tooltip("Score＝時間いっぱい得点を競う（いまのルール）。\n" +
             "ThreeKinds＝素材3種類をそろえたチームがその場で勝つ（前のルール。**コードだけ残してある**）。\n" +
             "ロビーには出していない。戻したいときはここを切り替える")]
    [SerializeField] private SpaceJunkWinRule winRule = SpaceJunkWinRule.Score;

    /// <summary>ラウンドの勝ち方。**ホストの値が使われる**（<see cref="SpaceJunkRound"/> が開始時に全員へ配る）。</summary>
    public SpaceJunkWinRule WinRule => winRule;

    [Header("イベント（得点制のときだけ起きる）")]
    [Tooltip("ラウンドの途中で起きるイベントの設定。何秒後に始めるか・候補の一覧・お題セットの個数とボーナス")]
    [SerializeField] private SpaceJunkEventSettings events = new SpaceJunkEventSettings();

    /// <summary>イベントの設定。**ホストの値が使われる**（<see cref="SpaceJunkRound"/> がホストで読む）。</summary>
    public SpaceJunkEventSettings Events => events;

    // ------------------------------------------------------------
    // ホストが決めて全員へ配るもの
    // ------------------------------------------------------------

    /// <summary>いくつのチームに分かれるか。**ホストが決める。**</summary>
    private readonly NetworkVariable<int> teamCount = new NetworkVariable<int>(2);

    /// <summary>何本先取か。**ホストがゲーム開始前に決める。**</summary>
    private readonly NetworkVariable<int> roundsToWin = new NetworkVariable<int>(2);

    /// <summary>1ラウンドの最大時間（秒）。**ホストがゲーム開始前に決める。**</summary>
    private readonly NetworkVariable<float> roundSeconds = new NetworkVariable<float>(60f);

    /// <summary>いまの段階。</summary>
    private readonly NetworkVariable<SpaceJunkMatchState> state =
        new NetworkVariable<SpaceJunkMatchState>(SpaceJunkMatchState.Lobby);

    /// <summary>いま何ラウンド目か（1から）。</summary>
    private readonly NetworkVariable<int> currentRound = new NetworkVariable<int>(0);

    /// <summary>直前のラウンドを取ったチーム。-1 なら引き分けで、誰も取っていない。</summary>
    private readonly NetworkVariable<int> lastRoundWinner = new NetworkVariable<int>(-1);

    /// <summary>試合に勝ったチーム。まだ決まっていなければ -1。</summary>
    private readonly NetworkVariable<int> matchWinner = new NetworkVariable<int>(-1);

    /// <summary>いま遊んでいるマップのシーン名。次のラウンドで同じマップを避けるために持つ。</summary>
    private readonly NetworkVariable<FixedString64Bytes> currentMap =
        new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>誰がどのチームか。</summary>
    private readonly NetworkList<SpaceJunkPlayerSlot> slots = new NetworkList<SpaceJunkPlayerSlot>();

    /// <summary>ホストが選んだ、この試合で使うマップ。</summary>
    private readonly NetworkList<FixedString64Bytes> selectedMaps = new NetworkList<FixedString64Bytes>();

    /// <summary>チームごとのラウンドの勝ち数。長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。</summary>
    private readonly NetworkList<int> roundWins = new NetworkList<int>();

    // ------------------------------------------------------------
    // 読むためのもの（参加者のPCからも使える）
    // ------------------------------------------------------------

    /// <summary>いくつのチームに分かれるか。</summary>
    public int TeamCount => Mathf.Clamp(teamCount.Value, 1, SpaceJunkTeams.MaxTeams);

    /// <summary>何本先取か。</summary>
    public int RoundsToWin => Mathf.Max(1, roundsToWin.Value);

    /// <summary>1ラウンドの最大時間（秒）。</summary>
    public float RoundSeconds => Mathf.Max(5f, roundSeconds.Value);

    /// <summary>いまの段階。</summary>
    public SpaceJunkMatchState State => state.Value;

    /// <summary>いま何ラウンド目か（1から。まだ始まっていなければ 0）。</summary>
    public int CurrentRound => currentRound.Value;

    /// <summary>直前のラウンドを取ったチーム。-1 なら引き分け。</summary>
    public int LastRoundWinner => lastRoundWinner.Value;

    /// <summary>試合に勝ったチーム。まだなら -1。</summary>
    public int MatchWinner => matchWinner.Value;

    /// <summary>いま遊んでいるマップのシーン名。</summary>
    public string CurrentMap => currentMap.Value.ToString();

    /// <summary>4つのゴールの持ち主。並び順は北・東・南・西。</summary>
    public int[] GoalOwners => SpaceJunkTeams.GoalOwners(TeamCount);

    /// <summary>そのチームが取ったラウンド数。</summary>
    public int RoundWinsOf(int team)
    {
        if (team < 0 || team >= roundWins.Count)
        {
            return 0;
        }

        return roundWins[team];
    }

    /// <summary>
    /// **そのチームに誰か入っているか。**
    ///
    /// 誰もいないチームのゴールに素材が入っても数えないようにするために使う。
    /// 入れてしまうと、**投げ間違いだけで「誰もいないチーム」がラウンドを取る**ことがある。
    /// </summary>
    public bool HasPlayers(int team)
    {
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team == team)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>そのチームの人数。ロビーの表示にも使う。</summary>
    public int PlayerCountOf(int team)
    {
        int count = 0;

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>その人の所属チーム。席が無ければ 0。</summary>
    public int TeamOf(ulong clientId)
    {
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.ClientId == clientId)
            {
                return slot.Team;
            }
        }

        return 0;
    }

    /// <summary>
    /// <see cref="Slots"/> と <see cref="SelectedMaps"/> が返す入れ物。**毎回作らず使い回す。**
    ///
    /// ロビーの画面は `OnGUI` で描いていて、**1フレームに何度も呼ばれる。**
    /// そのたびに新しいリストを作ると、捨てるゴミが積み上がって動きが重くなる。
    ///
    /// ⚠ **使い回しているので、1つの `foreach` を回している途中で
    /// もう一度同じものを取りに行かないこと**（中身が入れ替わる）。
    /// 取り出したら、最後まで使い切ってから次を取ること。
    /// </summary>
    private readonly List<SpaceJunkPlayerSlot> slotsView = new List<SpaceJunkPlayerSlot>();

    private readonly List<string> mapsView = new List<string>();

    /// <summary>いま席にいる人を、入った順に返す。ロビーの一覧に使う。</summary>
    public IReadOnlyList<SpaceJunkPlayerSlot> Slots
    {
        get
        {
            slotsView.Clear();
            foreach (SpaceJunkPlayerSlot slot in slots)
            {
                slotsView.Add(slot);
            }
            return slotsView;
        }
    }

    /// <summary>ホストが選んでいるマップ。</summary>
    public IReadOnlyList<string> SelectedMaps
    {
        get
        {
            mapsView.Clear();
            foreach (FixedString64Bytes map in selectedMaps)
            {
                mapsView.Add(map.ToString());
            }
            return mapsView;
        }
    }

    /// <summary>ホストが選んでいるマップの数。**中身が要らないときはこちらを使う**（入れ物を作らない）。</summary>
    public int SelectedMapCount => selectedMaps.Count;

    /// <summary>そのマップが選ばれているか。</summary>
    public bool IsMapSelected(string sceneName)
    {
        FixedString64Bytes value = new FixedString64Bytes(sceneName ?? string.Empty);

        foreach (FixedString64Bytes map in selectedMaps)
        {
            if (map.Equals(value))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------
    // 出入り
    // ------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            // ラウンドの勝ち数の枠を、チームの最大数ぶん用意する
            roundWins.Clear();
            for (int i = 0; i < SpaceJunkTeams.MaxTeams; i++)
            {
                roundWins.Add(0);
            }

            // いまつながっている人の席を作る（ホスト自身を含む）
            slots.Clear();
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                AddSlot(clientId);
            }

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Current == this)
        {
            Current = null;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        AddSlot(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i].ClientId == clientId)
            {
                slots.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 席を作る。**入ってきた人は、いちばん人数の少ないチームに入れておく。**
    /// ホストがあとから自由に動かせるので、これは仮の置き場でしかない。
    /// </summary>
    private void AddSlot(ulong clientId)
    {
        foreach (SpaceJunkPlayerSlot existing in slots)
        {
            if (existing.ClientId == clientId)
            {
                return;
            }
        }

        if (slots.Count >= SpaceJunkTeams.MaxPlayers)
        {
            Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）を超えたので席を作りませんでした（{clientId}）。");
            return;
        }

        slots.Add(new SpaceJunkPlayerSlot { ClientId = clientId, Team = SmallestTeam() });
    }

    /// <summary>いま一番人数の少ないチームの番号。</summary>
    private int SmallestTeam()
    {
        int teams = TeamCount;
        int[] counts = new int[teams];

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team >= 0 && slot.Team < teams)
            {
                counts[slot.Team]++;
            }
        }

        int best = 0;
        for (int i = 1; i < teams; i++)
        {
            if (counts[i] < counts[best])
            {
                best = i;
            }
        }

        return best;
    }

    // ------------------------------------------------------------
    // ホストが設定を変える（**ホストのPCからだけ呼ぶこと**）
    // ------------------------------------------------------------

    /// <summary>チーム数を変える。**減らしたときは、はみ出した人を拾い直す。**</summary>
    public void ServerSetTeamCount(int value)
    {
        if (!IsServer)
        {
            return;
        }

        teamCount.Value = Mathf.Clamp(value, 1, SpaceJunkTeams.MaxTeams);

        for (int i = 0; i < slots.Count; i++)
        {
            if (!SpaceJunkTeams.IsValidTeam(slots[i].Team, teamCount.Value))
            {
                slots[i] = new SpaceJunkPlayerSlot { ClientId = slots[i].ClientId, Team = SmallestTeam() };
            }
        }
    }

    /// <summary>その人を、指定したチームへ移す。</summary>
    public void ServerSetTeamOf(ulong clientId, int team)
    {
        if (!IsServer || !SpaceJunkTeams.IsValidTeam(team, TeamCount))
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].ClientId == clientId)
            {
                slots[i] = new SpaceJunkPlayerSlot { ClientId = clientId, Team = team };
                return;
            }
        }
    }

    /// <summary>いまのチーム数で、全員をランダムに割り振り直す。</summary>
    public void ServerRandomAssign()
    {
        if (!IsServer)
        {
            return;
        }

        int[] assigned = SpaceJunkTeams.RandomAssign(slots.Count, TeamCount);

        for (int i = 0; i < slots.Count; i++)
        {
            slots[i] = new SpaceJunkPlayerSlot { ClientId = slots[i].ClientId, Team = assigned[i] };
        }
    }

    /// <summary>何本先取かを変える。</summary>
    public void ServerSetRoundsToWin(int value)
    {
        if (IsServer)
        {
            roundsToWin.Value = Mathf.Clamp(value, 1, 5);
        }
    }

    /// <summary>1ラウンドの最大時間として、ホストが入れられる一番短い秒数。</summary>
    public const int MinRoundSeconds = 10;

    /// <summary>1ラウンドの最大時間として、ホストが入れられる一番長い秒数。</summary>
    public const int MaxRoundSeconds = 300;

    /// <summary>1ラウンドの最大時間を変える。範囲の外なら端にそろえる。</summary>
    public void ServerSetRoundSeconds(float value)
    {
        if (IsServer)
        {
            roundSeconds.Value = Mathf.Clamp(value, MinRoundSeconds, MaxRoundSeconds);
        }
    }

    /// <summary>使うマップを、選ぶ／選ばないで切り替える。</summary>
    public void ServerToggleMap(string sceneName)
    {
        if (!IsServer || string.IsNullOrEmpty(sceneName))
        {
            return;
        }

        FixedString64Bytes value = new FixedString64Bytes(sceneName);

        for (int i = 0; i < selectedMaps.Count; i++)
        {
            if (selectedMaps[i].Equals(value))
            {
                selectedMaps.RemoveAt(i);
                return;
            }
        }

        selectedMaps.Add(value);
    }

    // ------------------------------------------------------------
    // ラウンドの進行（**ホストだけが動かす**）
    // ------------------------------------------------------------

    /// <summary>
    /// 試合を始める。勝ち数を 0 に戻し、1ラウンド目のマップへ全員で移動する。
    /// **ロビーの「ゲーム開始」から呼ばれる。**
    /// </summary>
    public void ServerStartMatch()
    {
        if (!IsServer)
        {
            return;
        }

        if (selectedMaps.Count == 0)
        {
            Debug.LogError("[JUNK] 使うマップが1つも選ばれていないので、試合を始められません。");
            return;
        }

        for (int i = 0; i < roundWins.Count; i++)
        {
            roundWins[i] = 0;
        }

        currentRound.Value = 1;
        lastRoundWinner.Value = -1;
        matchWinner.Value = -1;
        state.Value = SpaceJunkMatchState.Playing;

        LoadNextMap();
    }

    /// <summary>
    /// **ラウンドの決着を受け取る。** SpaceJunkRound から呼ばれる。
    ///
    /// 引き分け（<paramref name="winnerTeam"/> が -1）のときは
    /// **誰もラウンドを取らず、次のラウンドへ行く。**
    /// </summary>
    public void ServerReportRoundResult(int winnerTeam)
    {
        if (!IsServer)
        {
            return;
        }

        // **黙って捨てない。** ここで捨てると、結果の表示が出たまま次へ進まず、
        // 何が起きているのか画面からは分からなくなる
        if (state.Value != SpaceJunkMatchState.Playing)
        {
            Debug.LogError(
                $"[JUNK] 試合が始まっていない状態（{state.Value}）で、ラウンドの結果を受け取りました。\n" +
                "次のラウンドへ進めません。**ロビーの「ゲーム開始」から始めてください。**\n" +
                "係がマップへ移る途中で消えている可能性もあります" +
                "（SpaceJunkSessionSpawner の Spawn は destroyWithScene を false にすること）。");
            return;
        }

        lastRoundWinner.Value = winnerTeam;

        if (winnerTeam >= 0 && winnerTeam < roundWins.Count)
        {
            roundWins[winnerTeam] = roundWins[winnerTeam] + 1;

            Debug.Log($"[JUNK] ラウンド {currentRound.Value} は {SpaceJunkTeams.TeamName(winnerTeam)} が取りました" +
                      $"（{roundWins[winnerTeam]} / {RoundsToWin} 本）。");

            if (roundWins[winnerTeam] >= RoundsToWin)
            {
                matchWinner.Value = winnerTeam;
                state.Value = SpaceJunkMatchState.MatchOver;
                StartCoroutine(ReturnToLobbyAfterDelay());
                return;
            }
        }
        else
        {
            Debug.Log($"[JUNK] ラウンド {currentRound.Value} は引き分けでした。誰もラウンドを取らずに次へ進みます。");
        }

        StartCoroutine(NextRoundAfterDelay());
    }

    private IEnumerator NextRoundAfterDelay()
    {
        // ⚠ **WaitForSeconds ではなく WaitForSecondsRealtime を使うこと。**
        //
        // `WaitForSeconds` は `Time.timeScale` の影響を受けるので、時間が止まると
        // **ここで永久に待ち続け、次のラウンドへ進まなくなる。**
        // 残り時間のほうは同期された時計で数えていて止まらないため、
        // 「時間だけ過ぎて試合が進まない」という分かりにくい形で出る。
        //
        // ※ 2026/9/20 から `GamePause` が**つながっている間は時間を止めない**ように
        //    なったので、いまは止まらないはず。**それでも realtime のままにしておく。**
        //    ラウンドの進行を、ポーズの作りに依存させたくないため
        yield return new WaitForSecondsRealtime(roundResultSeconds);

        if (!IsServer || state.Value != SpaceJunkMatchState.Playing)
        {
            yield break;
        }

        currentRound.Value = currentRound.Value + 1;
        LoadNextMap();
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        // 上と同じ理由で、止まらない時計で数える
        yield return new WaitForSecondsRealtime(matchResultSeconds);

        if (!IsServer)
        {
            yield break;
        }

        state.Value = SpaceJunkMatchState.Lobby;
        currentRound.Value = 0;

        LoadScene(lobbySceneName);
    }

    /// <summary>
    /// **次のマップをランダムに選んで、全員で移動する。**
    /// 選ばれているマップが2つ以上あるときは、**直前と同じマップは選ばない。**
    /// </summary>
    private void LoadNextMap()
    {
        List<string> candidates = new List<string>();
        string previous = currentMap.Value.ToString();

        foreach (FixedString64Bytes map in selectedMaps)
        {
            string name = map.ToString();

            if (selectedMaps.Count > 1 && name == previous)
            {
                continue;
            }

            candidates.Add(name);
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[JUNK] 移動できるマップがありません。");
            return;
        }

        string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        currentMap.Value = new FixedString64Bytes(chosen);

        // **読み込めなかったら、試合を打ち切ってロビーへ戻す。**
        // ここで黙って止まると、結果の表示が出たまま永久に動かなくなり、
        // 遊んでいる人には何が起きたのか分からない
        if (!LoadScene(chosen))
        {
            Debug.LogError($"[JUNK] マップ「{chosen}」へ移れませんでした。試合を打ち切ってロビーへ戻ります。");
            AbortToLobby();
        }
    }

    /// <summary>ロビーのシーンの名前。</summary>
    public string LobbySceneName => lobbySceneName;

    /// <summary>
    /// **ポーズ画面の「ロビーに戻る」をホストが押したとき。** 試合を打ち切って、全員をロビーへ戻す。
    /// ホストでなければ何もせず false（<see cref="SpaceJunkLeaveMatch"/> から呼ばれる）。
    /// </summary>
    public bool ServerReturnToLobby()
    {
        if (!IsServer)
        {
            return false;
        }

        StopAllCoroutines();
        AbortToLobby();
        return true;
    }

    /// <summary>試合を打ち切って、全員をロビーへ戻す。</summary>
    private void AbortToLobby()
    {
        state.Value = SpaceJunkMatchState.Lobby;
        currentRound.Value = 0;
        matchWinner.Value = -1;

        LoadScene(lobbySceneName);
    }

    /// <summary>全員のシーンをまとめて切り替える。切り替えを始められたら true。</summary>
    private bool LoadScene(string sceneName)
    {
        if (NetworkManager == null || NetworkManager.SceneManager == null)
        {
            Debug.LogError("[JUNK] SceneManager がまだ使えません。");
            return false;
        }

        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(
            sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[JUNK] シーン「{sceneName}」を読み込めませんでした（{status}）。\n" +
                "よくある原因：\n" +
                "・**File > Build Profiles のシーン一覧に、そのシーンが入っていない**\n" +
                "・NetworkManager の Enable Scene Management が OFF\n" +
                "・別のシーン切り替えがまだ終わっていない（SceneEventInProgress）");

            return false;
        }

        return true;
    }
}
