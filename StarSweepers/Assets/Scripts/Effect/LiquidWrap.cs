using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **つかんだ物資のまわりを、液体のリムライトで縁取る演出。**
/// <see cref="LiquidBeam"/>（液体の糸）が物資にくっついたときに使う。
///
/// 動き：
/// 1. くっついた所から、液体が物資の表面を**広がっていく**（広がる先は明るく光る）
/// 2. 約 0.5 秒（<see cref="wrapSeconds"/>）で物資全体を包みきり、**ふち（シルエット）が液体の色で光る**。包みきった瞬間に一度強く光り、しぶきが飛ぶ
/// 3. つかんでいる間は、ふちの光と外側の液体の膜が、ゆらゆら流れ続ける
/// 4. はなすと（<see cref="Release"/>）、膜が外へふくらみながら消える
///
/// 見た目は <c>StarSweepers/LiquidRim</c> シェーダー（<c>Assets/Art/Shaders/LiquidRim.shader</c>）。
/// 物資のメッシュを、そのマテリアルで**重ねて描くだけ**なので、物資のマテリアルや子オブジェクトは一切変えない。
///
/// **値はすべてインスペクターで変えられる。** 液体のビームのプレハブ（<c>Assets/Prefabs/Effect/LiquidBeam.prefab</c>）の
/// 子の「LiquidWrap」に付いている。
///
/// 使い方：くっついた瞬間に <see cref="Play"/>、はなしたら <see cref="Release"/>。
/// </summary>
public class LiquidWrap : MonoBehaviour
{
    [Header("素材")]
    [Tooltip("縁取りのマテリアル（シェーダーは StarSweepers/LiquidRim）。空なら実行時に作る（ビルドでは出ないことがあるので入れておく）")]
    [SerializeField] private Material rimMaterial;

    [Tooltip("しぶきのマテリアル（半透明。液体のビームと同じ LiquidAlpha）。空なら実行時に作る")]
    [SerializeField] private Material dropletMaterial;

    [Header("ふちの光（リムライト）")]
    [Tooltip("明るさ。1 より大きいと、ブルーム（光のにじみ）で光る")]
    [SerializeField] private float brightness = 1.6f;

    [Tooltip("ふちの細さ（大きいほど、ふちだけが細く光る）。箱のような平らな面は面ごと光りやすいので、大きめにする")]
    [Range(0.5f, 8f)]
    [SerializeField] private float rimPower = 4f;

    [Tooltip("ふちの光の強さ（物資の表面の、横を向いた所が光る）")]
    [Range(0f, 4f)]
    [SerializeField] private float rimStrength = 0.5f;

    [Tooltip("物資全体にうっすらかかる色の強さ（0 でふちだけ）")]
    [Range(0f, 1f)]
    [SerializeField] private float fillStrength = 0.02f;

    [Tooltip("ふちの光を、物資の表面からどれだけ浮かせるか（メートル）。0 だとちらつくことがある")]
    [Range(0f, 0.05f)]
    [SerializeField] private float shellOffset = 0.005f;

    [Header("外側の液体の膜")]
    [Tooltip("シルエットの外側を囲う膜の厚さ（メートル）。0 で膜なし（ふちの光だけ）")]
    [Range(0f, 0.3f)]
    [SerializeField] private float outlineWidth = 0.05f;

    [Tooltip("膜の濃さ（0 で透明、1 で不透明）")]
    [Range(0f, 1f)]
    [SerializeField] private float outlineAlpha = 0.9f;

    [Tooltip("膜の厚さのゆれ（0 で一定。0.35 なら ±35%）")]
    [Range(0f, 1f)]
    [SerializeField] private float wobble = 0.35f;

    [Tooltip("膜のさらに外にかかる、うっすらした光の幅（メートル）。0 でなし")]
    [Range(0f, 0.5f)]
    [SerializeField] private float glowWidth = 0.12f;

