using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **タイトル画面。**（2026/10/6・大槻さん。めっちゃカメレオンのタイトル画面を参考にした）
///
/// | 画面 | できること |
/// | --- | --- |
/// | メイン | ソロプレイ（オフラインでロビーへ）／マルチプレイ／設定／ゲームを終了。右下にバージョン（2026/10/19 から） |
/// | マルチプレイ | 左：クイックプレイ／プライベートサーバーを探す／サーバーを作る／サーバー検索設定（■パブリックサーバー ■フレンドのサーバー）。右：参加可能なサーバー（**LAN の部屋はいつも一番上**） |
/// | サーバーを作る | 名前・最大人数（2〜8。初期値8）・サーバーの名前・パスワード・□パブリック □プライベート □LAN → 作成 |
/// | プライベートサーバーを探す | 参加コード（とパスワード）を入れて参加 |
/// | 設定 | サウンド（音量）／グラフィック（解像度・画面モード・フレームレート上限・垂直同期・画質）／ゲーム全般（マウス感度・操作タイプ） |
///
/// ## 画面はプレハブ（2026/10/6 から）
///
/// **`Assets/Resources/TitleScreen.prefab`** が画面そのもの。TitleScene に置いてある。
/// **ボタンや文字は、エディタで自由に動かし・大きさを変え・画像や色を変えてよい。**
/// このスクリプトは、部品に付いている印を頼りに中身を動かす（名前や並び順は見ない）。
///
/// | 印 | 何を決めるか |
/// | --- | --- |
/// | <see cref="TitleMenuButton"/> の Action | ボタンを押したときにすること |
/// | <see cref="TitlePart"/> の Role | 画面（ページ）・コードが書き換える文字・一覧の入れ物など |
/// | <see cref="TitleInput"/> の Kind | 入力欄に何を入れるか（名前・参加コード など） |
/// | <see cref="TitleSettingRow"/> の Kind | 設定の1行が何の設定か |
///
/// ボタンの見た目（画像・色）は <see cref="TitleScreenTheme"/> でまとめて変えられる。
/// 作り直したいときは `Tools > StarSweepers > タイトル画面のプレハブを作り直す`（**手で直した配置は消える**）。
///
/// ## いつ出るか
///
/// **TitleScene で出る**（ゲームはここから始まる）。タイトルとロビーの行き来は <see cref="TitleSceneFlow"/> がする。
///
/// ## 操作
///
/// マウスで選ぶ。コントローラーでは十字キー／スティックで選んで A、**B（キーボードは Esc）で1つ前の画面へ戻る。**
/// </summary>
[DisallowMultipleComponent]
public class TitleScreen : MonoBehaviour
{
    /// <summary>タイトル画面を出すシーンの名前。</summary>
    public const string TitleSceneName = "TitleScene";

    /// <summary>ロビーのシーンの名前（つながったら、ホストが全員をここへ移す）。</summary>
    public const string LobbySceneName = "SpaceJunkLobby";

    /// <summary>画面のプレハブの場所（Resources の中）。TitleScene に置き忘れたときに使う。</summary>
    public const string PrefabResourcePath = "TitleScreen";

    /// <summary>LAN で最後に入れたIPアドレス（次に開いたときに入れておく）。</summary>
    private const string LastLanAddressKey = "StarSweepers.LastLanAddress";

    /// <summary>サーバーの名前の最大の文字数。</summary>
    private const int ServerNameMaxLength = 25;

    /// <summary>参加コード・IPアドレスの最大の文字数。</summary>
    private const int PrivateCodeMaxLength = 12;
    private const int LanAddressMaxLength = 40;

    /// <summary>いまタイトル画面が出ているか。ポーズ画面やロビーの画面が、これを見て自分を隠す。</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>画面の種類。</summary>
    public enum Page
    {
        Main,
        Create,
        Find,
        FindPrivate,
        FindPublic,
        FindLan,
        Settings,
        FindFriends,

        /// <summary>マルチプレイ（クイックプレイ・プライベートサーバーを探す・サーバーを作る・参加可能なサーバー）。2026/10/19</summary>
        Multi,
    }

    private enum SettingsTab
    {
        Sound,
        Graphics,
        General,
    }

    [Tooltip("ON：再生したときに、文字をパソコンの日本語フォントに差し替える（日本語が四角にならないように）")]
    [SerializeField] private bool useOsJapaneseFont = true;

    /// <summary>プレハブの作りの版（作るツールが入れる。古ければツールが作り直す）。</summary>
    [SerializeField, HideInInspector] private int prefabVersion;

    public int PrefabVersion => prefabVersion;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが版を入れる。</summary>
    public void EditorSetPrefabVersion(int value)
    {
        prefabVersion = value;
    }
#endif

    // 部品
    private readonly Dictionary<TitlePartRole, TitlePart> parts = new Dictionary<TitlePartRole, TitlePart>();
    private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    private readonly Dictionary<SettingsTab, GameObject> settingsGroups = new Dictionary<SettingsTab, GameObject>();
    private readonly List<TitleMenuButton> buttons = new List<TitleMenuButton>();
    private readonly List<TitleSettingRow> settingRows = new List<TitleSettingRow>();

    // 参加可能なサーバーの一覧（マルチプレイ画面。2026/10/19）
    private GameObject joinableCardTemplate;
    private readonly List<GameObject> joinableCards = new List<GameObject>();
    private string joinableSignature = string.Empty;
    private bool lanListening;

    /// <summary>ホストに切られて戻ってきたときの理由（例：満員）。出し終わったら空にする。</summary>
    private string disconnectNotice;

    /// <summary>前に出した切られた理由（同じ理由を、タイトルに戻るたびに出し直さないため）。</summary>
    private static string shownDisconnectReason = string.Empty;
    private readonly List<SteamFriendRoom> friendRooms = new List<SteamFriendRoom>();
    private bool searchingFriends;
    private bool initialized;

    private Page current = Page.Main;

    // サーバーを作る
    private int createMaxPlayers = SpaceJunkTeams.MaxPlayers; // 初期値は定員（8人）。2026/10/15・大槻さん
    private bool createLan;
    private bool createPrivate = false; // 最初はパブリック（2026/10/15・大槻さん）
    private string createServerName = string.Empty;

    /// <summary>作る部屋のパスワード（空ならなし。1〜32文字）。保存はしない。</summary>
    private string createPassword = string.Empty;

    // サーバーを探す
    private string privateCode = string.Empty;

    /// <summary>入る部屋のパスワード（パスワードの付いた部屋だけ要る）。</summary>
    private string joinPassword = string.Empty;
    private string lanAddress = string.Empty;
    private bool searching;

    // 設定
    private SettingsTab settingsTab = SettingsTab.Sound;
    private Resolution[] resolutions = new Resolution[0];
    private int resolutionIndex;
    private FullScreenMode pendingScreenMode;

    private static readonly FullScreenMode[] ScreenModes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

    // ------------------------------------------------------------
    // 出し入れ
    // ------------------------------------------------------------

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();

