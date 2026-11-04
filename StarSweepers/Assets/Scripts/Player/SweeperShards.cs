using UnityEngine;

/// <summary>
/// **飛び散る星のかけら（クリティカルプルが決まったときの仮のエフェクト。案1）。**（2026/11/4・小野田さんの依頼）
///
/// 縮んできた輪が弾けたように、物資のまわりから小さな星をいくつも外へ飛ばす。
/// 星は上へ跳ねてから落ち、回りながら小さく薄くなって消える。いつもカメラの方を向く。
/// 見た目はリングと同じシェーダー（`Art/Shaders/SweeperRing.shader` の星を塗りつぶしたもの）。
/// </summary>
public class SweeperShards : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
    private static readonly int RotationId = Shader.PropertyToID("_Rotation");

    private const int Count = 10;
    private const float Life = 0.7f;
    private const float Gravity = 14f;

    private readonly Transform[] shards = new Transform[Count];
    private readonly MeshRenderer[] renderers = new MeshRenderer[Count];
    private readonly Vector3[] velocities = new Vector3[Count];
    private readonly float[] spins = new float[Count];
    private MaterialPropertyBlock block;
    private Color color;
    private float size;
    private float elapsed;

    /// <summary>物資のまわり（半径 <paramref name="radius"/> m）から、星のかけらを外へ飛ばす。</summary>
    public static SweeperShards Spawn(Vector3 center, float radius, Color color, Material material)
    {
        GameObject shardsObject = new GameObject("SweeperShards");
        shardsObject.transform.position = center;

        SweeperShards burst = shardsObject.AddComponent<SweeperShards>();
        burst.color = color;
        burst.size = Mathf.Clamp(radius * 0.45f, 0.18f, 0.6f);
        Material useMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");

        float offset = Random.Range(0f, 360f);
        for (int i = 0; i < Count; i++)
        {
            // まわりに均等に、少しずつずらして並べる
            float angle = offset + i * 360f / Count + Random.Range(-12f, 12f);
            Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

            GameObject shard = new GameObject("Shard");
            shard.transform.SetParent(shardsObject.transform, false);
            shard.transform.position = center + outward * radius;
            shard.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;
            MeshRenderer shardRenderer = shard.AddComponent<MeshRenderer>();
            shardRenderer.sharedMaterial = useMaterial;
            shardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shardRenderer.receiveShadows = false;

            burst.shards[i] = shard.transform;
            burst.renderers[i] = shardRenderer;
            burst.velocities[i] = outward * Random.Range(4f, 6.5f) + Vector3.up * Random.Range(3f, 5.5f);
            burst.spins[i] = Random.Range(-540f, 540f);
        }

        burst.Apply(0f, 0f);
        return burst;
    }

    private void Awake()
    {
        // MaterialPropertyBlock は部品の作り始め（欄の初期値）では作れないので、ここで作る
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        elapsed += deltaTime;
        float t = Mathf.Clamp01(elapsed / Life);
        Apply(t, deltaTime);

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private void Apply(float t, float deltaTime)
    {
        Camera view = Camera.main;
        Vector3 toCamera = view != null ? -view.transform.forward : Vector3.up;

        for (int i = 0; i < Count; i++)
        {
            velocities[i] += Vector3.down * Gravity * deltaTime;
            shards[i].position += velocities[i] * deltaTime;
            // 板の表（上向き）をカメラへ向ける
            shards[i].rotation = Quaternion.FromToRotation(Vector3.up, toCamera);
            shards[i].localScale = Vector3.one * size * (1f - 0.6f * t);

            renderers[i].GetPropertyBlock(block);
            block.SetColor(ColorId, new Color(color.r, color.g, color.b, color.a * (1f - t * t)));
            block.SetFloat(ShapeId, 3f);
            block.SetFloat(ThicknessId, 0.3f);
            block.SetFloat(FillAlphaId, 1f);
            block.SetFloat(RotationId, spins[i] * elapsed);
            renderers[i].SetPropertyBlock(block);
        }
    }
}