    [Tooltip("うっすらした光の濃さ")]
    [Range(0f, 1f)]
    [SerializeField] private float glowAlpha = 0.25f;

    [Header("液体の模様")]
    [Tooltip("模様が流れる速さ")]
    [SerializeField] private float flowSpeed = 2f;

    [Tooltip("模様の細かさ（大きいほど細かい）")]
    [SerializeField] private float flowScale = 4f;

    [Header("包むまでの動き")]
    [Tooltip("くっついてから、物資全体を包みきるまでの時間（秒）")]
    [SerializeField] private float wrapSeconds = 0.5f;

    [Tooltip("広がる先のぼかし幅（メートル）")]
    [SerializeField] private float edgeSoftness = 0.25f;

    [Tooltip("広がる先の明るさ（0 で光らない）")]
    [SerializeField] private float frontGlow = 1.5f;

    [Tooltip("包みきった瞬間に、一度強く光る強さ（0 で光らない）")]
    [SerializeField] private float closeFlash = 0.8f;

    [Header("はなしたとき")]
    [Tooltip("消えるまでの時間（秒）")]
    [SerializeField] private float burstSeconds = 0.3f;

    [Tooltip("消えながら、膜が外へどれだけふくらむか（メートル）")]
    [SerializeField] private float burstExpand = 0.35f;

    [Header("しぶき（粒）")]
    [Tooltip("くっついた瞬間に飛ぶしぶきの数")]
    [SerializeField] private int contactSplashCount = 10;

    [Tooltip("包みきった瞬間に、くっついた所の反対側から飛ぶしぶきの数")]
    [SerializeField] private int closeSplashCount = 20;

    [Tooltip("はなしたときに、物資のまわりから飛ぶしぶきの数")]
    [SerializeField] private int burstSplashCount = 40;

    [Tooltip("つかんでいる間に落ちるしずくの数（1秒あたり）")]
    [SerializeField] private float dripRate = 6f;

    [Tooltip("しぶきの大きさ（最小・最大）")]
    [SerializeField] private Vector2 dropletSize = new Vector2(0.05f, 0.12f);

    [Tooltip("しぶきが飛び出す速さ（メートル／秒）")]
    [SerializeField] private float dropletSpeed = 2.5f;

    [Tooltip("しぶきが消えるまでの時間（秒。最小・最大）")]
    [SerializeField] private Vector2 dropletLife = new Vector2(0.3f, 0.7f);

    private enum State
    {
        Idle,
        Wrapping,   // くっついた所から広がっている
        Held,       // 包みきって、つかんでいる
        Bursting,   // はなして、消えている
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
    private static readonly int FillStrengthId = Shader.PropertyToID("_FillStrength");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineAlphaId = Shader.PropertyToID("_OutlineAlpha");
    private static readonly int ShellOffsetId = Shader.PropertyToID("_ShellOffset");
    private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
    private static readonly int FlowScaleId = Shader.PropertyToID("_FlowScale");
    private static readonly int WobbleId = Shader.PropertyToID("_Wobble");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int FrontGlowId = Shader.PropertyToID("_FrontGlow");
    private static readonly int ContactId = Shader.PropertyToID("_Contact");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int ExpandId = Shader.PropertyToID("_Expand");

    private State state = State.Idle;
    private float stateTime;
    private float dripCarry;

    private Transform target;
    private readonly List<MeshRenderer> targetRenderers = new List<MeshRenderer>();
    private readonly List<Mesh> targetMeshes = new List<Mesh>();

    private Color color = Color.white;

    /// <summary>くっついた所（物資から見た位置）。物資が動いても回っても、広がる中心がずれないように。</summary>
    private Vector3 contactLocal;

    // 物資の大きさ（毎フレーム測る）
    private Vector3 center;
    private Vector3 contactWorld;
    private float coverDistance;
    private bool hasMeasured;

    private MaterialPropertyBlock block;
    private MaterialPropertyBlock glowBlock;
    private ParticleSystem droplets;

