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
using UnityEngine.SceneManagement;
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
/// ## いつ出るか
///
/// **タイトルのシーン（TitleScene）で出る。** ゲームはここから始まる（ビルドの最初のシーン）。
/// サーバーを作る（ホストになる）と、**ホストが全員をロビー（SpaceJunkLobby）へ移す。** 参加者は、入った時点でホストのシーンへ自動で移る。
/// 参加者が「ロビーに戻る」で抜けたとき・ホストとの通信が切れたときは、タイトルへ戻ってくる。
/// ロビーを直接再生しても、どこにもつながっていなければタイトルへ移る。
/// シーンに置かなくても、再生したときに自分で1つ作る（<see cref="PauseMenu"/> と同じ考え方）。
///
/// ## 見た目を変えるには
///
/// **`Assets/Resources/TitleScreenTheme.asset`（<see cref="TitleScreenTheme"/>）の画像と色を差し替える。**
/// 画面はコードで組み立てているが、画像・色・文字の大きさはすべてテーマから読む。
/// （日本語のフォントを、再生したときにパソコンから借りてくる必要があるため、画面そのものはプレハブにしていない）
///
/// ## 操作
///
/// マウスで選ぶ。コントローラーでは十字キー／スティックで選んで A、**B（キーボードは Esc）で1つ前の画面へ戻る。**
/// </summary>
public class TitleScreen : MonoBehaviour
{
    /// <summary>タイトル画面を出すシーンの名前（2026/10/6 からタイトル専用のシーン）。</summary>
    private const string TitleSceneName = "TitleScene";

    /// <summary>ロビーのシーンの名前（つながったら、ホストが全員をここへ移す）。</summary>
    private const string LobbySceneName = "SpaceJunkLobby";

    /// <summary>テーマのファイルの場所（Resources の中）。</summary>
    private const string ThemeResourcePath = "TitleScreenTheme";

    /// <summary>LAN で最後に入れたIPアドレス（次に開いたときに入れておく）。</summary>
    private const string LastLanAddressKey = "StarSweepers.LastLanAddress";

    /// <summary>サーバーの名前の最大の文字数。</summary>
    private const int ServerNameMaxLength = 25;

    /// <summary>いまタイトル画面が出ているか。ポーズ画面やロビーの画面が、これを見て自分を隠す。</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>画面の種類（<see cref="TitleScreenPreview"/> で、見本に出す画面を選ぶのにも使う）。</summary>
    public enum Page
    {
        Main,
        Create,
        Find,
        FindPrivate,
        FindPublic,
        FindLan,
        Settings,
    }

    private enum SettingsTab
    {
        Sound,
        Graphics,
        General,
    }

    private TitleScreenTheme theme;
    private Font font;

    private GameObject root;
    private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    private Page current = Page.Main;

    private GameObject busyOverlay;
    private Text busyText;
    private Button busyButton;
    private Text busyButtonLabel;

    private Text mainNameText;

    // サーバーを作る
    private int createMaxPlayers = 4;
    private bool createLan;
    private bool createPrivate = true;
    private string createServerName = string.Empty;
    private Text maxPlayersText;
    private TitleMenuButton internetModeButton;
    private TitleMenuButton lanModeButton;
    private Text privateCheckText;
    private GameObject privateRow;
    private Text codeBoxTitle;
    private Text codeBoxBody;

    // サーバーを探す
    private string privateCode = string.Empty;
    private string lanAddress = string.Empty;
    private RectTransform publicListContent;
    private Text publicListMessage;
    private bool searching;

    // 設定
    private SettingsTab settingsTab = SettingsTab.Sound;
    private RectTransform settingsContent;
    private readonly Dictionary<SettingsTab, TitleMenuButton> tabButtons = new Dictionary<SettingsTab, TitleMenuButton>();
    private int resolutionIndex;
    private FullScreenMode pendingScreenMode;

    /// <summary>編集中に Scene・Game ビューへ出す見本か（<see cref="TitleScreenPreview"/> が作る）。見本は数えない。</summary>
    private bool isPreview;

