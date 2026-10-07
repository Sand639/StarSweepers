using UnityEngine;

/// <summary>
/// **プレイヤーの頭の上に出す、名前のタグ。**（2026/10/6・大槻さん）
///
/// ・タイトル画面で入れた名前を出す（入れていなければ仮の名前）。名前はホストが全員に配っている
///   （<see cref="SpaceJunkSession.NameOf"/>）
/// ・文字の色は**その人のチームの色**。黒い影を付けて、明るい床の上でも読めるようにしてある
/// ・**いつもカメラのほうを向く**（カメラが回っても読める）
///
/// 見た目は **`Assets/Resources/PlayerNameplate.prefab`** のインスペクターで変えられる（高さ・文字の大きさ・影・自分の名前も出すか）。
/// 全員のプレイヤーに、<see cref="SpaceJunkPlayerSetup"/> がこのプレハブを付ける。
/// 文字は日本語なので、フォントは再生したときにパソコンから借りる（<see cref="UiFont"/>。保存できないため）。
/// </summary>
public class SpaceJunkNameplate : MonoBehaviour
{
    [Header("場所と大きさ")]
    [Tooltip("プレイヤーの足もとから、どれだけ上に出すか（m）")]
    [SerializeField] private float height = 2.6f;

    [Tooltip("文字の大きさの倍率。0.05 で、文字の高さがだいたい 0.3m")]
    [SerializeField] private float characterSize = 0.05f;

    [Tooltip("フォントの細かさ（大きいほどきれいだが重い）")]
    [SerializeField] private int fontSize = 64;

    [Header("見た目")]
    [Tooltip("ON：文字をチームの色にする。OFF：Fixed Color にする")]
    [SerializeField] private bool useTeamColor = true;

    [Tooltip("Use Team Color が OFF のときの文字の色")]
    [SerializeField] private Color fixedColor = Color.white;

    [Tooltip("文字の影の色")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.85f);

    [Tooltip("影をずらす量（m）")]
    [SerializeField] private float shadowOffset = 0.035f;

    [Tooltip("自分の頭の上にも出すか")]
    [SerializeField] private bool showOwnName = true;

    private FishingNetPlayer owner;
    private TextMesh text;
    private TextMesh shadow;
    private GameObject visual;

    /// <summary>どのプレイヤーの名前を出すかを決める。</summary>
    public void Attach(FishingNetPlayer player)
    {
        owner = player;
    }

    private void Awake()
    {
        Font font = UiFont.Find(fontSize);

        visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);

        shadow = CreateText("Shadow", font, shadowColor);
        shadow.transform.localPosition = new Vector3(shadowOffset, -shadowOffset, 0.01f);

        text = CreateText("Text", font, fixedColor);
    }

    private TextMesh CreateText(string name, Font font, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(TextMesh));
        textObject.transform.SetParent(visual.transform, false);

        TextMesh mesh = textObject.GetComponent<TextMesh>();
        mesh.font = font;
        mesh.fontSize = fontSize;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.LowerCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;

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
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        SpaceJunkSession session = SpaceJunkSession.Current;
        // 1台で複数人のときは、このPCの人どうしを見分けられるように自分たちの名前も出す（2026/10/7）
        bool show = session != null && owner.IsSpawned && (showOwnName || !owner.IsOwner || LocalMultiplayer.IsActive);

        if (visual.activeSelf != show)
        {
            visual.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        // 頭の上に置き、カメラのほうを向ける（プレイヤーが回っても、タグは回らない）
        transform.position = owner.transform.position + Vector3.up * height;

        Camera view = Camera.main;
        if (view != null)
        {
            transform.rotation = view.transform.rotation;
        }

        // 人ごとの番号で引く（1台で複数人の2人目以降も、自分の名前とチームが出るように）
        string label = session.NameOf(owner.PlayerKey);
        if (text.text != label)
        {
            text.text = label;
            shadow.text = label;
        }

        text.color = useTeamColor ? SpaceJunkTeams.TeamColor(session.TeamOf(owner.PlayerKey)) : fixedColor;
    }
}