    /// <summary>縁取りが出ている（包み始め〜消え終わるまで）か。</summary>
    public bool IsPlaying => state != State.Idle;

    /// <summary>包みきっているか。</summary>
    public bool IsClosed => state == State.Held;

    /// <summary>
    /// **プレハブに入っていないとき用。** <paramref name="parent"/> の子に作る。
    /// 値は初期値のままになるので、ふだんは液体のビームのプレハブの子に入っている物を使う。
    /// </summary>
    public static LiquidWrap Create(Transform parent, Material droplet)
    {
        var child = new GameObject("LiquidWrap");
        child.transform.SetParent(parent, false);

        LiquidWrap wrap = child.AddComponent<LiquidWrap>();
        wrap.dropletMaterial = droplet;
        return wrap;
    }

    /// <summary>
    /// **物資を包み始める。** くっついた瞬間に呼ぶ。
    /// <paramref name="contact"/> はくっついた所（フックの先）。ここから液体が広がる。
    /// </summary>
    public void Play(Transform newTarget, Vector3 contact, Color newColor)
    {
        if (newTarget == null || !EnsureBuilt())
        {
            return;
        }

        target = newTarget;
        CollectMeshes();

        if (targetMeshes.Count == 0)
        {
            target = null;
            return;
        }

        color = newColor;
        contactLocal = target.InverseTransformPoint(contact);
        stateTime = 0f;
        hasMeasured = false;
        state = State.Wrapping;

        Measure();
        Splash(contactWorld, (contactWorld - center).normalized, contactSplashCount, 0.6f);
    }

    /// <summary>はなす。膜が外へふくらみながら消える。出ていなければ何もしない。</summary>
    public void Release()
    {
        if (state != State.Wrapping && state != State.Held)
        {
            return;
        }

        state = State.Bursting;
        stateTime = 0f;

        if (hasMeasured)
        {
            float radius = coverDistance * 0.5f;

            for (int i = 0; i < burstSplashCount; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                EmitDroplet(center + direction * radius, direction * dropletSpeed * Random.Range(0.6f, 1.2f));
            }
        }
    }

    /// <summary>すぐに消す（はじけさせない）。</summary>
    public void Clear()
    {
        state = State.Idle;
        target = null;
        targetRenderers.Clear();
        targetMeshes.Clear();
    }

    /// <summary>縁取りの色を変える（チームの色が後から決まったときなど）。</summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    // 物資やフックが動いたあとに描く
    private void LateUpdate()
    {
        if (state == State.Idle)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        stateTime += deltaTime;

        // 物資が消えた（ポケットに入ったなど）ら、最後にいた場所でしぶきだけ飛ばして終わる
        if (target == null)
        {
            Release();
            Clear();
            return;
        }

        Measure();

        switch (state)
        {
            case State.Wrapping:
                TickWrapping();
                break;

            case State.Held:
                TickHeld(deltaTime);
                break;

            case State.Bursting:
                TickBursting();
                break;
        }
    }

    private void OnDisable()
    {
        Clear();
    }

    private void TickWrapping()
    {
        float t = Mathf.Clamp01(stateTime / Mathf.Max(0.01f, wrapSeconds));

        if (t >= 1f)
        {
            state = State.Held;
            stateTime = 0f;

            // くっついた所の反対側で、包みきったしぶき
            Vector3 away = (center - contactWorld).normalized;
            Splash(center + away * coverDistance * 0.4f, away, closeSplashCount, 1f);

            TickHeld(0f);
            return;
        }

        // 最初は勢いよく、包みきる直前はゆっくり
        float progress = 1f - (1f - t) * (1f - t);
        Draw(progress * coverDistance, 1f, 0f, 1f);
    }

