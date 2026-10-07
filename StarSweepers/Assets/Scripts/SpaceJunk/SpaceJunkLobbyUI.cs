using System.Collections.Generic;
using System.IO;
using ProjectEL4S.InputControl;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **ロビーの画面の裏方。**（NetworkManager に付いていて、シーンをまたいで残る）
///
/// 画面そのもの（参加者の一覧・ゲーム設定・マップ選び）は、2026/10/6 から
/// **プレハブ `Assets/Resources/LobbyScreen.prefab`（<see cref="SpaceJunkLobbyScreen"/>）** が描く
/// （大槻さん「タイトルと同じ要領で、めっちゃカメレオンを参考に作り直して」。前は OnGUI で描いていた）。
///
/// ここに残っている仕事：
///
///   ・**マップの候補を拾う**（マップの一覧 <see cref="SpaceJunkMapList"/> のうち、ロビーに出す・ビルドに入っている・そのチーム数で遊べるもの）
///   ・ラウンドが始まったら、つなぐ画面（LanConnectionUi）を隠す
///   ・ロビーのシーンに画面が置かれていなければ、プレハブから出す（置き忘れの保険）
///   ・**このPCで遊ぶ人数（1台で複数人）**の小窓を出し、人数をホストへ伝える（<see cref="LocalMultiplayer"/>）。
///     プレハブの画面には入れず、OnGUI の小窓で出している（2026/10/7。ロビーの画面のプレハブを触らずに済むように）
/// </summary>
public class SpaceJunkLobbyUI : MonoBehaviour
{
    /// <summary>画面のプレハブの場所（Resources の中）。</summary>
    public const string ScreenPrefabResourcePath = "LobbyScreen";

    [Header("マップの候補")]
    [Tooltip("マップの一覧（SpaceJunkMapList）。**ここに入っていて「Show In Lobby」が付いたマップだけが候補に出る。**" +
             "`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で自動で入る")]
    [SerializeField] private SpaceJunkMapList mapList;

    [Tooltip("マップの一覧が入っていないときだけ使う、昔の見分け方。" +
             "名前がこれで始まるシーンを候補に並べる")]
    [SerializeField] private string mapScenePrefix = "SpaceJunkMap";

