using UnityEngine;

/// <summary>
/// **床に広がる光の輪（仮のエフェクト）。**（2026/10/27）
///
/// クリティカルプルの合図と、決まったときの光に使う。決められた時間で輪が広がりながら薄くなり、自分で消える。
/// 見た目はリングと同じシェーダー（`Art/Shaders/SweeperRing.shader` の円）。
/// 本物のエフェクトができたら、<see cref="SweeperReel"/> のエフェクトの欄にプレハブを入れれば、そちらが出る。
/// </summary>
public class SweeperFlash : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private Color color;
    private float startSize;
    private float endSize;
    private float duration;
    private float elapsed;
    private Transform follow;

    /// <summary>
    /// 光の輪を出す。<paramref name="target"/> を渡すと、その物の足元に付いていく（null ならその場）。
    /// </summary>
    public static SweeperFlash Spawn(Vector3 position, Transform target, Color color, float startSize, float endSize,
        float duration, Material material)
    {
        GameObject flashObject = new GameObject("SweeperFlash");
        flashObject.transform.position = position;
        flashObject.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;

        SweeperFlash flash = flashObject.AddComponent<SweeperFlash>();
        flash.meshRenderer = flashObject.AddComponent<MeshRenderer>();
        flash.meshRenderer.sharedMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");
        flash.meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flash.meshRenderer.receiveShadows = false;
        flash.block = new MaterialPropertyBlock();
        flash.color = color;
        flash.startSize = startSize;
        flash.endSize = endSize;
        flash.duration = Mathf.Max(0.01f, duration);
        flash.follow = target;
        flash.Apply(0f);
        return flash;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        Apply(t);

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private void Apply(float t)
    {
        if (follow != null)
        {
            Vector3 position = follow.position;
            position.y = transform.position.y;
            transform.position = position;
        }

        // 広がりながら薄くなる（最初はすぐ広がり、だんだんゆっくりに）
        float eased = 1f - (1f - t) * (1f - t);
        transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, eased);

        meshRenderer.GetPropertyBlock(block);
        block.SetColor(ColorId, new Color(color.r, color.g, color.b, color.a * (1f - t)));
        block.SetFloat(ShapeId, 0f);
        block.SetFloat(ThicknessId, 0.25f);
        block.SetFloat(FillAlphaId, 0.35f * (1f - t));
        meshRenderer.SetPropertyBlock(block);
    }
}
