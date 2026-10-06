using UnityEngine;

/// <summary>
/// **タイトル画面のボタンの見た目を、まとめて決めるファイル。**（2026/10/6・大槻さん「あとからUI画像で簡単に見た目を変えたい」）
///
/// `Assets/Resources/TitleScreenTheme.asset` を選ぶと、インスペクターで画像と色を差し替えられる。
/// **全部のボタン（<see cref="TitleMenuButton"/>）に一度に効く。** 編集中でも、変えるとすぐ見た目に出る。
///
/// ボタン以外（背景・ロゴ・入力欄の下地・文字の色や大きさ・配置）は、**プレハブ `Assets/Resources/TitleScreen.prefab` の中で直接変える**
/// （2026/10/6 にプレハブへ組み直したため、ここから外した）。
///
/// 画像は「Sprite (2D and UI)」で読み込むこと。ボタンの画像は、**Sprite Editor で9スライス（Border）を付けておくと**、
/// 大きさが変わっても角がつぶれない（付けていなければ、そのまま引き伸ばす）。
/// </summary>
[CreateAssetMenu(menuName = "StarSweepers/タイトル画面のテーマ", fileName = "TitleScreenTheme")]
public class TitleScreenTheme : ScriptableObject
{
    [Header("ボタンの画像（空なら色だけで描く）")]
    [Tooltip("ボタンの画像（ふだん）")]
    public Sprite button;

    [Tooltip("ボタンの画像（マウスが重なった・コントローラーで選んだとき）。空ならふだんの画像を色だけ変える")]
    public Sprite buttonHover;

    [Header("ボタンの色")]
    [Tooltip("ボタンの色（画像があれば、画像にこの色を掛ける）")]
    public Color buttonColor = new Color(0.10f, 0.12f, 0.12f, 0.92f);

    [Tooltip("ボタンの色（重なったとき・選ばれているとき）")]
    public Color buttonHoverColor = new Color(0.12f, 0.20f, 0.14f, 0.96f);

    [Tooltip("ボタンのふち（重なったとき・選ばれているとき）")]
    public Color buttonOutlineColor = new Color(0.20f, 0.95f, 0.35f, 1f);

    [Tooltip("ボタンの文字の色")]
    public Color buttonTextColor = Color.white;

    [Tooltip("ボタンの文字の色（重なったとき・選ばれているとき）")]
    public Color buttonHoverTextColor = new Color(0.30f, 1f, 0.45f, 1f);

#if UNITY_EDITOR
    /// <summary>インスペクターで値が変わったとき（編集中のボタンの見た目を描き直すのに使う）。</summary>
    public static event System.Action EditorChanged;

    private void OnValidate()
    {
        EditorChanged?.Invoke();
    }
#endif
}