    // ------------------------------------------------------------
    // 作る・出し入れ
    // ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        foreach (TitleScreen existing in FindObjectsByType<TitleScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // 編集中の見本（TitleScreenPreview）が万一残っていても、本物は作る
            if (!existing.isPreview)
            {
                return;
            }
        }

        GameObject created = new GameObject("TitleScreen (自動)");
        created.AddComponent<TitleScreen>();
        DontDestroyOnLoad(created);
    }

    private void Awake()
    {
        theme = Resources.Load<TitleScreenTheme>(ThemeResourcePath);
        if (theme == null)
        {
            Debug.LogWarning("[UI] タイトル画面のテーマ（Assets/Resources/TitleScreenTheme.asset）が見つからないので、仮の色で描きます。");
            theme = ScriptableObject.CreateInstance<TitleScreenTheme>();
        }

        font = UiFont.Find(theme.fontSize);
        lanAddress = PlayerPrefs.GetString(LastLanAddressKey, "127.0.0.1");

        Build();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (IsVisible)
        {
            IsVisible = false;
        }
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;

        HandleSceneFlow(activeScene);

        // **タイトルのシーンにいる間は出す。** つながったあと、ロビーへ移るまでの間も「移動しています」を出しておく
        bool shouldShow = activeScene == TitleSceneName;

        if (shouldShow != IsVisible)
        {
            SetVisible(shouldShow);
        }

        if (!IsVisible)
        {
            return;
        }

        // 遊んでいた画面から戻ってきたときなどに、カーソルが隠れたままにならないように
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateBusyOverlay();

        Keyboard keyboard = Keyboard.current;
        bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                    GamepadInput.WasPressed(GamepadButton.East);

        if (back && !busyOverlay.activeSelf)
        {
            GoBack();
        }
    }

    /// <summary>ロビーを直接開いたときに、つながっていなければタイトルへ移るまで待った秒数。</summary>
    private float notConnectedInLobbyTime;

    /// <summary>ホストがロビーへ移す指示を、このタイトルのシーンでもう出したか。</summary>
    private bool lobbyLoadRequested;

    /// <summary>
    /// **タイトルとロビーの行き来。**
    /// ・タイトルでホストになった（サーバーを作った・1人で練習）→ **全員でロビーへ移る**（参加者はホストのシーンに自動で移る）
    /// ・ロビーにいるのに、どこにもつながっていない（ロビーを直接再生した など）→ タイトルへ移る
    /// </summary>
    private void HandleSceneFlow(string activeScene)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (activeScene != TitleSceneName)
        {
            lobbyLoadRequested = false;
        }

        if (activeScene == TitleSceneName && manager != null && manager.IsServer && !lobbyLoadRequested &&
            manager.SceneManager != null)
        {
            lobbyLoadRequested = true;
            SceneEventProgressStatus status = manager.SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[UI] ロビー（{LobbySceneName}）へ移れませんでした（{status}）。" +
                               "ビルドのシーン一覧に入っているか確かめてください。");
                lobbyLoadRequested = false;
            }
        }

        bool idleInLobby = activeScene == LobbySceneName && (manager == null || !manager.IsListening);
        notConnectedInLobbyTime = idleInLobby ? notConnectedInLobbyTime + Time.unscaledDeltaTime : 0f;

        if (notConnectedInLobbyTime > 0.5f)
        {
            notConnectedInLobbyTime = 0f;
            Debug.Log("[UI] ロビーでどこにもつながっていないので、タイトルへ移ります。");
            SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
        }
    }

    /// <summary>つながっているか（ホストとして動いている／参加者としてつながり終わった）。</summary>
    private static bool IsConnected()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && (manager.IsServer || manager.IsConnectedClient);
    }

    private void SetVisible(bool visible)
    {
        IsVisible = visible;
        root.SetActive(visible);

        if (visible)
        {
            EnsureEventSystem();
            ShowPage(Page.Main);
        }

        // **閉じたときも、カーソルは出したままにする。**（2026/10/6・大槻さん「ロビーでクリックするたびにカーソルが隠れる」）
        // 以前は「開く前の状態に戻す」にしていたが、起動直後に閉じる（Awake）ときに、まだ覚えていない値＝「隠す」に戻してしまい、
        // ロビーでカーソルが消えていた（エディタでは、Game ビューをクリックするたびに隠れる）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

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
            case Page.Settings:
                ShowSettingsTab(settingsTab);
                break;
        }

        SelectFirstForGamepad(pages[page]);
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
                ShowPage(Page.Find);
                break;
        }
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
            Debug.LogError("[UI] NetworkManager が見つかりません。SpaceJunkLobby のシーンから始めてください。");
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

        internet.HostGame(serverName, createMaxPlayers, createPrivate);
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
            internet.JoinGame(privateCode.Trim());
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
        if (searching || publicListContent == null)
        {
            return;
        }

        ClearChildren(publicListContent);
        publicListMessage.text = "サーバーを探しています…";

        InternetConnection internet = Internet();
        if (internet == null)
        {
            publicListMessage.text = "インターネットの接続の部品が見つかりません。";
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
            publicListMessage.text = string.IsNullOrEmpty(internet.Message) ? "サーバーの一覧を取れませんでした。" : internet.Message;
            internet.ClearFailure();
            return;
        }

        publicListMessage.text = sessions.Count == 0 ? "サーバーが見つかりませんでした。「検索」でもう一度探せます。" : string.Empty;

        foreach (ISessionInfo info in sessions)
        {
            AddPublicServerCard(info);
        }
    }

    /// <summary>
    /// つないでいる途中・失敗したときの表示。
    /// ・インターネット：準備中・作っている・入ろうとしている → 説明を出す。失敗 → 理由と「戻る」
    /// ・LAN：つなぎに行っている → 説明と「やめる」
    /// </summary>
    private void UpdateBusyOverlay()
    {
        InternetConnection internet = Internet();
        NetworkManager manager = NetworkManager.Singleton;

        bool lanConnecting = manager != null && manager.IsListening && !manager.IsServer && !manager.IsConnectedClient &&
                             (internet == null || internet.State != InternetConnection.Phase.Joining);

        string message = null;
        string buttonLabel = null;
        UnityEngine.Events.UnityAction action = null;

        if (IsConnected())
        {
            // つながった。ホストがロビーへ移すまでの間
            message = "つながりました。ロビーに移動しています…";
        }
        else if (internet != null && internet.IsBusy && current != Page.FindPublic)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        else if (internet != null && internet.State == InternetConnection.Phase.Failed && current != Page.FindPublic)
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

        bool show = message != null;
        if (busyOverlay.activeSelf != show)
        {
            busyOverlay.SetActive(show);
            if (show && buttonLabel != null)
            {
                SelectForGamepad(busyButton.gameObject);
            }
        }

        if (!show)
        {
            return;
        }

        busyText.text = message;
        busyButton.gameObject.SetActive(buttonLabel != null);
        busyButton.onClick.RemoveAllListeners();
        if (action != null)
        {
            busyButton.onClick.AddListener(action);
            busyButtonLabel.text = buttonLabel;
        }
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    private void Build()
    {
        root = new GameObject("TitleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // ポーズ画面（100）より後ろ、ほかのUIより手前

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildBackground();

        pages[Page.Main] = BuildMainPage();
        pages[Page.Create] = BuildCreatePage();
        pages[Page.Find] = BuildFindPage();
        pages[Page.FindPrivate] = BuildFindPrivatePage();
        pages[Page.FindPublic] = BuildFindPublicPage();
        pages[Page.FindLan] = BuildFindLanPage();
        pages[Page.Settings] = BuildSettingsPage();

        BuildBusyOverlay();
    }

#if UNITY_EDITOR
    /// <summary>
    /// **編集中に Scene・Game ビューへ出す見本を組み立てる。**（<see cref="TitleScreenPreview"/> から呼ぶ）
    /// 再生したときと同じ組み立て方で作るので、見た目は本物と同じ。ボタンを押しても何も起きない（通信やシーンの移動はしない）。
    /// </summary>
    public void BuildPreview(TitleScreenTheme previewTheme, Page page)
    {
        isPreview = true;

        theme = previewTheme;
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<TitleScreenTheme>();
            theme.hideFlags = HideFlags.DontSave;
        }

        font = UiFont.Find(theme.fontSize);
        lanAddress = PlayerPrefs.GetString(LastLanAddressKey, "127.0.0.1");

        Build();

        current = page;
        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page);
        }

        // ShowPage と同じ中身の更新（パブリックの一覧の検索だけは、通信するのでしない）
        switch (page)
        {
            case Page.Main:
                RefreshMainName();
                break;
            case Page.Create:
                RefreshCreatePage();
                break;
            case Page.FindPublic:
                publicListMessage.text = "（見本）ここに、公開されているサーバーの一覧が出ます";
                break;
            case Page.Settings:
                ShowSettingsTab(settingsTab);
                break;
        }
    }
