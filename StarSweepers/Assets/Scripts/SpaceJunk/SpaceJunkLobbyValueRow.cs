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
///
/// ・中にある <see cref="SpaceJunkLobbyButton"/>（Action：Step）を押すと、その Amount だけ値が変わる
/// ・Input に入力欄を入れておくと、**数字を打ち込んでも変えられる**（めっちゃカメレオンと同じ。2026/10/6）。
///   半角数字はそのまま、**全角数字は半角に直して**受け取る。Enter か、入力欄から外れたときに反映し、範囲の外なら端にそろえる
/// ・Value Text に、いまの値が入る（入力欄があるときは、入力欄の文字）
///
/// 値の範囲はコードで決める（チーム数 1〜4、何本先取 1〜5、最大時間 10〜300秒）。
/// </summary>
public class SpaceJunkLobbyValueRow : MonoBehaviour
{
    [Tooltip("何の設定か")]
    [SerializeField] private SpaceJunkLobbyValueKind kind;

    [Tooltip("いまの値を出す文字（入力欄があるときは使わない）")]
    [SerializeField] private Text valueText;

    [Tooltip("数字を打ち込む入力欄。空なら打ち込めない（± のボタンだけ）")]
    [SerializeField] private InputField input;

    public SpaceJunkLobbyValueKind Kind => kind;
    public Text ValueText => valueText;
    public InputField Input => input;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(SpaceJunkLobbyValueKind value, Text text, InputField field)
    {
        kind = value;
        valueText = text;
        input = field;
    }
#endif
}