    /// <summary>
    /// **いま使われている NetworkManager に付いている裏方。**
    ///
    /// NetworkManager はタイトルとロビーの両方に置いてあり、あとから読み込んだほう（ロビーの NetworkManager）は
    /// 重複として消される（NetworkManagerCleanup）。消える側の Awake で「いまの裏方」を覚えてしまうと、
    /// 消えたあとに空になって**マップの候補が読めなくなっていた**（2026/10/6）。そこで、生き残った NetworkManager から引く。
    /// </summary>
    public static SpaceJunkLobbyUI Current
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;
            SpaceJunkLobbyUI found = manager != null ? manager.GetComponent<SpaceJunkLobbyUI>() : null;
            return found != null ? found : FindFirstObjectByType<SpaceJunkLobbyUI>();
        }
    }

    /// <summary>マップの一覧（画像や表示名を引くのに使う）。無ければ null。</summary>
    public SpaceJunkMapList MapList => mapList;

    /// <summary>つなぐ画面。試合が始まったら隠すために持っておく。</summary>
    private LanConnectionUi connectionUi;

    /// <summary>通信の様子の窓。ゲーム設定を開いている間は隠す（重なって読めないため）。</summary>
    private NetworkStatusHud statusHud;

    /// <summary>「このPCで遊ぶ人数」を最後に伝えた試合の係と人数。変わったとき・係が新しくなったときだけ伝え直す。</summary>
    private SpaceJunkSession sentLocalCountSession;
    private int sentLocalCount;

    [Header("このPCで遊ぶ人数の小窓")]
    [Tooltip("小窓の大きさ（1920×1080 のときの倍率）")]
    [SerializeField] private float localPlayersUiScale = 1f;

    private readonly DraggableGuiPanel localPlayersPanel = new DraggableGuiPanel("このPCで遊ぶ人数", 1f, -12f, 260f, 340f);
    private GUIStyle labelStyle;

    private void Awake()
    {
        connectionUi = GetComponent<LanConnectionUi>();
        statusHud = GetComponent<NetworkStatusHud>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        EnsureScreen(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        // **ラウンドが始まったら、つなぐ画面も隠す。**
        // NetworkManager はシーンをまたいで生き残るため、
        // 隠さないとプレイ画面に「ホストとして動作中／切断する」が出っぱなしになる
        // タイトル画面が出ている間・ロビーのゲーム設定を開いている間も隠す（2026/10/6）
        if (connectionUi != null)
        {
            // 古い「接続」の窓は使わない（つなぐのはタイトル画面。別の合言葉が出て紛らわしいため。2026/10/6）
            connectionUi.enabled = false;
        }

        if (statusHud != null)
        {
            statusHud.enabled = !SpaceJunkLobbyScreen.IsSettingsOpen;
        }

        SyncLocalPlayerCount();
    }

    // ------------------------------------------------------------
    // このPCで遊ぶ人数（1台で複数人。2026/10/7）
    // ------------------------------------------------------------

    /// <summary>
    /// **このPCで遊ぶ人数を、ホストへ伝える。** 人数を変えたときと、新しくつないだ（係が新しくなった）ときだけ。
    /// ホストは足りないプレイヤーを出し、多すぎるぶんを消す（<see cref="SpaceJunkSession.RequestLocalPlayerCount"/>）。
    /// </summary>
    private void SyncLocalPlayerCount()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsSpawned || session.State != SpaceJunkMatchState.Lobby)
        {
            return;
        }

        int count = LocalMultiplayer.PlayerCount;
        if (session == sentLocalCountSession && count == sentLocalCount)
        {
            return;
        }

        // 1人のままで新しくつないだときは、伝えることが無い（ホストは最初から1人ぶん出している）
        if (session != sentLocalCountSession && count == 1)
        {
            sentLocalCountSession = session;
            sentLocalCount = count;
            return;
        }

        session.RequestLocalPlayerCount(count);
        sentLocalCountSession = session;
        sentLocalCount = count;
    }

    /// <summary>
    /// 「このPCで遊ぶ人数」の小窓。ロビーにいる間だけ出す（ホストも参加者も、自分のPCの人数を決める）。
    /// ゲーム設定を開いている間・タイトル画面が出ている間は隠す（重なって読めないため）。
    /// </summary>
    private void OnGUI()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsSpawned || session.State != SpaceJunkMatchState.Lobby ||
            SpaceJunkRound.Current != null || SpaceJunkLobbyScreen.IsSettingsOpen || TitleScreen.IsVisible)
        {
            return;
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        }

        localPlayersPanel.Draw(localPlayersUiScale, _ => DrawLocalPlayers());
    }

    /// <summary>
    /// 人数の「－」「＋」と、席ごとにつながっている機器。
    /// 2人以上にすると、キーボード・マウス・コントローラを押した順に P1・P2… に分けて読む。
    /// </summary>
    private void DrawLocalPlayers()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("－", GUILayout.Width(32f)))
        {
            LocalMultiplayer.PlayerCount--;
        }
        GUILayout.Label($"　{LocalMultiplayer.PlayerCount} 人", labelStyle, GUILayout.Width(56f));
        if (GUILayout.Button("＋", GUILayout.Width(32f)))
        {
            LocalMultiplayer.PlayerCount++;
        }
        GUILayout.EndHorizontal();

        if (!LocalMultiplayer.IsActive)
        {
            GUILayout.Label("2人以上にすると、キーボード・マウス・コントローラを人ごとに分けて遊べます", labelStyle);
            return;
        }

        InputSeatManager seats = InputSeatManager.Instance;
        if (seats == null)
        {
            return;
        }

        if (seats.IsRegistering)
        {
            GUILayout.Label(seats.RegistrationPrompt, labelStyle);
            if (GUILayout.Button("登録をやめる"))
            {
                seats.CancelRegistration();
            }
            return;
        }

        Color saved = GUI.color;
        for (int i = 0; i < seats.SeatCount; i++)
        {
            InputSeat seat = seats.GetSeat(i);
            GUI.color = seat.Color;
            GUILayout.Label($"P{i + 1}：キーボード {Mark(seat.HasKeyboard)}　マウス {Mark(seat.HasMouse)}　コントローラ {Mark(seat.HasGamepad)}", labelStyle);
        }
        GUI.color = saved;

        GUILayout.Label("キーを押す／マウスを動かす／コントローラのボタンを押した順に P1・P2… に入ります", labelStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("キーボード・マウスを登録"))
        {
            seats.BeginRegistration();
        }
        if (GUILayout.Button("割り当てをやり直す"))
        {
            seats.ResetAssignments();
        }
        GUILayout.EndHorizontal();
    }

    private static string Mark(bool connected)
    {
        return connected ? "○" : "－";
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureScreen(scene);
    }

    /// <summary>ロビーのシーンに画面が無ければ、プレハブから出す。</summary>
    private static void EnsureScreen(Scene scene)
    {
        if (scene.name != TitleScreen.LobbySceneName || FindFirstObjectByType<SpaceJunkLobbyScreen>() != null)
        {
            return;
        }

        SpaceJunkLobbyScreen prefab = Resources.Load<SpaceJunkLobbyScreen>(ScreenPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[JUNK] ロビーの画面のプレハブ（Assets/Resources/LobbyScreen.prefab）が見つかりません。" +
                           "`Tools > StarSweepers > ロビーの画面のプレハブを作り直す` で作ってください。");
            return;
        }

        Debug.LogWarning("[JUNK] ロビーのシーンに画面が置かれていないので、プレハブから出しました。");
        SpaceJunkLobbyScreen created = Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(created.gameObject, scene);
    }

    // ------------------------------------------------------------
    // マップの候補
    // ------------------------------------------------------------

    /// <summary>そのマップの一覧の行。無ければ null。</summary>
    public SpaceJunkMapList.Entry FindEntry(string sceneName)
    {
        return mapList != null ? mapList.Find(sceneName) : null;
    }

    /// <summary>そのマップの、ロビーに出す名前。</summary>
    public string LabelOf(string sceneName)
    {
        SpaceJunkMapList.Entry entry = FindEntry(sceneName);
        return entry != null ? entry.Label : sceneName;
    }

    /// <summary>
    /// **ロビーに出すマップの候補を拾う。** Key＝シーンの名前、Value＝ロビーに出す名前。
    ///
    /// マップの一覧（<see cref="SpaceJunkMapList"/>）があれば、**そこに入っていて
    /// 「ロビーに出す」が付いたもの**だけを、一覧の順に並べる。
    /// ビルドの一覧に入っていないシーンは、選んでも切り替えられないので出さない。
    /// **いまのチーム数で、全チームが自分のゴールを持てないマップも出さない**（数を <paramref name="hiddenForTeams"/> に返す）。
    ///
    /// 一覧が無いときだけ、昔どおり「名前が `SpaceJunkMap` で始まるシーン」を拾う。
    /// </summary>
    public List<KeyValuePair<string, string>> GetAvailableMaps(int teamCount, out int hiddenForTeams)
    {
        List<KeyValuePair<string, string>> maps = new List<KeyValuePair<string, string>>();
        hiddenForTeams = 0;

        if (mapList != null)
        {
            foreach (SpaceJunkMapList.Entry entry in mapList.Maps)
            {
                if (entry == null || !entry.IsValid || !entry.showInLobby || !IsSceneInBuildList(entry.SceneName))
                {
                    continue;
                }

                // **いまのチーム数で、全チームが自分のゴールを持てないマップは出さない**（2026/9/30・大槻さん）
                if (!entry.SupportsTeamCount(teamCount))
                {
                    hiddenForTeams++;
                    continue;
                }

                // 4チームまで遊べないマップは、何チームまでかを名前の横に出す
                string label = entry.MaxTeams < SpaceJunkTeams.MaxTeams
                    ? $"{entry.Label}（{entry.MaxTeams}チームまで）"
                    : entry.Label;

                maps.Add(new KeyValuePair<string, string>(entry.SceneName, label));
            }

            return maps;
        }

        int count = SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(mapScenePrefix) && name.StartsWith(mapScenePrefix))
            {
                maps.Add(new KeyValuePair<string, string>(name, name));
            }
        }

        return maps;
    }

    /// <summary>
    /// 選んであるマップのうち、**いまの候補（チーム数で遊べるもの）に無いもの**を外す（ホストだけ）。
    /// 「このマップで遊ぶ」と1つ決めてあるマップが遊べなくなったときは、「ランダム」に戻す。
    /// チーム数をあとから増やしたとき用（2026/9/30）。残しておくと、ゴールを持てないチームが出るマップでラウンドが始まってしまう。
    /// 一覧が無いとき（昔の拾い方）は、ゴールの控えが無いので何もしない。
    /// </summary>
    public void PruneUnplayableSelection(SpaceJunkSession session, List<KeyValuePair<string, string>> available)
    {
        if (mapList == null || session == null)
        {
            return;
        }

        HashSet<string> playable = new HashSet<string>();
        foreach (KeyValuePair<string, string> map in available)
        {
            playable.Add(map.Key);
        }

        if (!session.IsRandomMap)
        {
            SpaceJunkMapList.Entry fixedEntry = mapList.Find(session.FixedMap);
            if (fixedEntry != null && !fixedEntry.SupportsTeamCount(session.TeamCount) && !playable.Contains(session.FixedMap))
            {
                Debug.Log($"[JUNK] {session.FixedMap} は {session.TeamCount} チームでは遊べないので、マップを「ランダム」に戻しました。");
                session.ServerSetFixedMap(string.Empty, remember: false);
            }
        }

        if (session.SelectedMapCount == 0)
        {
            return;
        }

        // SelectedMaps は使い回しの入れ物なので、先に写してから外す
        List<string> selected = new List<string>(session.SelectedMaps);

        foreach (string map in selected)
        {
            SpaceJunkMapList.Entry entry = mapList.Find(map);

            // 一覧に無い・ロビーに出さないマップは、ここでは触らない（遊べるかどうかだけを見る）
            if (entry != null && !entry.SupportsTeamCount(session.TeamCount) && !playable.Contains(map))
            {
                session.ServerToggleMap(map, remember: false);
                Debug.Log($"[JUNK] {map} は {session.TeamCount} チームでは遊べない（ゴールが足りない）ので、使うマップから外しました。");
            }
        }
    }

    /// <summary>そのシーンが**ビルドのシーン一覧に入っているか**。入っていないと切り替えられない。</summary>
    public static bool IsSceneInBuildList(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return false;
        }

        int count = SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);

            if (Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return true;
            }
        }

        return false;
    }
}
