using UnityEngine;

/// <summary>
/// ロビーの画面の中で、**コードが中身を書き換える・出し入れする場所**の役目。
/// 数字は保存に使うので、**途中に足さず、足すときは空いている番号を使う**こと。
/// </summary>
public enum SpaceJunkLobbyPartRole
{
    None = 0,

    // 大きな入れ物
    SettingsWindow = 1,
    Hud = 2,

    // ゲームを始める
    StartButton = 10,
    StartLabel = 11,
    StartMessage = 12,
    RuleText = 13,

    // パスワード（ホストが変える。パスワードありの部屋だけ出す）
    PasswordSection = 14,
    PasswordInput = 15,
    PasswordMessage = 16,

    // チーム分け
    PlayerListContent = 20,
    PlayerRowTemplate = 21,
    PlayerRowName = 22,
    PlayerRowTeam = 23,

    // マップ
    MapPreviewImage = 30,
    MapPreviewPlaceholder = 31,
    MapPreviewName = 32,
    MapListContent = 33,
    MapRandomHighlight = 34,
    MapRandomLabel = 35,
    MapRowTemplate = 36,
    MapRowName = 37,
    MapRowCheckOn = 38,
    MapRowCheckOff = 39,
    MapRowHighlight = 40,
    MapHiddenNote = 41,

    // ふだん左上に出ている案内
    HudMapImage = 50,
    HudMapPlaceholder = 51,
    HudMapName = 52,
    HudPlayerCount = 53,
    HudRoster = 54,
    HudSummary = 55,
    HudHint = 56,
    HudJoinCode = 57,
}

/// <summary>
/// **ロビーの画面の部品に「何の役目か」を付ける印。**（2026/10/6・大槻さん「タイトルと同じ要領で作り直して」）
///
/// <see cref="SpaceJunkLobbyScreen"/> は、この印を頼りに部品を見つける（名前や並び順は見ない）。
/// だから、**動かしても・名前を変えても・大きさを変えてもよい。** 印を消すと、その部品はコードから動かなくなる。
/// </summary>
[DisallowMultipleComponent]
public class SpaceJunkLobbyPart : MonoBehaviour
{
    [Tooltip("この部品の役目。コードはこれで部品を見つける")]
    [SerializeField] private SpaceJunkLobbyPartRole role;

    public SpaceJunkLobbyPartRole Role => role;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが役目を決める。</summary>
    public void EditorSetRole(SpaceJunkLobbyPartRole value)
    {
        role = value;
    }
#endif
}
