using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の頭の上の名前。**（2026/10/27・小野田さんの仕様）
///
/// 名前と色は <see cref="SweeperPlayer"/> が入れる（チームの色）。いつもカメラのほうを向く。
/// 黒い影を付けて、明るい床の上でも読めるようにしてある。
///
/// 宇宙ごみの試合の名前タグ（<see cref="SpaceJunkNameplate"/>）は通信の部品が無いと名前を受け取れないので、
/// 名前を直接もらえる形で別に作った（見た目の作りは同じ）。
/// </summary>
[DefaultExecutionOrder(100)]
public class SweeperNameTag : MonoBehaviour
{
    [Header("場所と大きさ")]
    [Tooltip("この部品を付けた物（ふつうはプレイヤーの足元）から、どれだけ上に出すか（m）")]
    [SerializeField] private float height = 2.4f;

    [Tooltip("文字の大きさの倍率。0.05 で、文字の高さがだいたい 0.3m")]
    [SerializeField] private float characterSize = 0.05f;

    [Tooltip("フォントの細かさ（大きいほどきれいだが重い）")]
    [SerializeField] private int fontSize = 64;

    [Header("見た目")]
    [Tooltip("文字の影の色")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.85f);

    [Tooltip("影をずらす量（m）")]
    [SerializeField] private float shadowOffset = 0.035f;

    private Transform visual;
    private TextMesh text;
    private TextMesh shadow;
    private string label = "";
    private Color color = Color.white;

    private void Awake()
    {
        Font font = UiFont.Find(fontSize);

        visual = new GameObject("NameTagVisual").transform;
        visual.SetParent(transform, false);

        // 影は右下へずらし、少し奥に置いて文字の後ろに見えるようにする
        shadow = CreateText("Shadow", font, shadowColor);
        shadow.transform.localPosition = new Vector3(shadowOffset, -shadowOffset, 0.01f);
        text = CreateText("Text", font, color);

        ApplyText();
    }

    /// <summary>名前を変える。</summary>
    public void SetName(string newName)
    {
        label = newName ?? "";
        ApplyText();
    }

    /// <summary>文字の色を変える（影の色は変えない）。</summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
        if (text != null)
        {
            text.color = color;
        }
    }

    private void ApplyText()
    {
        if (text == null)
        {
            return;
        }

        text.text = label;
        shadow.text = label;
        text.color = color;
    }

    private TextMesh CreateText(string objectName, Font font, Color textColor)
    {
        GameObject textObject = new GameObject(objectName, typeof(TextMesh));
        textObject.transform.SetParent(visual, false);

        TextMesh mesh = textObject.GetComponent<TextMesh>();
        mesh.font = font;
        mesh.fontSize = fontSize;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.LowerCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = textColor;

        MeshRenderer meshRenderer = textObject.GetComponent<MeshRenderer>();
        if (font != null)
        {
            meshRenderer.sharedMaterial = font.material;
        }
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        return mesh;
    }

    private void LateUpdate()
    {
        // 頭の上に置き、カメラのほうを向ける（プレイヤーが回っても、名前は回らない）
        visual.position = transform.position + Vector3.up * height;

        Camera view = Camera.main;
        if (view != null)
        {
            visual.rotation = view.transform.rotation;
        }
    }
}
