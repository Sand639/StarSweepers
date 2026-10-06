using UnityEngine;
using UnityEngine.UI;

/// <summary>タイトル画面の入力欄の種類。数字は保存に使うので、足すときは空いている番号を使う。</summary>
public enum TitleInputKind
{
    PlayerName = 0,
    ServerName = 1,
    PrivateCode = 2,
    LanAddress = 3,

    // パスワード（2026/10/6）。空ならパスワードなし。8〜64文字
    CreatePassword = 4,
    JoinPassword = 5,
}

/// <summary>
/// **タイトル画面の入力欄に「何を入れる欄か」を付ける印。**
/// 入れた値は <see cref="TitleScreen"/> が受け取る。文字数の上限はコードで決める（名前は16文字 など）。
/// Counter に文字を入れておくと、「いま何文字か」をそこに出す。
/// </summary>
[RequireComponent(typeof(InputField))]
public class TitleInput : MonoBehaviour
{
    [Tooltip("何を入れる欄か")]
    [SerializeField] private TitleInputKind kind;

    [Tooltip("「いま何文字か（3/16 など）」を出す文字。空なら出さない")]
    [SerializeField] private Text counter;

    public TitleInputKind Kind => kind;
    public Text Counter => counter;
    public InputField Field => GetComponent<InputField>();

    private void OnEnable()
    {
        // 名前はどの画面の欄でも同じ。ほかの画面で変えた名前を、開くたびに入れ直す
        InputField field = Field;
        if (kind == TitleInputKind.PlayerName && Application.isPlaying && field != null && field.text != GameSettings.PlayerName)
        {
            field.text = GameSettings.PlayerName;
        }
    }

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(TitleInputKind value, Text counterText)
    {
        kind = value;
        counter = counterText;
    }
#endif
}
