using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **ロビーの画面。**（2026/10/6・大槻さん「タイトルと同じ要領で、めっちゃカメレオンを参考に作り直して」）
///
/// 画面そのものは**プレハブ `Assets/Resources/LobbyScreen.prefab`**。ロビーのシーン（SpaceJunkLobby）に置いてある。
/// **ボタンや文字は、エディタで自由に動かし・大きさや画像・色を変えてよい。**
/// このスクリプトは、部品に付いている印（<see cref="SpaceJunkLobbyPart"/> / <see cref="SpaceJunkLobbyButton"/> /
/// <see cref="SpaceJunkLobbyValueRow"/>）を頼りに中身を動かす。
///
/// ## ふだん（左上）
///
/// 次に遊ぶマップの画像と名前・人数・参加者（チームの色）・いまの設定・案内。**全員に見える。**
///
/// ## ゲーム設定（ホストだけ。設定端末に近づいて E）
///
/// | 左：ゲーム設定 | 右：マップ |
/// | --- | --- |
/// | チーム数（＜ ＞）／何本先取（±1）／1ラウンドの最大時間（±1・±10・±50）／チーム分け（1人ずつ ＜ ＞、ランダムに割り振る） | 画像（無ければ「画像 未設定」）と、マップの一覧 |
///
/// **マップの選び方（めっちゃカメレオンと同じ）：**
///
///   ・**マップの名前を押す** → そのマップに決まる。**全部のラウンドをそのマップで遊ぶ**
///   ・**「ランダム」を押す** → **右の四角にチェックが付いたマップ**から、ラウンドごとにランダムに選ぶ（直前と同じマップは選ばない）
///   ・右の四角を押すと、チェックを付け外しする（緑＝候補に入れる／赤＝入れない）
///
/// 下の「ゲームを始める」で全員がマップへ移る。**閉じるのは Esc／B／「戻る」／もう一度 E。**
/// Esc はこの画面が開いている間はこちらが使う（ポーズ画面には渡さない。<see cref="BlocksEscape"/>）。
///
/// マップの画像は、マップの一覧（`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く`）の **Thumbnail** に入れる。
/// </summary>
[DisallowMultipleComponent]
public class SpaceJunkLobbyScreen : MonoBehaviour
{
    [Tooltip("ON：再生したときに、文字をパソコンの日本語フォントに差し替える（日本語が四角にならないように）")]
    [SerializeField] private bool useOsJapaneseFont = true;

    [Tooltip("左上の案内を書き直す間隔（秒）")]
    [SerializeField] private float hudRefreshSeconds = 0.2f;

    /// <summary>ゲーム設定の画面が開いているか。開いている間は、ほかの操作（R で戻る など）を止める。</summary>
    public static bool IsSettingsOpen { get; private set; }

    /// <summary>この画面が Esc／B で閉じたフレーム。同じ Esc でポーズ画面が開かないように持つ。</summary>
    private static int closedByBackFrame = -1;

    /// <summary>
    /// **Esc をこの画面が使っているか**（開いている間と、Esc で閉じたフレーム）。ポーズ画面はこれを見て開かない。
    /// 同じキーを2か所で読むと取り合いになるので、持ち主をはっきりさせる（AIの申し送り 2026/9/8・9/16）。
    /// </summary>
    public static bool BlocksEscape => IsSettingsOpen || closedByBackFrame == Time.frameCount;

    private readonly Dictionary<SpaceJunkLobbyPartRole, SpaceJunkLobbyPart> parts =
        new Dictionary<SpaceJunkLobbyPartRole, SpaceJunkLobbyPart>();

    private readonly List<SpaceJunkLobbyValueRow> valueRows = new List<SpaceJunkLobbyValueRow>();

    private GameObject playerRowTemplate;
    private GameObject mapRowTemplate;
    private readonly List<GameObject> playerRows = new List<GameObject>();
    private readonly List<MapRow> mapRows = new List<MapRow>();
    private string playerRowsSignature = string.Empty;
    private string mapRowsSignature = string.Empty;