    private void TickHeld(float deltaTime)
    {
        // 包みきった瞬間に一度強く光って、すっと落ち着く
        float flash = 1f + closeFlash * Mathf.Exp(-stateTime * 8f);
        Draw(coverDistance + edgeSoftness * 2f, 1f, 0f, flash);

        int count = TakeCount(ref dripCarry, dripRate * deltaTime);

        for (int i = 0; i < count; i++)
        {
            Vector3 direction = Random.onUnitSphere;
            EmitDroplet(center + direction * coverDistance * 0.4f, direction * dropletSpeed * 0.15f);
        }
    }

    private void TickBursting()
    {
        float t = Mathf.Clamp01(stateTime / Mathf.Max(0.01f, burstSeconds));

        if (t >= 1f)
        {
            Clear();
            return;
        }

        float expand = burstExpand * (1f - (1f - t) * (1f - t));
        Draw(coverDistance + edgeSoftness * 2f, 1f - t, expand, 1f);
    }

    // ------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------

    /// <summary>
    /// 物資のメッシュを縁取りのマテリアルで重ねて描く（このフレームだけ）。
    /// <paramref name="reveal"/> はくっついた所から広がった距離（メートル）、<paramref name="fade"/> は濃さ、
    /// <paramref name="expand"/> は外へふくらませる量、<paramref name="glow"/> は明るさの倍率。
    /// </summary>
    private void Draw(float reveal, float fade, float expand, float glow)
    {
        float front = state == State.Wrapping ? frontGlow : 0f;

        // 本体：ふちの光と、外側の膜
        FillBlock(block, reveal, fade, expand, glow, rimStrength, fillStrength, outlineWidth, outlineAlpha, front);
        DrawTarget(block);

        // そのさらに外の、うっすらした光（膜だけを、太く薄く重ねる。ふちの光は 0 にして描かない）
        if (glowWidth > 0f && glowAlpha > 0f)
        {
            FillBlock(glowBlock, reveal, fade, expand, glow, 0f, 0f, outlineWidth + glowWidth, outlineAlpha * glowAlpha, 0f);
            DrawTarget(glowBlock);
        }
    }

    /// <summary>シェーダーに渡す値を、1回ぶん入れる。</summary>
    private void FillBlock(MaterialPropertyBlock target, float reveal, float fade, float expand, float glow,
        float rim, float fill, float width, float alpha, float front)
    {
        target.SetColor(ColorId, color * brightness * glow);
        target.SetFloat(RimPowerId, rimPower);
        target.SetFloat(RimStrengthId, rim);
        target.SetFloat(FillStrengthId, fill);
        target.SetFloat(OutlineWidthId, width);
        target.SetFloat(OutlineAlphaId, alpha);
        target.SetFloat(ShellOffsetId, shellOffset);
        target.SetFloat(FlowSpeedId, flowSpeed);
        target.SetFloat(FlowScaleId, flowScale);
        target.SetFloat(WobbleId, wobble);
        target.SetFloat(EdgeSoftnessId, edgeSoftness);
        target.SetFloat(FrontGlowId, front);
        target.SetVector(ContactId, contactWorld);
        target.SetVector(CenterId, center);
        target.SetFloat(RevealId, reveal);
        target.SetFloat(FadeId, fade);
        target.SetFloat(ExpandId, expand);
    }

    /// <summary>物資のメッシュを、渡した値で1回描く。</summary>
    private void DrawTarget(MaterialPropertyBlock properties)
    {
        for (int i = 0; i < targetRenderers.Count; i++)
        {
            MeshRenderer renderer = targetRenderers[i];

            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            var parameters = new RenderParams(rimMaterial)
            {
                matProps = properties,
                layer = renderer.gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };

            Mesh mesh = targetMeshes[i];

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Graphics.RenderMesh(parameters, mesh, sub, renderer.localToWorldMatrix);
            }
        }
    }

    // ------------------------------------------------------------
    // 物資の形と大きさ
    // ------------------------------------------------------------

