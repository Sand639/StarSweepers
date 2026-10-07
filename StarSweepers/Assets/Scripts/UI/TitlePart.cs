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