#endif

    private void BuildBackground()
    {
        // 背景の画像（画面いっぱい。縦横比を保ったまま、はみ出す分は切る）
        Image background = CreateImage("Background", root.transform, Color.black, null);
        Stretch(background.rectTransform);

        if (theme.background != null)
        {
            Image picture = CreateImage("Picture", background.transform, Color.white, theme.background);
            AspectRatioFitter fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = theme.background.rect.width / Mathf.Max(1f, theme.background.rect.height);
        }

        // 文字を読みやすくする暗さ
        Image tint = CreateImage("Tint", root.transform, theme.backgroundTint, null);
        Stretch(tint.rectTransform);
    }

    private GameObject BuildMainPage()
    {
        GameObject page = CreatePage("MainPage");

        // ロゴ（画像が無ければ文字）
        if (theme.logo != null)
        {
            Image logo = CreateImage("Logo", page.transform, Color.white, theme.logo);
            logo.preserveAspect = true;
            Place(logo.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -90f), new Vector2(900f, 340f), new Vector2(0f, 1f));
        }
        else
        {
            Text title = CreateText("Title", page.transform, theme.titleText, 110, theme.titleColor, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4f, -4f);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -120f), new Vector2(1200f, 200f), new Vector2(0f, 1f));
        }

        // 左上：いまの名前
        mainNameText = CreateText("Name", page.transform, string.Empty, theme.fontSize - 4, theme.textColor, TextAnchor.UpperLeft);
        Place(mainNameText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(900f, 40f), new Vector2(0f, 1f));

        // メインのボタン（左下に縦に並べる）
        RectTransform column = CreateColumn("Buttons", page.transform, theme.mainButtonSize.x, 22f);
        Place(column, new Vector2(0f, 0f), new Vector2(220f, 180f), new Vector2(theme.mainButtonSize.x, 0f), new Vector2(0f, 0f));

        CreateButton(column, "サーバーを作る", theme.mainButtonSize, theme.buttonFontSize, () => ShowPage(Page.Create));
        CreateButton(column, "サーバーを探す", theme.mainButtonSize, theme.buttonFontSize, () => ShowPage(Page.Find));
        CreateButton(column, "設定", theme.mainButtonSize, theme.buttonFontSize, () => ShowPage(Page.Settings));
        CreateButton(column, "ゲームを終了", theme.mainButtonSize, theme.buttonFontSize, QuitGame);

        // 右下：バージョン
        string version = string.IsNullOrEmpty(theme.versionText) ? Application.version : theme.versionText;
        Text versionText = CreateText("Version", page.transform, $"version - {version}", theme.fontSize, theme.textColor, TextAnchor.LowerRight);
        Place(versionText.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(600f, 40f), new Vector2(1f, 0f));

        return page;
    }

    private GameObject BuildCreatePage()
    {
        GameObject page = CreatePage("CreatePage");
        RectTransform column = CreateColumn("Column", page.transform, 1100f, 14f);
        Place(column, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1100f, 0f), new Vector2(0.5f, 1f));

        CreateNameInput(column);

        // 最大人数
        CreateLabel(column, "最大人数", "2〜8人");
        RectTransform playersRow = CreateRow(column, 70f, 12f);
        CreateButton(playersRow, "-1", new Vector2(110f, 64f), theme.fontSize, () => ChangeMaxPlayers(-1));
        Image playersBox = CreateImage("Players", playersRow, theme.panelColor, theme.panel);
        SetSize(playersBox.gameObject, 300f, 64f);
        maxPlayersText = CreateText("Value", playersBox.transform, string.Empty, theme.fontSize + 6, theme.textColor, TextAnchor.MiddleCenter);
        Stretch(maxPlayersText.rectTransform);
        CreateButton(playersRow, "+1", new Vector2(110f, 64f), theme.fontSize, () => ChangeMaxPlayers(1));

        // サーバーの名前
        CreateLabel(column, "サーバーの名前を入力", "パブリックサーバーの一覧に出る名前。空なら「（名前）のサーバー」");
        CreateInput(column, "テキストを入力", ServerNameMaxLength, createServerName, value => createServerName = value);

        // つなぎかた
        CreateLabel(column, "つなぎかた", "展示会場では LAN（同じネットワークのPC同士）");
        RectTransform modeRow = CreateRow(column, 64f, 12f);
        internetModeButton = CreateButton(modeRow, "インターネット", new Vector2(360f, 60f), theme.fontSize, () => SetCreateLan(false));
        lanModeButton = CreateButton(modeRow, "LAN", new Vector2(360f, 60f), theme.fontSize, () => SetCreateLan(true));

        // プライベートサーバー
        privateRow = CreateRow(column, 60f, 16f).gameObject;
        TitleMenuButton privateCheck = CreateButton(privateRow.transform, "□", new Vector2(64f, 56f), theme.fontSize + 6, TogglePrivate);
        privateCheckText = privateCheck.GetComponentInChildren<Text>();
        Text privateLabel = CreateText("Label", privateRow.transform, "プライベートサーバー", theme.fontSize, theme.textColor, TextAnchor.MiddleLeft);
        SetSize(privateLabel.gameObject, 300f, 56f);
        Text privateNote = CreateText("Note", privateRow.transform, "ON：パブリックの一覧に出ない（参加コードを知っている人だけ入れる）",
            theme.fontSize - 8, theme.noteColor, TextAnchor.MiddleLeft);
        SetSize(privateNote.gameObject, 680f, 56f);

        // 参加コードの枠
        Image codeBox = CreateImage("CodeBox", column, theme.accentColor, theme.accentPanel);
        SetSize(codeBox.gameObject, 1100f, 120f);
        codeBoxTitle = CreateText("Title", codeBox.transform, "参加コード", theme.fontSize + 4, theme.textColor, TextAnchor.UpperCenter);
        Place(codeBoxTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(1060f, 40f), new Vector2(0.5f, 1f));
        codeBoxBody = CreateText("Body", codeBox.transform, string.Empty, theme.fontSize - 4, theme.noteColor, TextAnchor.UpperCenter);
        Place(codeBoxBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1060f, 64f), new Vector2(0.5f, 1f));

        CreateButton(column, "サーバーを作成", new Vector2(1100f, 70f), theme.buttonFontSize, CreateServer);
        CreateButton(column, "1人で練習する（通信を使わない）", new Vector2(1100f, 54f), theme.fontSize - 2, () =>
        {
            if (NetworkManager.Singleton != null)
            {
                StartLanHost(NetworkManager.Singleton);
            }
        });

        CreateBackButton(page.transform);
        return page;
    }

    private GameObject BuildFindPage()
    {
        GameObject page = CreatePage("FindPage");
        RectTransform column = CreateColumn("Buttons", page.transform, 640f, 22f);
        Place(column, new Vector2(0f, 0f), new Vector2(220f, 260f), new Vector2(640f, 0f), new Vector2(0f, 0f));

        Vector2 size = new Vector2(640f, theme.mainButtonSize.y);
        CreateButton(column, "プライベートサーバーを探す", size, theme.buttonFontSize, () => ShowPage(Page.FindPrivate));
        CreateButton(column, "パブリックサーバーを探す", size, theme.buttonFontSize, () => ShowPage(Page.FindPublic));
        CreateButton(column, "LANのサーバーに参加する", size, theme.buttonFontSize, () => ShowPage(Page.FindLan));

        CreateBackButton(page.transform);
        return page;
    }

    private GameObject BuildFindPrivatePage()
    {
        GameObject page = CreatePage("FindPrivatePage");
        RectTransform column = CreateColumn("Column", page.transform, 1000f, 14f);
        Place(column, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1000f, 0f), new Vector2(0.5f, 1f));

        CreateNameInput(column);
        CreateLabel(column, "参加コードを入力", "サーバーを作った人に教えてもらう");
        CreateInput(column, "例：6FJBMH", 12, privateCode, value => privateCode = value);
        CreateButton(column, "参加する", new Vector2(1000f, 70f), theme.buttonFontSize, JoinPrivate);

        CreateBackButton(page.transform);
        return page;
    }

    private GameObject BuildFindPublicPage()
    {
        GameObject page = CreatePage("FindPublicPage");

        // 左：名前と検索
        RectTransform left = CreateColumn("Left", page.transform, 640f, 14f);
        Place(left, new Vector2(0f, 1f), new Vector2(120f, -80f), new Vector2(640f, 0f), new Vector2(0f, 1f));
        CreateNameInput(left);
        CreateButton(left, "パブリックサーバーを検索", new Vector2(640f, 70f), theme.fontSize + 2, SearchPublic);
        Text note = CreateText("Note", left, "プライベートサーバーは一覧に出ません（参加コードで入る）。LAN のサーバーも出ません。",
            theme.fontSize - 8, theme.noteColor, TextAnchor.UpperLeft);
        SetSize(note.gameObject, 640f, 70f);

        // 右：検索結果
        Text resultsTitle = CreateText("ResultsTitle", page.transform, "検索結果", theme.fontSize + 6, theme.textColor, TextAnchor.LowerLeft);
        Place(resultsTitle.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -60f), new Vector2(800f, 50f), new Vector2(0f, 1f));

        Image listBox = CreateImage("ListBox", page.transform, theme.panelColor, theme.panel);
        Place(listBox.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -120f), new Vector2(820f, 760f), new Vector2(0f, 1f));
        publicListContent = CreateScrollList(listBox.transform);

        publicListMessage = CreateText("Message", listBox.transform, string.Empty, theme.fontSize - 2, theme.textColor, TextAnchor.UpperCenter);
        Place(publicListMessage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(780f, 120f), new Vector2(0.5f, 1f));

        CreateBackButton(page.transform);
        return page;
    }

    private void AddPublicServerCard(ISessionInfo info)
    {
        Image card = CreateImage("Server", publicListContent, theme.accentColor, theme.accentPanel);
        SetSize(card.gameObject, 780f, 150f);

        Text name = CreateText("Name", card.transform, string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name,
            theme.fontSize + 2, theme.textColor, TextAnchor.UpperLeft);
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(560f, 44f), new Vector2(0f, 1f));

        int players = Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
        Text count = CreateText("Players", card.transform, $"{players}/{info.MaxPlayers}", theme.fontSize + 2,
            theme.textColor, TextAnchor.UpperRight);
        Place(count.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -12f), new Vector2(160f, 44f), new Vector2(1f, 1f));

        bool full = info.AvailableSlots <= 0 || info.IsLocked;
        string sessionId = info.Id;
        TitleMenuButton join = CreateButton(card.transform, full ? "満員" : "参加する", new Vector2(740f, 64f), theme.fontSize,
            () =>
            {
                InternetConnection internet = Internet();
                if (internet != null && !full)
                {
                    internet.JoinGameById(sessionId);
                }
            });
        Place((RectTransform)join.transform, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(740f, 64f), new Vector2(0.5f, 0f));
    }

    private GameObject BuildFindLanPage()
    {
        GameObject page = CreatePage("FindLanPage");
        RectTransform column = CreateColumn("Column", page.transform, 1000f, 14f);
        Place(column, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1000f, 0f), new Vector2(0.5f, 1f));

        CreateNameInput(column);
        CreateLabel(column, "ホストのIPアドレスを入力", "サーバーを作った人の画面（サーバーを作る → LAN）に出ている番号");
        CreateInput(column, "例：192.168.0.10", 40, lanAddress, value => lanAddress = value);
        CreateButton(column, "参加する", new Vector2(1000f, 70f), theme.buttonFontSize, JoinLan);

        CreateBackButton(page.transform);
        return page;
    }

    private GameObject BuildSettingsPage()
    {
        GameObject page = CreatePage("SettingsPage");

        // 左：タブ
        RectTransform tabs = CreateColumn("Tabs", page.transform, 300f, 16f);
        Place(tabs, new Vector2(0f, 1f), new Vector2(80f, -100f), new Vector2(300f, 0f), new Vector2(0f, 1f));

        tabButtons[SettingsTab.Sound] = CreateButton(tabs, "サウンド", new Vector2(300f, 64f), theme.fontSize, () => ShowSettingsTab(SettingsTab.Sound));
        tabButtons[SettingsTab.Graphics] = CreateButton(tabs, "グラフィック", new Vector2(300f, 64f), theme.fontSize, () => ShowSettingsTab(SettingsTab.Graphics));
        tabButtons[SettingsTab.General] = CreateButton(tabs, "ゲーム全般", new Vector2(300f, 64f), theme.fontSize, () => ShowSettingsTab(SettingsTab.General));

        // 右：中身（タブを選ぶたびに作り直す）
        settingsContent = CreateColumn("Content", page.transform, 1300f, 12f);
        Place(settingsContent, new Vector2(0f, 1f), new Vector2(460f, -100f), new Vector2(1300f, 0f), new Vector2(0f, 1f));

        CreateBackButton(page.transform);
        return page;
    }

    private void ShowSettingsTab(SettingsTab tab)
    {
        settingsTab = tab;

        foreach (KeyValuePair<SettingsTab, TitleMenuButton> pair in tabButtons)
        {
            pair.Value.SetSelectedLook(pair.Key == tab);
        }

        ClearChildren(settingsContent);

        switch (tab)
        {
            case SettingsTab.Sound:
                CreateSliderRow(settingsContent, "音量", GameSettings.Volume, 0f, 1f,
                    value => GameSettings.Volume = value, value => $"{Mathf.RoundToInt(value * 100f)} %");
                break;

            case SettingsTab.Graphics:
                BuildGraphicsSettings();
                break;

            case SettingsTab.General:
                CreateSliderRow(settingsContent, "マウス感度", GameSettings.MouseSensitivity, 0.2f, 3f,
                    value => GameSettings.MouseSensitivity = value, value => $"{value:0.0} 倍");
                CreateStepperRow(settingsContent, "操作タイプ（コントローラー）",
                    () => GameSettings.ControllerOperation == ControllerOperationType.TypeB ? "タイプB" : "タイプA",
                    delta => GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
                        ? ControllerOperationType.TypeB
                        : ControllerOperationType.TypeA);
                break;
        }
    }

    private void BuildGraphicsSettings()
    {
        Resolution[] resolutions = UniqueResolutions();
        resolutionIndex = Mathf.Max(0, FindCurrentResolution(resolutions));
        pendingScreenMode = Screen.fullScreenMode;

        CreateStepperRow(settingsContent, "解像度",
            () => resolutions.Length == 0 ? "—" : $"{resolutions[resolutionIndex].width}x{resolutions[resolutionIndex].height}",
            delta =>
            {
                if (resolutions.Length > 0)
                {
                    resolutionIndex = (resolutionIndex + delta + resolutions.Length) % resolutions.Length;
                }
            });

        FullScreenMode[] modes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        CreateStepperRow(settingsContent, "スクリーンモード", () => ScreenModeName(pendingScreenMode), delta =>
        {
            int index = System.Array.IndexOf(modes, pendingScreenMode);
            index = index < 0 ? 0 : (index + delta + modes.Length) % modes.Length;
            pendingScreenMode = modes[index];
        });

        CreateButton(settingsContent, "解像度／スクリーンモードを適用", new Vector2(1300f, 60f), theme.fontSize, () =>
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
        });

        CreateSliderRow(settingsContent, "フレームレート上限", GameSettings.FrameRateLimit, 30f, 240f,
            value => GameSettings.FrameRateLimit = Mathf.RoundToInt(value / 10f) * 10, value => $"{Mathf.RoundToInt(value / 10f) * 10} fps");

        CreateStepperRow(settingsContent, "垂直同期", () => GameSettings.VSync ? "ON" : "OFF",
            delta => GameSettings.VSync = !GameSettings.VSync);

        string[] qualities = QualitySettings.names;
        CreateStepperRow(settingsContent, "画質",
            () => qualities.Length == 0 ? "—" : qualities[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualities.Length - 1)],
            delta =>
            {
                if (qualities.Length > 0)
                {
                    GameSettings.QualityLevel = (QualitySettings.GetQualityLevel() + delta + qualities.Length) % qualities.Length;
                }
            });

        Text note = CreateText("Note", settingsContent, "垂直同期が ON のときは、フレームレート上限は効きません（画面の更新に合わせます）。",
            theme.fontSize - 8, theme.noteColor, TextAnchor.MiddleLeft);
        SetSize(note.gameObject, 1300f, 40f);
    }

    private void BuildBusyOverlay()
    {
        busyOverlay = new GameObject("Busy", typeof(RectTransform));
        busyOverlay.transform.SetParent(root.transform, false);
        Stretch((RectTransform)busyOverlay.transform);

        Image shade = CreateImage("Shade", busyOverlay.transform, new Color(0f, 0f, 0f, 0.7f), null);
        Stretch(shade.rectTransform);

        Image box = CreateImage("Box", busyOverlay.transform, theme.panelColor, theme.panel);
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 360f), new Vector2(0.5f, 0.5f));

        busyText = CreateText("Message", box.transform, string.Empty, theme.fontSize, theme.textColor, TextAnchor.MiddleCenter);
        Place(busyText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1040f, 200f), new Vector2(0.5f, 1f));

        TitleMenuButton button = CreateButton(box.transform, "戻る", new Vector2(400f, 64f), theme.fontSize, null);
        Place((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(400f, 64f), new Vector2(0.5f, 0f));
        busyButton = button.GetComponent<Button>();
        busyButtonLabel = button.GetComponentInChildren<Text>();

        busyOverlay.SetActive(false);
    }

    // ------------------------------------------------------------
    // 画面ごとの更新
    // ------------------------------------------------------------

    private void RefreshMainName()
    {
        string name = GameSettings.PlayerName;
        mainNameText.text = string.IsNullOrEmpty(name) ? "名前：未設定（サーバーを作る・探すの画面で入れられます）" : $"名前：{name}";
    }

    private void RefreshCreatePage()
    {
        maxPlayersText.text = $"{createMaxPlayers} 人";
        internetModeButton.SetSelectedLook(!createLan);
        lanModeButton.SetSelectedLook(createLan);
        privateRow.SetActive(!createLan);
        privateCheckText.text = createPrivate ? "■" : "□";

        if (createLan)
        {
            codeBoxTitle.text = "このPCのIPアドレス";
            codeBoxBody.text = $"{LocalAddresses()}\n参加する人は「サーバーを探す → LAN」でこの番号を入れる";
        }
        else
        {
            codeBoxTitle.text = "参加コード";
            codeBoxBody.text = "サーバーを作成すると発行されます（ロビーの左上の「接続」に出ます）\n参加する人は「サーバーを探す → プライベートサーバー」で入れる";
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
    // 部品づくり
    // ------------------------------------------------------------

    private GameObject CreatePage(string name)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(root.transform, false);
        Stretch((RectTransform)page.transform);
        return page;
    }

    /// <summary>上から縦に並べる入れ物。</summary>
    private static RectTransform CreateColumn(string name, Transform parent, float width, float spacing)
    {
        GameObject column = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        column.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        column.transform.SetParent(parent, false);

        VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        SetSize(column, width, 0f);
        return (RectTransform)column.transform;
    }

    /// <summary>左から横に並べる入れ物。</summary>
    private static RectTransform CreateRow(Transform parent, float height, float spacing)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        SetSize(row, 1300f, height);
        return (RectTransform)row.transform;
    }

    /// <summary>見出し（と小さい説明）を1行。</summary>
    private void CreateLabel(Transform parent, string label, string note)
    {
        RectTransform row = CreateRow(parent, 40f, 16f);

        Text title = CreateText("Label", row, label, theme.fontSize, theme.textColor, TextAnchor.LowerLeft);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        SetSize(title.gameObject, Mathf.Max(200f, label.Length * theme.fontSize + 10f), 40f);

        if (!string.IsNullOrEmpty(note))
        {
            Text small = CreateText("Note", row, note, theme.fontSize - 8, theme.noteColor, TextAnchor.LowerLeft);
            SetSize(small.gameObject, 800f, 40f);
        }
    }

    /// <summary>「ゲーム内で表示する名前」の入力欄。どの画面で入れても同じ名前（保存される）。</summary>
    private void CreateNameInput(Transform parent)
    {
        CreateLabel(parent, "ゲーム内で表示する名前を入力", $"{GameSettings.PlayerNameMaxLength}文字まで");
        InputField field = CreateInput(parent, "名前を入力", GameSettings.PlayerNameMaxLength, GameSettings.PlayerName,
            value => GameSettings.PlayerName = value);

        // ほかの画面で変えた名前を、開くたびに入れ直す
        field.gameObject.AddComponent<TitleNameFieldSync>().Setup(field);
    }

    private InputField CreateInput(Transform parent, string placeholder, int maxLength, string initial,
        UnityEngine.Events.UnityAction<string> onChanged)
    {
        Image background = CreateImage("Input", parent, theme.panelColor, theme.panel);
        SetSize(background.gameObject, 1000f, 64f);

        Text text = CreateText("Text", background.transform, string.Empty, theme.fontSize + 4, theme.textColor, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        text.supportRichText = false;

        Text hint = CreateText("Placeholder", background.transform, placeholder, theme.fontSize + 2,
            new Color(theme.textColor.r, theme.textColor.g, theme.textColor.b, 0.35f), TextAnchor.MiddleCenter);
        Stretch(hint.rectTransform);

        InputField field = background.gameObject.AddComponent<InputField>();
        field.textComponent = text;
        field.placeholder = hint;
        field.characterLimit = maxLength;
        field.targetGraphic = background;
        field.text = initial ?? string.Empty;
        field.onValueChanged.AddListener(onChanged);

        // 右端に「いま何文字か」
        Text counter = CreateText("Counter", background.transform, string.Empty, theme.fontSize - 8, theme.noteColor, TextAnchor.MiddleRight);
        Place(counter.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(120f, 40f), new Vector2(1f, 0.5f));
        void UpdateCounter(string value) => counter.text = $"{value.Length}/{maxLength}";
        UpdateCounter(field.text);
        field.onValueChanged.AddListener(UpdateCounter);

        return field;
    }

    private TitleMenuButton CreateButton(Transform parent, string label, Vector2 size, int fontSize,
        UnityEngine.Events.UnityAction onClick)
    {
        Image background = CreateImage(label, parent, theme.buttonColor, theme.button);
        SetSize(background.gameObject, size.x, size.y);

        Text text = CreateText("Label", background.transform, label, fontSize, theme.buttonTextColor, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);

        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None; // 見た目は TitleMenuButton が変える
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        TitleMenuButton look = background.gameObject.AddComponent<TitleMenuButton>();
        look.Setup(background, text, theme);
        return look;
    }

    /// <summary>左下の「Esc 戻る」。</summary>
    private void CreateBackButton(Transform parent)
    {
        TitleMenuButton back = CreateButton(parent, "Esc／B  戻る", new Vector2(260f, 56f), theme.fontSize - 2, GoBack);
        Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(60f, 30f), new Vector2(260f, 56f), new Vector2(0f, 0f));
    }

    /// <summary>「名前　＜ 値 ＞」の1行。押すたびに値が変わる。</summary>
    private void CreateStepperRow(Transform parent, string label, System.Func<string> getValue, System.Action<int> change)
    {
        Image row = CreateImage(label, parent, theme.panelColor, theme.panel);
        SetSize(row.gameObject, 1300f, 64f);

        Text title = CreateText("Label", row.transform, label, theme.fontSize, theme.textColor, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(560f, 60f), new Vector2(0f, 0.5f));

        Text value = CreateText("Value", row.transform, getValue(), theme.fontSize, theme.textColor, TextAnchor.MiddleCenter);
        Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(460f, 60f), new Vector2(1f, 0.5f));

        TitleMenuButton left = CreateButton(row.transform, "＜", new Vector2(64f, 52f), theme.fontSize, () =>
        {
            change(-1);
            value.text = getValue();
        });
        Place((RectTransform)left.transform, new Vector2(1f, 0.5f), new Vector2(-560f, 0f), new Vector2(64f, 52f), new Vector2(1f, 0.5f));

        TitleMenuButton right = CreateButton(row.transform, "＞", new Vector2(64f, 52f), theme.fontSize, () =>
        {
            change(1);
            value.text = getValue();
        });
        Place((RectTransform)right.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(64f, 52f), new Vector2(1f, 0.5f));
    }

    /// <summary>「名前　値　━━●━━」の1行。</summary>
    private void CreateSliderRow(Transform parent, string label, float initial, float min, float max,
        System.Action<float> onChanged, System.Func<float, string> format)
    {
        Image row = CreateImage(label, parent, theme.panelColor, theme.panel);
        SetSize(row.gameObject, 1300f, 64f);

        Text title = CreateText("Label", row.transform, label, theme.fontSize, theme.textColor, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(460f, 60f), new Vector2(0f, 0.5f));

        Text value = CreateText("Value", row.transform, format(initial), theme.fontSize, theme.textColor, TextAnchor.MiddleRight);
        Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(480f, 0f), new Vector2(200f, 60f), new Vector2(0f, 0.5f));

        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(row.transform, false);
        Place((RectTransform)sliderObject.transform, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(520f, 16f), new Vector2(1f, 0.5f));

        Image track = CreateImage("Track", sliderObject.transform, new Color(1f, 1f, 1f, 0.25f), null);
        Stretch(track.rectTransform);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        Stretch((RectTransform)fillArea.transform);
        Image fill = CreateImage("Fill", fillArea.transform, theme.buttonOutlineColor, null);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        Image handle = CreateImage("Handle", sliderObject.transform, Color.white, null);
        handle.rectTransform.sizeDelta = new Vector2(24f, 36f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.onValueChanged.AddListener(v =>
        {
            onChanged(v);
            value.text = format(v);
        });
    }

    /// <summary>縦に流れる一覧（パブリックサーバーの検索結果）。中身を入れる入れ物を返す。</summary>
    private static RectTransform CreateScrollList(Transform parent)
    {
        GameObject view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
        view.transform.SetParent(parent, false);
        RectTransform viewRect = (RectTransform)view.transform;
        Stretch(viewRect);
        viewRect.offsetMin = new Vector2(16f, 16f);
        viewRect.offsetMax = new Vector2(-16f, -16f);

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(view.transform, false);
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = view.GetComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewRect;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        return contentRect;
    }

    private Image CreateImage(string name, Transform parent, Color color, Sprite sprite)
    {
        GameObject image = new GameObject(name, typeof(RectTransform), typeof(Image));
        image.transform.SetParent(parent, false);

        Image component = image.GetComponent<Image>();
        component.sprite = sprite;
        component.color = color;
        component.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        return component;
    }

    private Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        return text;
    }

    private static void SetSize(GameObject target, float width, float height)
    {
        RectTransform rect = (RectTransform)target.transform;
        rect.sizeDelta = new Vector2(width, height);

        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null)
        {
            element = target.AddComponent<LayoutElement>();
        }
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    private static void SetSize(RectTransform target, float width, float height)
    {
        SetSize(target.gameObject, width, height);
    }

    /// <summary>アンカー（画面のどこを基準にするか）・位置・大きさ・中心をまとめて決める。</summary>
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>このPCのIPアドレス（LAN でホストになるとき、参加する人に伝える番号）。</summary>
    private static string LocalAddresses()
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

    private static int FindCurrentResolution(Resolution[] resolutions)
    {
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height)
            {
                return i;
            }
        }
        return resolutions.Length - 1;
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