    private float hudTimer;
    private Font font;

    /// <summary>マップの一覧の1行（複製したもの）。</summary>
    private sealed class MapRow
    {
        public string SceneName;
        public GameObject Root;
        public GameObject Highlight;
        public GameObject CheckOn;
        public GameObject CheckOff;
    }

    // ------------------------------------------------------------
    // 出し入れ
    // ------------------------------------------------------------

    private void Awake()
    {
        if (useOsJapaneseFont)
        {
            font = UiFont.Find(32);
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.font = font;
            }
        }

        CollectParts();
        WireButtons();

        SetActive(SpaceJunkLobbyPartRole.SettingsWindow, false);
        SetActive(SpaceJunkLobbyPartRole.Hud, false);
    }

    private void OnDisable()
    {
        IsSettingsOpen = false;
    }

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        bool connected = manager != null && (manager.IsServer || manager.IsConnectedClient);
        bool inLobby = connected && SpaceJunkRound.Current == null;

        SetActive(SpaceJunkLobbyPartRole.Hud, inLobby && !IsSettingsOpen);

        SpaceJunkSession session = SpaceJunkSession.Current;
        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;

        bool open = inLobby && manager.IsServer && session != null && terminal != null && terminal.IsOpen;

        if (open && WasBackPressed())
        {
            terminal.Close();
            closedByBackFrame = Time.frameCount;
            open = false;
        }

        if (open != IsSettingsOpen)
        {
            IsSettingsOpen = open;
            SetActive(SpaceJunkLobbyPartRole.SettingsWindow, open);

            if (open)
            {
                EnsureEventSystem();
                playerRowsSignature = string.Empty;
                mapRowsSignature = string.Empty;
                SelectFirstForGamepad();
            }
        }

        if (open)
        {
            RefreshSettings(session);
        }

        hudTimer -= Time.unscaledDeltaTime;
        if (inLobby && !open && hudTimer <= 0f)
        {
            hudTimer = hudRefreshSeconds;
            RefreshHud(manager, session, terminal);
        }
    }

    /// <summary>Esc（キーボード）か B（コントローラー）が押されたか。ポーズ画面が開いている間は見ない。</summary>
    private static bool WasBackPressed()
    {
        if (GamePause.BlocksInput)
        {
            return false;
        }

        Keyboard keyboard = Keyboard.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || GamepadInput.WasPressed(GamepadButton.East);
    }

    // ------------------------------------------------------------
    // 部品を見つけて、つなぐ
    // ------------------------------------------------------------

    private void CollectParts()
    {
        // 行の見本（複製して使う）を先に探し、その中の印は数えない
        foreach (SpaceJunkLobbyPart part in GetComponentsInChildren<SpaceJunkLobbyPart>(true))
        {
            if (part.Role == SpaceJunkLobbyPartRole.PlayerRowTemplate)
            {
                playerRowTemplate = part.gameObject;
            }
            else if (part.Role == SpaceJunkLobbyPartRole.MapRowTemplate)
            {
                mapRowTemplate = part.gameObject;
            }
        }

        foreach (SpaceJunkLobbyPart part in GetComponentsInChildren<SpaceJunkLobbyPart>(true))
        {
            if (part.Role == SpaceJunkLobbyPartRole.None || IsInsideTemplate(part.transform) || parts.ContainsKey(part.Role))
            {
                continue;
            }

            parts[part.Role] = part;
        }

        if (playerRowTemplate != null)
        {
            playerRowTemplate.SetActive(false);
        }

        if (mapRowTemplate != null)
        {
            mapRowTemplate.SetActive(false);
        }

        valueRows.AddRange(GetComponentsInChildren<SpaceJunkLobbyValueRow>(true));
    }

    private bool IsInsideTemplate(Transform target)
    {
        return (playerRowTemplate != null && target.IsChildOf(playerRowTemplate.transform)) ||
               (mapRowTemplate != null && target.IsChildOf(mapRowTemplate.transform));
    }

    private void WireButtons()
    {
        foreach (SpaceJunkLobbyButton button in GetComponentsInChildren<SpaceJunkLobbyButton>(true))
        {
            // 行の中のボタンは、行を作るときにつなぐ
            if (IsInsideTemplate(button.transform))
            {
                continue;
            }

            SpaceJunkLobbyButton target = button;
            button.Button.onClick.AddListener(() => Do(target));
        }
    }

    private void Do(SpaceJunkLobbyButton button)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsServer)
        {
            return;
        }

        switch (button.Action)
        {
            case SpaceJunkLobbyAction.Close:
                if (SpaceJunkLobbyTerminal.Current != null)
                {
                    SpaceJunkLobbyTerminal.Current.Close();
                }
                break;

            case SpaceJunkLobbyAction.StartMatch:
                StartMatch(session);
                break;

            case SpaceJunkLobbyAction.RandomAssign:
                session.ServerRandomAssign();
                break;

            case SpaceJunkLobbyAction.SelectRandomMap:
                session.ServerSetFixedMap(string.Empty);
                break;

            case SpaceJunkLobbyAction.Step:
                SpaceJunkLobbyValueRow row = button.GetComponentInParent<SpaceJunkLobbyValueRow>();
                if (row != null)
                {
                    Step(session, row.Kind, button.Amount);
                }
                break;
        }
    }

    // ------------------------------------------------------------
    // ゲーム設定
    // ------------------------------------------------------------

    private static void Step(SpaceJunkSession session, SpaceJunkLobbyValueKind kind, int amount)
    {
        switch (kind)
        {
            case SpaceJunkLobbyValueKind.TeamCount:
                session.ServerSetTeamCount(Mathf.Clamp(session.TeamCount + amount, 1, SpaceJunkTeams.MaxTeams));
                break;
            case SpaceJunkLobbyValueKind.RoundsToWin:
                session.ServerSetRoundsToWin(session.RoundsToWin + amount);
                break;
            case SpaceJunkLobbyValueKind.RoundSeconds:
                session.ServerSetRoundSeconds(Mathf.RoundToInt(session.RoundSeconds) + amount);
                break;
        }
    }

    private void RefreshSettings(SpaceJunkSession session)
    {
        foreach (SpaceJunkLobbyValueRow row in valueRows)
        {
            if (row.ValueText == null)
            {
                continue;
            }

            switch (row.Kind)
            {
                case SpaceJunkLobbyValueKind.TeamCount:
                    SetText(row.ValueText, $"{session.TeamCount} チーム");
                    break;
                case SpaceJunkLobbyValueKind.RoundsToWin:
                    SetText(row.ValueText, $"{session.RoundsToWin} 本先取");
                    break;
                case SpaceJunkLobbyValueKind.RoundSeconds:
                    SetText(row.ValueText, $"{Mathf.RoundToInt(session.RoundSeconds)}");
                    break;
            }
        }

        RefreshPlayerRows(session);
        RefreshMapRows(session);
        RefreshStart(session);

        SetText(SpaceJunkLobbyPartRole.RuleText, session.WinRule == SpaceJunkWinRule.Score
            ? $"得点制：時間いっぱい宇宙ごみを集め、得点の高いチームがラウンドを取る（1個 {SpaceJunkRound.PointPerItem} 点）。" +
              $"{session.Events.startSeconds:0} 秒たつとイベントが起きる。{session.RoundsToWin} 本先に取ったチームの勝ち"
            : $"3種類そろえたチームが出たら、その時点でラウンドを取る。{session.RoundsToWin} 本先に取ったチームの勝ち");
    }

    /// <summary>チーム分けの行。参加者やチームが変わったときだけ作り直す。</summary>
    private void RefreshPlayerRows(SpaceJunkSession session)
    {
        GameObject content = PartObject(SpaceJunkLobbyPartRole.PlayerListContent);
        if (content == null || playerRowTemplate == null)
        {
            return;
        }

        StringBuilder signature = new StringBuilder();
        signature.Append(session.TeamCount).Append('|');
        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            signature.Append(slot.ClientId).Append(':').Append(slot.Team).Append(':').Append(slot.Name.ToString()).Append(';');
        }

        if (signature.ToString() == playerRowsSignature)
        {
            return;
        }

        playerRowsSignature = signature.ToString();

        foreach (GameObject row in playerRows)
        {
            Destroy(row);
        }
        playerRows.Clear();

        int teamCount = session.TeamCount;

        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            GameObject row = Instantiate(playerRowTemplate, content.transform);
            row.name = $"Player {slot.ClientId}";
            row.SetActive(true);
            playerRows.Add(row);

            Color color = SpaceJunkTeams.TeamColor(slot.Team);
            ulong clientId = slot.ClientId;
            int team = slot.Team;

            foreach (SpaceJunkLobbyPart part in row.GetComponentsInChildren<SpaceJunkLobbyPart>(true))
            {
                Text text = part.GetComponent<Text>();
                if (text == null)
                {
                    continue;
                }

                if (part.Role == SpaceJunkLobbyPartRole.PlayerRowName)
                {
                    text.text = (IsLocalPlayer(clientId) ? "▶ " : string.Empty) + session.NameOf(clientId);
                    text.color = color;
                }
                else if (part.Role == SpaceJunkLobbyPartRole.PlayerRowTeam)
                {
                    text.text = SpaceJunkTeams.TeamName(team);
                    text.color = color;
                }
            }

            foreach (SpaceJunkLobbyButton button in row.GetComponentsInChildren<SpaceJunkLobbyButton>(true))
            {
                int delta = button.Action == SpaceJunkLobbyAction.TeamPrevious ? -1
                    : button.Action == SpaceJunkLobbyAction.TeamNext ? 1 : 0;
                if (delta == 0)
                {
                    continue;
                }

                button.Button.onClick.RemoveAllListeners();
                button.Button.onClick.AddListener(() =>
                {
                    SpaceJunkSession current = SpaceJunkSession.Current;
                    if (current != null && current.IsServer)
                    {
                        current.ServerSetTeamOf(clientId, (team + delta + teamCount) % teamCount);
                    }
                });
            }
        }
    }

    /// <summary>マップの一覧。候補が変わったときだけ作り直し、選択とチェックの見た目は毎回合わせる。</summary>
    private void RefreshMapRows(SpaceJunkSession session)
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;
        GameObject content = PartObject(SpaceJunkLobbyPartRole.MapListContent);

        if (lobby == null || content == null || mapRowTemplate == null)
        {
            SetText(SpaceJunkLobbyPartRole.MapHiddenNote, "マップの候補を読めません（NetworkManager に SpaceJunkLobbyUI がありません）。");
            return;
        }

        List<KeyValuePair<string, string>> maps = lobby.GetAvailableMaps(session.TeamCount, out int hidden);
        lobby.PruneUnplayableSelection(session, maps);

        StringBuilder signature = new StringBuilder();
        foreach (KeyValuePair<string, string> map in maps)
        {
            signature.Append(map.Key).Append('=').Append(map.Value).Append(';');
        }

        if (signature.ToString() != mapRowsSignature)
        {
            mapRowsSignature = signature.ToString();
            RebuildMapRows(content.transform, maps);
        }

        // 選ばれている行・チェックの見た目
        foreach (MapRow row in mapRows)
        {
            bool selected = !session.IsRandomMap && session.FixedMap == row.SceneName;
            bool check = session.IsMapSelected(row.SceneName);

            SetActive(row.Highlight, selected);
            SetActive(row.CheckOn, check);
            SetActive(row.CheckOff, !check);
        }

        SetActive(PartObject(SpaceJunkLobbyPartRole.MapRandomHighlight), session.IsRandomMap);
        SetText(SpaceJunkLobbyPartRole.MapRandomLabel, $"ランダム（チェック {session.SelectedMapCount} 個から）");

        string note = maps.Count == 0
            ? "ロビーに出すマップがありません。`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で足してください"
            : hidden > 0 ? $"{session.TeamCount} チームでは遊べないマップ（ゴールが足りない）を {hidden} 個隠しています" : string.Empty;
        SetText(SpaceJunkLobbyPartRole.MapHiddenNote, note);

        // 大きい画像：選んだマップ。ランダムなら「ランダム」
        if (session.IsRandomMap)
        {
            ShowPicture(SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder, null, "ランダム");
            SetText(SpaceJunkLobbyPartRole.MapPreviewName, "ランダム（チェックの付いたマップから、ラウンドごとに選ぶ）");
        }
        else
        {
            SpaceJunkMapList.Entry entry = lobby.FindEntry(session.FixedMap);
            ShowPicture(SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder,
                entry != null ? entry.thumbnail : null, "画像 未設定");
            SetText(SpaceJunkLobbyPartRole.MapPreviewName, lobby.LabelOf(session.FixedMap));
        }
    }

    private void RebuildMapRows(Transform content, List<KeyValuePair<string, string>> maps)
    {
        foreach (MapRow row in mapRows)
        {
            Destroy(row.Root);
        }
        mapRows.Clear();

        foreach (KeyValuePair<string, string> map in maps)
        {
            GameObject root = Instantiate(mapRowTemplate, content);
            root.name = $"Map {map.Key}";
            root.SetActive(true);

            MapRow row = new MapRow { SceneName = map.Key, Root = root };
            string sceneName = map.Key;

            foreach (SpaceJunkLobbyPart part in root.GetComponentsInChildren<SpaceJunkLobbyPart>(true))
            {
                switch (part.Role)
                {
                    case SpaceJunkLobbyPartRole.MapRowName:
                        Text text = part.GetComponent<Text>();
                        if (text != null)
                        {
                            text.text = map.Value;
                        }
                        break;
                    case SpaceJunkLobbyPartRole.MapRowHighlight:
                        row.Highlight = part.gameObject;
                        break;
                    case SpaceJunkLobbyPartRole.MapRowCheckOn:
                        row.CheckOn = part.gameObject;
                        break;
                    case SpaceJunkLobbyPartRole.MapRowCheckOff:
                        row.CheckOff = part.gameObject;
                        break;
                }
            }

            foreach (SpaceJunkLobbyButton button in root.GetComponentsInChildren<SpaceJunkLobbyButton>(true))
            {
                button.Button.onClick.RemoveAllListeners();

                if (button.Action == SpaceJunkLobbyAction.SelectMap)
                {
                    button.Button.onClick.AddListener(() =>
                    {
                        SpaceJunkSession current = SpaceJunkSession.Current;
                        if (current != null && current.IsServer)
                        {
                            current.ServerSetFixedMap(sceneName);
                        }
                    });
                }
                else if (button.Action == SpaceJunkLobbyAction.ToggleMapCheck)
                {
                    button.Button.onClick.AddListener(() =>
                    {
                        SpaceJunkSession current = SpaceJunkSession.Current;
                        if (current != null && current.IsServer)
                        {
                            current.ServerToggleMap(sceneName);
                        }
                    });
                }
            }

            mapRows.Add(row);
        }
    }

    // ------------------------------------------------------------
    // ゲームを始める
    // ------------------------------------------------------------

    private void RefreshStart(SpaceJunkSession session)
    {
        string problem = StartProblem(session);

        Button start = PartComponent<Button>(SpaceJunkLobbyPartRole.StartButton);
        if (start != null && start.interactable != (problem == null))
        {
            start.interactable = problem == null;
        }

        SetText(SpaceJunkLobbyPartRole.StartLabel, problem == null
            ? $"ゲームを始める（{session.RoundsToWin} 本先取）"
            : "ゲームを始められません");

        string note = problem;
        if (note == null)
        {
            for (int team = 0; team < session.TeamCount; team++)
            {
                if (session.PlayerCountOf(team) == 0)
                {
                    // **無人のチームのゴールは、素材を入れても数えない。** 知らないと混乱するので伝える
                    note = $"{SpaceJunkTeams.TeamName(team)}に誰もいません（このチームのゴールは数えません）";
                    break;
                }
            }
        }

        SetText(SpaceJunkLobbyPartRole.StartMessage, note ?? string.Empty);
    }

    /// <summary>始められない理由。始められるなら null。</summary>
    private static string StartProblem(SpaceJunkSession session)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && !manager.NetworkConfig.EnableSceneManagement)
        {
            return "NetworkManager の Enable Scene Management が OFF です。ONにしないとシーンを切り替えられません。";
        }

        if (!session.HasPlayableMapChoice)
        {
            return "マップを1つ選ぶか、「ランダム」にしてマップに1つ以上チェックを付けてください。";
        }

        // **一番多い原因を先に出す。** ビルドの一覧に入っていないシーンは読み込めない
        if (!session.IsRandomMap)
        {
            if (!SpaceJunkLobbyUI.IsSceneInBuildList(session.FixedMap))
            {
                return $"シーン「{session.FixedMap}」がビルドの一覧に入っていません（File > Build Profiles で入れてください）。";
            }
        }
        else
        {
            foreach (string map in session.SelectedMaps)
            {
                if (!SpaceJunkLobbyUI.IsSceneInBuildList(map))
                {
                    return $"シーン「{map}」がビルドの一覧に入っていません（File > Build Profiles で入れてください）。";
                }
            }
        }

        return null;
    }

    private static void StartMatch(SpaceJunkSession session)
    {
        if (StartProblem(session) != null)
        {
            return;
        }

        if (SpaceJunkLobbyTerminal.Current != null)
        {
            SpaceJunkLobbyTerminal.Current.Close();
        }

        session.ServerStartMatch();
    }

    // ------------------------------------------------------------
    // ふだん（左上）
    // ------------------------------------------------------------

    private void RefreshHud(NetworkManager manager, SpaceJunkSession session, SpaceJunkLobbyTerminal terminal)
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;

        if (session == null)
        {
            SetText(SpaceJunkLobbyPartRole.HudRoster, "試合の係を待っています…");
            return;
        }

        // 次に遊ぶマップ
        if (session.IsRandomMap)
        {
            ShowPicture(SpaceJunkLobbyPartRole.HudMapImage, SpaceJunkLobbyPartRole.HudMapPlaceholder, null, "ランダム");
            SetText(SpaceJunkLobbyPartRole.HudMapName, $"ランダム（{session.SelectedMapCount} 個から）");
        }
        else
        {
            SpaceJunkMapList.Entry entry = lobby != null ? lobby.FindEntry(session.FixedMap) : null;
            ShowPicture(SpaceJunkLobbyPartRole.HudMapImage, SpaceJunkLobbyPartRole.HudMapPlaceholder,
                entry != null ? entry.thumbnail : null, "画像 未設定");
            SetText(SpaceJunkLobbyPartRole.HudMapName, lobby != null ? lobby.LabelOf(session.FixedMap) : session.FixedMap);
        }

        IReadOnlyList<SpaceJunkPlayerSlot> slots = session.Slots;
        SetText(SpaceJunkLobbyPartRole.HudPlayerCount, $"{slots.Count}/{SpaceJunkTeams.MaxPlayers}");

        // 参加者（チームの色つき）
        StringBuilder roster = new StringBuilder();
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            string hex = ColorUtility.ToHtmlStringRGB(SpaceJunkTeams.TeamColor(slot.Team));
            string mark = IsLocalPlayer(slot.ClientId) ? "▶ " : "　 ";
            roster.Append($"<color=#{hex}>{mark}{session.NameOf(slot.ClientId)}（{SpaceJunkTeams.TeamName(slot.Team)}）</color>\n");
        }
        SetText(SpaceJunkLobbyPartRole.HudRoster, roster.ToString().TrimEnd('\n'));

        SetText(SpaceJunkLobbyPartRole.HudSummary,
            $"{session.TeamCount} チーム ／ {session.RoundsToWin} 本先取 ／ 1ラウンド {Mathf.RoundToInt(session.RoundSeconds)} 秒");

        // 案内
        string hint;
        if (!manager.IsServer)
        {
            hint = "ホストが始めるのを待っています…";
        }
        else if (terminal == null)
        {
            hint = "設定端末がこのシーンにありません。";
        }
        else
        {
            hint = terminal.IsHostNearby
                ? $"［{terminal.InteractKeyName}］でゲーム設定を開く"
                : "設定端末に近づくと、ゲーム設定を開けます";
        }

        // **落ちて戻れなくなったときの案内。** 知らないと詰まるので常に出す
        if (!string.IsNullOrEmpty(SpaceJunkPlayerSetup.LocalResetKeyName))
        {
            hint += $"\n落ちたら［{SpaceJunkPlayerSetup.LocalResetKeyName}］で最初の位置に戻れます";
        }

        SetText(SpaceJunkLobbyPartRole.HudHint, hint);
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>画像があれば画像を、無ければ代わりの枠（文字つき）を出す。</summary>
    private void ShowPicture(SpaceJunkLobbyPartRole imageRole, SpaceJunkLobbyPartRole placeholderRole, Sprite sprite, string placeholderText)
    {
        Image image = PartComponent<Image>(imageRole);
        GameObject placeholder = PartObject(placeholderRole);

        if (image != null)
        {
            if (image.sprite != sprite)
            {
                image.sprite = sprite;
            }
            SetActive(image.gameObject, sprite != null);
        }

        if (placeholder != null)
        {
            SetActive(placeholder, sprite == null);
            Text text = placeholder.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                SetText(text, placeholderText);
            }
        }
    }

    private GameObject PartObject(SpaceJunkLobbyPartRole role)
    {
        return parts.TryGetValue(role, out SpaceJunkLobbyPart part) && part != null ? part.gameObject : null;
    }

    private T PartComponent<T>(SpaceJunkLobbyPartRole role) where T : Component
    {
        GameObject target = PartObject(role);
        return target != null ? target.GetComponent<T>() : null;
    }

    private void SetActive(SpaceJunkLobbyPartRole role, bool active)
    {
        SetActive(PartObject(role), active);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private void SetText(SpaceJunkLobbyPartRole role, string value)
    {
        SetText(PartComponent<Text>(role), value);
    }

    /// <summary>文字が変わったときだけ書き換える（毎回書くと、画面の組み直しが毎フレーム起きる）。</summary>
    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    private static bool IsLocalPlayer(ulong clientId)
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId;
    }

    /// <summary>コントローラーがつながっていれば、設定の最初の項目を選んでおく（十字キーで動かせるように）。</summary>
    private void SelectFirstForGamepad()
    {
        GameObject window = PartObject(SpaceJunkLobbyPartRole.SettingsWindow);
        if (Gamepad.current == null || window == null || EventSystem.current == null)
        {
            return;
        }

        Selectable first = window.GetComponentInChildren<Selectable>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    /// <summary>UIのクリックを受け取る仕組みがなければ作る（タイトル画面・ポーズ画面と同じ。シーンが変わっても残す）。</summary>
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
