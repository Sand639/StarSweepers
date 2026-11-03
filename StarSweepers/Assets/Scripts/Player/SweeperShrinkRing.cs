using UnityEngine;

/// <summary>
/// **縮んでくる輪（クリティカルプルの合図の仮のエフェクト。案1）。**（2026/11/4・小野田さんの依頼）
///
/// 物資の足元に、大きな輪と、物資のふちに合わせた薄い「的の輪」を出す。
/// 大きな輪は決まった時間（<c>lead</c>）でまっすぐ縮み、**的の輪にぴったり重なった瞬間がクリティカルの受付の始まり**。
/// 受付の間（<c>window</c>）は、重なった輪が白っぽく強く光る。リズムゲームの「輪が重なったら押す」と同じ考え。
///
/// 光ったのを見てから押すのでは間に合わない（受付は0.2秒）ので、**縮んでくる間に押すタイミングを読めるようにする**のがねらい。
/// <paramref name="keepAlive"/> が false を返したら（巻き取りが終わったら）、すぐ消える。
/// </summary>
public class SweeperShrinkRing : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

    /// <summary>受付が終わってから消えるまでの秒数。</summary>
    private const float FadeSeconds = 0.12f;

    /// <summary>縮み始めの輪の大きさ（的の輪の何倍か）。</summary>
    private const float StartScale = 3.2f;

    private MeshRenderer movingRing;
    private MeshRenderer guideRing;
    private MaterialPropertyBlock block;
    private Transform follow;
    private System.Func<bool> keepAlive;
    private Color color;
    private float targetSize;
    private float lead;
    private float window;
    private float elapsed;

    /// <summary>
    /// 縮んでくる輪を出す。<paramref name="targetSize"/> は的の輪（物資のふち）の大きさ（m）。
    /// <paramref name="lead"/> 秒で縮みきり、そこから <paramref name="window"/> 秒だけ強く光る。
    /// </summary>
    public static SweeperShrinkRing Spawn(Vector3 position, Transform target, Color color, float targetSize,
        float lead, float window, System.Func<bool> keepAlive, Material material)
    {
        GameObject ringObject = new GameObject("SweeperShrinkRing");
        ringObject.transform.position = position;

        SweeperShrinkRing ring = ringObject.AddComponent<SweeperShrinkRing>();
        Material useMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");
        ring.guideRing = CreatePlate(ringObject.transform, "Guide", useMaterial);
        ring.movingRing = CreatePlate(ringObject.transform, "Moving", useMaterial);
        ring.follow = target;
        ring.keepAlive = keepAlive;
        ring.color = color;
        ring.targetSize = Mathf.Max(0.2f, targetSize);
        ring.lead = Mathf.Max(0f, lead);
        ring.window = Mathf.Max(0.01f, window);
        ring.Apply();
        return ring;
    }

    private void Awake()
    {
        // MaterialPropertyBlock は部品の作り始め（欄の初期値）では作れないので、ここで作る
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (keepAlive != null && !keepAlive())
        {
            Destroy(gameObject);
            return;
        }

        elapsed += Time.deltaTime;
        if (elapsed >= lead + window + FadeSeconds)
        {
            Destroy(gameObject);
            return;
        }

        Apply();
    }

    private void Apply()
    {
        if (follow != null)
        {
            Vector3 position = follow.position;
            position.y = transform.position.y;
            transform.position = position;
        }

        float fade = elapsed > lead + window ? 1f - (elapsed - lead - window) / FadeSeconds : 1f;

        if (elapsed < lead)
        {
            // 縮んでくる間：まっすぐ（一定の速さで）縮む。押すタイミングを読みやすくするため、緩急は付けない
            float t = lead > 0f ? elapsed / lead : 1f;
            float size = Mathf.Lerp(targetSize * StartScale, targetSize, t);
            SetPlate(movingRing, size, new Color(color.r, color.g, color.b, Mathf.Lerp(0.35f, 0.95f, t)), 0.07f, 0f);
            SetPlate(guideRing, targetSize, new Color(color.r, color.g, color.b, 0.35f), 0.08f, 0.08f);
        }
        else
        {
            // 受付の間：重なった輪が白っぽく強く光り、少しふくらむ
            float t = Mathf.Clamp01((elapsed - lead) / window);
            Color bright = Color.Lerp(Color.white, color, t * 0.6f);
            bright.a = fade;
            SetPlate(movingRing, targetSize * (1f + 0.12f * Mathf.Sin(t * Mathf.PI)), bright, 0.22f, 0.3f * fade);
            SetPlate(guideRing, targetSize, new Color(color.r, color.g, color.b, 0f), 0.08f, 0f);
        }
    }

    private void SetPlate(MeshRenderer plate, float size, Color plateColor, float thickness, float fillAlpha)
    {
        plate.transform.localScale = Vector3.one * size;
        plate.GetPropertyBlock(block);
        block.SetColor(ColorId, plateColor);
        block.SetFloat(ShapeId, 0f);
        // ふちの太さは板の大きさに対する割合なので、大きいときほど細く見えないよう割り戻す
        block.SetFloat(ThicknessId, Mathf.Clamp(thickness * targetSize / Mathf.Max(0.01f, size), 0.02f, 1f));
        block.SetFloat(FillAlphaId, fillAlpha);
        plate.SetPropertyBlock(block);
    }

    private static MeshRenderer CreatePlate(Transform parent, string name, Material material)
    {
        GameObject plate = new GameObject(name);
        plate.transform.SetParent(parent, false);
        plate.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;
        MeshRenderer plateRenderer = plate.AddComponent<MeshRenderer>();
        plateRenderer.sharedMaterial = material;
        plateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        plateRenderer.receiveShadows = false;
        return plateRenderer;
    }
}
