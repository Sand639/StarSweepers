using UnityEngine;

/// <summary>
/// タイトル画面の中で、**コードが中身を書き換える・出し入れする場所**の役目。
/// 数字は保存に使うので、**途中に足さず、足すときは空いている番号を使う**こと。
/// </summary>
public enum TitlePartRole
{
    None = 0,

    // 画面（ページ）。選んだ画面だけが表示される
    PageMain = 1,
    PageCreate = 2,
    PageFind = 3,
    PageFindPrivate = 4,
    PageFindPublic = 5,
    PageFindLan = 6,
    PageSettings = 7,
    PageFindFriends = 8,
    // 2026/10/13 からのタイトル画面
    PageStart = 9,
    PageMode = 10,
    PageMulti = 11,

    // 文字（コードが書き換える）
    PlayerNameText = 20,
    VersionText = 21,
    MaxPlayersText = 22,
    CodeBoxTitle = 23,
    CodeBoxBody = 24,
    PrivateCheckText = 25,
    PrivateRow = 26,
    PasswordRow = 27,
    PublicCheckText = 28,

    // パブリックサーバーの一覧
    PublicListContent = 30,
    PublicListMessage = 31,
    ServerCardTemplate = 32,
    ServerCardName = 33,
    ServerCardPlayers = 34,

    // フレンドのサーバー（Steam。2026/10/6）
    FriendListContent = 35,
    FriendListMessage = 36,
    FriendCardTemplate = 37,
    FriendCardName = 38,
    FriendCardInfo = 39,

    // 設定のタブの中身
    SettingsSound = 40,
    SettingsGraphics = 41,
    SettingsGeneral = 42,

    // つないでいる途中・失敗したときの表示
    BusyOverlay = 50,
    BusyMessage = 51,
    BusyButton = 52,
    BusyButtonLabel = 53,

    // 2026/10/13 からのタイトル画面（1人プレイ／マルチプレイ・部屋の一覧・小さい窓）
    StartMessage = 60,
    SoloPreview = 61,
    MultiPreview = 62,
    RoomListPanel = 63,
    RoomListContent = 64,
    RoomListMessage = 65,
    RoomCardTemplate = 66,
    RoomCardName = 67,
    RoomCardPlayers = 68,
    CreateRoomButton = 69,
    CreateWindow = 70,
    CreateWindowNote = 71,
    PasswordWindow = 72,
    PasswordWindowTitle = 73,

    // 「LANのサーバーに参加する」画面の、見つかった LAN の部屋の一覧（2026/10/15。LanRoomDiscovery）
    LanListContent = 74,
    LanListMessage = 75,
    LanCardTemplate = 76,

    // サーバーを作る：□LAN のチェックの文字（2026/10/15）
    LanCheckText = 77,

    // マルチプレイ画面（2026/10/19）：サーバー検索設定のチェックと、参加可能なサーバーの一覧
    SearchPublicCheckText = 78,
    SearchFriendsCheckText = 79,
    JoinableListContent = 80,
    JoinableListMessage = 81,
    JoinableCardTemplate = 82,

    // 参加の確認の窓（2026/10/19）：「参加しますか？」／「パスワードを入力してください」
    JoinDialog = 83,
    JoinDialogTitle = 84,
    JoinDialogMessage = 85,
    JoinDialogPasswordRow = 86,
    JoinDialogYesLabel = 87,
    JoinDialogNoLabel = 88,

    // マルチプレイの最初の画面（クイックマッチ／部屋を探す／部屋を作る。2026/10/22）
    PageMultiMenu = 89,

    // 部屋を探す（2026/10/22。大槻さんの写真の形）：一覧の行の鍵の印・部屋番号、■パスワード不要 のチェック
    ServerCardLock = 90,
    ServerCardCode = 91,
    SearchNoPasswordCheckText = 92,
}

/// <summary>
/// **タイトル画面の部品に「何の役目か」を付ける印。**（2026/10/6・大槻さん「プレハブで組み直して、配置もいじれるように」）
///
/// <see cref="TitleScreen"/> は、この印を頼りに部品を見つける（名前や並び順は見ない）。
/// だから、**動かしても・名前を変えても・大きさを変えてもよい。** 印を消すと、その部品はコードから動かなくなる。
///
/// 例：Role が PlayerNameText の文字には「名前：○○」が入る。PageCreate の入れ物は「サーバーを作る」画面。
/// </summary>
[DisallowMultipleComponent]
public class TitlePart : MonoBehaviour
{
    [Tooltip("この部品の役目。コードはこれで部品を見つける")]
    [SerializeField] private TitlePartRole role;

    public TitlePartRole Role => role;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが役目を決める。</summary>
    public void EditorSetRole(TitlePartRole value)
    {
        role = value;
    }
#endif
}
