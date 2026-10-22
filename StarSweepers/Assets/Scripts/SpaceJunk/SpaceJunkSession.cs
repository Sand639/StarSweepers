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
/// 参加者1人ぶんの席。**誰が・どのチームか**を持つ。名前はプレイヤー自身が持つ。
/// <see cref="NetworkList{T}"/> に入れるため、構造体にして比較できるようにしてある。
/// </summary>
public struct SpaceJunkPlayerSlot : INetworkSerializable, IEquatable<SpaceJunkPlayerSlot>
{
    /// <summary>
    /// その人の番号。**1台に1人なら接続番号（ClientId）そのもの。**
    /// 1台で複数人のときの2人目以降は、接続番号に「何人目か」を足した人ごとの番号（<see cref="LocalMultiplayer.MakeKey"/>）。
    /// 接続番号に戻すときは <see cref="LocalMultiplayer.ClientOf"/>。
    /// </summary>
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
///
/// ## 流れ（何がどの順で起きるか）
///
/// 1. **部屋ができる** … ホストがこれをプレハブから出す（<see cref="OnNetworkSpawn"/>）。
///    つながっている人の席を作り、前回のマップの選び方を読み込む。参加者は自分の名前をホストへ送る
/// 2. **ロビー** … 人が入る・抜けるたびに席を直す。ホストは設定端末からチーム数・何本先取・時間・マップを変える
///    （`ServerSet…` のメソッド）。裏では1秒ごとに「席とつながっている人」の答え合わせをする
/// 3. **試合開始** … ロビーの「ゲームを始める」→ <see cref="ServerStartMatch"/>。勝ち数を 0 に戻してマップへ移る
/// 4. **ラウンド** … マップ側の <see cref="SpaceJunkRound"/> が決着を付けて <see cref="ServerReportRoundResult"/> を呼ぶ。
///    何本先取に届かなければ、結果を見せてから次のマップへ（<see cref="NextRoundAfterDelay"/>）
/// 5. **試合終了** … 届いたら <see cref="SpaceJunkMatchState.MatchOver"/> にして、結果を見せてからロビーへ戻る
///    （<see cref="ReturnToLobbyAfterDelay"/>）。ホストがポーズ画面から打ち切ったときは <see cref="ServerReturnToLobby"/>
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

    /// <summary>ホストが選んだ、この試合で使うマップ（「ランダム」のときの候補。ロビーのチェック）。</summary>
    private readonly NetworkList<FixedString64Bytes> selectedMaps = new NetworkList<FixedString64Bytes>();

    /// <summary>
    /// **ホストが「このマップで遊ぶ」と1つ選んだマップ。** 空なら「ランダム」（チェックの付いたマップから選ぶ）。
    /// 1つ選んだときは、**全部のラウンドをそのマップで遊ぶ**（2026/10/6・大槻さん。めっちゃカメレオンのマップ選び）。
    /// </summary>
    private readonly NetworkVariable<FixedString64Bytes> fixedMap = new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>チームごとのラウンドの勝ち数。長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。</summary>
    private readonly NetworkList<int> roundWins = new NetworkList<int>();

    // ------------------------------------------------------------
    // 読むためのもの（参加者のPCからも使える）
    // ------------------------------------------------------------

    /// <summary>いくつのチームに分かれるか。</summary>
    public int TeamCount => Mathf.Clamp(teamCount.Value, 1, SpaceJunkTeams.MaxTeams);

    /// <summary>何本先取か。</summary>
    public int RoundsToWin => Mathf.Max(1, roundsToWin.Value);

    /// <summary>
    /// 1ラウンドの最大時間（秒）。
    /// 5秒は念のための下限（ホストが入れられる下限は <see cref="MinRoundSeconds"/>。壊れた値が届いても一瞬で終わるラウンドにならないように）。
    /// </summary>
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
        int index = TeamSlotIndex(clientId);
        return index >= 0 ? slots[index].Team : 0;
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

