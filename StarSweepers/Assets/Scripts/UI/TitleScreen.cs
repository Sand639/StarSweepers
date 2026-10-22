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
/// | メイン | マルチプレイ／プラクティス（オフラインでロビーへ）／設定／ゲームを終了。右下にバージョン（2026/10/22 から） |
/// | マルチプレイ | 名前・クイックマッチ／部屋を探す／部屋を作る（2026/10/22） |
/// | 部屋を探す | 真ん中の枠に：右上の「番号入力で検索する」（番号の近い部屋に絞る。プライベートは全部一致で出す）／募集中の部屋の一覧（**LAN の部屋はいつも一番上**。鍵＝パスワードあり）／下に ■パブリック ■フレンド ■パスワード不要（2026/10/22。大槻さんの写真の形） |
/// | 部屋を作る | 最大人数（2〜8。初期値8）・サーバーの名前・パスワード・□パブリック □プライベート □LAN → 作成（名前の欄は無い。2026/10/22） |
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
///
/// ## 流れ（何がどの順で起きるか）
///
/// 1. **出たとき**（<see cref="Awake"/> / <see cref="OnEnable"/>）… 1回だけ <see cref="Initialize"/> で部品を集めてボタン・入力欄・設定の行をつなぎ、
///    メインの画面を出す。ホストに切られて戻ってきたなら、その理由を取っておく（<see cref="TakeDisconnectReason"/>）
/// 2. **毎フレーム**（<see cref="Update"/>）… Steam から頼まれた参加を受ける → つないでいる途中の表示を出し入れ（<see cref="UpdateBusyOverlay"/>）
///    → マルチプレイの画面なら参加可能なサーバーの一覧を取り直す（<see cref="UpdateJoinableList"/>）→ B／Esc で1つ前の画面へ
/// 3. **ボタンを押したとき** … どのボタンも <see cref="Do"/> に来て、Action ごとの処理に分かれる
/// 4. **つないだあと** … ホストになる／参加するのは NetworkManager と <see cref="InternetConnection"/> の仕事。
///    つながったら <see cref="TitleSceneFlow"/> がロビーへ移し、この画面はシーンごと消える
///
/// 画面（ページ）の行き来：
///
/// ```
/// メイン ─┬─ マルチプレイ ─┬─ 部屋を探す（Page.Multi）
///         │               └─ 部屋を作る（Page.Create）
///         └─ 設定
/// ```
///
/// 戻る（<see cref="GoBack"/>）は、この木を1つ上へ戻る。参加の確認の窓が開いていれば、窓を閉じるだけ。
///
/// ファイルの中は「出し入れ → 部品をつなぐ → ボタン → 画面の切り替え → つなぐ → ソロ・マルチ → 参加の確認の窓 →
/// クイックプレイ → LAN → 切られたとき → フレンド・つないでいる途中の表示 → 設定 → 補助」の順に並んでいる。
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

    /// <summary>
    /// LAN で最後に入れたIPアドレス（次に開いたときに入れておく）。
    /// ※ 2026/10/19 に IP アドレスを打つ欄が無くなってからは、読むだけで書き込む所が無い（いつも初期値か、前に保存された値）。
    /// </summary>
    private const string LastLanAddressKey = "StarSweepers.LastLanAddress";

    /// <summary>サーバーの名前の最大の文字数。</summary>
    private const int ServerNameMaxLength = 25;

    /// <summary>参加コード・IPアドレスの最大の文字数。</summary>
    private const int PrivateCodeMaxLength = 12;
    private const int LanAddressMaxLength = 40;

    /// <summary>「サーバーを作る」で選べる最大人数の下限（上限は定員 <see cref="SpaceJunkTeams.MaxPlayers"/>）。</summary>
    private const int CreateMinPlayers = 2;

    /// <summary>パスワードが長すぎるときの、「いま何文字か」の文字の色（赤）。</summary>
    private static readonly Color CounterTooLongColor = new Color(1f, 0.45f, 0.4f, 1f);

    /// <summary>いまタイトル画面が出ているか。ポーズ画面やロビーの画面が、これを見て自分を隠す。</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>
    /// 画面の種類。
    /// Find・FindPublic・FindLan・FindFriends は、2026/10/19 にマルチプレイの画面へまとめて**いまは使っていない**
    /// （プレハブからも消えている）。FindPrivate も 2026/10/22 に「部屋を探す」（Multi）へまとめた。並び順を変えないように残してある。
    /// </summary>
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

        /// <summary>
        /// **部屋を探す**（参加コードで入る・サーバー検索設定・参加可能なサーバー）。
        /// 2026/10/19 に「マルチプレイ」の画面として作り、2026/10/22 に「部屋を探す」になった（名前はそのまま）。
        /// </summary>
        Multi,

        /// <summary>マルチプレイ（クイックマッチ・部屋を探す・部屋を作る）。2026/10/22</summary>
        MultiMenu,
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

    // 部品（Initialize で1回だけ集める。同じ Role が2つあれば、先に見つかったほうを使う）
    private readonly Dictionary<TitlePartRole, TitlePart> parts = new Dictionary<TitlePartRole, TitlePart>();
    private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    private readonly Dictionary<SettingsTab, GameObject> settingsGroups = new Dictionary<SettingsTab, GameObject>();

    /// <summary>画面にあるボタン全部（一覧のカードの見本の中のボタンは除く）。「選ばれている」見た目を付け外しするのに使う。</summary>
    private readonly List<TitleMenuButton> buttons = new List<TitleMenuButton>();
    private readonly List<TitleSettingRow> settingRows = new List<TitleSettingRow>();

    // 参加可能なサーバーの一覧（マルチプレイ画面。2026/10/19）
    private GameObject joinableCardTemplate;
    private readonly List<GameObject> joinableCards = new List<GameObject>();

    /// <summary>
    /// 前に並べた一覧の中身を1本の文字にしたもの。毎フレーム作り直すと重いうえ、コントローラーで選んでいたカードが外れるので、
    /// これが変わったときだけカードを作り直す（<see cref="UpdateJoinableList"/>）。null にすると、次のフレームで必ず作り直す。
    /// </summary>
    private string joinableSignature = string.Empty;

    /// <summary>LAN の部屋の知らせをいま聞いているか（<see cref="SetLanListening"/> で二重に始めたり止めたりしないため）。</summary>
    private bool lanListening;

    /// <summary>ホストに切られて戻ってきたときの理由（例：満員）。出し終わったら空にする。</summary>
    private string disconnectNotice;

    /// <summary>
    /// 前に出した切られた理由（同じ理由を、タイトルに戻るたびに出し直さないため）。
    /// タイトルに戻るたびにこの画面は作り直されるので、static にしてシーンをまたいで覚えておく。
    /// </summary>
    private static string shownDisconnectReason = string.Empty;

    /// <summary>見つかった Steam のフレンドの部屋（一覧に並べる用。<see cref="SearchFriendsForList"/> が入れ直す）。</summary>
    private readonly List<SteamFriendRoom> friendRooms = new List<SteamFriendRoom>();

    /// <summary>フレンドの部屋を探している途中か（二重に探さないため）。</summary>
    private bool searchingFriends;

    /// <summary>準備（<see cref="Initialize"/>）が済んだか。Awake と OnEnable のどちらからも呼ぶので、2回目以降は何もしない。</summary>
    private bool initialized;

    /// <summary>いま出している画面（戻る先を決めるのと、毎フレームの処理を分けるのに使う）。</summary>
    private Page current = Page.Main;

    // サーバーを作る
    // 公開のしかたは createLan と createPrivate の組み合わせで決まる：
    // LAN（createLan が true。createPrivate は見ない）／パブリック（どちらも false）／プライベート（createPrivate だけ true）
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

    /// <summary>LAN でつなぎに行く先のIPアドレス（いまは一覧で選んだ部屋のもの。つないでいる途中の表示に出す）。</summary>
    private string lanAddress = string.Empty;

    /// <summary>
    /// 一覧に並べる公開のサーバーを取っている途中か（<see cref="QueryPublicForList"/>）。
    /// インターネットの係は1度に1つのことしかできないので、クイックプレイはこれが終わるのを待つ。
    /// </summary>
    private bool searching;

    // 設定
    private SettingsTab settingsTab = SettingsTab.Sound;

    /// <summary>選べる解像度（同じ大きさはまとめてある）。</summary>
    private Resolution[] resolutions = new Resolution[0];

    /// <summary>
    /// 選んでいる解像度（<see cref="resolutions"/> の番号）と画面モード。
    /// **「解像度／スクリーンモードを適用」を押すまでは画面に効かせない**（<see cref="ApplyScreen"/>）。
    /// </summary>
    private int resolutionIndex;
    private FullScreenMode pendingScreenMode;

    /// <summary>「＜」「＞」で画面モードを切り替える順番。</summary>
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

        // 出すたびに、メインの画面から始める
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

        // つないでいる途中の表示が出ている間は戻らない（その表示の「戻る」「やめる」「OK」で閉じる）
        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        if (WasBackPressed() && (busy == null || !busy.activeSelf))
        {
            GoBack();
        }
    }

    /// <summary>「1つ前の画面へ戻る」が押されたか（キーボードの Esc／コントローラーの B ＝右のボタン）。</summary>
    private static bool WasBackPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
               GamepadInput.WasPressed(GamepadButton.East);
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

    /// <summary>
    /// **最初に1回だけの準備。** フォントを差し替え、印の付いた部品を集めて、ボタン・入力欄・設定の行に処理をつなぐ。
    /// 順番に意味がある：<see cref="CollectParts"/> で一覧のカードの見本を見つけてから <see cref="WireButtons"/> する（見本の中のボタンをつながないため）。
    /// </summary>
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

    /// <summary>
    /// 印（<see cref="TitlePart"/> の Role）の付いた部品を集め、画面（ページ）と設定のタブの中身を覚える。
    /// 再生したときに隠しておく部品（カードの見本・参加の確認の窓・つないでいる途中の表示）は、ここで隠す。
    /// </summary>
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
        AddPage(Page.Settings, TitlePartRole.PageSettings);
        AddPage(Page.Multi, TitlePartRole.PageMulti);
        AddPage(Page.MultiMenu, TitlePartRole.PageMultiMenu);

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

    /// <summary>
    /// ボタンを押したら <see cref="Do"/> に Action が届くようにする。
    /// Button の無い <see cref="TitleMenuButton"/> も <see cref="buttons"/> には入れる（「選ばれている」見た目だけ使うため）。
    /// </summary>
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

    /// <summary>
    /// 入力欄に、いまの値と打てる文字数を入れ、打ったら値を覚えるようにする。
    /// 値は欄ではなくこのスクリプト（名前は <see cref="GameSettings"/>）が持つ。同じ Kind の欄が別の画面にあっても、同じ値になる。
    /// </summary>
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
        counter.color = tooLong ? CounterTooLongColor : normal;
    }

    /// <summary>入力欄の最大の文字数（「いま何文字か」の分母）。どれでもないときは LAN の IP アドレスの欄。</summary>
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

    /// <summary>その Kind の入力欄に入れる、いまの値。<see cref="SetValue"/> と対になっている。</summary>
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

    /// <summary>
    /// **ボタンを押したときの入口。** Action ごとに処理を呼び分ける。
    ///
    /// ・同じ処理に来る Action がいくつかあるのは、古い版のプレハブのボタンでも動くようにするため
    ///   （例：OpenFind・OpenFindPrivate は部屋を探す画面へ、PracticeSolo はプラクティスへ、UseInternet／UseLan は版 6 より前のつなぎかたのボタン）
    /// ・ここに無い Action は、別の場所でつないでいる：一覧のカードの「参加する」は <see cref="AddJoinableCard"/>、
    ///   つないでいる途中の表示のボタン（None）は <see cref="UpdateBusyOverlay"/>、設定の「＜」「＞」は <see cref="WireSettings"/>
    /// </summary>
    private void Do(TitleButtonAction action)
    {
        switch (action)
        {
            case TitleButtonAction.QuickPlay: QuickPlay(); break;
            case TitleButtonAction.PlaySolo: StartSolo(); break;
            case TitleButtonAction.OpenMulti: ShowPage(Page.MultiMenu); break;
            case TitleButtonAction.OpenFindRoom: ShowPage(Page.Multi); break;
            case TitleButtonAction.ToggleSearchPublic: SetSearchPublic(!searchPublic); break;
            case TitleButtonAction.ToggleSearchFriends: SetSearchFriends(!searchFriends); break;
            case TitleButtonAction.ConfirmJoinDialog: ConfirmJoinDialog(); break;
            case TitleButtonAction.CloseJoinDialog: CloseJoinDialog(); break;
            case TitleButtonAction.RefreshRoomList: RefreshRoomListNow(); break;
            case TitleButtonAction.ToggleSearchNoPassword: SetSearchNoPassword(!searchNoPassword); break;
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

            case TitleButtonAction.OpenFindPrivate: ShowPage(Page.Multi); break;
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

    /// <summary>
    /// **画面（ページ）を切り替える。** その画面だけを出し、中身をいまの値で出し直してから、
    /// コントローラーなら最初の項目を選んでおく。
    /// LAN の部屋の知らせは、マルチプレイの画面（一覧がある）を出している間だけ聞く。
    /// </summary>
    private void ShowPage(Page page)
    {
        current = page;
        SetLanListening(page == Page.Multi);
        if (page == Page.Multi)
        {
            // 前に開いたときのカードが残っていても、必ず並べ直す
            joinableSignature = null;
        }

        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page);
        }

        // 画面ごとの中身を、いまの値で出し直す
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
            case Page.MultiMenu:
            case Page.Settings:
                ShowPage(Page.Main);
                break;
            case Page.Multi:
            case Page.Create:
                ShowPage(Page.MultiMenu);
                break;

            // メインには戻る先が無いので、何もしない
        }
    }

    private void RefreshMainName()
    {
        string name = GameSettings.PlayerName;
        SetText(TitlePartRole.PlayerNameText,
            string.IsNullOrEmpty(name) ? "名前：未設定（マルチプレイの画面で入れられます）" : $"名前：{name}");
    }

    /// <summary>「サーバーを作る」の画面を、いまの選び方（人数・公開のしかた）で出し直す。</summary>
    private void RefreshCreatePage()
    {
        SetText(TitlePartRole.MaxPlayersText, $"{createMaxPlayers} 人");
        // □パブリック □プライベート □LAN は、どれか1つだけにチェックが付く（2026/10/15。前は「つなぎかた（インターネット／LAN）」が別の行だった）
        bool publicRoom = !createLan && !createPrivate;
        bool privateRoom = !createLan && createPrivate;
        SetText(TitlePartRole.PublicCheckText, CheckMark(publicRoom));
        SetText(TitlePartRole.PrivateCheckText, CheckMark(privateRoom));
        SetText(TitlePartRole.LanCheckText, CheckMark(createLan));
        // 新しいプレハブは SetPublic／SetPrivate／SetLan、版 6 より前のプレハブは UseInternet／UseLan のボタン（両方に見た目を付ける）
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

        RefreshCodeBox();
    }

    /// <summary>
    /// 「サーバーを作る」の下の枠。参加する人に何を伝えればよいかを出す
    /// （LAN ならこのPCのIPアドレス、インターネットなら参加コードの出る場所）。
    /// </summary>
    private void RefreshCodeBox()
    {
        if (createLan)
        {
            SetText(TitlePartRole.CodeBoxTitle, "このPCのIPアドレス");
            SetText(TitlePartRole.CodeBoxBody, $"{LocalAddresses()}\n参加する人の「部屋を探す」の一覧の一番上に自動で出る");
        }
        else
        {
            SetText(TitlePartRole.CodeBoxTitle, "参加コード");
            SetText(TitlePartRole.CodeBoxBody,
                "サーバーを作成すると発行されます（ロビーの左上に出ます）\n参加する人は「部屋を探す」の「番号入力で検索する」に入れる");
        }
    }

    /// <summary>チェックの印。付いていれば「■」、外れていれば「□」。</summary>
    private static string CheckMark(bool on)
    {
        return on ? "■" : "□";
    }

    private void ChangeMaxPlayers(int delta)
    {
        createMaxPlayers = Mathf.Clamp(createMaxPlayers + delta, CreateMinPlayers, SpaceJunkTeams.MaxPlayers);
        RefreshCreatePage();
    }

    private void SetCreateLan(bool lan)
    {
        createLan = lan;
        RefreshCreatePage();
    }

    /// <summary>パブリック／プライベートを入れ替える（古い版のプレハブの、1つのボタンで切り替えるチェック用）。</summary>
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

    /// <summary>インターネットでつなぐ係（NetworkManager に付いている）。見つからなければ null。</summary>
    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    /// <summary>
    /// 「サーバーを作成」。LAN ならこのPCがホストになって同じネットワークに部屋を知らせる。
    /// インターネットなら <see cref="InternetConnection"/> に部屋を作ってもらう（パブリック／プライベート・パスワード）。
    /// </summary>
    private void CreateServer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(createServerName) ? DefaultServerName() : createServerName;

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

    /// <summary>サーバーの名前を入れなかったときの名前：「（自分の名前）のサーバー」。名前も無ければ「StarSweepers」。</summary>
    private static string DefaultServerName()
    {
        return string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "StarSweepers" : $"{GameSettings.PlayerName} のサーバー";
    }

    /// <summary>LAN（同じ場所のPC同士）のホストになる。1人で練習するときも同じ（ほかの人が入ってこないだけ）。</summary>
    private static void StartLanHost(NetworkManager manager)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            // 3つ目の「0.0.0.0」は、どのネットワークから来た人も受け付けるという意味（ソロプレイは「127.0.0.1」でこのPCの中だけ）。
            // 1つ目のアドレスは参加する側がつなぎに行く先で、ホストでは使わない
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "0.0.0.0");
        }

        manager.StartHost();
    }

    /// <summary>「プライベートサーバーを探す」の「参加する」。参加コードが空なら何もしない（パスワードは空でもよい）。</summary>
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
    private const string SearchNoPasswordKey = "StarSweepers.SearchNoPassword";

    /// <summary>
    /// 部屋番号（合言葉）の文字数。検索欄にこの文字数をすべて入れると、一覧に無い番号（プライベートの部屋）も「入ってみる」行として出す。
    /// </summary>
    private const int RoomCodeLength = 6;

    /// <summary>公開のサーバーの一覧を何秒ごとに取り直すか（短いと、頼みすぎ（429）で断られる）。</summary>
    private const float PublicRefreshSeconds = 5f;

    /// <summary>公開のサーバーの一覧を取れなかったとき、次に取り直すまでの秒数。</summary>
    private const float PublicRetrySeconds = 15f;

    /// <summary>フレンドのサーバーを何秒ごとに探し直すか。</summary>
    private const float FriendRefreshSeconds = 10f;

    /// <summary>Steam にフレンドの様子を頼んでから、届くのを待って読むまでのミリ秒（1000 ＝ 1秒）。</summary>
    private const int FriendReplyWaitMilliseconds = 1000;

    // サーバー検索設定（■パブリックサーバー ■フレンドのサーバー）。最初にマルチプレイの画面を開いたときに PlayerPrefs から読む
    private bool searchPublic = true;
    private bool searchFriends = true;
    private bool searchSettingsLoaded;

    /// <summary>■パスワード不要：パスワードのかかった部屋を一覧に出さない（2026/10/22。最初は外れている）。</summary>
    private bool searchNoPassword;

    // 一覧に並べる元（取り直すたびに入れ替わる）
    private readonly List<ISessionInfo> publicSessions = new List<ISessionInfo>();

    /// <summary>公開のサーバーの一覧を取れなかった理由（一覧の下に出す。取れたら空）。</summary>
    private string publicProblem = string.Empty;

    /// <summary>次に公開のサーバーの一覧を取り直してよい時刻（Time.unscaledTime の秒）。</summary>
    private float nextPublicQueryTime;

    /// <summary>フレンドのサーバーを探せないときの理由（Steam が動いていない など。一覧の下に出す）。</summary>
    private string friendStatus = string.Empty;

    /// <summary>次にフレンドの部屋を探し直してよい時刻（Time.unscaledTime の秒。0 にすると次のフレームですぐ探す）。</summary>
    private float nextFriendQueryTime;

    /// <summary>参加可能なサーバーの一覧の1行ぶん（LAN・公開・フレンドのどれか）。</summary>
    private struct JoinableEntry
    {
        public string Name;

        /// <summary>人数の文字（例：「3/8」）。</summary>
        public string Players;

        /// <summary>満員か（人数の横に「満員」と出て、押しても何もしない）。</summary>
        public bool Full;

        /// <summary>部屋番号（合言葉）。分からない部屋（LAN・番号を書いていない古い版のホスト）は空。</summary>
        public string Code;

        /// <summary>パスワードがかかっているか（鍵の印を出す。■パスワード不要 なら一覧に出さない）。</summary>
        public bool Locked;

        /// <summary>行を押したときにすること。</summary>
        public UnityEngine.Events.UnityAction Join;

        /// <summary>どのサーバーかを見分ける文字（「lan:」「pub:」「friend:」「code:」で始まる）。並べ直すかどうかの判定に使う。</summary>
        public string Key;
    }

    /// <summary>
    /// **プラクティス**（2026/10/22 まではソロプレイという名前だった。中身は同じ）。このPCの中だけで動くホストになって、そのままロビーへ（移すのは <see cref="TitleSceneFlow"/>）。
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
            searchNoPassword = PlayerPrefs.GetInt(SearchNoPasswordKey, 0) != 0;
        }

        RefreshSearchChecks();

        // 開いたときは、取り直してよい間隔なら、すぐ探す（連打で 429 にならないように、間隔は守る）
        nextFriendQueryTime = 0f;
        joinableSignature = null;
        UpdateJoinableList();
    }

    private void RefreshSearchChecks()
    {
        SetText(TitlePartRole.SearchPublicCheckText, CheckMark(searchPublic));
        SetText(TitlePartRole.SearchFriendsCheckText, CheckMark(searchFriends));
        SetSelectedLook(TitleButtonAction.ToggleSearchPublic, searchPublic);
        SetSelectedLook(TitleButtonAction.ToggleSearchFriends, searchFriends);
        SetText(TitlePartRole.SearchNoPasswordCheckText, CheckMark(searchNoPassword));
        SetSelectedLook(TitleButtonAction.ToggleSearchNoPassword, searchNoPassword);
    }

    /// <summary>■パスワード不要 のチェック。覚えておき、一覧を並べ直す。</summary>
    private void SetSearchNoPassword(bool value)
    {
        searchNoPassword = value;
        PlayerPrefs.SetInt(SearchNoPasswordKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        joinableSignature = null;
    }

    /// <summary>
    /// 「募集中の部屋」の横の ⟳。公開の部屋とフレンドの部屋を、すぐ取り直す
    /// （頼みすぎ（429）にならない間隔は <see cref="InternetConnection.QueryPublicSessionsAsync"/> が守る）。
    /// </summary>
    private void RefreshRoomListNow()
    {
        nextPublicQueryTime = 0f;
        nextFriendQueryTime = 0f;
        joinableSignature = null;
    }

    /// <summary>
    /// 検索欄（部屋番号で検索）に入っている文字。前後の空白と「#」を除き、大文字にそろえる（合言葉は大文字の英数字）。
    /// 値は参加コードの欄と同じ <see cref="privateCode"/> を使う。
    /// </summary>
    private string RoomCodeSearch()
    {
        return (privateCode ?? string.Empty).Trim().TrimStart('#').Trim().ToUpperInvariant();
    }

    /// <summary>■パブリックサーバー のチェック。覚えておき、一覧を並べ直す（取り直しの間隔は変えない）。</summary>
    private void SetSearchPublic(bool value)
    {
        searchPublic = value;
        PlayerPrefs.SetInt(SearchPublicKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        joinableSignature = null;
    }

    /// <summary>■フレンドのサーバー のチェック。覚えておき、フレンドの部屋をすぐ探し直す。</summary>
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

        // 取り直すのは、前の取り直しが終わっていて、間隔が空いたときだけ（結果は非同期で publicSessions／friendRooms に入る）。
        // クイックプレイの途中は、インターネットの係を取り合わないように公開のサーバーは取らない
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

        // 並べる中身が前と同じなら、カードは作り直さない
        List<JoinableEntry> entries = CollectJoinable();
        string signature = JoinableSignatureOf(entries);
        if (signature == joinableSignature)
        {
            return;
        }

        joinableSignature = signature;
        RebuildJoinableCards(entries);
    }

    /// <summary>
    /// 一覧に出るもの（チェック・理由の文字・各行の中身）を1本の文字にする。
    /// これが前と同じなら、画面に出ている一覧も同じ（<see cref="joinableSignature"/>）。
    /// </summary>
    private string JoinableSignatureOf(List<JoinableEntry> entries)
    {
        StringBuilder signature = new StringBuilder();
        signature.Append(searchPublic).Append(searchFriends).Append(searchNoPassword).Append('|').Append(RoomCodeSearch())
            .Append('|').Append(publicProblem).Append('|').Append(friendStatus).Append('|');
        foreach (JoinableEntry entry in entries)
        {
            signature.Append(entry.Key).Append(',').Append(entry.Name).Append(',').Append(entry.Code).Append(',')
                .Append(entry.Players).Append(entry.Full).Append(entry.Locked).Append(';');
        }

        return signature.ToString();
    }

    /// <summary>
    /// 一覧に並べる行を、上から順に集める：LAN の部屋 → 公開のサーバー（■パブリック）→ フレンドの部屋（■フレンド）。
    /// 押したときに使う値（アドレス・ID・参加コード）は、ここで取り出してから渡しておく。
    ///
    /// **部屋番号で検索**（2026/10/22・大槻さん）：検索欄に文字があれば、番号にその文字が入っている部屋だけを、
    /// 番号がその文字で始まるもの → 途中に入っているもの の順に並べる（番号の分からない LAN の部屋は出さない）。
    /// **プライベートの部屋は一覧に出てこないので、番号を {RoomCodeLength} 文字すべて入れたときだけ**「入ってみる」行を出す。
    /// ■パスワード不要 なら、パスワードのかかった部屋は出さない。
    /// </summary>
    private List<JoinableEntry> CollectJoinable()
    {
        List<JoinableEntry> all = new List<JoinableEntry>();
        AddLanEntries(all);

        if (searchPublic)
        {
            AddPublicEntries(all);
        }

        if (searchFriends)
        {
            AddFriendEntries(all);
        }

        string search = RoomCodeSearch();
        List<JoinableEntry> startsWith = new List<JoinableEntry>();
        List<JoinableEntry> contains = new List<JoinableEntry>();
        bool exactFound = false;
        foreach (JoinableEntry entry in all)
        {
            if (searchNoPassword && entry.Locked)
            {
                continue;
            }

            if (search.Length == 0)
            {
                startsWith.Add(entry);
                continue;
            }

            string code = (entry.Code ?? string.Empty).ToUpperInvariant();
            exactFound |= code == search;
            if (code.StartsWith(search, System.StringComparison.Ordinal))
            {
                startsWith.Add(entry);
            }
            else if (code.Length > 0 && code.Contains(search))
            {
                contains.Add(entry);
            }
        }

        startsWith.AddRange(contains);

        if (search.Length == RoomCodeLength && !exactFound)
        {
            startsWith.Add(PrivateCodeEntry(search));
        }

        return startsWith;
    }

    /// <summary>
    /// 一覧に無い部屋番号（プライベートの部屋など）に「入ってみる」行。番号を全部入れたときだけ出す。
    /// パスワードがかかっているかは入ってみるまで分からないので、まず「参加しますか？」を出し、断られたらパスワードを聞く。
    /// </summary>
    private JoinableEntry PrivateCodeEntry(string code)
    {
        string roomName = $"部屋番号 #{code}";
        return new JoinableEntry
        {
            Key = $"code:{code}",
            Name = "プライベートの部屋（この番号で入ってみる）",
            Code = code,
            Players = "?",
            Full = false,
            Locked = false,
            Join = () => OpenJoinDialog(roomName, false, password =>
            {
                InternetConnection internet = Internet();
                if (internet != null)
                {
                    internet.JoinGame(code, password);
                }
            }, true),
        };
    }

    /// <summary>LAN の部屋（チェックに関係なく、いつも一番上）。押すと、そのままつなぎに行く（確認の窓は出さない）。</summary>
    private void AddLanEntries(List<JoinableEntry> entries)
    {
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
    }

    /// <summary>公開のサーバー（■パブリックサーバー のとき）。押すと、参加の確認の窓を出す。</summary>
    private void AddPublicEntries(List<JoinableEntry> entries)
    {
        foreach (ISessionInfo info in publicSessions)
        {
            string sessionId = info.Id;
            string roomName = string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name;
            bool hasPassword = info.HasPassword;
            entries.Add(new JoinableEntry
            {
                Key = $"pub:{info.Id}",
                Name = roomName,
                Code = InternetConnection.RoomCodeOf(info),
                Locked = hasPassword,
                Players = $"{PlayersIn(info)}/{info.MaxPlayers}",
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

    /// <summary>
    /// Steam のフレンドの部屋（■フレンドのサーバー のとき）。押すと、参加の確認の窓を出す。
    /// 満員かどうかは Steam からは分からないので、いつも「参加する」にしておく（満員なら入るときに断られる）。
    /// </summary>
    private void AddFriendEntries(List<JoinableEntry> entries)
    {
        foreach (SteamFriendRoom room in friendRooms)
        {
            string code = room.JoinCode;
            string roomName = $"{room.FriendName} の部屋";
            bool hasPassword = room.HasPassword;
            entries.Add(new JoinableEntry
            {
                Key = $"friend:{room.JoinCode}",
                Name = $"【フレンド】{room.FriendName} の部屋（{room.RoomName}）",
                Code = code,
                Locked = hasPassword,
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

    /// <summary>公開のサーバーにいま何人いるか（定員 − 空き。0 より小さくはしない）。</summary>
    private static int PlayersIn(ISessionInfo info)
    {
        return Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
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

    /// <summary>
    /// パスワードがかかっているか、入ってみるまで分からない部屋か（番号で入るプライベートの部屋。2026/10/22）。
    /// まずパスワードなしで入ってみて、パスワードで断られたら、窓をパスワードを聞く形に変える。
    /// </summary>
    private bool pendingPasswordUnknown;

    /// <summary>窓に出している部屋の名前（パスワードを聞く形に変えるときに使う）。</summary>
    private string pendingRoomName = string.Empty;

    private bool IsJoinDialogOpen()
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        return dialog != null && dialog.activeSelf;
    }

    /// <summary>
    /// **参加の確認の窓を出す。** 公開・フレンドのサーバーを一覧で押したとき。
    /// ・パスワードなし … 「「○○」に参加しますか？」と「はい」「いいえ」
    /// ・パスワードあり … 「パスワードを入力してください」と入力欄、「参加する」「やめる」。違ったら「パスワードが間違えています」
    /// **パスワードを聞くのは、パスワードがかかっている部屋だけ**（2026/10/22・大槻さん）。
    /// passwordUnknown（番号で入るプライベートの部屋）は「参加しますか？」で始め、パスワードで断られたら聞く形に変える。
    /// </summary>
    private void OpenJoinDialog(string roomName, bool needsPassword, System.Action<string> join, bool passwordUnknown = false)
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog == null)
        {
            // プレハブに窓が無い（古いプレハブ）。確かめずにそのまま入る
            join?.Invoke(joinPassword.Trim());
            return;
        }

        pendingJoin = join;
        pendingRoomName = roomName;
        pendingPasswordUnknown = passwordUnknown;
        joinDialogWaiting = false;

        SetJoinDialogMode(needsPassword);
        SetText(TitlePartRole.JoinDialogMessage, string.Empty);

        dialog.SetActive(true);

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

    /// <summary>窓の見出し・ボタンの文字・パスワードの欄を、パスワードを聞く形（true）か「参加しますか？」の形（false）にする。</summary>
    private void SetJoinDialogMode(bool needsPassword)
    {
        pendingNeedsPassword = needsPassword;

        SetText(TitlePartRole.JoinDialogTitle, needsPassword
            ? $"「{pendingRoomName}」\nパスワードを入力してください"
            : $"「{pendingRoomName}」に参加しますか？");
        SetText(TitlePartRole.JoinDialogYesLabel, needsPassword ? "参加する" : "はい");
        SetText(TitlePartRole.JoinDialogNoLabel, needsPassword ? "やめる" : "いいえ");

        GameObject passwordRow = PartObject(TitlePartRole.JoinDialogPasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(needsPassword);
        }

        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog != null)
        {
            RefreshInputs(dialog);
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

        // 結果は待たずに戻る。失敗したら、UpdateBusyOverlay が joinDialogWaiting を見て、窓の中に理由を出す
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

        // 待っている間にこの画面が消えた（ロビーへ移った など）ときは何もしない。
        // Unity では、消えた物も「== null」で true になる
        if (this == null)
        {
            return;
        }

        // 取れなかったら、理由を一覧の下に出し、失敗の状態を消しておく（つないでいる途中の表示に「失敗」と出さないため）
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
        await System.Threading.Tasks.Task.Delay(FriendReplyWaitMilliseconds);
        searchingFriends = false;

        // 待っている間にこの画面が消えたときは何もしない（QueryPublicForList と同じ）
        if (this == null)
        {
            return;
        }

        friendRooms.Clear();
        friendRooms.AddRange(SteamFriendsService.FindFriendRooms());
        friendStatus = string.Empty;
    }

    /// <summary>一覧のカードを全部消して、entries の順に作り直す。一覧の下の文字（見つからない・取れない理由）も出し直す。</summary>
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

        SetText(TitlePartRole.JoinableListMessage, JoinableListMessageOf(entries.Count));
    }

    /// <summary>
    /// 一覧の下に出す文字。1つも無いときの案内と、公開のサーバー・フレンドのサーバーを取れなかった理由を、改行でつなぐ。
    /// </summary>
    private string JoinableListMessageOf(int entryCount)
    {
        StringBuilder message = new StringBuilder();
        string search = RoomCodeSearch();
        if (entryCount == 0 && search.Length > 0)
        {
            message.Append($"番号「{search}」に近い部屋は見つかりません。\nプライベートの部屋は、番号を{RoomCodeLength}文字すべて入れると出ます");
        }
        else if (entryCount == 0)
        {
            message.Append(searchPublic || searchFriends
                ? "参加できる部屋が見つかりません。\n1つ前の画面の「クイックマッチ」か「部屋を作る」で遊べます"
                : "パブリック・フレンドのチェックが外れています（LAN の部屋だけを探しています）");
        }

        if (searchPublic && !string.IsNullOrEmpty(publicProblem))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("公開のサーバー：").Append(publicProblem);
        }

        if (searchFriends && !string.IsNullOrEmpty(friendStatus))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("フレンドのサーバー：").Append(friendStatus);
        }

        return message.ToString();
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
                // 行そのものが押すボタンの形（2026/10/22）では、満員は人数の横に出す
                text.text = entry.Full ? $"{entry.Players} 満員" : entry.Players;
            }
            else if (part.Role == TitlePartRole.ServerCardCode)
            {
                text.text = string.IsNullOrEmpty(entry.Code) ? string.Empty : $"#{entry.Code}";
            }
        }

        // 鍵の印（パスワードがかかっている部屋だけ出す）
        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == TitlePartRole.ServerCardLock)
            {
                part.gameObject.SetActive(entry.Locked);
            }
        }

        foreach (TitleMenuButton look in card.GetComponentsInChildren<TitleMenuButton>(true))
        {
            // 「参加する」ボタンの文字だけ書き換える（行そのものがボタンのときは、文字が部屋の名前なので触らない）
            if (look.Label != null && look.Label.GetComponent<TitlePart>() == null)
            {
                look.Label.text = entry.Full ? "満員" : "参加する";
            }

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            // 見本のボタンの Action（JoinListedServer）は使わず、この行の Join だけをつなぐ。満員なら何もつながない
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

    /// <summary>クイックプレイが、一覧の取り直しが終わるのを待つとき、何ミリ秒ごとに確かめるか。</summary>
    private const int QuickPlayWaitStepMilliseconds = 100;

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
            await System.Threading.Tasks.Task.Delay(QuickPlayWaitStepMilliseconds);
        }

        // 待っている間に画面が消えた、またはほかの作業（入る・作る）が始まっていたらやめる
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

        ISessionInfo best = FindFullestOpenSession(sessions, out int bestPlayers);
        if (best != null)
        {
            Debug.Log($"[UI] クイックプレイ：公開のサーバー「{best.Name}」（{bestPlayers}/{best.MaxPlayers}）に入ります。");
            internet.JoinGameById(best.Id);
            return;
        }

        string serverName = DefaultServerName();

        Debug.Log($"[UI] クイックプレイ：入れる公開のサーバーが無いので、「{serverName}」を作ります。");
        internet.HostGame(serverName, SpaceJunkTeams.MaxPlayers, false, string.Empty);
    }

    /// <summary>
    /// すぐ入れるサーバー（空きがある・鍵がかかっていない・パスワードなし）のうち、いちばん人の多いもの。無ければ null。
    /// 人数が同じなら、先に見つかったほう。
    /// </summary>
    private static ISessionInfo FindFullestOpenSession(List<ISessionInfo> sessions, out int bestPlayers)
    {
        ISessionInfo best = null;
        bestPlayers = -1;
        foreach (ISessionInfo info in sessions)
        {
            if (info.AvailableSlots <= 0 || info.IsLocked || info.HasPassword)
            {
                continue;
            }

            int players = PlayersIn(info);
            if (players > bestPlayers)
            {
                best = info;
                bestPlayers = players;
            }
        }

        return best;
    }

    // ------------------------------------------------------------
    // 見つかった LAN の部屋（「LANのサーバーに参加する」画面。2026/10/15・大槻さん「LAN の部屋の自動表示も残して」）
    // ------------------------------------------------------------

    /// <summary>
    /// LAN の部屋の知らせを聞くか（「LANのサーバーに参加する」画面を開いている間だけ聞く）。
    /// 2026/10/19 からは、その画面の代わりに**マルチプレイの画面**を開いている間だけ聞く（<see cref="ShowPage"/>）。
    /// </summary>
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

        lanAddress = address; // つないでいる途中の表示に出す
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

        // 出しても出さなくても「見た」ことにする（次にタイトルに戻ったとき、同じ理由を出し直さないため）
        shownDisconnectReason = reason;

        // **Netcode が自分で付ける記録（「[Disconnect Event]…TransportShutdown」など）は出さない。**（2026/10/19・大槻さんの報告）
        // 自分で通信を止めた（ホストがタイトルに戻った など）ときにも付くので、出すと「入れませんでした」と誤って見えた。
        // 出すのは、ホストが切るときに付けた理由（「満員です（最大8人）」など）だけ
        if (reason.StartsWith("["))
        {
            return;
        }

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

        ChooseBusyContent(Internet(), NetworkManager.Singleton,
            out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action);
        ShowBusyOverlay(overlay, message, buttonLabel, action);
    }

    /// <summary>
    /// つないでいる途中の表示に、いま何を出すかを決める（上の条件ほど優先）。出さないときは message が null。
    /// ボタンを出すときは buttonLabel と action（押したときにすること）が入る。
    /// ※ 参加の確認の窓から入って失敗したときだけは、ここで窓に理由を書いて失敗を消す（表示は出さない）。
    /// </summary>
    private void ChooseBusyContent(InternetConnection internet, NetworkManager manager,
        out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action)
    {
        // LAN でつなぎに行っている途中：通信は始めた（IsListening）が、ホストでもなく、まだつながってもいない。
        // インターネットで部屋に入る途中も同じ形になるので、それは除く
        bool lanConnecting = manager != null && manager.IsListening && !manager.IsServer && !manager.IsConnectedClient &&
                             (internet == null || internet.State != InternetConnection.Phase.Joining);

        message = null;
        buttonLabel = null;
        action = null;

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
            if (internet.LastJoinFailedByPassword && pendingPasswordUnknown && !pendingNeedsPassword)
            {
                // 番号で入ったらパスワードで断られた：パスワードを聞く形に変える（2026/10/22）
                SetJoinDialogMode(true);
                SetText(TitlePartRole.JoinDialogMessage, "この部屋にはパスワードがかかっています");
            }
            else
            {
                SetText(TitlePartRole.JoinDialogMessage, internet.LastJoinFailedByPassword
                    ? "パスワードが間違えています"
                    : internet.Message);
            }
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
    }

    /// <summary>
    /// つないでいる途中の表示を出し入れし、文字とボタンを入れ替える。
    /// コントローラーでボタンを選んでおくのは、表示を出した瞬間だけ（出ている間は選び直さない）。
    /// </summary>
    private void ShowBusyOverlay(GameObject overlay, string message, string buttonLabel, UnityEngine.Events.UnityAction action)
    {
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

    /// <summary>
    /// 設定の行（<see cref="TitleSettingRow"/>）に処理をつなぐ。スライダーは動かしたら、「＜」「＞」は押したら値を変え、その行を出し直す。
    /// どの行が何の設定かは Kind で決まる（<see cref="SetSliderValue"/> / <see cref="Step"/> / <see cref="RefreshSettingRow"/>）。
    /// </summary>
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

    /// <summary>設定のタブを切り替える。選んだタブの中身だけを出し、全部の行をいまの値で出し直す。</summary>
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
        // ※ タブを切り替えても、まだ「適用」していない解像度・画面モードの選択は、いまの画面の値に戻る
        resolutionIndex = Mathf.Max(0, FindCurrentResolution(resolutions));
        pendingScreenMode = Screen.fullScreenMode;

        foreach (TitleSettingRow row in settingRows)
        {
            RefreshSettingRow(row);
        }
    }

    /// <summary>
    /// スライダーの端の値を決める（プレハブで変えられていても、ここで上書きする）。
    /// 音量は 0〜1（1 ＝ 100%）、マウス感度は 0.2〜3 倍、フレームレート上限は 30〜240 fps。
    /// </summary>
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

    /// <summary>スライダーを動かしたとき。値はすぐ <see cref="GameSettings"/> に入れる（すぐ効く。覚えておくのも GameSettings の仕事）。</summary>
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
                // 10 fps きざみにそろえる（例：57 → 60）
                GameSettings.FrameRateLimit = Mathf.RoundToInt(value / 10f) * 10;
                break;
        }
    }

    /// <summary>
    /// 「＜」（delta = -1）・「＞」（delta = +1）を押したとき。端まで行ったら反対の端へ回る。
    /// 解像度と画面モードは選ぶだけで、「適用」を押すまで画面には効かない。ほかはすぐ <see cref="GameSettings"/> に入れる。
    /// </summary>
    private void Step(TitleSettingKind kind, int delta)
    {
        switch (kind)
        {
            // 2つしかないので、どちらを押しても入れ替える
            case TitleSettingKind.ControllerType:
                GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
                    ? ControllerOperationType.TypeB
                    : ControllerOperationType.TypeA;
                break;

            case TitleSettingKind.Resolution:
                if (resolutions.Length > 0)
                {
                    // 個数を足してから割った余りを取るのは、先頭で「＜」を押したときに負の番号にならず、最後へ回るようにするため
                    resolutionIndex = (resolutionIndex + delta + resolutions.Length) % resolutions.Length;
                }
                break;

            case TitleSettingKind.ScreenMode:
                // いまの画面モードが選べる3つに無い（最大化ウィンドウ など）ときは、先頭から
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
            default: // 画質（Quality）
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

    /// <summary>「解像度／スクリーンモードを適用」。選んでおいた解像度と画面モードを、ここで初めて画面に効かせる。</summary>
    private void ApplyScreen()
    {
        if (resolutions.Length > 0)
        {
            Resolution chosen = resolutions[resolutionIndex];
            Screen.SetResolution(chosen.width, chosen.height, pendingScreenMode);
        }
        else
        {
            // 解像度の一覧が空のときは、画面モードだけ変える
            Screen.fullScreenMode = pendingScreenMode;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>その印（Role）の付いた部品。プレハブに無ければ null（古いプレハブでも止まらないように、使う側で null を確かめる）。</summary>
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

    /// <summary>その印の付いた文字を書き換える。部品が無ければ何もしない。</summary>
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

    /// <summary>
    /// いまの画面の大きさが、一覧の何番目か。一覧に無ければ最後（ふつうは一番大きい解像度）。
    /// 一覧が空なら -1（呼ぶ側で 0 にそろえる）。
    /// </summary>
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

    /// <summary>コントローラーがつながっていれば、その項目を選んでおく（コントローラーが無いときは何もしない）。</summary>
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
