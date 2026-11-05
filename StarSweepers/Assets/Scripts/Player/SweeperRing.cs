using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の足元のリング。**（2026/10/27・小野田さんの仕様）
///
/// チームが見分けやすく、自分がどれか分かりやすくするための印。床の少し上に板を置き、
/// シェーダー（`Art/Shaders/SweeperRing.shader`）で**形を計算して描く**ので、画像は要らない。
/// 形は 円／三角／四角／星／六角形 から選べる。色は <see cref="SweeperPlayer"/> がチームの色を入れる。
///
/// この部品を付けた物の位置（ふつうはプレイヤーの足元）に描く。狙いのカーソル（<see cref="SweeperAim"/>）の目印にも使っている。
///
/// ※ 床の中から直接描く方法（URP のデカール）もあるが、全員共通の描画設定（`PC_Renderer`）を変える必要があるので使っていない。
/// </summary>
[DefaultExecutionOrder(100)]
public class SweeperRing : MonoBehaviour
{
    /// <summary>リングの形。</summary>
    public enum RingShape
    {
        Circle = 0,
        Triangle = 1,
        Square = 2,
        Star = 3,
        Hexagon = 4
    }

    [Header("形")]
    [Tooltip("リングの形")]
    [SerializeField] private RingShape shape = RingShape.Circle;

    [Tooltip("リングの外側の直径（m）")]
    [Min(0.05f)]
    [SerializeField] private float size = 1.4f;

    [Tooltip("ふちの太さ（m）")]
    [Min(0.005f)]
    [SerializeField] private float thickness = 0.1f;

    [Tooltip("内側の塗りの濃さ（0 で塗らない）")]
    [Range(0f, 1f)]
    [SerializeField] private float fillAlpha = 0.15f;

    [Tooltip("星のくぼみ（小さいほど細い星）")]
    [Range(0.2f, 0.9f)]
    [SerializeField] private float starInner = 0.5f;

    [Header("置き方")]
    [Tooltip("床からどれだけ浮かせるか（m）。床とちらつくときは少し上げる")]
    [Min(0f)]
    [SerializeField] private float lift = 0.02f;

    [Tooltip("ON：体と一緒に回る（三角などで前が分かる）。OFF：いつも同じ向き")]
    [SerializeField] private bool rotateWithBody = true;

    [Header("色と描き方")]
    [Tooltip("色。ふつうは SweeperPlayer がチームの色を入れる")]
    [SerializeField] private Color color = new Color(0.3f, 0.6f, 0.95f);

    [Tooltip("Art/Materials/SweeperRing.mat を入れる。空なら再生したときに作る")]
    [SerializeField] private Material material;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
    private static readonly int RotationId = Shader.PropertyToID("_Rotation");
    private static readonly int StarInnerId = Shader.PropertyToID("_StarInner");

    private Transform visual;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;

    /// <summary>リングの外側の半径（m）。狙いの帯は、ここから伸ばす。</summary>
    public float Radius => size * 0.5f;

    /// <summary>形。</summary>
    public RingShape Shape
    {
        get => shape;
        set
        {
            shape = value;
            ApplyLook();
        }
    }

    private void Awake()
    {
        BuildVisual();
        ApplyLook();
    }

    private void LateUpdate()
    {
        if (visual == null)
        {
            return;
        }

        visual.position = transform.position + Vector3.up * lift;
        visual.rotation = rotateWithBody
            ? Quaternion.Euler(0f, transform.eulerAngles.y, 0f)
            : Quaternion.identity;
        visual.localScale = Vector3.one * size;
    }

    /// <summary>色を変える（<see cref="SweeperPlayer"/> がチームの色を入れる）。</summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
        ApplyLook();
    }

    /// <summary>形の板を子として作る。板は1m四方で、大きさは LateUpdate で Size に合わせる。</summary>
    private void BuildVisual()
    {
        if (visual != null)
        {
            return;
        }

        GameObject child = new GameObject("RingVisual");
        child.transform.SetParent(transform, false);
        visual = child.transform;

        child.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;
        meshRenderer = child.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    private void ApplyLook()
    {
        if (meshRenderer == null)
        {
            return;
        }

        block ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        block.SetFloat(ShapeId, (float)shape);
        // 太さはシェーダーでは「板の半分を 1 とした割合」で渡す
        block.SetFloat(ThicknessId, Mathf.Clamp(thickness / Mathf.Max(0.01f, size * 0.5f), 0.01f, 1f));
        block.SetFloat(FillAlphaId, fillAlpha);
        // 四角は頂点が真上だとひし形になるので、45度回して辺を前に向ける
        block.SetFloat(RotationId, shape == RingShape.Square ? 45f : 0f);
        block.SetFloat(StarInnerId, starInner);
        meshRenderer.SetPropertyBlock(block);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyLook();
        }
    }
#endif
}