    /// <summary>「このマップで遊ぶ」と選ばれたマップのシーン名。空なら「ランダム」。</summary>
    public string FixedMap => fixedMap.Value.ToString();

    /// <summary>マップを「ランダム」で選ぶか（1つに決めていないか）。</summary>
    public bool IsRandomMap => fixedMap.Value.Length == 0;

    /// <summary>
    /// 試合を始められるマップの選び方になっているか。
    /// 1つに決めている、または「ランダム」でチェックの付いたマップが1つ以上ある。
    /// </summary>
    public bool HasPlayableMapChoice => !IsRandomMap || selectedMaps.Count > 0;

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

    /// <summary>
    /// 全員のPCで、この係が現れたとき。
    /// **ホストは席と勝ち数の枠を作って、人の出入りの知らせを受け取り始める。**
    /// </summary>
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

            // 前回ホストをしたときのマップの選び方（チェックと「このマップで遊ぶ」）を読み込む
            LoadSavedMapChoice();
        }
    }

    // ------------------------------------------------------------
    // マップの選び方を覚えておく（2026/10/6・大槻さん「チェックしたかは前回の設定を読み込んでほしい」）
    // ------------------------------------------------------------

    /// <summary>チェックしたマップ（シーン名を改行でつないだもの）を保存する鍵。</summary>
    private const string SavedMapsKey = "StarSweepers.SpaceJunk.SelectedMaps";

    /// <summary>「このマップで遊ぶ」で選んだマップ（空ならランダム）を保存する鍵。</summary>
    private const string SavedFixedMapKey = "StarSweepers.SpaceJunk.FixedMap";

    /// <summary>
    /// 前回のマップの選び方を読み込む（ホストだけ）。**ビルドの一覧に無くなったマップは読み込まない**
    /// （名前を変えた・消したマップがチェックの数に入ってしまわないように）。
    /// </summary>
    private void LoadSavedMapChoice()
    {
        selectedMaps.Clear();

        string saved = PlayerPrefs.GetString(SavedMapsKey, string.Empty);
        foreach (string name in saved.Split('\n'))
        {
            if (!string.IsNullOrEmpty(name) && SpaceJunkLobbyUI.IsSceneInBuildList(name) && !IsMapSelected(name))
            {
                selectedMaps.Add(new FixedString64Bytes(name));
            }
        }

        string fixedName = PlayerPrefs.GetString(SavedFixedMapKey, string.Empty);
        fixedMap.Value = SpaceJunkLobbyUI.IsSceneInBuildList(fixedName)
            ? new FixedString64Bytes(fixedName)
            : default;

        if (selectedMaps.Count > 0 || !IsRandomMap)
        {
            Debug.Log($"[JUNK] 前回のマップの選び方を読み込みました（チェック {selectedMaps.Count} 個／{(IsRandomMap ? "ランダム" : FixedMap)}）。");
        }
    }

    /// <summary>いまのマップの選び方を保存する（ホストだけ）。</summary>
    private void SaveMapChoice()
    {
        PlayerPrefs.SetString(SavedMapsKey, string.Join("\n", SelectedMaps));
        PlayerPrefs.SetString(SavedFixedMapKey, FixedMap);
        PlayerPrefs.Save();
    }

    /// <summary>係が消えるとき（部屋を閉じた・切断した）。ホストは人の出入りの知らせを受け取るのをやめる。</summary>
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

    /// <summary>誰かがつながってきたとき（ホストだけ）。定員を超えていなければ席を作る。</summary>
    private void OnClientConnected(ulong clientId)
    {
        // **定員（6人）を超えてつながってきた人は、断って切る。**（2026/10/14・大槻さん「定員を6人に」）
        // インターネットの部屋は中継サーバーが定員で止めてくれるが、LAN の部屋はつなぐ時点で止める仕組みが無く、
        // 7人目もつながったまま「席が無い（体が出ない）」状態になっていた
        if (IsServer && clientId != NetworkManager.ServerClientId && !HasSlot(clientId) &&
            slots.Count >= SpaceJunkTeams.MaxPlayers)
        {
            Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、接続番号 {clientId} を断りました。");
            NetworkManager.DisconnectClient(clientId, $"満員です（最大{SpaceJunkTeams.MaxPlayers}人）");
            return;
        }

        AddSlot(clientId);
    }

    /// <summary>その接続番号の席が、もうあるか。</summary>
    private bool HasSlot(ulong clientId)
    {
        return TeamSlotIndex(clientId) >= 0;
    }

    // ------------------------------------------------------------
    // 席の答え合わせ（ホストだけ。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>席の答え合わせの間隔（秒）。</summary>
    private const float SlotCheckInterval = 1f;

    /// <summary>次の答え合わせまでの数え上げ（秒）。</summary>
    private float slotCheckTimer;

    /// <summary>「席とつながっている人が食い違う」が何回続いたか。</summary>
    private int slotMismatchCount;

    /// <summary>
    /// 食い違いが何回続いたら席を直すか。
    /// 入ってきた直後は席ができるまで一瞬食い違うので、1回だけでは直さない。
    /// </summary>
    private const int SlotMismatchRepairCount = 2;

    // ------------------------------------------------------------
    // 名前（2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// その人の表示名。名前の番号と同期はプレイヤー自身が持つ。
    /// 人ごとの番号で探すので、同じPCの2人目以降もそれぞれの名前になる。
    /// </summary>
    public string NameOf(ulong clientId)
    {
        // デバッグ用のボットは、従来どおり番号で見分ける。
        if (IsBotKey(clientId))
        {
            return $"BOT {LocalMultiplayer.SeatOf(clientId) - BotSeatStart + 1}";
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.NetworkManager == NetworkManager
                && player.PlayerKey == clientId)
            {
                return player.DisplayName;
            }
        }

        return $"参加者 {clientId}";
    }

    /// <summary>ホストが席と接続中の人を定期的に比べる。名前はPlayerのNetworkVariableが配る。</summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer || NetworkManager == null)
        {
            return;
        }

        // ホストのPC：席の一覧とつながっている人を、一定の間隔で比べる
        slotCheckTimer += Time.unscaledDeltaTime;
        if (slotCheckTimer < SlotCheckInterval)
        {
            return;
        }
        slotCheckTimer = 0f;

        ServerCheckSlots();
    }

    /// <summary>
    /// **席の一覧と、実際につながっている人の一覧を比べて合わせる。**
    /// 入ってきた／抜けたの知らせ（OnClientConnected / OnClientDisconnected）を取りこぼしても、
    /// 「抜けた人の席が残る（そのチームが誰もいないのに数えられる）」「席の無い人がいる（チームの色が付かない）」
    /// のまま残らないようにする。入ってきた直後は一瞬食い違うので、続けて食い違ったときだけ直す。
    /// </summary>
    private void ServerCheckSlots()
    {
        IReadOnlyList<ulong> connected = NetworkManager.ConnectedClientsIds;

        if (!HasSlotMismatch(connected))
        {
            slotMismatchCount = 0;
            return;
        }

        slotMismatchCount++;
        if (slotMismatchCount < SlotMismatchRepairCount)
        {
            return;
        }
        slotMismatchCount = 0;

        ServerRepairSlots(connected);
    }

    /// <summary>
    /// 席の一覧と、つながっている人が食い違っているか。
    /// 「抜けた人の席が残っている」「席の無い人がいる」のどちらかがあれば true。
    /// </summary>
    private bool HasSlotMismatch(IReadOnlyList<ulong> connected)
    {
        // 抜けた人の席が残っていないか
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (!SlotStillValid(connected, slot.ClientId))
            {
                return true;
            }
        }

        // つながっている人全員に席があるか
        foreach (ulong clientId in connected)
        {
            if (TeamSlotIndex(clientId) < 0)
            {
                return true;
            }
        }

        // 1台で複数人のときの2人目以降（ホストが追加で出したプレイヤー）にも、席があるか
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (IsExtraLocalPlayer(player) && TeamSlotIndex(player.PlayerKey) < 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>食い違いを直す。抜けた人の席を消し、席の無い人（2人目以降も含む）の席を作る。</summary>
    private void ServerRepairSlots(IReadOnlyList<ulong> connected)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (!SlotStillValid(connected, slots[i].ClientId))
            {
                Debug.LogWarning($"[NET][答え合わせ] もうつながっていない人（{slots[i].ClientId}）の席が残っていたので消しました。");
                slots.RemoveAt(i);
            }
        }

        foreach (ulong clientId in connected)
        {
            if (TeamSlotIndex(clientId) < 0)
            {
                Debug.LogWarning($"[NET][答え合わせ] 席の無い人（{clientId}）がいたので、席を作りました。");
                AddSlot(clientId);
            }
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (IsExtraLocalPlayer(player) && TeamSlotIndex(player.PlayerKey) < 0)
            {
                Debug.LogWarning($"[NET][答え合わせ] 席の無い人（{player.PlayerKey}）がいたので、席を作りました。");
                AddSlot(player.PlayerKey);
            }
        }
    }

    /// <summary>接続番号の一覧に、その番号が入っているか。</summary>
    private static bool ContainsId(IReadOnlyList<ulong> ids, ulong id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// その席がまだ有効か。1人目（接続番号そのまま）は、そのPCがつながっていれば有効。
    /// 1台で複数人の2人目以降は、そのプレイヤーがまだいれば有効。
    /// </summary>
    private static bool SlotStillValid(IReadOnlyList<ulong> connected, ulong key)
    {
        if (!ContainsId(connected, LocalMultiplayer.ClientOf(key)))
        {
            return false;
        }

        return LocalMultiplayer.SeatOf(key) == 0 || FindPlayerByKey(key) != null;
    }

    /// <summary>1台で複数人のときに、ホストが追加で出した2人目以降のプレイヤーか。</summary>
    private static bool IsExtraLocalPlayer(FishingNetPlayer player)
    {
        return player != null && player.IsSpawned && player.LocalSeat > 0;
    }

    /// <summary>人ごとの番号から、そのプレイヤーの体を探す。いなければ null。</summary>
    private static FishingNetPlayer FindPlayerByKey(ulong key)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.PlayerKey == key)
            {
                return player;
            }
        }
        return null;
    }

    // ------------------------------------------------------------
    // 1台のPCで複数人（2026/10/7）
    // ------------------------------------------------------------
    //
    // ふつうはつないだPC1台につき1人（NetworkManager の Player Prefab）が出る。
    // 1台で複数人のときは、2人目以降を**ホストが同じプレハブから出し、そのPCを持ち主にする**。
    // 持ち主が同じなので、そのPCで動かせる。誰がどの席（P1・P2…）の機器で動くかは LocalSeat で決まる。
    // チーム分けの席は、人ごとの番号（FishingNetPlayer.PlayerKey）で作る。

    /// <summary>
    /// **このPCで遊ぶ人数をホストへ伝える。** 足りなければ出してもらい、多ければ消してもらう。
    /// ロビーにいる間だけ効く。ホストのPCからも参加者のPCからも呼んでよい。
    /// </summary>
    public void RequestLocalPlayerCount(int count)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            ServerApplyLocalPlayerCount(NetworkManager.LocalClientId, count);
        }
        else
        {
            RequestLocalPlayerCountServerRpc(count);
        }
    }

    /// <summary>参加者のPCから「このPCで遊ぶ人数」が届いたとき（ホストで動く）。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestLocalPlayerCountServerRpc(int count, ServerRpcParams rpcParams = default)
    {
        ServerApplyLocalPlayerCount(rpcParams.Receive.SenderClientId, count);
    }

    /// <summary>
    /// そのPCで遊ぶ人数を合わせる（ホストだけ）。多ければ2人目以降を消し、足りなければ出す。
    /// 1人目（接続番号そのもの）はここでは触らない。
    /// </summary>
    private void ServerApplyLocalPlayerCount(ulong clientId, int count)
    {
        if (state.Value != SpaceJunkMatchState.Lobby)
        {
            Debug.LogWarning($"[JUNK] 1台で遊ぶ人数は、ロビーにいる間しか変えられません（接続番号 {clientId}）。");
            return;
        }

        count = Mathf.Clamp(count, 1, LocalMultiplayer.MaxLocalPlayers);

        ServerRemoveExtraLocalPlayers(clientId, count);
        ServerSpawnMissingLocalPlayers(clientId, count);
    }

    /// <summary>そのPCの、<paramref name="count"/> 人目より後ろのプレイヤーを消す（席も消す）。</summary>
    private void ServerRemoveExtraLocalPlayers(ulong clientId, int count)
    {
        // 後ろから回す（消すと FishingNetPlayer.All から抜けるため）
        for (int i = FishingNetPlayer.All.Count - 1; i >= 0; i--)
        {
            FishingNetPlayer player = FishingNetPlayer.All[i];

            // デバッグ用のボット（ホストのPCが持ち主）は、人数の「－」では消さない（ボットの数は別に決める）
            if (!IsExtraLocalPlayer(player) || player.OwnerClientId != clientId || player.LocalSeat < count ||
                player.LocalSeat >= BotSeatStart)
            {
                continue;
            }

            RemoveSlot(player.PlayerKey);
            Debug.Log($"[JUNK] 1台で複数人：接続番号 {clientId} の {player.LocalSeat + 1} 人目を消しました。");
            player.NetworkObject.Despawn(true);
        }
    }

    /// <summary>
    /// そのPCの2人目〜<paramref name="count"/> 人目のうち、まだ出ていない人を Player Prefab から出す（席も作る）。
    /// 定員に達したらそこでやめる。
    /// </summary>
    private void ServerSpawnMissingLocalPlayers(ulong clientId, int count)
    {
        GameObject prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("[JUNK] NetworkManager に Player Prefab が入っていないので、2人目以降を出せません。");
            return;
        }

        for (int seat = 1; seat < count; seat++)
        {
            ulong key = LocalMultiplayer.MakeKey(clientId, seat);
            if (FindPlayerByKey(key) != null)
            {
                continue;
            }

            if (slots.Count >= SpaceJunkTeams.MaxPlayers)
            {
                Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、接続番号 {clientId} の {seat + 1} 人目は出しませんでした。");
                break;
            }

            GameObject spawned = Instantiate(prefab);
            FishingNetPlayer netPlayer = spawned.GetComponent<FishingNetPlayer>();
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (netPlayer == null || networkObject == null)
            {
                Debug.LogError("[JUNK] Player Prefab に FishingNetPlayer か NetworkObject がありません。2人目以降を出せません。");
                Destroy(spawned);
                return;
            }

            // 出す前に「何人目か」を決める（出したときに全員へ一緒に届く）。
            // シーンを切り替えても消えないように出す（1人目と同じ。マップへそのまま付いていく）
            netPlayer.ServerInitLocalSeat(seat);
            networkObject.SpawnWithOwnership(clientId, false);
            AddSlot(key);

            Debug.Log($"[JUNK] 1台で複数人：接続番号 {clientId} の {seat + 1} 人目を出しました。");
        }
    }

    // ------------------------------------------------------------
    // デバッグ用のボット（2026/10/21・大槻さんの依頼）
    // ------------------------------------------------------------
    //
    // 人数がそろわなくても複数人・チーム戦を確かめられるよう、ホストが**ボットのプレイヤー**を足せる。
    // ボットは「ホストのPCの、何人目か」が BotSeatStart 以上のプレイヤーとして出す（1台で複数人の仕組みをそのまま使う）。
    // だから席（チーム分け）・マップへの移動・出てくる場所は、人のプレイヤーと同じ流れで決まる。
    //
    // 体は人と同じ Player Prefab。**ホストのPCでだけ**、見えないコントローラー（VirtualPlayerInput）と
    // ボットの頭（SpaceJunkBot）を付けて動かす。ほかのPCには、ふつうの「ほかの人」として届く。

    /// <summary>
    /// ボットに使う「何人目か」の番号の始まり。**1台で複数人の人数（最大 <see cref="LocalMultiplayer.MaxLocalPlayers"/>）と
    /// 重ならない大きさ**にしてある。人ごとの番号の上の桁（8ビット）に入るので、255 までなら使える。
    /// </summary>
    public const int BotSeatStart = 16;

    /// <summary>出せるボットの数の上限（全体の定員 <see cref="SpaceJunkTeams.MaxPlayers"/> にも止められる）。</summary>
    public const int MaxBots = 7;

    /// <summary>その人ごとの番号が、デバッグ用のボットか。</summary>
    public static bool IsBotKey(ulong key)
    {
        return LocalMultiplayer.SeatOf(key) >= BotSeatStart;
    }

    /// <summary>いま出ているボットの数。</summary>
    public int BotCount
    {
        get
        {
            int count = 0;
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player != null && player.IsSpawned && player.LocalSeat >= BotSeatStart)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// **ボットの数を合わせる（ホストだけ・ロビーにいる間だけ）。** 多ければ後ろから消し、足りなければ出す。
    /// 定員（<see cref="SpaceJunkTeams.MaxPlayers"/>）に達したら、それ以上は出さない。
    /// </summary>
    public void ServerSetBotCount(int count)
    {
        if (!IsServer)
        {
            return;
        }

        if (state.Value != SpaceJunkMatchState.Lobby)
        {
            Debug.LogWarning("[JUNK] ボットの数は、ロビーにいる間しか変えられません。");
            return;
        }

        count = Mathf.Clamp(count, 0, MaxBots);
        ulong hostId = NetworkManager.LocalClientId;

        // 多すぎるぶんを消す（後ろから回す。消すと FishingNetPlayer.All から抜けるため）
        for (int i = FishingNetPlayer.All.Count - 1; i >= 0; i--)
        {
            FishingNetPlayer player = FishingNetPlayer.All[i];
            if (player == null || !player.IsSpawned || player.LocalSeat < BotSeatStart + count)
            {
                continue;
            }

            RemoveSlot(player.PlayerKey);
            Debug.Log($"[JUNK] ボット：{NameOf(player.PlayerKey)} を消しました。");
            player.NetworkObject.Despawn(true);
        }

        GameObject prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("[JUNK] NetworkManager に Player Prefab が入っていないので、ボットを出せません。");
            return;
        }

        // 足りないぶんを出す
        for (int seat = BotSeatStart; seat < BotSeatStart + count; seat++)
        {
            ulong key = LocalMultiplayer.MakeKey(hostId, seat);
            if (FindPlayerByKey(key) != null)
            {
                continue;
            }

            if (slots.Count >= SpaceJunkTeams.MaxPlayers)
            {
                Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、これ以上ボットを出せません。");
                break;
            }

            GameObject spawned = Instantiate(prefab);
            FishingNetPlayer netPlayer = spawned.GetComponent<FishingNetPlayer>();
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (netPlayer == null || networkObject == null)
            {
                Debug.LogError("[JUNK] Player Prefab に FishingNetPlayer か NetworkObject がありません。ボットを出せません。");
                Destroy(spawned);
                return;
            }

            // 1台で複数人の2人目以降と同じ出し方（持ち主はホストのPC。シーンを切り替えても消さない）
            netPlayer.ServerInitLocalSeat(seat);
            networkObject.SpawnWithOwnership(hostId, false);
            AddSlot(key);

            // ホストのPCでだけ、ボットの頭を付ける（見えないコントローラーも一緒に付く）
            SpaceJunkBot.AttachTo(spawned);

            Debug.Log($"[JUNK] ボット：{NameOf(key)} を出しました（チーム {TeamOf(key)}）。");
        }
    }

    /// <summary>その人（人ごとの番号）の席を消す。</summary>
    private void RemoveSlot(ulong key)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i].ClientId == key)
            {
                slots.RemoveAt(i);
            }
        }
    }

    /// <summary>その人の席の番号（無ければ -1）。</summary>
    private int TeamSlotIndex(ulong clientId)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].ClientId == clientId)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 誰かが抜けたとき（ホストだけ）。**そのPCの席を全部消す**
    /// （1台で複数人なら、2人目以降の席も一緒に。人ごとの番号から接続番号に戻して比べる）。
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (LocalMultiplayer.ClientOf(slots[i].ClientId) == clientId)
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
        if (HasSlot(clientId))
        {
            return;
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

    /// <summary>何本先取として、ホストが入れられる一番大きい数。</summary>
    private const int MaxRoundsToWin = 5;

    /// <summary>何本先取かを変える。範囲の外なら端にそろえる。</summary>
    public void ServerSetRoundsToWin(int value)
    {
        if (IsServer)
        {
            roundsToWin.Value = Mathf.Clamp(value, 1, MaxRoundsToWin);
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

    /// <summary>
    /// 使うマップを、選ぶ／選ばないで切り替える。
    /// <paramref name="remember"/> が true なら、次にホストをしたときのために保存する
    /// （チーム数に合わないマップを自動で外すときは false。チーム数を戻したら、またチェックが付いた状態で始められるように）。
    /// </summary>
    public void ServerToggleMap(string sceneName, bool remember = true)
    {
        if (!IsServer || string.IsNullOrEmpty(sceneName))
        {
            return;
        }

        FixedString64Bytes value = new FixedString64Bytes(sceneName);
        bool removed = false;

        for (int i = 0; i < selectedMaps.Count; i++)
        {
            if (selectedMaps[i].Equals(value))
            {
                selectedMaps.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (!removed)
        {
            selectedMaps.Add(value);
        }

        if (remember)
        {
            SaveMapChoice();
        }
    }

    /// <summary>
    /// 「このマップで遊ぶ」を決める。**空を渡すと「ランダム」**（チェックの付いたマップから、ラウンドごとに選ぶ）。
    /// <paramref name="remember"/> は <see cref="ServerToggleMap"/> と同じ。
    /// </summary>
    public void ServerSetFixedMap(string sceneName, bool remember = true)
    {
        if (!IsServer)
        {
            return;
        }

        fixedMap.Value = new FixedString64Bytes(sceneName ?? string.Empty);

        if (remember)
        {
            SaveMapChoice();
        }
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

        if (!HasPlayableMapChoice)
        {
            Debug.LogError("[JUNK] マップが選ばれていない（ランダムなのにチェックの付いたマップが無い）ので、試合を始められません。");
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

    /// <summary>ラウンドの結果を見せる時間だけ待ってから、次のラウンドのマップへ移る。</summary>
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

    /// <summary>試合の結果を見せる時間だけ待ってから、全員をロビーへ戻す。</summary>
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

        // **1つに決めてあれば、毎ラウンドそのマップ**（2026/10/6）
        if (!IsRandomMap)
        {
            candidates.Add(FixedMap);
        }
        else
        {
            // ランダム：チェックの付いたマップから。2つ以上あれば、直前と同じマップは選ばない
            foreach (FixedString64Bytes map in selectedMaps)
            {
                string name = map.ToString();

                if (selectedMaps.Count > 1 && name == previous)
                {
                    continue;
                }

                candidates.Add(name);
            }
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