    /// <summary>物資の見た目（MeshRenderer とそのメッシュ）を集める。</summary>
    private void CollectMeshes()
    {
        targetRenderers.Clear();
        targetMeshes.Clear();

        foreach (MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>())
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();

            if (filter != null && filter.sharedMesh != null)
            {
                targetRenderers.Add(renderer);
                targetMeshes.Add(filter.sharedMesh);
            }
        }
    }

    /// <summary>物資の中心と、くっついた所から物資全体を包むのに要る距離を測る。</summary>
    private void Measure()
    {
        contactWorld = target.TransformPoint(contactLocal);

        bool found = false;
        var bounds = new Bounds(target.position, Vector3.zero);

        foreach (MeshRenderer renderer in targetRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (found)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                found = true;
            }
        }

        center = bounds.center;

        // くっついた所から、いちばん遠い角までの距離
        float farthest = 0f;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                (i & 4) == 0 ? bounds.min.z : bounds.max.z);

            farthest = Mathf.Max(farthest, Vector3.Distance(corner, contactWorld));
        }

        // 外側の膜とぼかしの分も足して、包みきったときに端が残らないようにする
        coverDistance = farthest + outlineWidth + edgeSoftness;
        hasMeasured = true;
    }

    // ------------------------------------------------------------
    // しぶき
    // ------------------------------------------------------------

    /// <summary><paramref name="origin"/> から <paramref name="outward"/> の向きへ、しぶきを散らす。</summary>
    private void Splash(Vector3 origin, Vector3 outward, int count, float strength)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 velocity = (outward * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.6f) * dropletSpeed * strength;
            EmitDroplet(origin + Random.insideUnitSphere * 0.05f, velocity);
        }
    }

    private void EmitDroplet(Vector3 position, Vector3 velocity)
    {
        Color dropColor = Color.Lerp(color, Color.white, 0.2f);
        dropColor.a = 0.9f;

        var parameters = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = Random.Range(dropletSize.x, dropletSize.y),
            startLifetime = Random.Range(dropletLife.x, dropletLife.y),
            startColor = dropColor,
        };

        droplets.Emit(parameters, 1);
    }

    /// <summary>「1フレームに 0.3 個」のような端数を持ち越して、整数の個数にする。</summary>
    private static int TakeCount(ref float carry, float amount)
    {
        carry += amount;
        int count = Mathf.FloorToInt(carry);
        carry -= count;
        return count;
    }

    // ------------------------------------------------------------
    // 部品を作る
    // ------------------------------------------------------------

    /// <summary>マテリアルとしぶきを、初めて使うときに用意する。縁取りのシェーダーが見つからなければ false。</summary>
    private bool EnsureBuilt()
    {
        if (droplets != null)
        {
            return true;
        }

        if (rimMaterial == null)
        {
            Shader shader = Shader.Find("StarSweepers/LiquidRim");

            if (shader == null)
            {
                Debug.LogWarning("[LiquidWrap] 縁取りのシェーダー（StarSweepers/LiquidRim）が見つかりません。Rim Material を入れてください");
                return false;
            }

            rimMaterial = new Material(shader);
        }

        if (dropletMaterial == null)
        {
            dropletMaterial = LiquidBeam.CreateAlphaMaterial();
        }

        block = new MaterialPropertyBlock();
        glowBlock = new MaterialPropertyBlock();
        droplets = CreateDroplets();
        return true;
    }

    /// <summary>しぶきの粒。液体のビームのしぶきと同じ作り（ワールド基準で動き、重力で落ちる）。</summary>
    private ParticleSystem CreateDroplets()
    {
        var child = new GameObject("WrapDroplets");
        child.transform.SetParent(transform, false);

        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.2f;
        main.startSpeed = 0f;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

        ParticleSystem.ColorOverLifetimeModule fadeModule = system.colorOverLifetime;
        fadeModule.enabled = true;
        var fadeOut = new Gradient();
        fadeOut.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fadeModule.color = fadeOut;

        var material = new Material(dropletMaterial);
        material.SetTexture("_BaseMap", LiquidBeam.LiquidTextures.Drop);
        material.SetColor("_BaseColor", Color.white * 1.3f);

        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }
}
