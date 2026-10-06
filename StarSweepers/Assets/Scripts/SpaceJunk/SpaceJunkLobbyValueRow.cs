using UnityEngine;
using UnityEngine.UI;

/// <summary>ロビーのゲーム設定の項目。数字は保存に使うので、足すときは空いている番号を使う。</summary>
public enum SpaceJunkLobbyValueKind
{
    TeamCount = 0,
    RoundsToWin = 1,
    RoundSeconds = 2,
}

/// <summary>
/// **ロビーのゲーム設定の1行に「何の設定か」を付ける印。**
/// 中にある <see cref="SpaceJunkLobbyButton"/>（Action：Step）を押すと、その Amount だけ値が変わる。
/// Value Text に、いまの値が入る。値の範囲はコードで決める（チーム数 1〜4、何本先取 1〜5、最大時間 10〜300秒）。
/// </summary>
public class SpaceJunkLobbyValueRow : MonoBehaviour
{
    [Tooltip("何の設定か")]
    [SerializeField] private SpaceJunkLobbyValueKind kind;

    [Tooltip("いまの値を出す文字")]
    [SerializeField] private Text valueText;

    public SpaceJunkLobbyValueKind Kind => kind;
    public Text ValueText => valueText;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(SpaceJunkLobbyValueKind value, Text text)
    {
        kind = value;
        valueText = text;
    }
#endif
}
