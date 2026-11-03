using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **物資のフチを光らせる・物資を白く光らせる（クリティカルプルの仮のエフェクト。案4）。**（2026/11/4・小野田さんの依頼）
///
/// 物資の形（MeshFilter）を少しだけ大きくした写しを、物資の子として作り、`Art/Shaders/SweeperOutline.shader` で塗る。
///
/// | 呼び方 | 見え方 |
/// | --- | --- |
/// | <see cref="SpawnOutline"/> | **金色のフチ取り**（合図）。<c>rise</c> 秒かけてうっすら強くなり、受付の間（<c>hold</c>）はくっきり光る |
/// | <see cref="SpawnFlash"/> | **物資全体が白く光る**（決まったとき）。<c>fade</c> 秒で消える |
///
/// フチは、写しを箱の形に合わせて各軸に同じ厚みだけ大きくして作る（箱の物資ならきれいにそろう。丸い物は少しむらになる）。
/// 光の当たり方や影は付けない。物資が消えたり、<c>keepAlive</c> が false を返したら、すぐ消える。
/// </summary>
public class SweeperItemGlow : MonoBehaviour
{
    private const string ShaderName = "StarSweepers/SweeperOutline";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CullId = Shader.PropertyToID("_Cull");

    private static Material outlineMaterial;
    private static Material flashMaterial;

    /// <summary>写し1つぶん（元の形の中心と大きさを覚えておき、厚みを変えたときに置き直す）。</summary>
    private class Shell
    {
        public Transform transform;
        public MeshRenderer renderer;
        public Vector3 center;
        public Vector3 worldExtents;
    }

    private readonly List<Shell> shells = new List<Shell>();
    private MaterialPropertyBlock block;
    private Color color;
    private float width;
    private float rise;
    private float hold;
    private float fade;
    private bool isFlash;
    private System.Func<bool> keepAlive;
    private float elapsed;

    /// <summary>
    /// **金色のフチ取り**を出す。<paramref name="rise"/> 秒かけてうっすら強くなり、
    /// そこから <paramref name="hold"/> 秒くっきり光って、<paramref name="fade"/> 秒で消える。
    /// </summary>
    public static SweeperItemGlow SpawnOutline(GameObject target, Color color, float width, float rise, float hold, float fade,
        System.Func<bool> keepAlive)
    {
        return Spawn(target, color, width, rise, hold, fade, keepAlive, false);
    }

    /// <summary>**物資全体を白く光らせる。** すぐに明るくなり、<paramref name="fade"/> 秒で消える。</summary>
    public static SweeperItemGlow SpawnFlash(GameObject target, Color color, float fade)
    {
        return Spawn(target, color, 0.015f, 0f, 0f, fade, null, true);
    }

    private static SweeperItemGlow Spawn(GameObject target, Color color, float width, float rise, float hold, float fade,
        System.Func<bool> keepAlive, bool isFlash)
    {
        if (target == null)
        {
            return null;
        }

        SweeperItemGlow glow = target.AddComponent<SweeperItemGlow>();
        glow.color = color;
        glow.width = width;
        glow.rise = Mathf.Max(0f, rise);
        glow.hold = Mathf.Max(0f, hold);
        glow.fade = Mathf.Max(0.01f, fade);
        glow.isFlash = isFlash;
        glow.keepAlive = keepAlive;
        glow.BuildShells(isFlash ? FlashMaterial : OutlineMaterial);
        glow.Apply();
        return glow;
    }

    private static Material OutlineMaterial => outlineMaterial != null ? outlineMaterial : outlineMaterial = MakeMaterial(UnityEngine.Rendering.CullMode.Front);

    private static Material FlashMaterial => flashMaterial != null ? flashMaterial : flashMaterial = MakeMaterial(UnityEngine.Rendering.CullMode.Back);

    /// <summary>
    /// シェーダーの名前からマテリアルを作る。**ビルドではシェーダーが入らないことがある**（リスクリスト参照）。
    /// フチ用と全体用は描く面（Cull）が違うので、別のマテリアルにする（MaterialPropertyBlock では Cull を変えられない）。
    /// </summary>
    private static Material MakeMaterial(UnityEngine.Rendering.CullMode cull)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"シェーダー {ShaderName} が見つかりません。物資のフチの光は出ません。");
            return null;
        }

        Material made = new Material(shader) { name = $"{ShaderName} ({cull}・自動)" };
        made.SetFloat(CullId, (float)cull);
        return made;
    }

    private void BuildShells(Material material)
    {
        if (material == null)
        {
            return;
        }

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer source = filter.GetComponent<MeshRenderer>();
            // 自分たちが作った写し（ほかのフチ・光）や、見えていない部品は写さない
            if (filter.sharedMesh == null || source == null || !source.enabled || filter.name == "GlowShell")
            {
                continue;
            }

            GameObject shellObject = new GameObject("GlowShell");
            shellObject.transform.SetParent(filter.transform, false);
            shellObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer shellRenderer = shellObject.AddComponent<MeshRenderer>();

            // サブメッシュの数だけ同じマテリアルを並べる（全部の面を描く）
            Material[] materials = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }
            shellRenderer.sharedMaterials = materials;
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;

            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 lossy = filter.transform.lossyScale;
            shells.Add(new Shell
            {
                transform = shellObject.transform,
                renderer = shellRenderer,
                center = bounds.center,
                worldExtents = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)))
            });
        }
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
            Destroy(this);
            return;
        }

        elapsed += Time.deltaTime;
        if (elapsed >= rise + hold + fade)
        {
            Destroy(this);
            return;
        }

        Apply();
    }

    private void OnDestroy()
    {
        foreach (Shell shell in shells)
        {
            if (shell.transform != null)
            {
                Destroy(shell.transform.gameObject);
            }
        }
        shells.Clear();
    }

    private void Apply()
    {
        float alpha;
        float thickness = width;

        if (elapsed < rise)
        {
            // うっすら → 少し強く（まだ受付の前）
            float t = elapsed / rise;
            alpha = Mathf.Lerp(0.1f, 0.5f, t * t);
        }
        else if (elapsed < rise + hold)
        {
            // 受付の間：くっきり。始まった瞬間に一度太くなってから戻る
            float t = hold > 0f ? (elapsed - rise) / hold : 1f;
            alpha = 1f;
            thickness = width * Mathf.Lerp(isFlash ? 1f : 1.8f, 1f, t);
        }
        else
        {
            float t = (elapsed - rise - hold) / fade;
            alpha = 1f - t;
        }

        Color shown = new Color(color.r, color.g, color.b, color.a * alpha);
        foreach (Shell shell in shells)
        {
            if (shell.transform == null)
            {
                continue;
            }

            // 各軸とも、外側へ同じ厚み（m）だけ大きくする。中心は元の形の中心のまま
            Vector3 scale = new Vector3(
                Grow(shell.worldExtents.x, thickness),
                Grow(shell.worldExtents.y, thickness),
                Grow(shell.worldExtents.z, thickness));
            shell.transform.localScale = scale;
            shell.transform.localPosition = shell.center - Vector3.Scale(shell.center, scale);

            shell.renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, shown);
            shell.renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>半分の大きさが <paramref name="extent"/> m の形を、外側へ <paramref name="thickness"/> m 厚くする倍率。</summary>
    private static float Grow(float extent, float thickness)
    {
        return extent > 0.001f ? (extent + thickness) / extent : 1f;
    }
}
