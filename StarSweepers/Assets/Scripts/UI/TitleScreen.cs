using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
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
/// **タイトル画面。**（2026/10/13 に作り直した。大槻さんの絵コンテ。前の画面はブランチ `backup/title-before-redesign-1013` に残してある）
///
/// | 画面 | できること |
/// | --- | --- |
/// | はじめ | 名前を入れて「START」 |
/// | えらぶ | 左に「1人プレイ」「マルチプレイ」。右に、選んでいるほうの表示（いまは文字。のちのち人の絵にする） |
/// | 部屋の一覧 | 「マルチプレイ」を押すと右に出る。いまある部屋（インターネットの公開の部屋・同じネットワークの LAN の部屋）が並び、押すと入る。一番下の「＋」で部屋を作る |
///
/// ・**1人プレイ**…押すとそのままロビーへ（このPCの中だけで動くホストになる）
/// ・**部屋を作る**（「＋」）…小さい窓で「インターネット」か「LAN」を選ぶと、すぐ部屋ができてロビーへ。部屋の名前もその窓で入れられる（空なら「自分の名前の部屋」）。パスワードは付けない（2026/10/13〜）
/// ・**パスワードの付いた部屋**に入るときは、小さい窓でパスワードを聞く
///
/// ## 画面はプレハブ
///
/// **`Assets/Resources/TitleScreen.prefab`** が画面そのもの。TitleScene に置いてある。
/// **ボタンや文字は、エディタで自由に動かし・大きさを変え・画像や色を変えてよい。**
/// このスクリプトは、部品に付いている印（<see cref="TitlePart"/> の Role・<see cref="TitleMenuButton"/> の Action・<see cref="TitleInput"/> の Kind）を頼りに動かす。
/// 作り直したいときは `Tools > StarSweepers > タイトル画面のプレハブを作り直す`（**手で直した配置は消える**）。
///
/// ## 操作
///
/// マウス、またはコントローラー（右スティックのポインター＋LT／A、左スティック・十字キーで選んで A）。
/// **B（キーボードは Esc）で1つ前に戻る**（小さい窓が開いていれば、窓を閉じる）。
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

    /// <summary>作る部屋の最大人数（作る窓では決めない。宇宙ごみ集めの定員と同じ6人。2026/10/14）。</summary>
    private const int RoomMaxPlayers = SpaceJunkTeams.MaxPlayers;

    /// <summary>部屋の名前の最大の文字数。</summary>
    private const int RoomNameMaxLength = 25;

    /// <summary>インターネットの部屋の一覧を、何秒ごとに取り直すか。</summary>
    private const float InternetRefreshSeconds = 5f;

    /// <summary>
    /// 一覧を取れなかったとき、次に取り直すまでの秒数。
    /// 短い間に何度も頼むと、相手のサービスに「頼みすぎ（429 Too Many Requests）」と断られるため、長めに待つ。
    /// </summary>
    private const float InternetRetrySeconds = 15f;

    /// <summary>いまタイトル画面が出ているか。ポーズ画面やロビーの画面が、これを見て自分を隠す。</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>画面の種類。</summary>
    public enum Page
    {
        /// <summary>名前とスタート</summary>
        Start,

        /// <summary>1人プレイ／マルチプレイ（と部屋の一覧）</summary>
        Mode,
    }

    /// <summary>右側に出しているもの。</summary>
    private enum Preview
    {
        Solo,
        Multi,
    }

    /// <summary>開いている小さい窓。</summary>
    private enum Window
    {
        None,
        Create,
        Password,
    }

    /// <summary>部屋の一覧の1行ぶん（インターネットの部屋か LAN の部屋）。</summary>
    private struct RoomEntry
    {
        public bool Lan;
        public string SessionId;
        public string Address;
        public ushort Port;
        public string Name;
        public int Players;
        public int MaxPlayers;
        public bool HasPassword;
        public bool Full;
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
    private readonly List<TitleMenuButton> buttons = new List<TitleMenuButton>();
    private readonly List<GameObject> roomCards = new List<GameObject>();
    private GameObject roomCardTemplate;
    private bool initialized;

    private Page current = Page.Start;
    private Preview preview = Preview.Solo;
    private bool roomListOpen;
    private Window window = Window.None;

    // 入力欄
    /// <summary>作る部屋の名前（空なら「自分の名前の部屋」）。保存はしない。2026/10/13 にパスワードの欄から変えた。</summary>
    private string createRoomName = string.Empty;
    private string joinPassword = string.Empty;

    // 部屋の一覧
    private readonly List<ISessionInfo> internetRooms = new List<ISessionInfo>();
    private string internetProblem = string.Empty;
    private bool searching;
    private bool searchedOnce;
    /// <summary>次にインターネットの部屋の一覧を取ってよい時刻（Time.unscaledTime）。一覧を開き直しても、これより前には取らない。</summary>
    private float nextQueryTime;
    private bool lanListening;
    private string roomsSignature = string.Empty;
    private RoomEntry pendingJoin;

    /// <summary>LAN でつなぎに行っている先（つないでいる途中の表示に出す）。</summary>
    private string lanTarget = string.Empty;

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
        ShowPage(Page.Start);
        ShowCursor();
        ShowDisconnectReason();
    }

    /// <summary>前に出した切られた理由（同じ理由を、タイトルに戻るたびに出し直さないため）。</summary>
    private static string shownDisconnectReason = string.Empty;

    /// <summary>
    /// **ホストに切られて戻ってきたときは、その理由を名前の欄の下に出す**（例：「満員です（最大6人）」。2026/10/14）。
    /// </summary>
    private void ShowDisconnectReason()
    {
        NetworkManager manager = NetworkManager.Singleton;
        string reason = manager != null ? manager.DisconnectReason : string.Empty;
        if (string.IsNullOrEmpty(reason) || reason == shownDisconnectReason)
        {
            return;
        }

        shownDisconnectReason = reason;
        SetText(TitlePartRole.StartMessage, $"部屋に入れませんでした：{reason}");
    }

    private void OnDisable()
    {
        IsVisible = false;
        SetLanListening(false);
        ShowCursor();
    }

    private void Update()
    {
        ShowCursor();

        JoinFromSteamRequest();
        UpdateBusyOverlay();

        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        bool busyShown = busy != null && busy.activeSelf;

        if (!busyShown && WasBackPressed())
        {
            GoBack();
        }

        if (current == Page.Mode)
        {
            UpdatePreview();

            if (roomListOpen)
            {
                UpdateRoomList();
            }
        }
    }

    private static bool WasBackPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || GamepadInput.WasPressed(GamepadButton.East);
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

        if (useOsJapaneseFont)
        {
            ApplyJapaneseFont();
        }

        CollectParts();
        WireButtons();
        WireInputs();
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

        AddPage(Page.Start, TitlePartRole.PageStart);
        AddPage(Page.Mode, TitlePartRole.PageMode);

        // 部屋の一覧のカードの見本。これを複製して1枚ずつ作る（見本そのものは隠しておく）
        roomCardTemplate = PartObject(TitlePartRole.RoomCardTemplate);
        if (roomCardTemplate != null)
        {
            roomCardTemplate.SetActive(false);
        }

        SetPartActive(TitlePartRole.CreateWindow, false);
        SetPartActive(TitlePartRole.PasswordWindow, false);
        SetPartActive(TitlePartRole.BusyOverlay, false);
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

    private void WireButtons()
    {
        foreach (TitleMenuButton look in GetComponentsInChildren<TitleMenuButton>(true))
        {
            // 一覧のカードのボタンは、カードを作るときにつなぐ
            if (roomCardTemplate != null && look.transform.IsChildOf(roomCardTemplate.transform))
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

            field.characterLimit = IsPassword(kind) ? InternetConnection.PasswordInputLimit : MaxLengthOf(kind);
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
        return kind == TitleInputKind.JoinPassword;
    }

    private static int MaxLengthOf(TitleInputKind kind)
    {
        if (kind == TitleInputKind.ServerName)
        {
            return RoomNameMaxLength;
        }

        return IsPassword(kind) ? InternetConnection.PasswordMaxLength : GameSettings.PlayerNameMaxLength;
    }

    private string ValueOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                return createRoomName;
            case TitleInputKind.JoinPassword:
                return joinPassword;
            default:
                return GameSettings.PlayerName ?? string.Empty;
        }
    }

    private void SetValue(TitleInputKind kind, string value)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                createRoomName = value;
                break;
            case TitleInputKind.JoinPassword:
                joinPassword = value;
                break;
            default:
                GameSettings.PlayerName = value;
                SetText(TitlePartRole.StartMessage, string.Empty);
                break;
        }
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

        counter.horizontalOverflow = HorizontalWrapMode.Overflow;
        counter.text = tooLong ? InternetConnection.PasswordTooLongMessage : $"{value.Length}/{max}";
        counter.color = tooLong ? new Color(1f, 0.45f, 0.4f, 1f) : normal;
    }

    /// <summary>その窓・画面の入力欄に、いまの値を入れ直す。</summary>
    private void RefreshInputs(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (TitleInput input in root.GetComponentsInChildren<TitleInput>(true))
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
            case TitleButtonAction.Start: PressStart(); break;
            case TitleButtonAction.Back: GoBack(); break;
            case TitleButtonAction.PlaySolo: StartSolo(); break;
            case TitleButtonAction.ShowMulti: OpenRoomList(); break;
            case TitleButtonAction.OpenCreateRoom: OpenWindow(Window.Create); break;
            case TitleButtonAction.CreateRoomInternet: CreateInternetRoom(); break;
            case TitleButtonAction.CreateRoomLan: CreateLanRoom(); break;
            case TitleButtonAction.CloseWindow: CloseWindow(); break;
            case TitleButtonAction.ConfirmJoinPassword: ConfirmJoinPassword(); break;
        }
    }

    /// <summary>その Action のボタン全部に「選ばれている」見た目を付け外しする。</summary>
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

    private TitleMenuButton FindButton(TitleButtonAction action)
    {
        foreach (TitleMenuButton look in buttons)
        {
            if (look != null && look.Action == action)
            {
                return look;
            }
        }
        return null;
    }

    // ------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------

    private void ShowPage(Page page)
    {
        current = page;
        CloseWindow();

        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page);
        }

        if (page == Page.Mode)
        {
            roomListOpen = false;
            SetPreview(Preview.Solo);
        }
        else
        {
            roomListOpen = false;
            SetLanListening(false);
            SetText(TitlePartRole.StartMessage, string.Empty);
        }

        if (pages.TryGetValue(page, out GameObject shown))
        {
            RefreshInputs(shown);
            SelectFirstForGamepad(shown);
        }
    }

    /// <summary>1つ前へ戻る（小さい窓が開いていれば閉じる）。</summary>
    private void GoBack()
    {
        if (window != Window.None)
        {
            CloseWindow();
            return;
        }

        if (current == Page.Mode)
        {
            ShowPage(Page.Start);
        }
    }

    /// <summary>「START」。名前が入っていれば、1人プレイ／マルチプレイを選ぶ画面へ。</summary>
    private void PressStart()
    {
        if (string.IsNullOrWhiteSpace(GameSettings.PlayerName))
        {
            SetText(TitlePartRole.StartMessage, "名前を入れてください");
            return;
        }

        ShowPage(Page.Mode);
    }

    // ------------------------------------------------------------
    // 1人プレイ／マルチプレイ
    // ------------------------------------------------------------

    /// <summary>
    /// **右側の表示を、ポインターが重なっている（十字キーで選ばれている）ほうに合わせる。**（マリオカートの選び方）
    /// 「1人プレイ」に合わせたら、部屋の一覧は閉じる。
    /// </summary>
    private void UpdatePreview()
    {
        if (window != Window.None)
        {
            return;
        }

        TitleMenuButton solo = FindButton(TitleButtonAction.PlaySolo);
        TitleMenuButton multi = FindButton(TitleButtonAction.ShowMulti);

        if (solo != null && solo.IsHighlighted && preview != Preview.Solo)
        {
            roomListOpen = false;
            SetLanListening(false);
            SetPreview(Preview.Solo);
        }
        else if (multi != null && multi.IsHighlighted && preview != Preview.Multi)
        {
            SetPreview(Preview.Multi);
        }
    }

    private void SetPreview(Preview value)
    {
        preview = value;

        SetPartActive(TitlePartRole.SoloPreview, preview == Preview.Solo);
        SetPartActive(TitlePartRole.MultiPreview, preview == Preview.Multi && !roomListOpen);
        SetPartActive(TitlePartRole.RoomListPanel, preview == Preview.Multi && roomListOpen);

        SetSelectedLook(TitleButtonAction.PlaySolo, preview == Preview.Solo);
        SetSelectedLook(TitleButtonAction.ShowMulti, preview == Preview.Multi);
    }

    /// <summary>「1人プレイ」。このPCの中だけで動くホストになり、そのままロビーへ（移すのは <see cref="TitleSceneFlow"/>）。</summary>
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
            // ほかのPCからは入れないように、このPCの中だけで待つ
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "127.0.0.1");
        }

        manager.StartHost();
    }

    /// <summary>「マルチプレイ」。右に部屋の一覧を出して、探し始める。</summary>
    private void OpenRoomList()
    {
        roomListOpen = true;
        SetPreview(Preview.Multi);
        SetLanListening(true);

        roomsSignature = null;
        UpdateRoomList();

        GameObject plus = PartObject(TitlePartRole.CreateRoomButton);
        if (plus != null)
        {
            SelectForGamepad(plus);
        }
    }

    // ------------------------------------------------------------
    // 部屋の一覧
    // ------------------------------------------------------------

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

    /// <summary>一覧を開いている間、インターネットの部屋を取り直し、LAN の部屋と合わせて並べ直す。</summary>
    private void UpdateRoomList()
    {
        // 「マルチプレイ」を何度押しても、決まった間隔より短くは取り直さない（2026/10/13・連打で 429 になった）
        if (Time.unscaledTime >= nextQueryTime && !searching)
        {
            nextQueryTime = Time.unscaledTime + InternetRefreshSeconds;
            SearchInternetRooms();
        }

        List<RoomEntry> entries = CollectRooms();
        string signature = Signature(entries);
        if (signature == roomsSignature)
        {
            return;
        }

        roomsSignature = signature;
        RebuildRoomCards(entries);
    }

    private async void SearchInternetRooms()
    {
        InternetConnection internet = Internet();
        if (internet == null)
        {
            internetProblem = "インターネットの接続の部品が見つかりません";
            return;
        }

        if (internet.IsBusy)
        {
            return;
        }

        searching = true;
        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        searching = false;
        searchedOnce = true;

        if (this == null)
        {
            return;
        }

        internetRooms.Clear();

        if (sessions == null)
        {
            internetProblem = (string.IsNullOrEmpty(internet.Message) ? "インターネットの部屋を取れませんでした" : internet.Message) +
                              $"。{InternetRetrySeconds:0}秒後に自動でもう一度探します";
            nextQueryTime = Time.unscaledTime + InternetRetrySeconds;
            internet.ClearFailure();
        }
        else
        {
            internetProblem = string.Empty;
            internetRooms.AddRange(sessions);
        }

        roomsSignature = null; // 次のフレームで並べ直す
    }

    private List<RoomEntry> CollectRooms()
    {
        List<RoomEntry> entries = new List<RoomEntry>();

        foreach (ISessionInfo info in internetRooms)
        {
            entries.Add(new RoomEntry
            {
                Lan = false,
                SessionId = info.Id,
                Name = string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name,
                Players = Mathf.Max(0, info.MaxPlayers - info.AvailableSlots),
                MaxPlayers = info.MaxPlayers,
                HasPassword = info.HasPassword,
                Full = info.AvailableSlots <= 0 || info.IsLocked,
            });
        }

        foreach (LanRoomDiscovery.Room room in LanRoomDiscovery.Rooms)
        {
            entries.Add(new RoomEntry
            {
                Lan = true,
                Address = room.Address,
                Port = room.Port,
                Name = room.Name,
                Players = room.Players,
                MaxPlayers = room.MaxPlayers,
                Full = room.Players >= room.MaxPlayers,
            });
        }

        return entries;
    }

    private string Signature(List<RoomEntry> entries)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(searchedOnce).Append('|').Append(internetProblem).Append('|');
        foreach (RoomEntry entry in entries)
        {
            builder.Append(entry.Lan ? entry.Address + ":" + entry.Port : entry.SessionId)
                .Append(',').Append(entry.Name).Append(',').Append(entry.Players).Append('/').Append(entry.MaxPlayers)
                .Append(entry.HasPassword).Append(entry.Full).Append(';');
        }
        return builder.ToString();
    }

    private void RebuildRoomCards(List<RoomEntry> entries)
    {
        // コントローラーで選んでいたカードが消えても、迷子にならないように「＋」へ戻す
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool selectedCard = selected != null && roomCards.Exists(card => card != null && selected.transform.IsChildOf(card.transform));

        foreach (GameObject card in roomCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }
        roomCards.Clear();

        RectTransform content = PartObject(TitlePartRole.RoomListContent) != null
            ? (RectTransform)PartObject(TitlePartRole.RoomListContent).transform
            : null;

        if (content != null && roomCardTemplate != null)
        {
            foreach (RoomEntry entry in entries)
            {
                roomCards.Add(AddRoomCard(content, entry));
            }
        }
        else if (roomCardTemplate == null)
        {
            Debug.LogWarning("[UI] 部屋の一覧のカードの見本（Role：RoomCardTemplate）がプレハブにありません。");
        }

        // 「＋」はいつも一番下
        GameObject plus = PartObject(TitlePartRole.CreateRoomButton);
        if (plus != null)
        {
            plus.transform.SetAsLastSibling();
        }

        string message;
        if (entries.Count > 0)
        {
            message = string.Empty;
        }
        else if (!searchedOnce)
        {
            message = "部屋を探しています…";
        }
        else
        {
            message = "部屋が見つかりません。「＋」で部屋を作れます";
        }

        if (!string.IsNullOrEmpty(internetProblem))
        {
            message = (message.Length > 0 ? message + "\n" : string.Empty) + $"（インターネットの部屋：{internetProblem}）";
        }

        SetText(TitlePartRole.RoomListMessage, message);
        SetPartActive(TitlePartRole.RoomListMessage, message.Length > 0);

        if (selectedCard && plus != null)
        {
            SelectForGamepad(plus);
        }
    }

    /// <summary>見本のカード（RoomCardTemplate）を複製して、部屋1つぶんを一覧に足す。</summary>
    private GameObject AddRoomCard(RectTransform content, RoomEntry entry)
    {
        GameObject card = Instantiate(roomCardTemplate, content);
        card.name = $"Room {entry.Name}";
        card.SetActive(true);

        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            if (text == null)
            {
                continue;
            }

            if (part.Role == TitlePartRole.RoomCardName)
            {
                text.text = entry.Name + (entry.Lan ? "（LAN）" : string.Empty) + (entry.HasPassword ? "（パスワードあり）" : string.Empty);
            }
            else if (part.Role == TitlePartRole.RoomCardPlayers)
            {
                text.text = entry.Full ? "満員" : $"{entry.Players}/{entry.MaxPlayers}人";
            }
        }

        Button button = card.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => JoinRoom(entry));
        }

        return card;
    }

    // ------------------------------------------------------------
    // 入る・作る
    // ------------------------------------------------------------

    private void JoinRoom(RoomEntry entry)
    {
        if (entry.Full)
        {
            return;
        }

        if (entry.Lan)
        {
            JoinLan(entry.Address, entry.Port);
            return;
        }

        if (entry.HasPassword)
        {
            pendingJoin = entry;
            SetText(TitlePartRole.PasswordWindowTitle, $"「{entry.Name}」のパスワード");
            OpenWindow(Window.Password);
            return;
        }

        InternetConnection internet = Internet();
        if (internet != null && !WaitForListQuery(internet))
        {
            internet.JoinGameById(entry.SessionId);
        }
    }

    /// <summary>
    /// 部屋の一覧を取っている最中は、部屋を作る・入るができない（インターネットの係が1つのことしかできないため）。
    /// そのときは「少し待って」と出して true を返す。
    /// </summary>
    private bool WaitForListQuery(InternetConnection internet)
    {
        if (!internet.IsBusy)
        {
            return false;
        }

        const string wait = "部屋の一覧を取っています。少し待ってから、もう一度押してください";
        if (window == Window.Create)
        {
            SetText(TitlePartRole.CreateWindowNote, wait);
        }
        else
        {
            SetText(TitlePartRole.RoomListMessage, wait);
            SetPartActive(TitlePartRole.RoomListMessage, true);
            roomsSignature = null;
        }
        return true;
    }

    private void ConfirmJoinPassword()
    {
        InternetConnection internet = Internet();
        if (internet == null || string.IsNullOrEmpty(pendingJoin.SessionId))
        {
            CloseWindow();
            return;
        }

        if (internet.IsBusy)
        {
            SetText(TitlePartRole.PasswordWindowTitle, "部屋の一覧を取っています。少し待ってから、もう一度押してください");
            return;
        }

        CloseWindow();
        internet.JoinGameById(pendingJoin.SessionId, joinPassword.Trim());
    }

    private void JoinLan(string address, ushort port)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        lanTarget = address;
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(address, port);
        }

        manager.StartClient();
    }

    /// <summary>作る部屋の名前。部屋の名前の欄に入れた名前、空なら「自分の名前の部屋」。</summary>
    private string RoomName()
    {
        if (!string.IsNullOrWhiteSpace(createRoomName))
        {
            return createRoomName.Trim();
        }

        string name = GameSettings.PlayerName;
        return string.IsNullOrWhiteSpace(name) ? "StarSweepers の部屋" : $"{name.Trim()}の部屋";
    }

    /// <summary>
    /// 「インターネットで作る」。**公開の部屋**を、**パスワードなし**で作る（ほかの人の部屋の一覧に出る）。
    /// （2026/10/13 から、作る窓ではパスワードを決めない。大槻さんの依頼）
    /// </summary>
    private void CreateInternetRoom()
    {
        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        if (WaitForListQuery(internet))
        {
            return;
        }

        CloseWindow();
        SetLanListening(false);
        internet.HostGame(RoomName(), RoomMaxPlayers, false, string.Empty);
    }

    /// <summary>「LANで作る」。同じネットワークのPCから入れるホストになり、部屋を LAN に知らせる。</summary>
    private void CreateLanRoom()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        CloseWindow();
        SetLanListening(false);

        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "0.0.0.0");
        }

        if (manager.StartHost())
        {
            LanRoomDiscovery.StartAdvertising(RoomName(), RoomMaxPlayers);
        }
    }

    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    // ------------------------------------------------------------
    // 小さい窓
    // ------------------------------------------------------------

    private void OpenWindow(Window value)
    {
        CloseWindow();
        window = value;

        TitlePartRole role = value == Window.Create ? TitlePartRole.CreateWindow : TitlePartRole.PasswordWindow;
        GameObject shown = PartObject(role);
        if (shown == null)
        {
            window = Window.None;
            return;
        }

        if (value == Window.Create)
        {
            SetText(TitlePartRole.CreateWindowNote,
                "インターネット：どこからでも入れる部屋（ほかの人の一覧に出る）\nLAN：同じネットワークのPCだけが入れる部屋\n" +
                "部屋の名前が空なら「自分の名前の部屋」になります");
        }

        shown.SetActive(true);
        RefreshInputs(shown);
        SelectFirstButtonForGamepad(shown);
    }

    private void CloseWindow()
    {
        window = Window.None;
        SetPartActive(TitlePartRole.CreateWindow, false);
        SetPartActive(TitlePartRole.PasswordWindow, false);
    }

    // ------------------------------------------------------------
    // つないでいる途中
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
    /// ・インターネット：作っている・入ろうとしている → 説明を出す。失敗 → 理由と「戻る」
    /// ・LAN：つなぎに行っている → 説明と「やめる」
    /// ・つながった → ホストがロビーへ移すまでの間「移動しています」
    /// （部屋の一覧を取っている間は出さない）
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
        else if (internet != null && internet.IsBusy && !searching)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        else if (internet != null && internet.State == InternetConnection.Phase.Failed && !searching)
        {
            message = internet.Message;
            buttonLabel = "戻る";
            action = () => internet.ClearFailure();
        }
        else if (lanConnecting)
        {
            message = $"LAN の部屋（{lanTarget}）につないでいます…\nつながらないときは、同じネットワークにいるかを確かめてください。";
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
    // 小物
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

    private void SetPartActive(TitlePartRole role, bool active)
    {
        GameObject target = PartObject(role);
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    /// <summary>このPCのIPアドレス（LAN でホストになったとき、ロビーの左上に出す）。</summary>
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

    /// <summary>窓を開いたとき用。入力欄ではなく最初のボタンを選ぶ（入力欄を選ぶと、すぐ文字を打つ状態になるため）。</summary>
    private static void SelectFirstButtonForGamepad(GameObject root)
    {
        if (Gamepad.current == null || root == null || EventSystem.current == null)
        {
            return;
        }

        Button first = root.GetComponentInChildren<Button>();
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
