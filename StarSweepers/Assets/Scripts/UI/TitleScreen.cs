using System.Collections.Generic;
using System.Net;
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
/// | メイン | サーバーを作る／サーバーを探す／設定／ゲームを終了。右下にバージョン |
/// | サーバーを作る | 名前・最大人数（2〜8）・サーバーの名前・つなぎかた（インターネット／LAN）・プライベートにするか → 作成。1人で練習もここから |
/// | サーバーを探す | プライベートサーバー（参加コード）／パブリックサーバー（一覧から選ぶ）／LAN（IPアドレス）の3つから選んで参加 |
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
    private readonly List<GameObject> serverCards = new List<GameObject>();
    private GameObject serverCardTemplate;
    private GameObject friendCardTemplate;
    private readonly List<GameObject> friendCards = new List<GameObject>();
    private bool searchingFriends;
    private bool initialized;

    private Page current = Page.Main;

    // サーバーを作る
    private int createMaxPlayers = 4;
    private bool createLan;
    private bool createPrivate = true;
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
    }

    private void OnDisable()
    {
        IsVisible = false;

        // **閉じたあとも、カーソルは出したままにする。**（2026/10/6「ロビーでクリックするたびにカーソルが隠れる」の修正）
        ShowCursor();
    }

    private void Update()
    {
        // 遊んでいた画面から戻ってきたときなどに、カーソルが隠れたままにならないように
        ShowCursor();

        JoinFromSteamRequest();
        UpdateBusyOverlay();

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
        AddPage(Page.Find, TitlePartRole.PageFind);
        AddPage(Page.FindPrivate, TitlePartRole.PageFindPrivate);
        AddPage(Page.FindPublic, TitlePartRole.PageFindPublic);
        AddPage(Page.FindLan, TitlePartRole.PageFindLan);
        AddPage(Page.Settings, TitlePartRole.PageSettings);
        AddPage(Page.FindFriends, TitlePartRole.PageFindFriends);

        AddSettingsGroup(SettingsTab.Sound, TitlePartRole.SettingsSound);
        AddSettingsGroup(SettingsTab.Graphics, TitlePartRole.SettingsGraphics);
        AddSettingsGroup(SettingsTab.General, TitlePartRole.SettingsGeneral);

        // サーバーの一覧のカードの見本。これを複製して1枚ずつ作る（見本そのものは隠しておく）
        serverCardTemplate = PartObject(TitlePartRole.ServerCardTemplate);
        if (serverCardTemplate != null)
        {
            serverCardTemplate.SetActive(false);
        }

        // フレンドのサーバーのカードの見本（Steam。2026/10/6）
        friendCardTemplate = PartObject(TitlePartRole.FriendCardTemplate);
        if (friendCardTemplate != null)
        {
            friendCardTemplate.SetActive(false);
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
            if ((serverCardTemplate != null && look.transform.IsChildOf(serverCardTemplate.transform)) ||
                (friendCardTemplate != null && look.transform.IsChildOf(friendCardTemplate.transform)))
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
            case TitleButtonAction.OpenCreate: ShowPage(Page.Create); break;
            case TitleButtonAction.OpenFind: ShowPage(Page.Find); break;
            case TitleButtonAction.OpenSettings: ShowPage(Page.Settings); break;
            case TitleButtonAction.Quit: QuitGame(); break;
            case TitleButtonAction.Back: GoBack(); break;

            case TitleButtonAction.CreateServer: CreateServer(); break;
            case TitleButtonAction.PracticeSolo:
                if (NetworkManager.Singleton != null)
                {
                    StartLanHost(NetworkManager.Singleton);
                }
                break;
            case TitleButtonAction.MaxPlayersDown: ChangeMaxPlayers(-1); break;
            case TitleButtonAction.MaxPlayersUp: ChangeMaxPlayers(1); break;
            case TitleButtonAction.UseInternet: SetCreateLan(false); break;
            case TitleButtonAction.UseLan: SetCreateLan(true); break;
            case TitleButtonAction.TogglePrivate: TogglePrivate(); break;
            case TitleButtonAction.SetPublic: SetCreatePrivate(false); break;
            case TitleButtonAction.SetPrivate: SetCreatePrivate(true); break;

            case TitleButtonAction.OpenFindPrivate: ShowPage(Page.FindPrivate); break;
            case TitleButtonAction.OpenFindPublic: ShowPage(Page.FindPublic); break;
            case TitleButtonAction.OpenFindLan: ShowPage(Page.FindLan); break;
            case TitleButtonAction.JoinPrivate: JoinPrivate(); break;
            case TitleButtonAction.JoinLan: JoinLan(); break;
            case TitleButtonAction.SearchPublic: SearchPublic(); break;
            case TitleButtonAction.OpenFindFriends: ShowPage(Page.FindFriends); break;
            case TitleButtonAction.RefreshFriends: SearchFriends(); break;

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
            case Page.FindPublic:
                SearchPublic();
                break;
            case Page.FindFriends:
                SearchFriends();
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
        switch (current)
        {
            case Page.Create:
            case Page.Find:
            case Page.Settings:
                ShowPage(Page.Main);
                break;
            case Page.FindPrivate:
            case Page.FindPublic:
            case Page.FindLan:
            case Page.FindFriends:
                ShowPage(Page.Find);
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
        SetSelectedLook(TitleButtonAction.UseInternet, !createLan);
        SetSelectedLook(TitleButtonAction.UseLan, createLan);
        // パブリック／プライベートは、どちらか片方だけにチェックが付く（2026/10/6）
        SetText(TitlePartRole.PrivateCheckText, createPrivate ? "■" : "□");
        SetText(TitlePartRole.PublicCheckText, createPrivate ? "□" : "■");
        SetSelectedLook(TitleButtonAction.SetPrivate, createPrivate);
        SetSelectedLook(TitleButtonAction.SetPublic, !createPrivate);

        GameObject privateRow = PartObject(TitlePartRole.PrivateRow);
        if (privateRow != null)
        {
            privateRow.SetActive(!createLan);
        }

        // パスワードはインターネットの部屋だけ（LAN では使わない）
        GameObject passwordRow = PartObject(TitlePartRole.PasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(!createLan);
        }

        if (createLan)
        {
            SetText(TitlePartRole.CodeBoxTitle, "このPCのIPアドレス");
            SetText(TitlePartRole.CodeBoxBody, $"{LocalAddresses()}\n参加する人は「サーバーを探す → LAN」でこの番号を入れる");
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
        createMaxPlayers = Mathf.Clamp(createMaxPlayers + delta, 2, 8);
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

        if (createLan)
        {
            StartLanHost(manager);
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(createServerName)
            ? (string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "StarSweepers" : $"{GameSettings.PlayerName} のサーバー")
            : createServerName;

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

    private void JoinLan()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || string.IsNullOrWhiteSpace(lanAddress))
        {
            return;
        }

        PlayerPrefs.SetString(LastLanAddressKey, lanAddress.Trim());
        PlayerPrefs.Save();

        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(lanAddress.Trim(), transport.ConnectionData.Port);
        }

        manager.StartClient();
    }

    private async void SearchPublic()
    {
        RectTransform content = PartObject(TitlePartRole.PublicListContent) != null
            ? (RectTransform)PartObject(TitlePartRole.PublicListContent).transform
            : null;

        if (searching || content == null)
        {
            return;
        }

        ClearServerCards();
        SetText(TitlePartRole.PublicListMessage, "サーバーを探しています…");

        InternetConnection internet = Internet();
        if (internet == null)
        {
            SetText(TitlePartRole.PublicListMessage, "インターネットの接続の部品が見つかりません。");
            return;
        }

        searching = true;
        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        searching = false;

        // 待っている間に画面を移っていたら何もしない
        if (this == null || current != Page.FindPublic)
        {
            return;
        }

        if (sessions == null)
        {
            SetText(TitlePartRole.PublicListMessage,
                string.IsNullOrEmpty(internet.Message) ? "サーバーの一覧を取れませんでした。" : internet.Message);
            internet.ClearFailure();
            return;
        }

        SetText(TitlePartRole.PublicListMessage,
            sessions.Count == 0 ? "サーバーが見つかりませんでした。「検索」でもう一度探せます。" : string.Empty);

        foreach (ISessionInfo info in sessions)
        {
            AddPublicServerCard(content, info);
        }
    }

    private void ClearServerCards()
    {
        foreach (GameObject card in serverCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }

        serverCards.Clear();
    }

    /// <summary>見本のカード（ServerCardTemplate）を複製して、サーバー1つぶんを一覧に足す。</summary>
    private void AddPublicServerCard(RectTransform content, ISessionInfo info)
    {
        if (serverCardTemplate == null)
        {
            Debug.LogWarning("[UI] サーバーの一覧のカードの見本（Role：ServerCardTemplate）がプレハブにありません。");
            return;
        }

        GameObject card = Instantiate(serverCardTemplate, content);
        card.name = $"Server {info.Name}";
        card.SetActive(true);
        serverCards.Add(card);

        int players = Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
        bool full = info.AvailableSlots <= 0 || info.IsLocked;
        string sessionId = info.Id;

        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            if (text == null)
            {
                continue;
            }

            if (part.Role == TitlePartRole.ServerCardName)
            {
                text.text = (string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name) +
                            (info.HasPassword ? "（パスワードあり）" : string.Empty);
            }
            else if (part.Role == TitlePartRole.ServerCardPlayers)
            {
                text.text = $"{players}/{info.MaxPlayers}";
            }
        }

        foreach (TitleMenuButton look in card.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action != TitleButtonAction.JoinListedServer)
            {
                continue;
            }

            if (look.Label != null)
            {
                look.Label.text = full ? "満員" : "参加する";
            }

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                InternetConnection internet = Internet();
                if (internet != null && !full)
                {
                    internet.JoinGameById(sessionId, joinPassword.Trim());
                }
            });
        }
    }

    // ------------------------------------------------------------
    // フレンドのサーバー（Steam。2026/10/6・大槻さん「スプラみたいにフレンドのサーバーを出したい」）
    // ------------------------------------------------------------

    /// <summary>
    /// Steam のフレンドのうち、このゲームで部屋に入っている人を並べる（<see cref="SteamFriendsService"/>）。
    /// フレンドの様子は取り寄せてから届くまで少しかかるので、頼んでから少し待って読む。
    /// </summary>
    private async void SearchFriends()
    {
        if (searchingFriends)
        {
            return;
        }

        ClearFriendCards();

        // 説明が長くなるので、枠からはみ出しても下へ続けて出す
        Text friendMessage = PartText(TitlePartRole.FriendListMessage);
        if (friendMessage != null)
        {
            friendMessage.verticalOverflow = VerticalWrapMode.Overflow;
        }

        if (!SteamFriendsService.IsAvailable)
        {
            // つながらなかった理由（Steam が起動していない など）をそのまま出す
            SetText(TitlePartRole.FriendListMessage, SteamFriendsService.StatusMessage);
            return;
        }

        SetText(TitlePartRole.FriendListMessage, "フレンドを探しています…");
        searchingFriends = true;
        SteamFriendsService.RequestFriendRooms();
        await System.Threading.Tasks.Task.Delay(1000);

        // フレンドの様子は届くのに時間がかかることがある。遊んでいる人がいるのに部屋が見つからなければ、少し待ってもう一度読む
        List<SteamFriendRoom> rooms = SteamFriendsService.FindFriendRooms();
        if (rooms.Count == 0 && SteamFriendsService.CountFriendsPlayingThisGame() > 0 && this != null && current == Page.FindFriends)
        {
            SteamFriendsService.RequestFriendRooms();
            await System.Threading.Tasks.Task.Delay(2000);
            rooms = SteamFriendsService.FindFriendRooms();
        }

        searchingFriends = false;

        // 待っている間に画面を移っていたら何もしない
        if (this == null || current != Page.FindFriends)
        {
            return;
        }

        string hint = SteamFriendsService.CountFriendsPlayingThisGame() > 0
            ? "このゲームを遊んでいるフレンドはいますが、インターネットの部屋に入っている人はいません（LAN の部屋は出ません）。"
            : "このゲームを遊んでいるフレンドがいません。相手もゲームを起動しているか、Steam でフレンドになっているか確かめてください。";
        SetText(TitlePartRole.FriendListMessage, rooms.Count == 0
            ? $"{hint}\n「フレンドのサーバーを更新」でもう一度探せます。\n\n{SteamFriendsService.Diagnose()}"
            : string.Empty);

        GameObject content = PartObject(TitlePartRole.FriendListContent);
        if (content == null || friendCardTemplate == null)
        {
            return;
        }

        foreach (SteamFriendRoom room in rooms)
        {
            AddFriendCard(content.transform, room);
        }
    }

    private void ClearFriendCards()
    {
        foreach (GameObject card in friendCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }

        friendCards.Clear();
    }

    /// <summary>見本のカード（FriendCardTemplate）を複製して、フレンドの部屋1つぶんを足す。</summary>
    private void AddFriendCard(Transform content, SteamFriendRoom room)
    {
        GameObject card = Instantiate(friendCardTemplate, content);
        card.name = $"Friend {room.FriendName}";
        card.SetActive(true);
        friendCards.Add(card);

        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            if (text == null)
            {
                continue;
            }

            if (part.Role == TitlePartRole.FriendCardName)
            {
                text.text = $"{room.FriendName} の部屋" + (room.HasPassword ? "（パスワードあり）" : string.Empty);
            }
            else if (part.Role == TitlePartRole.FriendCardInfo)
            {
                text.text = $"{room.RoomName}　{room.Players}";
            }
        }

        string code = room.JoinCode;
        foreach (TitleMenuButton look in card.GetComponentsInChildren<TitleMenuButton>(true))
        {
            Button button = look.GetComponent<Button>();
            if (look.Action != TitleButtonAction.JoinFriendServer || button == null)
            {
                continue;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                InternetConnection internet = Internet();
                if (internet != null)
                {
                    internet.JoinGame(code, joinPassword.Trim());
                }
            });
        }
    }

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
        else if (internet != null && internet.IsBusy && current != Page.FindPublic)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        // 失敗はどの画面でも出す（一覧から入るときに、パスワードが違うと分かるように。一覧の検索の失敗は、一覧の中に出してすぐ消す）
        else if (internet != null && internet.State == InternetConnection.Phase.Failed)
        {
            message = internet.Message;
            buttonLabel = "戻る";
            action = () => internet.ClearFailure();
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
