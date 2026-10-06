using UnityEngine;

/// <summary>
/// **タイトル画面の見た目をまとめたファイル。**（2026/10/6・大槻さん「あとからUI画像で簡単に見た目を変えたい」）
///
/// `Assets/Resources/TitleScreenTheme.asset` を選ぶと、インスペクターで画像と色を差し替えられる。
/// **画像を入れていない所は、色だけの四角で描く**（仮の見た目のまま遊べる）。
///
/// | 項目 | どこに使うか |
/// | --- | --- |
/// | Background | 画面いっぱいの背景（いまは `Assets/Art/Sprites/title.png`） |
/// | Logo | 左上のタイトルロゴ。空なら Title Text を文字で出す |
/// | Button / Button Hover | ボタンの画像（ふだん／マウスが重なった・コントローラーで選んだとき） |
/// | Panel | 入力欄・一覧のカードなどの下地 |
/// | Accent Panel | 参加コードなど、目立たせる枠の下地 |
///
/// 画像は「Sprite (2D and UI)」で読み込むこと。ボタンや枠の画像は、**Sprite Editor で9スライス（Border）を付けておくと**、
/// 大きさが変わっても角がつぶれない（付けていなければ、そのまま引き伸ばす）。
/// </summary>
[CreateAssetMenu(menuName = "StarSweepers/タイトル画面のテーマ", fileName = "TitleScreenTheme")]
public class TitleScreenTheme : ScriptableObject
{
    [Header("画像（空なら色だけで描く）")]
    [Tooltip("画面いっぱいの背景")]
    public Sprite background;

    [Tooltip("タイトルロゴ。空なら Title Text を文字で出す")]
    public Sprite logo;

    [Tooltip("ボタンの画像（ふだん）")]
    public Sprite button;

    [Tooltip("ボタンの画像（マウスが重なった・コントローラーで選んだとき）。空ならふだんの画像を色だけ変える")]
    public Sprite buttonHover;

    [Tooltip("入力欄・一覧のカードなどの下地")]
    public Sprite panel;

    [Tooltip("参加コードなど、目立たせる枠の下地")]
    public Sprite accentPanel;

    [Header("文字")]
    [Tooltip("ロゴの画像が無いときに出すタイトル")]
    public string titleText = "Star Sweepers";

    [Tooltip("右下に出すバージョン。空なら Project Settings の Version を出す")]
    public string versionText = string.Empty;

    [Tooltip("ボタンの文字の大きさ")]
    public int buttonFontSize = 30;

    [Tooltip("ふつうの文字の大きさ")]
    public int fontSize = 24;

    [Header("色")]
    [Tooltip("背景の上に重ねる暗さ（文字を読みやすくする）。透明にすれば背景そのまま")]
    public Color backgroundTint = new Color(0f, 0f, 0f, 0.25f);

    [Tooltip("ボタンの色（画像があれば、画像にこの色を掛ける）")]
    public Color buttonColor = new Color(0.10f, 0.12f, 0.12f, 0.92f);

    [Tooltip("ボタンの色（重なったとき）")]
    public Color buttonHoverColor = new Color(0.12f, 0.20f, 0.14f, 0.96f);

    [Tooltip("ボタンのふち（重なったとき・選ばれているとき）")]
    public Color buttonOutlineColor = new Color(0.20f, 0.95f, 0.35f, 1f);

    [Tooltip("ボタンの文字の色")]
    public Color buttonTextColor = Color.white;

    [Tooltip("ボタンの文字の色（重なったとき）")]
    public Color buttonHoverTextColor = new Color(0.30f, 1f, 0.45f, 1f);

    [Tooltip("入力欄・カードの色")]
    public Color panelColor = new Color(0.08f, 0.08f, 0.10f, 0.75f);

    [Tooltip("参加コードの枠など、目立たせる色")]
    public Color accentColor = new Color(0.58f, 0.10f, 0.95f, 0.95f);

    [Tooltip("ふつうの文字の色")]
    public Color textColor = Color.white;

    [Tooltip("補足の文字の色（小さい説明）")]
    public Color noteColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Tooltip("ロゴの文字の色（ロゴの画像が無いとき）")]
    public Color titleColor = new Color(1f, 0.95f, 0.55f, 1f);

    [Header("大きさ（1920×1080 を基準にした値）")]
    [Tooltip("メインのボタンの大きさ")]
    public Vector2 mainButtonSize = new Vector2(420f, 76f);
}
