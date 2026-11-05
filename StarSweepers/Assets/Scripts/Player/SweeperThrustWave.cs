using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **推進の衝撃波（クリティカルプルの仮のエフェクト）。**（2026/11/4・小野田さんの依頼）
///
/// クリティカルが決まった瞬間に、物資の**引っ張られている側とは逆の側**（プレイヤーから遠い側）に、
/// 縦向きの光の輪を出す。物資が飛び始めたら、少しの間だけ**進む向きの後ろ側**から輪を出し続ける。
/// ロケットが噴射して押し出したように見せるため。
///
/// 輪はその場に残り、後ろへ流れながら広がって薄くなる。見た目はリングと同じシェーダー（`Art/Shaders/SweeperRing.shader` の円）。
/// </summary>
public class SweeperThrustWave : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

    /// <summary>最初の大きな輪のあと、輪を出し続ける秒数。</summary>
    private const float EmitSeconds = 0.35f;

    /// <summary>輪を出す間隔（秒）。</summary>
    private const float EmitInterval = 0.05f;

    /// <summary>これより遅いと「まだ飛んでいない」とみなし、最初に決めた向きを使う（m/秒）。</summary>
    private const float MinMoveSpeed = 1f;

    private class Wave
    {
        public MeshRenderer renderer;
        public Vector3 velocity;
        public float startSize;
        public float endSize;
        public float life;
        public float age;
        public float thickness;
    }

    private readonly List<Wave> waves = new List<Wave>();
    private MaterialPropertyBlock block;

    private Transform target;
    private Vector3 pushDirection;
    private float halfSize;
    private float scale;
    private Color color;
    private Material material;
    private float elapsed;
    private float emitTimer;
    private Vector3 lastPosition;

    /// <summary>
    /// 推進の衝撃波を出す。<paramref name="awayFromPlayer"/> は「プレイヤーから物資へ」の向き（真上から見た向き）。
    /// 輪はその向きの側（物資の向こう側）に出て、物資はその逆へ押し出されて見える。
    /// </summary>
    public static SweeperThrustWave Spawn(Transform target, Vector3 awayFromPlayer, float itemSize, Color color, Material material)
    {
        GameObject waveObject = new GameObject("SweeperThrustWave");
        SweeperThrustWave thrust = waveObject.AddComponent<SweeperThrustWave>();
        thrust.target = target;
        thrust.pushDirection = awayFromPlayer.sqrMagnitude > 0.0001f ? awayFromPlayer.normalized : Vector3.back;
        thrust.halfSize = itemSize * 0.5f;
        thrust.scale = Mathf.Max(0.5f, itemSize);
        thrust.color = color;
        thrust.material = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");
        thrust.lastPosition = target != null ? target.position : Vector3.zero;

        // 決まった瞬間の大きな輪（衝撃波）を2枚：大きくゆっくり広がる輪と、小さく速い輪
        Vector3 origin = thrust.lastPosition + thrust.pushDirection * (thrust.halfSize + 0.05f);
        thrust.AddWave(origin, thrust.pushDirection, 4f, 0.6f, 3.2f, 0.45f, 0.18f);
        thrust.AddWave(origin, thrust.pushDirection, 7f, 0.3f, 1.6f, 0.3f, 0.3f);
        return thrust;
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

        if (target != null && target.gameObject.activeInHierarchy && elapsed <= EmitSeconds)
        {
            // 飛んでいる向きの後ろ側から出す。まだ動いていなければ、決まった瞬間の向き
            Vector3 position = target.position;
            Vector3 moved = deltaTime > 0f ? (position - lastPosition) / deltaTime : Vector3.zero;
            lastPosition = position;
            Vector3 behind = moved.magnitude > MinMoveSpeed ? -moved.normalized : pushDirection;

            emitTimer -= deltaTime;
            if (emitTimer <= 0f)
            {
                emitTimer = EmitInterval;
                float fade = 1f - elapsed / EmitSeconds;
                AddWave(position + behind * (halfSize + 0.05f), behind, 2.5f, 0.35f, 1.1f + 0.4f * fade, 0.3f, 0.22f);
            }
        }

        for (int i = waves.Count - 1; i >= 0; i--)
        {
            Wave wave = waves[i];
            wave.age += deltaTime;
            float t = Mathf.Clamp01(wave.age / wave.life);
            if (t >= 1f || wave.renderer == null)
            {
                if (wave.renderer != null)
                {
                    Destroy(wave.renderer.gameObject);
                }
                waves.RemoveAt(i);
                continue;
            }

            // 最初は速く、だんだんゆっくり流れて広がる
            float eased = 1f - (1f - t) * (1f - t);
            Transform waveTransform = wave.renderer.transform;
            waveTransform.position += wave.velocity * (1f - t) * deltaTime;
            waveTransform.localScale = Vector3.one * Mathf.Lerp(wave.startSize, wave.endSize, eased) * scale;

            block.SetColor(ColorId, new Color(color.r, color.g, color.b, color.a * (1f - t)));
            block.SetFloat(ShapeId, 0f);
            block.SetFloat(ThicknessId, Mathf.Lerp(wave.thickness, 0.04f, t));
            block.SetFloat(FillAlphaId, 0.25f * (1f - t) * (1f - t));
            wave.renderer.SetPropertyBlock(block);
        }

        if (elapsed > EmitSeconds && waves.Count == 0)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        foreach (Wave wave in waves)
        {
            if (wave.renderer != null)
            {
                Destroy(wave.renderer.gameObject);
            }
        }
        waves.Clear();
    }

    /// <summary>輪を1枚足す。板の表（上向き）を <paramref name="direction"/> に向けるので、輪はその向きに対して縦に立つ。</summary>
    private void AddWave(Vector3 position, Vector3 direction, float speed, float startSize, float endSize, float life, float thickness)
    {
        GameObject waveObject = new GameObject("ThrustRing");
        waveObject.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
        waveObject.transform.localScale = Vector3.one * startSize * scale;
        waveObject.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;

        MeshRenderer waveRenderer = waveObject.AddComponent<MeshRenderer>();
        waveRenderer.sharedMaterial = material;
        waveRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        waveRenderer.receiveShadows = false;

        waves.Add(new Wave
        {
            renderer = waveRenderer,
            velocity = direction * speed,
            startSize = startSize,
            endSize = endSize,
            life = life,
            thickness = thickness
        });
    }
}
