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
    SetPublic = 17,
    SetPrivate = 18,

    // サーバーを探す
    OpenFindPrivate = 20,
    OpenFindPublic = 21,
    OpenFindLan = 22,
    JoinPrivate = 23,
    JoinLan = 24,
    SearchPublic = 25,
    JoinListedServer = 26,
    OpenFindFriends = 27,
    JoinFriendServer = 28,
    RefreshFriends = 29,

    // 設定
    TabSound = 30,
    TabGraphics = 31,
    TabGeneral = 32,
    ApplyScreen = 33,

    // 2026/10/13 からのタイトル画面（名前とスタート → 1人プレイ／マルチプレイ → 部屋の一覧）
    Start = 40,
    PlaySolo = 41,
    ShowMulti = 42,
    OpenCreateRoom = 43,
    CreateRoomInternet = 44,
    CreateRoomLan = 45,
    CloseWindow = 46,
    JoinRoom = 47,
    ConfirmJoinPassword = 48,

    // サーバーを作る：□パブリック □プライベート □LAN の「LAN」（2026/10/15）
    SetLan = 49,

    // メイン：クイックプレイ（公開のサーバーに入る。無ければ作る。2026/10/15）
    QuickPlay = 50,

    // 2026/10/19 からのタイトル（ソロプレイ／マルチプレイ）
    OpenMulti = 51,
    ToggleSearchPublic = 52,
    ToggleSearchFriends = 53,

    // 参加の確認の窓（公開・フレンドのサーバーを押したとき。2026/10/19）
    ConfirmJoinDialog = 54,
    CloseJoinDialog = 55,

    // マルチプレイ（クイックマッチ／部屋を探す／部屋を作る）の「部屋を探す」（2026/10/22）
    OpenFindRoom = 56,

    // 部屋を探す（2026/10/22）：「募集中の部屋」の ⟳（すぐ取り直す）と、■パスワード不要
    RefreshRoomList = 57,
    ToggleSearchNoPassword = 58,
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
    /// <summary>テーマが指定されていないときに読む、Resources フォルダの中のテーマの名前。</summary>
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

    [Tooltip("マウスが重なったとき・コントローラーで選ばれたときの大きさ（1 で変わらない。1.08 なら 8% 大きくなる）。" +
             "真ん中から大きくするには、RectTransform の Pivot を (0.5, 0.5) にしておく")]
    [SerializeField] private float highlightScale = 1f;

    /// <summary>大きさを変える速さ（大きいほどすぐ変わる）。</summary>
    private const float ScaleSpeed = 18f;

    /// <summary>下地に付いているふち（無ければ null）。重なったときに色を濃くする。</summary>
    private Outline outline;

    /// <summary>マウス（ポインター）が重なっているか。</summary>
    private bool pointerOver;

    /// <summary>十字キー・スティックで選ばれているか。</summary>
    private bool selectedByNavigation;

    /// <summary>「選ばれている」見た目にしているか（タブ・切り替えボタン用。<see cref="SetSelectedLook"/>）。</summary>
    private bool selectedLook;

    /// <summary>Resources から読んだ共通のテーマ。全部のボタンで1つを使い回す。</summary>
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

        if (Application.isPlaying)
        {
            transform.localScale = Vector3.one;
        }
    }

    /// <summary>重なっている・選ばれている間は <see cref="highlightScale"/> へ、そうでなければ元の大きさへ、少しずつ近づける（再生中だけ）。</summary>
    private void Update()
    {
        // 大きくしないボタン（1）は、元の大きさのままなら何もしない
        if (!Application.isPlaying || (Mathf.Approximately(highlightScale, 1f) && transform.localScale == Vector3.one))
        {
            return;
        }

        float target = IsHighlighted ? highlightScale : 1f;
        float now = transform.localScale.x;
        if (Mathf.Approximately(now, target))
        {
            return;
        }

        // Time.timeScale に関係なく動くように unscaledDeltaTime を使う。近くまで来たらぴったり合わせる
        float next = Mathf.Lerp(now, target, 1f - Mathf.Exp(-ScaleSpeed * Time.unscaledDeltaTime));
        if (Mathf.Abs(next - target) < 0.002f)
        {
            next = target;
        }

        transform.localScale = new Vector3(next, next, 1f);
    }

    /// <summary>「選ばれている」見た目にする（タブ・切り替えボタンで、いまの選択を示す）。</summary>
    public void SetSelectedLook(bool selected)
    {
        selectedLook = selected;
        Refresh();
    }

    /// <summary>いま、マウス（ポインター）が重なっているか、十字キーで選ばれているか。タイトルの「1人プレイ／マルチプレイ」の右側の表示に使う。</summary>
    public bool IsHighlighted => pointerOver || selectedByNavigation;

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

    /// <summary>いまの状態（重なり・選択）に合わせて、画像・色・文字の色・ふちを描き直す。</summary>
    private void Refresh()
    {
        TitleScreenTheme look = Theme;

        // `this == null` は、消されたボタンに編集中のテーマ変更の知らせ（EditorChanged）が届いたときの用心
        if (!useTheme || this == null || background == null || look == null)
        {
            return;
        }

        // highlighted … マウスか十字キーで指されている（重なり用の画像に変わる）
        // emphasized  … それに加えて「選ばれている」見た目も含む（色・文字・ふちが変わる）
        bool highlighted = pointerOver || selectedByNavigation;
        bool emphasized = highlighted || selectedLook;

        Sprite sprite = highlighted && look.buttonHover != null ? look.buttonHover : look.button;
        background.sprite = sprite;

        // Sprite Editor で9スライス（Border）を付けた画像は、角をつぶさずに引き伸ばす
        background.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        background.color = emphasized ? look.buttonHoverColor : look.buttonColor;

        if (label != null)
        {
            label.color = emphasized ? look.buttonHoverTextColor : look.buttonTextColor;
        }

        if (outline != null)
        {
            // ふだんは同じ色を薄く（不透明度 35%）残し、重なったときにはっきり出す
            outline.effectColor = emphasized ? look.buttonOutlineColor : new Color(look.buttonOutlineColor.r,
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

    /// <summary>プレハブを作るツールが、重なったときの大きさを決める。</summary>
    public void EditorSetHighlightScale(float value)
    {
        highlightScale = value;
    }
#endif
}
