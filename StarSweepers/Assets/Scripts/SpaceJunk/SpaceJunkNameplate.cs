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

    /// <summary>名前を出す相手のプレイヤー。いなくなったら、このタグも消す。</summary>
    private FishingNetPlayer owner;

    /// <summary>名前の文字（チームの色）。</summary>
    private TextMesh text;

    /// <summary>文字の影（同じ文字を黒くして、少しずらして後ろに置いたもの）。</summary>
    private TextMesh shadow;

    /// <summary>
    /// 文字と影をまとめた子。出す・隠すはこれの ON/OFF で行う
    /// （この部品自身を OFF にすると LateUpdate が止まり、また出すときに気づけなくなるため）。
    /// </summary>
    private GameObject visual;

    /// <summary>もともとの大きさ。始まりの演出で強調するとき（<see cref="SpaceJunkGoalFlyIntro"/>）は、これに倍率を掛ける。</summary>
    private Vector3 baseScale;

    /// <summary>どのプレイヤーの名前を出すかを決める。</summary>
    public void Attach(FishingNetPlayer player)
    {
        owner = player;
    }

    private void Awake()
    {
        baseScale = transform.localScale;
        Font font = UiFont.Find(fontSize);

        visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);

        // 影は右下へずらし、0.01m だけ奥（カメラから遠い側）に置いて、文字の後ろに見えるようにする。
        // 描く順番は Sorting Order で「影 → 文字」に決める（下の ShadowSortingOrder）
        shadow = CreateText("Shadow", font, shadowColor, ShadowSortingOrder);
        shadow.transform.localPosition = new Vector3(shadowOffset, -shadowOffset, 0.01f);

        text = CreateText("Text", font, fixedColor, TextSortingOrder);
    }

    /// <summary>
    /// 影と文字を描く順番（Sorting Order。大きいほど後に描かれて手前に見える）。**影を必ず文字より先に描く**（2026/10/28）。
    /// 文字も影も半透明の扱いなので、順番を決めないとカメラからの距離で並べられ、
    /// カメラの角度によっては**影が文字の上に重なって**見えた（大槻さんの報告）。
    /// どの名前のタグの影も、どの名前のタグの文字より先に描かれるので、タグどうしが重なっても影が文字にかぶらない。
    /// </summary>
    private const int ShadowSortingOrder = 0;
    private const int TextSortingOrder = 1;

    /// <summary>文字を1つ作り、<see cref="visual"/> の子にする。下の真ん中を基準に置く（頭の真上に文字が乗るように）。</summary>
    private TextMesh CreateText(string name, Font font, Color color, int sortingOrder)
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

        // 文字の絵はフォントの素材に入っているので、それを使う（借りられなかったときは元のまま）。
        // 名前のタグが床に影を落とさないよう、影は落とさず受けもしない
        MeshRenderer meshRenderer = textObject.GetComponent<MeshRenderer>();
        if (font != null)
        {
            meshRenderer.sharedMaterial = font.material;
        }
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.sortingOrder = sortingOrder;

        return mesh;
    }

    private void LateUpdate()
    {
        // 相手のプレイヤーが抜けた（消えた）ら、タグも片付ける
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

        // 頭の上に置き、カメラのほうを向ける（プレイヤーが回っても、タグは回らない）。
        // 始まりの演出中は、ふだんのカメラの上に重ねた演出のカメラのほうを向ける（2026/10/27）
        transform.position = owner.transform.position + Vector3.up * height;

        Camera view = SpaceJunkGoalFlyIntro.ViewCamera != null ? SpaceJunkGoalFlyIntro.ViewCamera : Camera.main;
        if (view != null)
        {
            transform.rotation = view.transform.rotation;
        }

        // 始まりの演出で、順番に強調されている間だけ大きくなる（ふだんは倍率 1）
        transform.localScale = baseScale * SpaceJunkGoalFlyIntro.NameEmphasisOf(owner);

        // 人ごとの番号で引く（1台で複数人の2人目以降も、自分の名前とチームが出るように）
        string label = session.NameOf(owner.PlayerKey);
        if (text.text != label)
        {
            text.text = label;
            shadow.text = label;
        }

        // チームはロビーで変わることがあるので、色は毎フレーム合わせる（影の色は変えない）
        text.color = useTeamColor ? SpaceJunkTeams.TeamColor(session.TeamOf(owner.PlayerKey)) : fixedColor;
    }
}
