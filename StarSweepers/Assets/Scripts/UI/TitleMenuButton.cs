using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// タイトル画面のボタンを押したときにすること。数字は保存に使うので、**足すときは空いている番号を使う**こと。
/// </summary>
public enum TitleButtonAction
{
    None = 0,

    // メイン
    OpenCreate = 1,
    OpenFind = 2,
    OpenSettings = 3,
    Quit = 4,
    Back = 5,

    // サーバーを作る
    CreateServer = 10,
    PracticeSolo = 11,
    MaxPlayersDown = 12,
    MaxPlayersUp = 13,
    UseInternet = 14,
    UseLan = 15,
    TogglePrivate = 16,

    // サーバーを探す
    OpenFindPrivate = 20,
    OpenFindPublic = 21,
    OpenFindLan = 22,
    JoinPrivate = 23,
    JoinLan = 24,
    SearchPublic = 25,
    JoinListedServer = 26,

    // 設定
    TabSound = 30,
    TabGraphics = 31,
    TabGeneral = 32,
    ApplyScreen = 33,
}

/// <summary>
/// **タイトル画面のボタン1つ。** 押したときにすること（Action）と、見た目の切り替えを持つ。
///
/// ・**Action を選ぶだけで、ボタンの働きが決まる。** ボタンを複製して Action を変えれば、別の場所に同じ働きのボタンを置ける
/// ・マウスが重なったとき・コントローラーで選ばれたときに、画像・色・文字の色・ふちを変える
/// ・見た目は <see cref="TitleScreenTheme"/>（`Assets/Resources/TitleScreenTheme.asset`）から読む。**全部のボタンの見た目をまとめて変えられる**
/// ・このボタンだけ見た目を変えたいときは、Use Theme を OFF にして、Image の画像と色を直接変える（重なっても変わらなくなる）
///
/// 「選ばれている」状態（設定のタブ・つなぎかたの切り替えなど）も、同じ見た目で出せる（<see cref="SetSelectedLook"/>）。
/// </summary>
[ExecuteAlways]
public class TitleMenuButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private const string ThemeResourcePath = "TitleScreenTheme";

    [Tooltip("押したときにすること")]
    [SerializeField] private TitleButtonAction action;

    [Tooltip("ON：テーマ（TitleScreenTheme）の見た目を使う。OFF：Image の画像と色のまま（重なっても変わらない）")]
    [SerializeField] private bool useTheme = true;

    [Tooltip("使うテーマ。空なら Assets/Resources/TitleScreenTheme")]
    [SerializeField] private TitleScreenTheme theme;

    [Tooltip("ボタンの下地の画像")]
    [SerializeField] private Image background;

    [Tooltip("ボタンの文字")]
    [SerializeField] private Text label;

    private Outline outline;

    private bool pointerOver;
    private bool selectedByNavigation;
    private bool selectedLook;

    private static TitleScreenTheme defaultTheme;

    /// <summary>押したときにすること。</summary>
    public TitleButtonAction Action => action;

    /// <summary>ボタンの文字（「満員」などに書き換えるとき用）。</summary>
    public Text Label => label;

    private TitleScreenTheme Theme
    {
        get
        {
            if (theme != null)
            {
                return theme;
            }

            if (defaultTheme == null)
            {
                defaultTheme = Resources.Load<TitleScreenTheme>(ThemeResourcePath);
            }

            return defaultTheme;
        }
    }

    private void OnEnable()
    {
        if (background == null)
        {
            background = GetComponent<Image>();
        }

        if (label == null)
        {
            label = GetComponentInChildren<Text>(true);
        }

        outline = background != null ? background.GetComponent<Outline>() : null;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 編集中も、テーマを変えたらすぐ見た目に出す
            TitleScreenTheme.EditorChanged += Refresh;
        }
#endif

        Refresh();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        TitleScreenTheme.EditorChanged -= Refresh;
#endif

        // 画面を切り替えたときに「重なっている」見た目が残らないように
        pointerOver = false;
        selectedByNavigation = false;
        Refresh();
    }

    /// <summary>「選ばれている」見た目にする（タブ・切り替えボタンで、いまの選択を示す）。</summary>
    public void SetSelectedLook(bool selected)
    {
        selectedLook = selected;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        Refresh();
    }

    public void OnSelect(BaseEventData eventData)
    {
        selectedByNavigation = true;
        Refresh();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selectedByNavigation = false;
        Refresh();
    }

    private void Refresh()
    {
        TitleScreenTheme look = Theme;
        if (!useTheme || this == null || background == null || look == null)
        {
            return;
        }

        bool hot = pointerOver || selectedByNavigation;
        bool lit = hot || selectedLook;

        Sprite sprite = hot && look.buttonHover != null ? look.buttonHover : look.button;
        background.sprite = sprite;
        background.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        background.color = lit ? look.buttonHoverColor : look.buttonColor;

        if (label != null)
        {
            label.color = lit ? look.buttonHoverTextColor : look.buttonTextColor;
        }

        if (outline != null)
        {
            outline.effectColor = lit ? look.buttonOutlineColor : new Color(look.buttonOutlineColor.r,
                look.buttonOutlineColor.g, look.buttonOutlineColor.b, 0.35f);
        }
    }

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(TitleButtonAction value, Image backgroundImage, Text labelText)
    {
        action = value;
        background = backgroundImage;
        label = labelText;
        outline = background.GetComponent<Outline>();
        Refresh();
    }
#endif
}
