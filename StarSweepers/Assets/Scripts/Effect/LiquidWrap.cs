using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **つかんだ物資のまわりを、液体の枠でぐるっと囲う演出。**
/// <see cref="LiquidBeam"/>（液体の糸）が物資にくっついたときに使う。
///
/// 動き：
/// 1. くっついた所から、液体が**左右の2本に分かれて**物資のふちを回りこむ
/// 2. 約 0.5 秒（<see cref="wrapSeconds"/>）で反対側で合わさり、**枠が閉じる**。外からきゅっと締めつけ、閉じた瞬間にぷるんと揺れて、しぶきが飛ぶ
/// 3. つかんでいる間は、枠が物資についていき、波打ちながら流れ続ける
/// 4. はなすと（<see cref="Release"/>）、枠が外へはじけて消える
///
/// 枠は**いつもカメラのほうを向いた面**に描く（どの角度から見ても枠に見えるように）。
/// 大きさは物資の見た目（Renderer）の大きさに合わせて毎フレーム測り直すので、細長い物にはそれに合った形の枠になる。
///
/// 使い方：<see cref="Create"/> で作り、くっついた瞬間に <see cref="Play"/>、はなしたら <see cref="Release"/>。
/// </summary>
public class LiquidWrap : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("半透明（アルファ合成）のマテリアル。液体のビームと同じものを使う。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    [Tooltip("明るさ。1 より大きいと、ブルーム（光のにじみ）で少し光る")]
    [SerializeField] private float brightness = 1.3f;

    [Tooltip("枠の濃さ（0 で透明、1 で不透明）")]
    [Range(0f, 1f)]
    [SerializeField] private float alpha = 0.8f;

    [Tooltip("枠の太さ（メートル）")]
    [SerializeField] private float width = 0.14f;

    [Tooltip("枠の真ん中の、明るい筋の太さ（メートル）")]
    [SerializeField] private float highlightWidth = 0.035f;

    [Tooltip("枠の形。2 で楕円、大きいほど四角に近づく（角の丸い四角）")]
    [Range(2f, 8f)]
    [SerializeField] private float squareness = 3.5f;

    [Tooltip("物資のふちから、枠までのすき間（メートル）")]
    [SerializeField] private float padding = 0.18f;

    [Header("囲うまでの動き")]
    [Tooltip("くっついてから、枠が閉じるまでの時間（秒）")]
    [SerializeField] private float wrapSeconds = 0.5f;

    [Tooltip("囲い始めの枠の大きさ（何倍か）。1 より大きいと、外から締めつけるように見える")]
    [SerializeField] private float startScale = 1.3f;

    [Tooltip("回りこんでいる液体の、先のふくらみ（太さの何倍か）")]
    [SerializeField] private float tipBlobScale = 1.7f;

    [Tooltip("枠が閉じた瞬間に、ぷるんと縮む強さ（0 で揺れない）")]
    [Range(0f, 0.3f)]
    [SerializeField] private float closeSqueeze = 0.08f;

    [Header("液体のゆれ")]
    [Tooltip("枠が外へ内へ波打つ大きさ（メートル）")]
    [SerializeField] private float wobble = 0.04f;

    [Tooltip("波が枠のまわりを流れる速さ")]
    [SerializeField] private float waveSpeed = 4f;

    [Tooltip("太さのムラ（0 で一定。0.3 なら ±30%）")]
    [Range(0f, 0.8f)]
    [SerializeField] private float bulge = 0.35f;

    [Header("はなしたとき")]
    [Tooltip("はじけて消えるまでの時間（秒）")]
    [SerializeField] private float burstSeconds = 0.3f;

    [Tooltip("はじけたときに、枠がどこまで広がるか（何倍か）")]
    [SerializeField] private float burstScale = 1.5f;

    [Header("しぶき（粒）")]
    [Tooltip("くっついた瞬間に飛ぶしぶきの数")]
    [SerializeField] private int contactSplashCount = 10;

    [Tooltip("枠が閉じた瞬間に、合わさった所から飛ぶしぶきの数")]
    [SerializeField] private int closeSplashCount = 24;

    [Tooltip("はじけたときに、枠のまわりから飛ぶしぶきの数")]
    [SerializeField] private int burstSplashCount = 40;

    [Tooltip("つかんでいる間に、枠から落ちるしずくの数（1秒あたり）")]
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
        Wrapping,   // くっついた所から回りこんでいる
        Held,       // 枠が閉じて、つかんでいる
        Bursting,   // はなして、はじけている
    }

    /// <summary>枠を描く点の数。</summary>
    private const int PointCount = 72;

    /// <summary>太さのムラを表す目印の数。</summary>
    private const int WidthKeyCount = 16;

    private State state = State.Idle;
    private float stateTime;
    private float flowTime;
    private float dripCarry;

    private Transform target;
    private readonly List<Renderer> targetRenderers = new List<Renderer>();
    private readonly List<Collider> targetColliders = new List<Collider>();

    private Color color = Color.white;
    private Vector3 contactPoint;

    /// <summary>回りこみ具合（0〜1）。1 で枠が閉じている。はじけるときは、はなした瞬間の値のまま。</summary>
    private float progress;

    /// <summary>くっついた所の、枠の上での角度（ラジアン）。</summary>
    private float contactAngle;

    // 物資の大きさ（カメラから見た面の上で測る）
    private Vector3 center;
    private Vector2 halfExtents;
    private bool hasMeasured;
    private Vector3 right = Vector3.right;
    private Vector3 up = Vector3.up;

    private Camera viewCamera;
    private LineRenderer body;
    private LineRenderer highlight;
    private ParticleSystem droplets;

    private readonly Vector3[] points = new Vector3[PointCount];
    private readonly Keyframe[] widthKeys = new Keyframe[WidthKeyCount];
    private readonly AnimationCurve widthCurve = new AnimationCurve();
    private readonly Gradient gradient = new Gradient();
    private readonly GradientColorKey[] colorKeys = new GradientColorKey[4];
    private readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];

    /// <summary>枠が出ている（囲い始め〜はじけ終わるまで）か。</summary>
    public bool IsPlaying => state != State.Idle;

    /// <summary>枠が閉じきっているか。</summary>
    public bool IsClosed => state == State.Held;

    /// <summary>
    /// **枠を作る。** <paramref name="parent"/> の子に置く（描く位置はワールド座標なので、親が動いても関係ない）。
    /// <paramref name="material"/> は液体のビームと同じもの（<see cref="LiquidBeam.LineMaterial"/>）を渡す。
    /// </summary>
    public static LiquidWrap Create(Transform parent, Material material)
    {
        var child = new GameObject("LiquidWrap");
        child.transform.SetParent(parent, false);

        LiquidWrap wrap = child.AddComponent<LiquidWrap>();
        wrap.lineMaterial = material;
        return wrap;
    }

    /// <summary>
    /// **物資を囲い始める。** くっついた瞬間に呼ぶ。
    /// <paramref name="contact"/> はくっついた所（フックの先）。ここから液体が左右に分かれて回りこむ。
    /// </summary>
    public void Play(Transform newTarget, Vector3 contact, Color newColor)
    {
        if (newTarget == null)
        {
            return;
        }

        EnsureBuilt();

        target = newTarget;
        CollectTargetParts();

        color = newColor;
        contactPoint = contact;
        progress = 0f;
        stateTime = 0f;
        hasMeasured = false;
        state = State.Wrapping;

        if (Measure())
        {
            contactAngle = AngleOf(contactPoint);
            Splash(contactAngle, contactSplashCount, 0.6f);
        }
    }

    /// <summary>はなす。枠が外へはじけて消える。出ていなければ何もしない。</summary>
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
            for (int i = 0; i < burstSplashCount; i++)
            {
                float angle = contactAngle + Random.Range(-Mathf.PI, Mathf.PI) * progress;
                EmitDroplet(PointAt(angle, 1f), Outward(angle) * dropletSpeed * Random.Range(0.6f, 1.2f));
            }
        }
    }

    /// <summary>すぐに消す（はじけさせない）。</summary>
    public void Clear()
    {
        state = State.Idle;
        target = null;

        if (body != null)
        {
            body.enabled = false;
            highlight.enabled = false;
        }
    }

    /// <summary>
    /// くっついた所（フックの先）を教える。毎フレーム呼んでよい。
    /// 回りこんでいる途中は、液体の分かれ目がここについていく（糸とつながって見えるように）。
    /// </summary>
    public void SetContactPoint(Vector3 contact)
    {
        contactPoint = contact;
    }

    /// <summary>枠の色を変える（チームの色が後から決まったときなど）。</summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    // 液体のビーム（LiquidBeam）と同じく、物資やフックが動いたあとに描く
    private void LateUpdate()
    {
        if (state == State.Idle)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        flowTime += deltaTime;
        stateTime += deltaTime;

        // 物資が消えた（ポケットに入ったなど）ら、最後にいた場所ではじけさせる
        if (target == null && (state == State.Wrapping || state == State.Held))
        {
            Release();
        }

        if (state != State.Bursting && !Measure())
        {
            Clear();
            return;
        }

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
        // 回りこんでいる途中は、分かれ目を糸の先についていかせる
        contactAngle = AngleOf(contactPoint);

        float t = Mathf.Clamp01(stateTime / Mathf.Max(0.01f, wrapSeconds));

        // 最初は勢いよく、合わさる直前はゆっくり
        progress = 1f - (1f - t) * (1f - t);

        if (t >= 1f)
        {
            progress = 1f;
            state = State.Held;
            stateTime = 0f;
            Splash(contactAngle + Mathf.PI, closeSplashCount, 1f);
            DrawClosed(1f, 1f);
            return;
        }

        float scale = Mathf.Lerp(startScale, 1f, progress);
        DrawOpen(scale, 1f, true);
    }

    private void TickHeld(float deltaTime)
    {
        // 閉じた瞬間に、ぷるんと縮んで戻る
        float squeeze = closeSqueeze * Mathf.Sin(stateTime * 22f) * Mathf.Exp(-stateTime * 7f);
        DrawClosed(1f - squeeze, 1f);

        int count = TakeCount(ref dripCarry, dripRate * deltaTime);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            EmitDroplet(PointAt(angle, 1f), Outward(angle) * dropletSpeed * 0.15f);
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

        float scale = Mathf.Lerp(1f, burstScale, 1f - (1f - t) * (1f - t));
        float fade = 1f - t;

        if (progress >= 1f)
        {
            DrawClosed(scale, fade);
        }
        else
        {
            DrawOpen(scale, fade, false);
        }
    }

    // ------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------

    /// <summary>
    /// 回りこんでいる途中の枠。くっついた所から左右へ <see cref="progress"/> の割合だけのびた、開いた線。
    /// <paramref name="anchorContact"/> なら、大きさの倍率をくっついた所では掛けず、反対側ほど強く掛ける
    /// （分かれ目が糸の先から離れず、腕だけが外から回りこんで見える）。
    /// </summary>
    private void DrawOpen(float scale, float fade, bool anchorContact)
    {
        float span = Mathf.PI * progress;

        if (span < 0.02f)
        {
            body.enabled = false;
            highlight.enabled = false;
            return;
        }

        float from = contactAngle - span;

        for (int i = 0; i < PointCount; i++)
        {
            float angle = from + span * 2f * i / (PointCount - 1);
            float pointScale = anchorContact
                ? Mathf.Lerp(1f, scale, (1f - Mathf.Cos(angle - contactAngle)) * 0.5f)
                : scale;

            points[i] = PointAt(angle, pointScale);
        }

        // 先のふくらみは、のびた長さによらず同じくらいの大きさにする
        float arcLength = span * 2f * (halfExtents.x + halfExtents.y + padding * 2f) * 0.5f;
        float tipSpan = Mathf.Clamp(width * 2f / Mathf.Max(0.01f, arcLength), 0.03f, 0.3f);

        int bodyKeys = WidthKeyCount - 4;
        widthKeys[0] = new Keyframe(0f, tipBlobScale * 0.7f);
        widthKeys[1] = new Keyframe(tipSpan * 0.4f, tipBlobScale);

        for (int i = 0; i < bodyKeys; i++)
        {
            float u = Mathf.Lerp(tipSpan, 1f - tipSpan, i / (float)(bodyKeys - 1));
            widthKeys[i + 2] = new Keyframe(u, WidthAt(from + span * 2f * u));
        }

        widthKeys[WidthKeyCount - 2] = new Keyframe(1f - tipSpan * 0.4f, tipBlobScale);
        widthKeys[WidthKeyCount - 1] = new Keyframe(1f, tipBlobScale * 0.7f);
        widthCurve.keys = widthKeys;

        // 回りこんでいる先は、少し白っぽく光らせる
        SetLines(false, Mathf.Lerp(0.6f, 1f, progress) * fade, fade, 0.25f);
    }

    /// <summary>閉じた枠（ぐるっと1周）。</summary>
    private void DrawClosed(float scale, float fade)
    {
        // 合わさった所（くっついた所の反対側）から1周する
        float from = contactAngle + Mathf.PI;

        for (int i = 0; i < PointCount; i++)
        {
            points[i] = PointAt(from + Mathf.PI * 2f * i / PointCount, scale);
        }

        for (int i = 0; i < WidthKeyCount; i++)
        {
            float u = i / (float)(WidthKeyCount - 1);
            widthKeys[i] = new Keyframe(u, WidthAt(from + Mathf.PI * 2f * u));
        }

        widthCurve.keys = widthKeys;

        SetLines(true, fade, fade, 0f);
    }

    /// <summary>点・太さ・色を、本体と明るい筋の2本に入れる。</summary>
    private void SetLines(bool loop, float widthScale, float alphaScale, float tipWhiten)
    {
        // 本体だけ太さのムラをつける。明るい筋は一定の太さ
        body.widthCurve = widthCurve;

        SetLine(body, loop, width * widthScale, 0f, alpha * alphaScale, tipWhiten);
        SetLine(highlight, loop, highlightWidth * widthScale, 0.45f, alpha * 0.6f * alphaScale, tipWhiten);
    }

    /// <summary>線1本に点と色を入れる。<paramref name="whiten"/> は白に寄せる割合、<paramref name="tipWhiten"/> は両端だけさらに寄せる割合。</summary>
    private void SetLine(LineRenderer line, bool loop, float lineWidth, float whiten, float lineAlpha, float tipWhiten)
    {
        line.loop = loop;
        line.positionCount = PointCount;
        line.SetPositions(points);
        line.widthMultiplier = lineWidth;

        Color main = Color.Lerp(color, Color.white, whiten);
        Color tip = Color.Lerp(main, Color.white, tipWhiten);

        colorKeys[0] = new GradientColorKey(tip, 0f);
        colorKeys[1] = new GradientColorKey(main, 0.1f);
        colorKeys[2] = new GradientColorKey(main, 0.9f);
        colorKeys[3] = new GradientColorKey(tip, 1f);

        alphaKeys[0] = new GradientAlphaKey(lineAlpha, 0f);
        alphaKeys[1] = new GradientAlphaKey(lineAlpha, 1f);

        gradient.SetKeys(colorKeys, alphaKeys);
        line.colorGradient = gradient;
        line.enabled = true;
    }

    /// <summary>
    /// 枠の上の点（ワールド座標）。<paramref name="angle"/> は枠の上の角度、<paramref name="scale"/> は枠の大きさの倍率。
    /// 「角の丸い四角」（スーパー楕円）に、流れていく波をのせる。
    /// </summary>
    private Vector3 PointAt(float angle, float scale)
    {
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float e = 2f / squareness;

        var p = new Vector2(
            Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e) * (halfExtents.x + padding),
            Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e) * (halfExtents.y + padding));

        // 波は角度の整数倍で作るので、1周してもつなぎ目がずれない
        float wave = 0.6f * Mathf.Sin(angle * 3f - flowTime * waveSpeed)
                   + 0.4f * Mathf.Sin(angle * 5f + flowTime * waveSpeed * 0.7f + 1.3f);

        p = p * scale + p.normalized * wobble * wave;

        return center + right * p.x + up * p.y;
    }

    /// <summary>その角度の、枠の外向き（ワールド座標）。</summary>
    private Vector3 Outward(float angle)
    {
        return (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;
    }

    /// <summary>その場所の太さの倍率（1 が基準）。波は枠のまわりを流れていく。</summary>
    private float WidthAt(float angle)
    {
        float n = 0.6f * Mathf.Sin(angle * 4f - flowTime * waveSpeed * 1.5f)
                + 0.4f * Mathf.Sin(angle * 7f + flowTime * waveSpeed + 1.1f);

        return 1f + bulge * n;
    }

    /// <summary>ワールド座標の点が、枠の上で何度（ラジアン）の所にあたるか。<see cref="PointAt"/> の逆。</summary>
    private float AngleOf(Vector3 world)
    {
        Vector3 offset = world - center;
        float x = Vector3.Dot(offset, right) / Mathf.Max(0.01f, halfExtents.x + padding);
        float y = Vector3.Dot(offset, up) / Mathf.Max(0.01f, halfExtents.y + padding);
        float inverse = squareness / 2f;

        return Mathf.Atan2(
            Mathf.Sign(y) * Mathf.Pow(Mathf.Abs(y), inverse),
            Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), inverse));
    }

    // ------------------------------------------------------------
    // 物資の大きさを測る
    // ------------------------------------------------------------

    /// <summary>物資の見た目の部品（Renderer）と当たり判定を集める。エフェクト（線・粒）は除く。</summary>
    private void CollectTargetParts()
    {
        targetRenderers.Clear();
        targetColliders.Clear();

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is SpriteRenderer)
            {
                targetRenderers.Add(renderer);
            }
        }

        target.GetComponentsInChildren(targetColliders);
    }

    /// <summary>
    /// カメラから見た面の上で、物資の中心と大きさを測る。測れなければ false。
    /// 物資が回転しても枠ががたつかないよう、大きさは少しなめらかに追いかける。
    /// </summary>
    private bool Measure()
    {
        if (target == null)
        {
            return hasMeasured;
        }

        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
        {
            viewCamera = Camera.main;
        }

        Vector3 forward;

        if (viewCamera != null)
        {
            right = viewCamera.transform.right;
            up = viewCamera.transform.up;
            forward = viewCamera.transform.forward;
        }
        else
        {
            // カメラが無いときは、真上から見ているとみなす
            right = Vector3.right;
            up = Vector3.forward;
            forward = Vector3.down;
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool found = false;

        foreach (Renderer renderer in targetRenderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            // 物の向きに沿った箱で測る（ワールドの箱だと、回転するたびに大きさが変わってしまう）
            Bounds local = renderer.localBounds;
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);

                Encapsulate(matrix.MultiplyPoint3x4(corner), forward, ref min, ref max);
            }

            found = true;
        }

        // 見た目が無い物は、当たり判定の箱で測る
        if (!found)
        {
            foreach (Collider collider in targetColliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                    Encapsulate(corner, forward, ref min, ref max);
                }

                found = true;
            }
        }

        if (!found)
        {
            min = new Vector3(Vector3.Dot(target.position, right) - 0.5f, Vector3.Dot(target.position, up) - 0.5f, Vector3.Dot(target.position, forward) - 0.5f);
            max = min + Vector3.one;
        }

        // 枠は物資の手前（カメラ側）の面に置く。真ん中だと、物資の手前側に隠れてしまうことがあるため
        Vector3 mid = (min + max) * 0.5f;
        center = right * mid.x + up * mid.y + forward * min.z;

        var measured = new Vector2((max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f);
        halfExtents = hasMeasured ? Vector2.Lerp(halfExtents, measured, 1f - Mathf.Exp(-20f * Time.deltaTime)) : measured;
        hasMeasured = true;
        return true;
    }

    private void Encapsulate(Vector3 world, Vector3 forward, ref Vector3 min, ref Vector3 max)
    {
        var p = new Vector3(Vector3.Dot(world, right), Vector3.Dot(world, up), Vector3.Dot(world, forward));
        min = Vector3.Min(min, p);
        max = Vector3.Max(max, p);
    }

    // ------------------------------------------------------------
    // しぶき
    // ------------------------------------------------------------

    /// <summary>枠の上の <paramref name="angle"/> の所から、しぶきを外へ散らす。</summary>
    private void Splash(float angle, int count, float strength)
    {
        Vector3 origin = PointAt(angle, 1f);
        Vector3 outward = Outward(angle);

        for (int i = 0; i < count; i++)
        {
            Vector3 velocity = (outward * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.6f) * dropletSpeed * strength;
            EmitDroplet(origin + Random.insideUnitSphere * width * 0.5f, velocity);
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

    /// <summary>線としぶきを、初めて使うときに作る（Create() でマテリアルを受け取ってから作るため）。</summary>
    private void EnsureBuilt()
    {
        if (body != null)
        {
            return;
        }

        if (lineMaterial == null)
        {
            lineMaterial = LiquidBeam.CreateAlphaMaterial();
        }

        body = CreateLine("WrapBody", LiquidBeam.LiquidTextures.Tube);
        highlight = CreateLine("WrapHighlight", LiquidBeam.LiquidTextures.Soft);
        highlight.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        droplets = CreateDroplets();
    }

    private LineRenderer CreateLine(string lineName, Texture texture)
    {
        var child = new GameObject(lineName);
        child.transform.SetParent(transform, false);

        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = lineMaterial;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 8;
        line.numCornerVertices = 2;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", Color.white * brightness);
        block.SetTexture("_BaseMap", texture);
        line.SetPropertyBlock(block);

        line.enabled = false;
        return line;
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

        var material = new Material(lineMaterial);
        material.SetTexture("_BaseMap", LiquidBeam.LiquidTextures.Drop);
        material.SetColor("_BaseColor", Color.white * brightness);

        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }
}