        IsVisible = true;
        EnsureEventSystem();
        ShowPage(Page.Main);
        ShowCursor();
        TakeDisconnectReason();
    }

    private void OnDisable()
    {
        IsVisible = false;
        SetLanListening(false);

        // **閉じたあとも、カーソルは出したままにする。**（2026/10/6「ロビーでクリックするたびにカーソルが隠れる」の修正）
        ShowCursor();
    }

    private void Update()
    {
        // 遊んでいた画面から戻ってきたときなどに、カーソルが隠れたままにならないように
        ShowCursor();

        JoinFromSteamRequest();
        UpdateBusyOverlay();

        if (current == Page.Multi)
        {
            UpdateJoinableList();
        }

        Keyboard keyboard = Keyboard.current;
        bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                    GamepadInput.WasPressed(GamepadButton.East);

        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        if (back && (busy == null || !busy.activeSelf))
        {
            GoBack();
        }
    }

    /// <summary>
    /// カーソルを画面に閉じ込めない。**Windows のカーソルの見え隠れは <see cref="UiPointer"/> が受け持つ**
    /// （いつも隠して、代わりにスコープレンズのポインターを出す。2026/10/11）。
    /// </summary>
    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
    }

    // ------------------------------------------------------------
    // 部品を見つけて、つなぐ
    // ------------------------------------------------------------

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        lanAddress = PlayerPrefs.GetString(LastLanAddressKey, "127.0.0.1");

        if (useOsJapaneseFont)
        {
            ApplyJapaneseFont();
        }

        CollectParts();
        WireButtons();
        WireInputs();
        WireSettings();
        ApplyVersionText();
    }

    /// <summary>
    /// **文字を、パソコンの日本語フォントに差し替える。**
    /// プレハブには保存できないフォントなので、再生したときに毎回ここで入れる（<see cref="UiFont"/>）。
    /// </summary>
    private void ApplyJapaneseFont()
    {
        Font font = UiFont.Find(32);
        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            text.font = font;
        }
    }

    private void CollectParts()
    {
        foreach (TitlePart part in GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role != TitlePartRole.None && !parts.ContainsKey(part.Role))
            {
                parts[part.Role] = part;
            }
        }

        AddPage(Page.Main, TitlePartRole.PageMain);
        AddPage(Page.Create, TitlePartRole.PageCreate);
        AddPage(Page.FindPrivate, TitlePartRole.PageFindPrivate);
        AddPage(Page.Settings, TitlePartRole.PageSettings);
        AddPage(Page.Multi, TitlePartRole.PageMulti);

        AddSettingsGroup(SettingsTab.Sound, TitlePartRole.SettingsSound);
        AddSettingsGroup(SettingsTab.Graphics, TitlePartRole.SettingsGraphics);
        AddSettingsGroup(SettingsTab.General, TitlePartRole.SettingsGeneral);

        // 参加可能なサーバーの一覧のカードの見本（マルチプレイ画面。2026/10/19）。これを複製して1枚ずつ作る（見本そのものは隠しておく）
        joinableCardTemplate = PartObject(TitlePartRole.JoinableCardTemplate);
        if (joinableCardTemplate != null)
        {
            joinableCardTemplate.SetActive(false);
        }

        // 参加の確認の窓（2026/10/19）は、はじめは閉じておく
        GameObject joinDialog = PartObject(TitlePartRole.JoinDialog);
        if (joinDialog != null)
        {
            joinDialog.SetActive(false);
        }

        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        if (busy != null)
        {
            busy.SetActive(false);
        }
    }

    private void AddPage(Page page, TitlePartRole role)
    {
        GameObject target = PartObject(role);
        if (target != null)
        {
            pages[page] = target;
        }
        else
        {
            Debug.LogWarning($"[UI] タイトル画面のプレハブに、画面「{role}」が見つかりません（TitlePart の Role を確かめてください）。");
        }
    }

    private void AddSettingsGroup(SettingsTab tab, TitlePartRole role)
    {
        GameObject target = PartObject(role);
        if (target != null)
        {
            settingsGroups[tab] = target;
        }
    }

    private void WireButtons()
    {
        foreach (TitleMenuButton look in GetComponentsInChildren<TitleMenuButton>(true))
        {
            // 一覧のカードのボタンは、カードを作るときにつなぐ
            if (joinableCardTemplate != null && look.transform.IsChildOf(joinableCardTemplate.transform))
            {
                continue;
            }

            buttons.Add(look);

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            TitleButtonAction action = look.Action;
            button.onClick.AddListener(() => Do(action));
        }
    }

    private void WireInputs()
    {
        foreach (TitleInput input in GetComponentsInChildren<TitleInput>(true))
        {
            InputField field = input.Field;
            TitleInputKind kind = input.Kind;
            Text counter = input.Counter;

            field.characterLimit = InputLimitOf(kind);
            field.text = ValueOf(kind);

            field.onValueChanged.AddListener(value =>
            {
                SetValue(kind, value);
                UpdateCounter(kind, counter, value);
            });

            UpdateCounter(kind, counter, field.text);
        }
    }

    private static bool IsPassword(TitleInputKind kind)
    {
        return kind == TitleInputKind.CreatePassword || kind == TitleInputKind.JoinPassword;
    }

    /// <summary>
    /// 入力欄に打てる文字数。パスワードは 32 文字を超えたことを知らせたいので、少し多めに打てるようにする。
    /// </summary>
    private static int InputLimitOf(TitleInputKind kind)
    {
        return IsPassword(kind) ? InternetConnection.PasswordInputLimit : MaxLengthOf(kind);
    }

    /// <summary>「いま何文字か」の文字の、プレハブで決めた色（32文字を超えて赤くしたあと、戻すため）。</summary>
    private readonly Dictionary<Text, Color> counterColors = new Dictionary<Text, Color>();

    /// <summary>右端の「いま何文字か」。パスワードが 32 文字を超えたら「パスワードは32文字までです」を出す。</summary>
    private void UpdateCounter(TitleInputKind kind, Text counter, string value)
    {
        if (counter == null)
        {
            return;
        }

        if (!counterColors.TryGetValue(counter, out Color normal))
        {
            normal = counter.color;
            counterColors[counter] = normal;
        }

        int max = MaxLengthOf(kind);
        bool tooLong = IsPassword(kind) && value.Length > max;

        // 長い文が切れないように、左へはみ出して出せるようにしておく
        counter.horizontalOverflow = HorizontalWrapMode.Overflow;
        counter.text = tooLong ? InternetConnection.PasswordTooLongMessage : $"{value.Length}/{max}";
        counter.color = tooLong ? new Color(1f, 0.45f, 0.4f, 1f) : normal;
    }

    private static int MaxLengthOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.PlayerName:
                return GameSettings.PlayerNameMaxLength;
            case TitleInputKind.ServerName:
                return ServerNameMaxLength;
            case TitleInputKind.PrivateCode:
                return PrivateCodeMaxLength;
            case TitleInputKind.CreatePassword:
            case TitleInputKind.JoinPassword:
                return InternetConnection.PasswordMaxLength;
            default:
                return LanAddressMaxLength;
        }
    }

    private string ValueOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.PlayerName:
                return GameSettings.PlayerName ?? string.Empty;
            case TitleInputKind.ServerName:
                return createServerName;
            case TitleInputKind.PrivateCode:
                return privateCode;
            case TitleInputKind.CreatePassword:
                return createPassword;
            case TitleInputKind.JoinPassword:
                return joinPassword;
            default:
                return lanAddress;
        }
    }

    private void SetValue(TitleInputKind kind, string value)
    {
        switch (kind)
        {
            case TitleInputKind.PlayerName:
                GameSettings.PlayerName = value;
                break;
            case TitleInputKind.ServerName:
                createServerName = value;
                break;
            case TitleInputKind.PrivateCode:
                privateCode = value;
                break;
            case TitleInputKind.CreatePassword:
                createPassword = value;
                break;
            case TitleInputKind.JoinPassword:
                joinPassword = value;
                break;
            default:
                lanAddress = value;
                break;
        }
    }

    /// <summary>
    /// その画面の入力欄に、いまの値を入れ直す（同じ値を入れる欄が別の画面にもあるため。
    /// 例：パスワードは「プライベートサーバー」と「パブリックサーバー」の両方の画面にある）。
    /// </summary>
    private void RefreshInputs(GameObject page)
    {
        foreach (TitleInput input in page.GetComponentsInChildren<TitleInput>(true))
        {
            string value = ValueOf(input.Kind);
            if (input.Field.text != value)
            {
                input.Field.SetTextWithoutNotify(value);
                UpdateCounter(input.Kind, input.Counter, value);
            }
        }
    }

    /// <summary>右下のバージョン。文字の中の {version} を、Project Settings の Version に置き換える。</summary>
    private void ApplyVersionText()
    {
        Text version = PartText(TitlePartRole.VersionText);
        if (version != null)
        {
            version.text = version.text.Replace("{version}", Application.version);
        }
    }

    // ------------------------------------------------------------
    // ボタン
    // ------------------------------------------------------------

    private void Do(TitleButtonAction action)
    {
        switch (action)
        {
            case TitleButtonAction.QuickPlay: QuickPlay(); break;
            case TitleButtonAction.PlaySolo: StartSolo(); break;
            case TitleButtonAction.OpenMulti: ShowPage(Page.Multi); break;
            case TitleButtonAction.ToggleSearchPublic: SetSearchPublic(!searchPublic); break;
            case TitleButtonAction.ToggleSearchFriends: SetSearchFriends(!searchFriends); break;
            case TitleButtonAction.ConfirmJoinDialog: ConfirmJoinDialog(); break;
            case TitleButtonAction.CloseJoinDialog: CloseJoinDialog(); break;
            case TitleButtonAction.OpenCreate: ShowPage(Page.Create); break;
            case TitleButtonAction.OpenFind: ShowPage(Page.Multi); break;
            case TitleButtonAction.OpenSettings: ShowPage(Page.Settings); break;
            case TitleButtonAction.Quit: QuitGame(); break;
            case TitleButtonAction.Back: GoBack(); break;

            case TitleButtonAction.CreateServer: CreateServer(); break;
            case TitleButtonAction.PracticeSolo: StartSolo(); break;
            case TitleButtonAction.MaxPlayersDown: ChangeMaxPlayers(-1); break;
            case TitleButtonAction.MaxPlayersUp: ChangeMaxPlayers(1); break;
            case TitleButtonAction.UseInternet: SetCreateLan(false); break;
            case TitleButtonAction.UseLan: SetCreateLan(true); break;
            case TitleButtonAction.TogglePrivate: TogglePrivate(); break;
            case TitleButtonAction.SetPublic: SetCreatePrivate(false); break;
            case TitleButtonAction.SetPrivate: SetCreatePrivate(true); break;
            case TitleButtonAction.SetLan: SetCreateLan(true); break;

            case TitleButtonAction.OpenFindPrivate: ShowPage(Page.FindPrivate); break;
            case TitleButtonAction.JoinPrivate: JoinPrivate(); break;

            case TitleButtonAction.TabSound: ShowSettingsTab(SettingsTab.Sound); break;
            case TitleButtonAction.TabGraphics: ShowSettingsTab(SettingsTab.Graphics); break;
            case TitleButtonAction.TabGeneral: ShowSettingsTab(SettingsTab.General); break;
            case TitleButtonAction.ApplyScreen: ApplyScreen(); break;
        }
    }

    /// <summary>その Action のボタン全部に「選ばれている」見た目を付け外しする（タブ・切り替え）。</summary>
    private void SetSelectedLook(TitleButtonAction action, bool selected)
    {
        foreach (TitleMenuButton look in buttons)
        {
            if (look != null && look.Action == action)
            {
                look.SetSelectedLook(selected);
            }
        }
    }

    // ------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------

    private void ShowPage(Page page)
    {
        current = page;
        SetLanListening(page == Page.Multi);
        if (page == Page.Multi)
        {
            joinableSignature = null;
        }

        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page);
        }

        switch (page)
        {
            case Page.Main:
                RefreshMainName();
                break;
            case Page.Create:
                RefreshCreatePage();
                break;
            case Page.Multi:
                RefreshMultiPage();
                break;
            case Page.Settings:
                ShowSettingsTab(settingsTab);
                break;
        }

        if (pages.TryGetValue(page, out GameObject shown))
        {
            RefreshInputs(shown);
            SelectFirstForGamepad(shown);
        }
    }

    /// <summary>1つ前の画面へ戻る。</summary>
    private void GoBack()
    {
        // 参加の確認の窓が開いていれば、窓を閉じるだけ
        if (IsJoinDialogOpen())
        {
            CloseJoinDialog();
            return;
        }

        switch (current)
        {
            case Page.Multi:
            case Page.Settings:
                ShowPage(Page.Main);
                break;
            case Page.Create:
            case Page.FindPrivate:
                ShowPage(Page.Multi);
                break;
        }
    }

    private void RefreshMainName()
    {
        string name = GameSettings.PlayerName;
        SetText(TitlePartRole.PlayerNameText,
            string.IsNullOrEmpty(name) ? "名前：未設定（サーバーを作る・探すの画面で入れられます）" : $"名前：{name}");
    }

    private void RefreshCreatePage()
    {
        SetText(TitlePartRole.MaxPlayersText, $"{createMaxPlayers} 人");
        // □パブリック □プライベート □LAN は、どれか1つだけにチェックが付く（2026/10/15。前は「つなぎかた（インターネット／LAN）」が別の行だった）
        bool publicRoom = !createLan && !createPrivate;
        bool privateRoom = !createLan && createPrivate;
        SetText(TitlePartRole.PublicCheckText, publicRoom ? "■" : "□");
        SetText(TitlePartRole.PrivateCheckText, privateRoom ? "■" : "□");
        SetText(TitlePartRole.LanCheckText, createLan ? "■" : "□");
        SetSelectedLook(TitleButtonAction.SetPublic, publicRoom);
        SetSelectedLook(TitleButtonAction.SetPrivate, privateRoom);
        SetSelectedLook(TitleButtonAction.SetLan, createLan);
        SetSelectedLook(TitleButtonAction.UseInternet, !createLan);
        SetSelectedLook(TitleButtonAction.UseLan, createLan);

        // パスワードはインターネットの部屋だけ（LAN では使わない）
        GameObject passwordRow = PartObject(TitlePartRole.PasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(!createLan);
        }

        if (createLan)
        {
            SetText(TitlePartRole.CodeBoxTitle, "このPCのIPアドレス");
            SetText(TitlePartRole.CodeBoxBody, $"{LocalAddresses()}\n参加する人の「サーバーを探す → LAN」の一覧に自動で出る（出ないときはこの番号を入れる）");
        }
        else
        {
            SetText(TitlePartRole.CodeBoxTitle, "参加コード");
            SetText(TitlePartRole.CodeBoxBody,
                "サーバーを作成すると発行されます（ロビーの左上に出ます）\n参加する人は「サーバーを探す → プライベートサーバー」で入れる");
        }
    }

    private void ChangeMaxPlayers(int delta)
    {
        createMaxPlayers = Mathf.Clamp(createMaxPlayers + delta, 2, SpaceJunkTeams.MaxPlayers);
        RefreshCreatePage();
    }

    private void SetCreateLan(bool lan)
    {
        createLan = lan;
        RefreshCreatePage();
    }

    private void TogglePrivate()
    {
        createPrivate = !createPrivate;
        RefreshCreatePage();
    }

    /// <summary>パブリック（false）かプライベート（true）かを決める。押したほうにだけチェックが付く。</summary>
    private void SetCreatePrivate(bool value)
    {
        createLan = false; // パブリック・プライベートはインターネットの部屋
        createPrivate = value;
        RefreshCreatePage();
    }

    private static void QuitGame()
    {
        Debug.Log("[UI] タイトル画面からゲームを終了します");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------
    // つなぐ
    // ------------------------------------------------------------

    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    private void CreateServer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(createServerName)
            ? (string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "StarSweepers" : $"{GameSettings.PlayerName} のサーバー")
            : createServerName;

        if (createLan)
        {
            StartLanHost(manager);

            // **同じネットワークに部屋を知らせる**（「サーバーを探す → LAN」の一覧に自動で出る。2026/10/15）
            if (manager.IsServer)
            {
                LanRoomDiscovery.StartAdvertising(serverName, createMaxPlayers);
            }
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        internet.HostGame(serverName, createMaxPlayers, createPrivate, createPassword.Trim());
    }

    /// <summary>LAN（同じ場所のPC同士）のホストになる。1人で練習するときも同じ（ほかの人が入ってこないだけ）。</summary>
    private static void StartLanHost(NetworkManager manager)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "0.0.0.0");
        }

        manager.StartHost();
    }

    private void JoinPrivate()
    {
        InternetConnection internet = Internet();
        if (internet != null && !string.IsNullOrWhiteSpace(privateCode))
        {
            internet.JoinGame(privateCode.Trim(), joinPassword.Trim());
        }
    }

    // ------------------------------------------------------------
    // ソロプレイ・マルチプレイ（2026/10/19・大槻さんの絵コンテ）
    // ------------------------------------------------------------

    /// <summary>サーバー検索設定を覚えておく場所（PlayerPrefs）。</summary>
    private const string SearchPublicKey = "StarSweepers.SearchPublic";
    private const string SearchFriendsKey = "StarSweepers.SearchFriends";

    /// <summary>公開のサーバーの一覧を何秒ごとに取り直すか（短いと、頼みすぎ（429）で断られる）。</summary>
    private const float PublicRefreshSeconds = 5f;

    /// <summary>公開のサーバーの一覧を取れなかったとき、次に取り直すまでの秒数。</summary>
    private const float PublicRetrySeconds = 15f;

    /// <summary>フレンドのサーバーを何秒ごとに探し直すか。</summary>
    private const float FriendRefreshSeconds = 10f;

    private bool searchPublic = true;
    private bool searchFriends = true;
    private bool searchSettingsLoaded;

    private readonly List<ISessionInfo> publicSessions = new List<ISessionInfo>();
    private string publicProblem = string.Empty;
    private float nextPublicQueryTime;
    private string friendStatus = string.Empty;
    private float nextFriendQueryTime;

    /// <summary>参加可能なサーバーの一覧の1行ぶん（LAN・公開・フレンドのどれか）。</summary>
    private struct JoinableEntry
    {
        public string Name;
        public string Players;
        public bool Full;
        public UnityEngine.Events.UnityAction Join;
        public string Key;
    }

    /// <summary>
    /// **ソロプレイ。** このPCの中だけで動くホストになって、そのままロビーへ（移すのは <see cref="TitleSceneFlow"/>）。
    /// ほかのPCからは入れない（オフライン）。のちのち CPU を足して1人でも遊べるようにする予定。
    /// </summary>
    private static void StartSolo()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            // ほかのPCから入れないように、このPCの中だけで待つ
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "127.0.0.1");
        }

        manager.StartHost();
    }

    /// <summary>マルチプレイの画面を開いたとき。検索設定のチェックを出し、一覧をすぐ取り直す。</summary>
    private void RefreshMultiPage()
    {
        if (!searchSettingsLoaded)
        {
            searchSettingsLoaded = true;
            searchPublic = PlayerPrefs.GetInt(SearchPublicKey, 1) != 0;
            searchFriends = PlayerPrefs.GetInt(SearchFriendsKey, 1) != 0;
        }

        RefreshSearchChecks();

        // 開いたときは、取り直してよい間隔なら、すぐ探す（連打で 429 にならないように、間隔は守る）
        nextFriendQueryTime = 0f;
        joinableSignature = null;
        UpdateJoinableList();
    }

    private void RefreshSearchChecks()
    {
        SetText(TitlePartRole.SearchPublicCheckText, searchPublic ? "■" : "□");
        SetText(TitlePartRole.SearchFriendsCheckText, searchFriends ? "■" : "□");
        SetSelectedLook(TitleButtonAction.ToggleSearchPublic, searchPublic);
        SetSelectedLook(TitleButtonAction.ToggleSearchFriends, searchFriends);
    }

    private void SetSearchPublic(bool value)
    {
        searchPublic = value;
        PlayerPrefs.SetInt(SearchPublicKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        joinableSignature = null;
    }

    private void SetSearchFriends(bool value)
    {
        searchFriends = value;
        PlayerPrefs.SetInt(SearchFriendsKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        nextFriendQueryTime = 0f;
        joinableSignature = null;
    }

    /// <summary>
    /// **参加可能なサーバーの一覧。** マルチプレイの画面を開いている間、毎フレーム呼ぶ。
    ///
    /// ・**LAN の部屋は、チェックに関係なく、いつも一番上**（同じネットワークにいる時点で一緒に遊ぶのが決まっているため。大槻さん）
    /// ・その下に、■パブリックサーバー なら公開のサーバー、■フレンドのサーバー なら Steam のフレンドの部屋
    /// ・公開のサーバーは5秒ごと、フレンドは10秒ごとに取り直す。中身が変わったときだけ並べ直す
    /// </summary>
    private void UpdateJoinableList()
    {
        float now = Time.unscaledTime;

        if (searchPublic && !searching && !quickPlaying && now >= nextPublicQueryTime)
        {
            nextPublicQueryTime = now + PublicRefreshSeconds;
            QueryPublicForList();
        }

        if (searchFriends && !searchingFriends && now >= nextFriendQueryTime)
        {
            nextFriendQueryTime = now + FriendRefreshSeconds;
            SearchFriendsForList();
        }

        List<JoinableEntry> entries = CollectJoinable();

        StringBuilder signature = new StringBuilder();
        signature.Append(searchPublic).Append(searchFriends).Append('|').Append(publicProblem).Append('|').Append(friendStatus).Append('|');
        foreach (JoinableEntry entry in entries)
        {
            signature.Append(entry.Key).Append(',').Append(entry.Name).Append(',').Append(entry.Players).Append(entry.Full).Append(';');
        }

        if (signature.ToString() == joinableSignature)
        {
            return;
        }

        joinableSignature = signature.ToString();
        RebuildJoinableCards(entries);
    }

    private List<JoinableEntry> CollectJoinable()
    {
        List<JoinableEntry> entries = new List<JoinableEntry>();

        // LAN の部屋（いつも一番上）
        foreach (LanRoomDiscovery.Room room in LanRoomDiscovery.Rooms)
        {
            string address = room.Address;
            ushort port = room.Port;
            entries.Add(new JoinableEntry
            {
                Key = $"lan:{room.Address}:{room.Port}",
                Name = $"【LAN】{room.Name}",
                Players = $"{room.Players}/{room.MaxPlayers}",
                Full = room.Players >= room.MaxPlayers,
                Join = () => JoinLanRoom(address, port),
            });
        }

        if (searchPublic)
        {
            foreach (ISessionInfo info in publicSessions)
            {
                string sessionId = info.Id;
                string roomName = string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name;
                bool hasPassword = info.HasPassword;
                entries.Add(new JoinableEntry
                {
                    Key = $"pub:{info.Id}",
                    Name = roomName + (hasPassword ? "（パスワードあり）" : string.Empty),
                    Players = $"{Mathf.Max(0, info.MaxPlayers - info.AvailableSlots)}/{info.MaxPlayers}",
                    Full = info.AvailableSlots <= 0 || info.IsLocked,
                    // 押したら「参加しますか？」／「パスワードを入力してください」の窓を出す（2026/10/19）
                    Join = () => OpenJoinDialog(roomName, hasPassword, password =>
                    {
                        InternetConnection internet = Internet();
                        if (internet != null)
                        {
                            internet.JoinGameById(sessionId, password);
                        }
                    }),
                });
            }
        }

        if (searchFriends)
        {
            foreach (SteamFriendRoom room in friendRooms)
            {
                string code = room.JoinCode;
                string roomName = $"{room.FriendName} の部屋";
                bool hasPassword = room.HasPassword;
                entries.Add(new JoinableEntry
                {
                    Key = $"friend:{room.JoinCode}",
                    Name = $"【フレンド】{room.FriendName} の部屋（{room.RoomName}）" + (hasPassword ? "（パスワードあり）" : string.Empty),
                    Players = room.Players,
                    Full = false,
                    Join = () => OpenJoinDialog(roomName, hasPassword, password =>
                    {
                        InternetConnection internet = Internet();
                        if (internet != null)
                        {
                            internet.JoinGame(code, password);
                        }
                    }),
                });
            }
        }

        return entries;
    }

    // ------------------------------------------------------------
    // 参加の確認の窓（2026/10/19・大槻さん）
    // ------------------------------------------------------------

    /// <summary>「参加する」「はい」を押したときに入る処理（パスワードを渡す）。</summary>
    private System.Action<string> pendingJoin;

    /// <summary>窓で、パスワードを入れて入るか（パスワードの付いたサーバー）。</summary>
    private bool pendingNeedsPassword;

    /// <summary>窓から入ろうとして、結果を待っているか（失敗したら窓に理由を出す）。</summary>
    private bool joinDialogWaiting;

    private bool IsJoinDialogOpen()
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        return dialog != null && dialog.activeSelf;
    }

    /// <summary>
    /// **参加の確認の窓を出す。** 公開・フレンドのサーバーを一覧で押したとき。
    /// ・パスワードなし … 「「○○」に参加しますか？」と「はい」「いいえ」
    /// ・パスワードあり … 「パスワードを入力してください」と入力欄、「参加する」「やめる」。違ったら「パスワードが間違えています」
    /// </summary>
    private void OpenJoinDialog(string roomName, bool needsPassword, System.Action<string> join)
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog == null)
        {
            // プレハブに窓が無い（古いプレハブ）。確かめずにそのまま入る
            join?.Invoke(joinPassword.Trim());
            return;
        }

        pendingJoin = join;
        pendingNeedsPassword = needsPassword;
        joinDialogWaiting = false;

        SetText(TitlePartRole.JoinDialogTitle, needsPassword
            ? $"「{roomName}」\nパスワードを入力してください"
            : $"「{roomName}」に参加しますか？");
        SetText(TitlePartRole.JoinDialogMessage, string.Empty);
        SetText(TitlePartRole.JoinDialogYesLabel, needsPassword ? "参加する" : "はい");
        SetText(TitlePartRole.JoinDialogNoLabel, needsPassword ? "やめる" : "いいえ");

        GameObject passwordRow = PartObject(TitlePartRole.JoinDialogPasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(needsPassword);
        }

        dialog.SetActive(true);
        RefreshInputs(dialog);

        // コントローラーでは「はい／参加する」を選んでおく
        foreach (TitleMenuButton look in dialog.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.ConfirmJoinDialog)
            {
                SelectForGamepad(look.gameObject);
                break;
            }
        }
    }

    /// <summary>「はい」「参加する」。パスワードが要るのに空なら、窓に「パスワードを入力してください」と出す。</summary>
    private void ConfirmJoinDialog()
    {
        if (pendingJoin == null)
        {
            CloseJoinDialog();
            return;
        }

        string password = pendingNeedsPassword ? joinPassword.Trim() : string.Empty;
        if (pendingNeedsPassword && password.Length == 0)
        {
            SetText(TitlePartRole.JoinDialogMessage, "パスワードを入力してください");
            return;
        }

        InternetConnection internet = Internet();
        if (internet != null && internet.IsBusy)
        {
            SetText(TitlePartRole.JoinDialogMessage, "サーバーの一覧を取っています。少し待ってから、もう一度押してください");
            return;
        }

        SetText(TitlePartRole.JoinDialogMessage, string.Empty);
        joinDialogWaiting = true;
        pendingJoin(password);
    }

    /// <summary>「いいえ」「やめる」。窓を閉じる。</summary>
    private void CloseJoinDialog()
    {
        pendingJoin = null;
        joinDialogWaiting = false;

        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog != null)
        {
            dialog.SetActive(false);
        }
    }

    /// <summary>公開のサーバーの一覧を取る（一覧に並べる用。取れなかったら15秒待つ）。</summary>
    private async void QueryPublicForList()
    {
        InternetConnection internet = Internet();
        if (internet == null)
        {
            publicProblem = "インターネットの接続の部品が見つかりません";
            return;
        }

        if (internet.IsBusy)
        {
            return;
        }

        searching = true;
        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        searching = false;

        if (this == null)
        {
            return;
        }

        publicSessions.Clear();
        if (sessions == null)
        {
            publicProblem = (string.IsNullOrEmpty(internet.Message) ? "公開のサーバーの一覧を取れませんでした" : internet.Message) +
                            $"（{PublicRetrySeconds:0}秒後にもう一度探します）";
            internet.ClearFailure();
            nextPublicQueryTime = Time.unscaledTime + PublicRetrySeconds;
        }
        else
        {
            publicProblem = string.Empty;
            publicSessions.AddRange(sessions);
        }
    }

    /// <summary>
    /// Steam のフレンドのうち、このゲームで部屋に入っている人を探す（一覧に並べる用。<see cref="SteamFriendsService"/>）。
    /// フレンドの様子は届くまで少しかかるので、頼んでから少し待って読む。
    /// </summary>
    private async void SearchFriendsForList()
    {
        if (!SteamFriendsService.IsAvailable)
        {
            friendRooms.Clear();
            friendStatus = SteamFriendsService.StatusMessage;
            return;
        }

        searchingFriends = true;
        SteamFriendsService.RequestFriendRooms();
        await System.Threading.Tasks.Task.Delay(1000);
        searchingFriends = false;

        if (this == null)
        {
            return;
        }

        friendRooms.Clear();
        friendRooms.AddRange(SteamFriendsService.FindFriendRooms());
        friendStatus = string.Empty;
    }

    private void RebuildJoinableCards(List<JoinableEntry> entries)
    {
        foreach (GameObject card in joinableCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }
        joinableCards.Clear();

        GameObject content = PartObject(TitlePartRole.JoinableListContent);
        if (content != null && joinableCardTemplate != null)
        {
            foreach (JoinableEntry entry in entries)
            {
                joinableCards.Add(AddJoinableCard(content.transform, entry));
            }
        }

        StringBuilder message = new StringBuilder();
        if (entries.Count == 0)
        {
            message.Append(searchPublic || searchFriends
                ? "参加できるサーバーが見つかりません。\n「クイックプレイ」か「サーバーを作る」で遊べます"
                : "サーバー検索設定のチェックが外れています（LAN の部屋だけを探しています）");
        }

        if (searchPublic && !string.IsNullOrEmpty(publicProblem))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("公開のサーバー：").Append(publicProblem);
        }

        if (searchFriends && !string.IsNullOrEmpty(friendStatus))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("フレンドのサーバー：").Append(friendStatus);
        }

        SetText(TitlePartRole.JoinableListMessage, message.ToString());
    }

    /// <summary>見本のカード（JoinableCardTemplate）を複製して、1行ぶんを一覧に足す。押すとそのサーバーに入る。</summary>
    private GameObject AddJoinableCard(Transform content, JoinableEntry entry)
    {
        GameObject card = Instantiate(joinableCardTemplate, content);
        card.name = $"Server {entry.Name}";
        card.SetActive(true);

        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            if (text == null)
            {
                continue;
            }

            if (part.Role == TitlePartRole.ServerCardName)
            {
                text.text = entry.Name;
            }
            else if (part.Role == TitlePartRole.ServerCardPlayers)
            {
                text.text = entry.Players;
            }
        }

        foreach (TitleMenuButton look in card.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Label != null)
            {
                look.Label.text = entry.Full ? "満員" : "参加する";
            }

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            button.onClick.RemoveAllListeners();
            if (!entry.Full && entry.Join != null)
            {
                button.onClick.AddListener(entry.Join);
            }
        }

        return card;
    }

    // ------------------------------------------------------------
    // クイックプレイ（2026/10/15・大槻さん）
    // ------------------------------------------------------------

    /// <summary>クイックプレイで、公開のサーバーを探している途中か（二度押し防止）。</summary>
    private bool quickPlaying;

    /// <summary>
    /// **クイックプレイ。** 公開されているサーバー（パブリック）を探して入る。
    ///
    /// ・入れるサーバー（満員でない・鍵がかかっていない・パスワードなし）のうち、**いちばん人の多いところ**に入る
    ///   （人がばらばらの部屋に分かれないように）
    /// ・1つも無ければ、**初期の設定でパブリックのサーバーを作る**（名前は「（名前）のサーバー」・定員・パスワードなし）
    ///
    /// 探している途中・入る途中の表示は、つないでいる途中の表示（<see cref="UpdateBusyOverlay"/>）がそのまま出す。
    /// </summary>
    private async void QuickPlay()
    {
        if (quickPlaying)
        {
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        quickPlaying = true;

        // 参加可能なサーバーの一覧を取っている途中なら、終わるまで待つ（インターネットの係は1つのことしかできないため）
        while (searching && this != null)
        {
            await System.Threading.Tasks.Task.Delay(100);
        }

        if (this == null || internet.IsBusy)
        {
            quickPlaying = false;
            return;
        }

        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        quickPlaying = false;

        if (this == null)
        {
            return;
        }

        // 一覧を取れなかった（つながっていない など）。理由は、つないでいる途中の表示に「戻る」と一緒に出る
        if (sessions == null)
        {
            return;
        }

        ISessionInfo best = null;
        int bestPlayers = -1;
        foreach (ISessionInfo info in sessions)
        {
            if (info.AvailableSlots <= 0 || info.IsLocked || info.HasPassword)
            {
                continue;
            }

            int players = Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
            if (players > bestPlayers)
            {
                best = info;
                bestPlayers = players;
            }
        }

        if (best != null)
        {
            Debug.Log($"[UI] クイックプレイ：公開のサーバー「{best.Name}」（{bestPlayers}/{best.MaxPlayers}）に入ります。");
            internet.JoinGameById(best.Id);
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(GameSettings.PlayerName)
            ? "StarSweepers"
            : $"{GameSettings.PlayerName} のサーバー";

        Debug.Log($"[UI] クイックプレイ：入れる公開のサーバーが無いので、「{serverName}」を作ります。");
        internet.HostGame(serverName, SpaceJunkTeams.MaxPlayers, false, string.Empty);
    }

    // ------------------------------------------------------------
    // 見つかった LAN の部屋（「LANのサーバーに参加する」画面。2026/10/15・大槻さん「LAN の部屋の自動表示も残して」）
    // ------------------------------------------------------------

    /// <summary>LAN の部屋の知らせを聞くか（「LANのサーバーに参加する」画面を開いている間だけ聞く）。</summary>
    private void SetLanListening(bool listen)
    {
        if (listen == lanListening)
        {
            return;
        }

        lanListening = listen;
        if (listen)
        {
            LanRoomDiscovery.StartListening();
        }
        else
        {
            LanRoomDiscovery.StopListening();
        }
    }

    /// <summary>一覧で選んだ LAN の部屋に入る（IPアドレスとポートは、部屋の知らせに入っていたもの）。</summary>
    private void JoinLanRoom(string address, ushort port)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        lanAddress = address;
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(address, port);
        }

        manager.StartClient();
    }

    // ------------------------------------------------------------
    // ホストに切られて戻ってきたとき（2026/10/15）
    // ------------------------------------------------------------

    /// <summary>
    /// **ホストに切られて戻ってきたときは、その理由を出す**（例：「満員です（最大8人）」）。
    /// つないでいる途中の表示と同じ枠に出し、「OK」で閉じる（<see cref="UpdateBusyOverlay"/>）。
    /// </summary>
    private void TakeDisconnectReason()
    {
        NetworkManager manager = NetworkManager.Singleton;
        string reason = manager != null ? manager.DisconnectReason : string.Empty;
        if (string.IsNullOrEmpty(reason) || reason == shownDisconnectReason)
        {
            return;
        }

        // **Netcode が自分で付ける記録（「[Disconnect Event]…TransportShutdown」など）は出さない。**（2026/10/19・大槻さんの報告）
        // 自分で通信を止めた（ホストがタイトルに戻った など）ときにも付くので、出すと「入れませんでした」と誤って見えた。
        // 出すのは、ホストが切るときに付けた理由（「満員です（最大8人）」など）だけ
        if (reason.StartsWith("["))
        {
            shownDisconnectReason = reason;
            return;
        }

        shownDisconnectReason = reason;
        disconnectNotice = $"部屋に入れませんでした：{reason}";
    }

    // ------------------------------------------------------------
    // フレンドのサーバー（Steam。2026/10/6・大槻さん「スプラみたいにフレンドのサーバーを出したい」）
    // ------------------------------------------------------------

    /// <summary>
    /// Steam のフレンド一覧の「ゲームに参加」で頼まれた部屋に入る（タイトル画面にいて、まだつながっていないときだけ）。
    /// </summary>
    private void JoinFromSteamRequest()
    {
        if (string.IsNullOrEmpty(SteamFriendsService.PendingJoinCode) || IsConnected())
        {
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null || internet.IsBusy)
        {
            return;
        }

        string code = SteamFriendsService.ConsumePendingJoinCode();
        Debug.Log($"[STEAM] Steam から頼まれた部屋（{code}）に入ります。");
        internet.JoinGame(code, joinPassword.Trim());
    }

    /// <summary>
    /// つないでいる途中・失敗したときの表示。
    /// ・インターネット：準備中・作っている・入ろうとしている → 説明を出す。失敗 → 理由と「戻る」
    /// ・LAN：つなぎに行っている → 説明と「やめる」
    /// ・つながった → ホストがロビーへ移すまでの間「移動しています」
    /// </summary>
    private void UpdateBusyOverlay()
    {
        GameObject overlay = PartObject(TitlePartRole.BusyOverlay);
        if (overlay == null)
        {
            return;
        }

        InternetConnection internet = Internet();
        NetworkManager manager = NetworkManager.Singleton;

        bool lanConnecting = manager != null && manager.IsListening && !manager.IsServer && !manager.IsConnectedClient &&
                             (internet == null || internet.State != InternetConnection.Phase.Joining);

        string message = null;
        string buttonLabel = null;
        UnityEngine.Events.UnityAction action = null;

        if (IsConnected())
        {
            message = "つながりました。ロビーに移動しています…";
        }
        // 参加可能なサーバーの一覧を取っている間は出さない（一覧の中に「探しています」が出る）
        else if (internet != null && internet.IsBusy && !searching)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        // 失敗はどの画面でも出す（一覧から入るときに、パスワードが違うと分かるように。一覧の検索の失敗は、一覧の中に出してすぐ消す）
        // 参加の確認の窓から入ろうとして失敗した：つないでいる途中の表示ではなく、窓の中に理由を出す（2026/10/19）
        else if (internet != null && internet.State == InternetConnection.Phase.Failed && joinDialogWaiting)
        {
            joinDialogWaiting = false;
            SetText(TitlePartRole.JoinDialogMessage, internet.LastJoinFailedByPassword
                ? "パスワードが間違えています"
                : internet.Message);
            internet.ClearFailure();
        }
        else if (internet != null && internet.State == InternetConnection.Phase.Failed)
        {
            message = internet.Message;
            buttonLabel = "戻る";
            action = () => internet.ClearFailure();
        }
        else if (!string.IsNullOrEmpty(disconnectNotice) && (manager == null || !manager.IsListening))
        {
            // ホストに切られて戻ってきた理由（満員など）。「OK」で閉じる（2026/10/15）
            message = disconnectNotice;
            buttonLabel = "OK";
            action = () => disconnectNotice = null;
        }
        else if (lanConnecting)
        {
            message = $"LAN のサーバー（{lanAddress}）につないでいます…\nつながらないときは、IPアドレスと、同じネットワークにいるかを確かめてください。";
            buttonLabel = "やめる";
            action = () => manager.Shutdown();
        }

        Button busyButton = PartComponent<Button>(TitlePartRole.BusyButton);

        bool show = message != null;
        if (overlay.activeSelf != show)
        {
            overlay.SetActive(show);
            if (show && buttonLabel != null && busyButton != null)
            {
                SelectForGamepad(busyButton.gameObject);
            }
        }

        if (!show)
        {
            return;
        }

        SetText(TitlePartRole.BusyMessage, message);

        if (busyButton != null)
        {
            busyButton.gameObject.SetActive(buttonLabel != null);
            busyButton.onClick.RemoveAllListeners();
            if (action != null)
            {
                busyButton.onClick.AddListener(action);
                SetText(TitlePartRole.BusyButtonLabel, buttonLabel);
            }
        }
    }

    /// <summary>つながっているか（ホストとして動いている／参加者としてつながり終わった）。</summary>
    private static bool IsConnected()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && (manager.IsServer || manager.IsConnectedClient);
    }

    // ------------------------------------------------------------
    // 設定
    // ------------------------------------------------------------

    private void WireSettings()
    {
        resolutions = UniqueResolutions();

        foreach (TitleSettingRow row in GetComponentsInChildren<TitleSettingRow>(true))
        {
            settingRows.Add(row);
            TitleSettingRow target = row;

            if (row.Slider != null)
            {
                SetupSliderRange(row);
                row.Slider.onValueChanged.AddListener(value =>
                {
                    SetSliderValue(target.Kind, value);
                    RefreshSettingRow(target);
                });
            }

            if (row.Previous != null)
            {
                row.Previous.onClick.AddListener(() =>
                {
                    Step(target.Kind, -1);
                    RefreshSettingRow(target);
                });
            }

            if (row.Next != null)
            {
                row.Next.onClick.AddListener(() =>
                {
                    Step(target.Kind, 1);
                    RefreshSettingRow(target);
                });
            }
        }
    }

    private void ShowSettingsTab(SettingsTab tab)
    {
        settingsTab = tab;

        foreach (KeyValuePair<SettingsTab, GameObject> pair in settingsGroups)
        {
            pair.Value.SetActive(pair.Key == tab);
        }

        SetSelectedLook(TitleButtonAction.TabSound, tab == SettingsTab.Sound);
        SetSelectedLook(TitleButtonAction.TabGraphics, tab == SettingsTab.Graphics);
        SetSelectedLook(TitleButtonAction.TabGeneral, tab == SettingsTab.General);

        // 開くたびに、いまの値を出し直す（ポーズ画面で変えた値にも合わせる）
        resolutionIndex = Mathf.Max(0, FindCurrentResolution(resolutions));
        pendingScreenMode = Screen.fullScreenMode;

        foreach (TitleSettingRow row in settingRows)
        {
            RefreshSettingRow(row);
        }
    }

    private static void SetupSliderRange(TitleSettingRow row)
    {
        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                row.Slider.minValue = 0f;
                row.Slider.maxValue = 1f;
                break;
            case TitleSettingKind.MouseSensitivity:
                row.Slider.minValue = 0.2f;
                row.Slider.maxValue = 3f;
                break;
            case TitleSettingKind.FrameRateLimit:
                row.Slider.minValue = 30f;
                row.Slider.maxValue = 240f;
                break;
        }
    }

    private static void SetSliderValue(TitleSettingKind kind, float value)
    {
        switch (kind)
        {
            case TitleSettingKind.Volume:
                GameSettings.Volume = value;
                break;
            case TitleSettingKind.MouseSensitivity:
                GameSettings.MouseSensitivity = value;
                break;
            case TitleSettingKind.FrameRateLimit:
                GameSettings.FrameRateLimit = Mathf.RoundToInt(value / 10f) * 10;
                break;
        }
    }

    private void Step(TitleSettingKind kind, int delta)
    {
        switch (kind)
        {
            case TitleSettingKind.ControllerType:
                GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
                    ? ControllerOperationType.TypeB
                    : ControllerOperationType.TypeA;
                break;

            case TitleSettingKind.Resolution:
                if (resolutions.Length > 0)
                {
                    resolutionIndex = (resolutionIndex + delta + resolutions.Length) % resolutions.Length;
                }
                break;

            case TitleSettingKind.ScreenMode:
                int index = System.Array.IndexOf(ScreenModes, pendingScreenMode);
                index = index < 0 ? 0 : (index + delta + ScreenModes.Length) % ScreenModes.Length;
                pendingScreenMode = ScreenModes[index];
                break;

            case TitleSettingKind.VSync:
                GameSettings.VSync = !GameSettings.VSync;
                break;

            case TitleSettingKind.Quality:
                string[] qualities = QualitySettings.names;
                if (qualities.Length > 0)
                {
                    GameSettings.QualityLevel = (QualitySettings.GetQualityLevel() + delta + qualities.Length) % qualities.Length;
                }
                break;
        }
    }

    /// <summary>設定の1行に、いまの値を出す。</summary>
    private void RefreshSettingRow(TitleSettingRow row)
    {
        string text;
        float? sliderValue = null;

        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                sliderValue = GameSettings.Volume;
                text = $"{Mathf.RoundToInt(GameSettings.Volume * 100f)} %";
                break;
            case TitleSettingKind.MouseSensitivity:
                sliderValue = GameSettings.MouseSensitivity;
                text = $"{GameSettings.MouseSensitivity:0.0} 倍";
                break;
            case TitleSettingKind.FrameRateLimit:
                sliderValue = GameSettings.FrameRateLimit;
                text = $"{GameSettings.FrameRateLimit} fps";
                break;
            case TitleSettingKind.ControllerType:
                text = GameSettings.ControllerOperation == ControllerOperationType.TypeB ? "タイプB" : "タイプA";
                break;
            case TitleSettingKind.Resolution:
                text = resolutions.Length == 0
                    ? "—"
                    : $"{resolutions[resolutionIndex].width}x{resolutions[resolutionIndex].height}";
                break;
            case TitleSettingKind.ScreenMode:
                text = ScreenModeName(pendingScreenMode);
                break;
            case TitleSettingKind.VSync:
                text = GameSettings.VSync ? "ON" : "OFF";
                break;
            default:
                string[] qualities = QualitySettings.names;
                text = qualities.Length == 0
                    ? "—"
                    : qualities[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualities.Length - 1)];
                break;
        }

        if (row.ValueText != null)
        {
            row.ValueText.text = text;
        }

        if (sliderValue.HasValue && row.Slider != null)
        {
            row.Slider.SetValueWithoutNotify(sliderValue.Value);
        }
    }

    /// <summary>「解像度／スクリーンモードを適用」。</summary>
    private void ApplyScreen()
    {
        if (resolutions.Length > 0)
        {
            Resolution r = resolutions[resolutionIndex];
            Screen.SetResolution(r.width, r.height, pendingScreenMode);
        }
        else
        {
            Screen.fullScreenMode = pendingScreenMode;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    private GameObject PartObject(TitlePartRole role)
    {
        return parts.TryGetValue(role, out TitlePart part) && part != null ? part.gameObject : null;
    }

    private T PartComponent<T>(TitlePartRole role) where T : Component
    {
        GameObject target = PartObject(role);
        return target != null ? target.GetComponent<T>() : null;
    }

    private Text PartText(TitlePartRole role)
    {
        return PartComponent<Text>(role);
    }

    private void SetText(TitlePartRole role, string value)
    {
        Text text = PartText(role);
        if (text != null)
        {
            text.text = value;
        }
    }

    /// <summary>このPCのIPアドレス（LAN でホストになるとき、参加する人に伝える番号）。</summary>
    public static string LocalAddresses()
    {
        List<string> found = new List<string>();

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                    {
                        found.Add(address.Address.ToString());
                    }
                }
            }
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[UI] IPアドレスを調べられませんでした：{error.Message}");
        }

        return found.Count > 0 ? string.Join(" ／ ", found) : "（見つかりませんでした）";
    }

    /// <summary>同じ大きさの解像度を1つにまとめた一覧（リフレッシュレート違いを除く）。</summary>
    private static Resolution[] UniqueResolutions()
    {
        List<Resolution> list = new List<Resolution>();
        foreach (Resolution r in Screen.resolutions)
        {
            if (!list.Exists(x => x.width == r.width && x.height == r.height))
            {
                list.Add(r);
            }
        }
        return list.ToArray();
    }

    private static int FindCurrentResolution(Resolution[] list)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].width == Screen.width && list[i].height == Screen.height)
            {
                return i;
            }
        }
        return list.Length - 1;
    }

    private static string ScreenModeName(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.ExclusiveFullScreen:
                return "フルスクリーン";
            case FullScreenMode.FullScreenWindow:
                return "ウィンドウフルスクリーン";
            case FullScreenMode.MaximizedWindow:
                return "最大化ウィンドウ";
            default:
                return "ウィンドウ";
        }
    }

    /// <summary>コントローラーがつながっていれば、その画面の最初の項目を選んでおく（十字キーで動かせるように）。</summary>
    private static void SelectFirstForGamepad(GameObject page)
    {
        if (Gamepad.current == null || page == null || EventSystem.current == null)
        {
            return;
        }

        Selectable first = page.GetComponentInChildren<Selectable>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    private static void SelectForGamepad(GameObject target)
    {
        if (Gamepad.current != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
        }
    }

    /// <summary>UIのクリックを受け取る仕組みがなければ作る（ポーズ画面と同じ。シーンが変わっても残す）。</summary>
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
