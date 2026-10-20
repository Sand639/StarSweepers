using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **まっすぐ飛ぶ液体の流れと、そのまわりにらせん状に巻きつく水流。**
/// <see cref="HelixBeam"/>（電撃のビーム）の液体版。
///
/// 使い方は <see cref="HelixBeam"/> と同じ：ボタンを押した瞬間に <see cref="BeginCharge"/>、離した瞬間に <see cref="Release"/>。
/// **押している時間が長いほど、遠くまで届く**（<see cref="minLength"/>〜<see cref="maxLength"/>）。
///
/// 電撃との違い：
/// - 形を「作り直す」のではなく、**毎フレーム少しずつ流す**（波が根元から先端へ流れていく）
/// - 流れの太さが場所によってふくらんだり細くなったりする（液体っぽいムラ）
/// - まわりのらせんはギザギザにせず、なめらかな水流が**回りながら先へ流れていく**
/// - しぶき（粒）が先端や流れから飛び散り、重力で落ちる
///
/// **色は途中で変えられる。** <see cref="ChangeColorFromTip"/> を呼ぶと、**先端から根元へ向かって**新しい色に染まっていく
/// （先端に物資がついたときの演出を想定）。すぐ変えたいときは <see cref="SetColor"/>。
///
/// このコンポーネントを付けたオブジェクトの**前方向（青い矢印・Z）へ**飛ぶ。
/// </summary>
public class LiquidBeam : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("半透明（アルファ合成）のマテリアル。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    [Tooltip("液体の色（最初の色）。途中で変えるときは ChangeColorFromTip() / SetColor() を使う")]
    [SerializeField] private Color liquidColor = new Color(0.2f, 0.6f, 1f, 1f);

    [Tooltip("明るさ。1 より大きいと、ブルーム（光のにじみ）で少し光る")]
    [SerializeField] private float brightness = 1.3f;

    [Tooltip("流れの濃さ（0 で透明、1 で不透明）")]
    [Range(0f, 1f)]
    [SerializeField] private float bodyAlpha = 0.75f;

    [Tooltip("流れの太さ")]
    [SerializeField] private float streamWidth = 0.4f;

    [Tooltip("流れの真ん中の、明るい筋の太さ")]
    [SerializeField] private float highlightWidth = 0.07f;

    [Tooltip("先端のふくらみ（流れの太さの何倍か）")]
    [SerializeField] private float headBlobScale = 1.6f;

    [Header("溜め（長押しの時間で距離が決まる）")]
    [Tooltip("ちょっと押しただけのときの距離（メートル）")]
    [SerializeField] private float minLength = 3f;

    [Tooltip("いちばん溜めたときの距離（メートル）")]
    [SerializeField] private float maxLength = 15f;

    [Tooltip("いちばん遠くまで届くのに必要な長押しの時間（秒）")]
    [SerializeField] private float maxChargeSeconds = 1.5f;

    [Header("発射")]
    [Tooltip("流れの先端が進む速さ（メートル／秒）")]
    [SerializeField] private float beamSpeed = 30f;

    [Tooltip("届いてから消え始めるまでの時間（秒）。Hold Until Stopped がオンなら使わない")]
    [SerializeField] private float holdSeconds = 0.6f;

    [Tooltip("オンにすると、Stop() を呼ぶまで出しっぱなしにする（先端に物資をつけて引っぱるときなど）")]
    [SerializeField] private bool holdUntilStopped;

    [Tooltip("消えるまでにかかる時間（秒）。根元から切れて、先端へ向かって流れ去る")]
    [SerializeField] private float fadeSeconds = 0.4f;

    [Header("液体のゆれ")]
    [Tooltip("流れが横にゆれる大きさ（メートル）")]
    [SerializeField] private float wobble = 0.05f;

    [Tooltip("横ゆれの波の長さ（メートル）")]
    [SerializeField] private float wobbleWavelength = 2.2f;

    [Tooltip("波が根元から先端へ流れる速さ（メートル／秒）")]
    [SerializeField] private float flowSpeed = 9f;

    [Tooltip("太さのムラ（0 で一定。0.3 なら ±30%）")]
    [Range(0f, 0.8f)]
    [SerializeField] private float bulge = 0.35f;

    [Tooltip("太さのムラの波の長さ（メートル）")]
    [SerializeField] private float bulgeWavelength = 0.9f;

    [Header("まわりのらせん（水流）")]
    [SerializeField] private int ribbonCount = 8;

    [Tooltip("らせんの半径（流れの中心から）")]
    [SerializeField] private float radius = 0.32f;

    [Tooltip("らせん1本の長さ（メートル。最小・最大）")]
    [SerializeField] private Vector2 ribbonLengthRange = new Vector2(1.5f, 4f);

    [Tooltip("1メートルあたり何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsPerMeter = new Vector2(0.35f, 0.8f);

    [Tooltip("らせん1本が出てから消えるまでの時間（秒。最小・最大）")]
    [SerializeField] private Vector2 ribbonLifeRange = new Vector2(0.3f, 0.7f);

    [SerializeField] private float ribbonWidth = 0.08f;

    [Range(0f, 1f)]
    [SerializeField] private float ribbonAlpha = 0.8f;

    [Tooltip("らせんが先端へ流れていく速さ（メートル／秒）")]
    [SerializeField] private float ribbonFlowSpeed = 3f;

    [Tooltip("らせんが回る速さ（1秒あたりの角度。ラジアン）")]
    [SerializeField] private float spinSpeed = 5f;

    [Header("しぶき（粒）")]
    [Tooltip("流れ1メートルあたり、1秒に落ちるしずくの数")]
    [SerializeField] private float dripsPerMeter = 5f;

    [Tooltip("先端から飛び散るしぶきの数（1秒あたり）")]
    [SerializeField] private float headSplashRate = 80f;

    [Tooltip("しぶきの大きさ（最小・最大）")]
    [SerializeField] private Vector2 dropletSize = new Vector2(0.06f, 0.14f);

    [Tooltip("しぶきが飛び出す速さ（メートル／秒）")]
    [SerializeField] private float dropletSpeed = 3f;

    [Tooltip("しぶきが消えるまでの時間（秒。最小・最大）")]
    [SerializeField] private Vector2 dropletLife = new Vector2(0.35f, 0.8f);

    [SerializeField] private int maxDroplets = 400;

    [Header("色の変わり方")]
    [Tooltip("ChangeColorFromTip() で、先端から根元まで染まりきるのにかかる時間（秒）")]
    [SerializeField] private float colorSpreadSeconds = 0.35f;

    [Header("光")]
    [Tooltip("流れの先端についていくライト（空なら照らさない）。色は液体の色に合わせる")]
    [SerializeField] private Light headLight;

    [SerializeField] private float lightIntensity = 3f;

    private enum State
    {
        Idle,
        Charging,
        Firing,
        Holding,
        Fading,
    }

    /// <summary>流れの芯の点の数。</summary>
    private const int StreamPointCount = 40;

    /// <summary>太さのムラを表す目印の数（先端のふくらみの2つを含む）。</summary>
    private const int WidthKeyCount = 10;

    /// <summary>らせん1本の目印の点の最大数。</summary>
    private const int MaxControlPoints = 16;

    private const int SamplesPerSegment = 5;

    /// <summary>色が切り替わる境目のぼかし幅（流れの長さに対する割合）。</summary>
    private const float ColorEdgeSoftness = 0.12f;

    private State state = State.Idle;
    private float chargeTime;
    private float targetLength;
    private float headDistance;
    private float tailDistance;
    private float stateTimer;
    private float flowTime;

    private Color defaultColor;
    private Color fromColor;
    private Color toColor;

    /// <summary>先端から何割まで新しい色に染まったか（0〜1）。1 なら全部 <see cref="toColor"/>。</summary>
    private float colorSpread = 1f;

    private float headSplashCarry;
    private float dripCarry;

    private LiquidLine body;
    private LiquidLine highlight;
    private LiquidLine preview;
    private LiquidLine nozzleBlob;
    private Ribbon[] ribbons;
    private ParticleSystem droplets;

    private readonly Vector3[] streamPoints = new Vector3[StreamPointCount];
    private readonly Keyframe[] widthKeys = new Keyframe[WidthKeyCount];
    private readonly AnimationCurve widthCurve = new AnimationCurve();

    /// <summary>溜め具合（0〜1）。溜めていないときは 0。</summary>
    public float Charge01 => state == State.Charging ? Mathf.Clamp01(chargeTime / maxChargeSeconds) : 0f;

    /// <summary>溜め中か。</summary>
    public bool IsCharging => state == State.Charging;

    /// <summary>流れが出ている（発射〜消えるまで）か。この間は次の溜めを始められない。</summary>
    public bool IsFiring => state == State.Firing || state == State.Holding || state == State.Fading;

    /// <summary>先端が届ききって、出たままになっているか。</summary>
    public bool IsHolding => state == State.Holding;

    /// <summary>いま溜めを離したら、何メートル飛ぶか。</summary>
    public float CurrentLength => Mathf.Lerp(minLength, maxLength, Charge01);

    /// <summary>流れの先端の位置（ワールド座標）。物資をくっつけるときなどに使う。</summary>
    public Vector3 HeadPosition => transform.TransformPoint(new Vector3(0f, 0f, headDistance));

    /// <summary>液体の色（変えている途中なら、変えた先の色）。</summary>
    public Color LiquidColor => toColor;

    /// <summary>オンなら、<see cref="Stop"/> を呼ぶまで出しっぱなしにする。</summary>
    public bool HoldUntilStopped
    {
        get => holdUntilStopped;
        set => holdUntilStopped = value;
    }

    /// <summary>溜めを始める（ボタンを押した瞬間に呼ぶ）。流れが出ている間は無視する。</summary>
    public void BeginCharge()
    {
        if (state != State.Idle)
        {
            return;
        }

        state = State.Charging;
        chargeTime = 0f;
    }

    /// <summary>流れを撃つ（ボタンを離した瞬間に呼ぶ）。溜めた時間で距離が決まる。</summary>
    public void Release()
    {
        if (state != State.Charging)
        {
            return;
        }

        targetLength = CurrentLength;
        headDistance = 0f;
        tailDistance = 0f;
        state = State.Firing;
        preview.SetVisible(false);
        nozzleBlob.SetVisible(false);
    }

    /// <summary>
    /// 止める。流れが出ていれば消し始め、溜め中なら溜めをやめる。
    /// <see cref="HoldUntilStopped"/> がオンのときは、これを呼ぶまで流れが出たままになる。
    /// </summary>
    public void Stop()
    {
        if (state == State.Charging)
        {
            preview.SetVisible(false);
            nozzleBlob.SetVisible(false);
            state = State.Idle;
            return;
        }

        if (state == State.Firing || state == State.Holding)
        {
            BeginFade();
        }
    }

    /// <summary>
    /// **先端から根元へ向かって、新しい色に染めていく。** 先端に物資がついた瞬間などに呼ぶ。
    /// 流れが出ていないときは、すぐその色になる。
    /// </summary>
    public void ChangeColorFromTip(Color color)
    {
        if (!IsFiring)
        {
            SetColor(color);
            return;
        }

        if (color == toColor)
        {
            return;
        }

        // 染まっている途中でまた変えられたら、半分以上染まっていたほうの色を「元の色」とみなす
        fromColor = colorSpread >= 0.5f ? toColor : fromColor;
        toColor = color;
        colorSpread = 0f;
    }

    /// <summary>すぐに色を変える（全体がいっぺんに変わる）。</summary>
    public void SetColor(Color color)
    {
        fromColor = color;
        toColor = color;
        colorSpread = 1f;
    }

    /// <summary>最初の色（インスペクターの Liquid Color）に戻す。</summary>
    public void ResetColor()
    {
        SetColor(defaultColor);
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    private void Awake()
    {
        if (lineMaterial == null)
        {
            lineMaterial = CreateAlphaMaterial();
        }

        defaultColor = liquidColor;
        SetColor(liquidColor);

        body = new LiquidLine(transform, "Stream", lineMaterial, LiquidTextures.Tube, brightness, 8);
        highlight = new LiquidLine(transform, "StreamHighlight", lineMaterial, LiquidTextures.Soft, brightness * 1.4f, 4);
        preview = new LiquidLine(transform, "ChargePreview", lineMaterial, LiquidTextures.Soft, brightness, 2);
        nozzleBlob = new LiquidLine(transform, "NozzleBlob", lineMaterial, Texture2D.whiteTexture, brightness, 10);

        // 流れの芯と溜めの目安は、根元から先まで同じ太さ（ムラは毎フレーム widthCurve で付ける）
        var flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        preview.SetWidthCurve(flat);
        nozzleBlob.SetWidthCurve(flat);
        highlight.SetWidthCurve(new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0.4f)));

        ribbons = new Ribbon[ribbonCount];

        for (int i = 0; i < ribbonCount; i++)
        {
            ribbons[i] = new Ribbon(this, i);
        }

        droplets = CreateDroplets();

        if (headLight != null)
        {
            headLight.enabled = false;
        }
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (body == null)
        {
            return;
        }

        HideAll();
        state = State.Idle;
    }

    /// <summary>1フレームぶん進める。</summary>
    private void Tick(float deltaTime)
    {
        flowTime += deltaTime;

        if (colorSpread < 1f)
        {
            colorSpread = Mathf.Min(1f, colorSpread + deltaTime / Mathf.Max(0.01f, colorSpreadSeconds));
        }

        switch (state)
        {
            case State.Charging:
                chargeTime += deltaTime;
                DrawCharge();
                break;

            case State.Firing:
                headDistance += beamSpeed * deltaTime;

                if (headDistance >= targetLength)
                {
                    headDistance = targetLength;
                    state = State.Holding;
                    stateTimer = holdSeconds;
                }

                DrawStream(1f, deltaTime);
                EmitHeadSpray(deltaTime, headSplashRate, true);
                break;

            case State.Holding:
                if (!holdUntilStopped)
                {
                    stateTimer -= deltaTime;

                    if (stateTimer <= 0f)
                    {
                        BeginFade();
                        break;
                    }
                }

                DrawStream(1f, deltaTime);
                EmitHeadSpray(deltaTime, headSplashRate * 0.35f, false);
                EmitDrips(deltaTime, dripsPerMeter);
                break;

            case State.Fading:
                stateTimer -= deltaTime;

                if (stateTimer <= 0f)
                {
                    HideAll();
                    state = State.Idle;
                    break;
                }

                // 根元から切れて、残りが先端へ流れ去る
                float fade = stateTimer / fadeSeconds;
                tailDistance = headDistance * (1f - fade);

                DrawStream(Mathf.Sqrt(fade), deltaTime);
                EmitDrips(deltaTime, dripsPerMeter * 8f);
                break;
        }
    }

    private void BeginFade()
    {
        state = State.Fading;
        stateTimer = fadeSeconds;
    }

    // ------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------

    /// <summary>溜め中：口もとに液体の玉がふくらみ、届く距離の目安がうすい線でのびる。</summary>
    private void DrawCharge()
    {
        float charge = Charge01;
        float length = CurrentLength;

        streamPoints[0] = Vector3.zero;
        streamPoints[1] = new Vector3(0f, 0f, length);

        // 溜めきったら点滅して「最大」と分かるようにする
        float flicker = charge >= 1f ? (Mathf.Repeat(flowTime * 8f, 1f) < 0.5f ? 1f : 0.45f) : 1f;

        preview.SetSolidColor(toColor, Mathf.Lerp(0.1f, 0.35f, charge) * flicker);
        preview.SetPoints(streamPoints, 2, streamWidth * 0.2f);

        // 口もとの玉：ごく短い線を丸い端で描くと、丸く見える。ぷるぷる震わせる
        float wobbleScale = 1f + Mathf.Sin(flowTime * 22f) * 0.08f + Mathf.Sin(flowTime * 13f) * 0.05f;
        streamPoints[0] = Vector3.zero;
        streamPoints[1] = new Vector3(0f, 0f, 0.01f);

        nozzleBlob.SetSolidColor(Color.Lerp(toColor, Color.white, 0.15f), bodyAlpha);
        nozzleBlob.SetPoints(streamPoints, 2, streamWidth * Mathf.Lerp(0.4f, 1.5f, charge) * wobbleScale);
    }

    /// <summary>流れ本体と、まわりのらせんを描く。<paramref name="fade"/> は 1（そのまま）〜0（消える）。</summary>
    private void DrawStream(float fade, float deltaTime)
    {
        float tail = tailDistance;
        float head = headDistance;
        float length = head - tail;

        if (length < 0.02f)
        {
            body.SetVisible(false);
            highlight.SetVisible(false);
        }
        else
        {
            for (int i = 0; i < StreamPointCount; i++)
            {
                float z = Mathf.Lerp(tail, head, i / (float)(StreamPointCount - 1));
                streamPoints[i] = StreamCenter(z);
            }

            UpdateWidthCurve(tail, head);
            body.SetWidthCurve(widthCurve);

            body.SetColors(this, tail, head, 0f, bodyAlpha);
            body.SetPoints(streamPoints, StreamPointCount, streamWidth * fade);

            highlight.SetColors(this, tail, head, 0.45f, bodyAlpha * 0.6f);
            highlight.SetPoints(streamPoints, StreamPointCount, highlightWidth * fade);
        }

        for (int i = 0; i < ribbons.Length; i++)
        {
            ribbons[i].Tick(deltaTime, fade);
        }

        if (headLight != null)
        {
            headLight.enabled = true;
            headLight.transform.localPosition = new Vector3(0f, 0f, head);
            headLight.color = ColorAt(1f);
            headLight.intensity = lightIntensity * fade;
        }
    }

    /// <summary>流れの中心線上の点（付けたオブジェクト基準）。波が根元から先端へ流れていく。</summary>
    private Vector3 StreamCenter(float z)
    {
        // 根元は口から離れないよう、ゆれを小さくする
        float envelope = Mathf.Clamp01(z / 1.5f);
        float k = Mathf.PI * 2f / Mathf.Max(0.01f, wobbleWavelength);
        float a = (z - flowTime * flowSpeed) * k;

        float x = Mathf.Sin(a);
        float y = Mathf.Sin(a * 0.7f + 1.9f);

        return new Vector3(x * wobble * envelope, y * wobble * envelope, z);
    }

    /// <summary>その場所の太さの倍率（1 が基準）。2つの波を重ねて、ムラが規則的に見えないようにする。</summary>
    private float WidthAt(float z)
    {
        float k = Mathf.PI * 2f / Mathf.Max(0.01f, bulgeWavelength);
        float a = (z - flowTime * flowSpeed) * k;
        float n = 0.6f * Mathf.Sin(a) + 0.4f * Mathf.Sin(a * 2.3f + 1.1f);

        return 1f + bulge * n;
    }

    /// <summary>流れの太さのムラと、先端のふくらみを widthCurve に入れる。</summary>
    private void UpdateWidthCurve(float tail, float head)
    {
        float length = Mathf.Max(0.01f, head - tail);

        // 先端のふくらみは、流れの長さによらず同じくらいの大きさにする
        float headSpan = Mathf.Clamp(streamWidth * 1.5f / length, 0.02f, 0.3f);
        float uHead = 1f - headSpan;
        int bodyKeys = WidthKeyCount - 2;

        for (int i = 0; i < bodyKeys; i++)
        {
            float u = uHead * i / (bodyKeys - 1);
            float value = WidthAt(tail + u * length);

            // 口もと（根元がまだ切れていないとき）は細くしぼる
            if (i == 0 && tail <= 0.001f)
            {
                value *= 0.55f;
            }

            widthKeys[i] = new Keyframe(u, value);
        }

        widthKeys[bodyKeys] = new Keyframe(1f - headSpan * 0.4f, headBlobScale);
        widthKeys[bodyKeys + 1] = new Keyframe(1f, headBlobScale * 0.75f);

        widthCurve.keys = widthKeys;
    }

    private void HideAll()
    {
        body.SetVisible(false);
        highlight.SetVisible(false);
        preview.SetVisible(false);
        nozzleBlob.SetVisible(false);

        for (int i = 0; i < ribbons.Length; i++)
        {
            ribbons[i].Hide();
        }

        if (headLight != null)
        {
            headLight.enabled = false;
        }

        headDistance = 0f;
        tailDistance = 0f;
    }

    // ------------------------------------------------------------
    // 色
    // ------------------------------------------------------------

    /// <summary>
    /// 流れの上の位置 <paramref name="t"/>（0＝根元、1＝先端）の色。
    /// 色を変えている途中は、先端側が新しい色、根元側が元の色で、境目はぼかす。
    /// </summary>
    private Color ColorAt(float t)
    {
        if (colorSpread >= 1f)
        {
            return toColor;
        }

        GetColorEdge(out float edgeStart, out float edgeEnd);
        return Color.Lerp(fromColor, toColor, Mathf.InverseLerp(edgeStart, edgeEnd, t));
    }

    /// <summary>色の境目（0＝根元、1＝先端）。edgeStart より根元は元の色、edgeEnd より先端は新しい色。</summary>
    private void GetColorEdge(out float edgeStart, out float edgeEnd)
    {
        // 染まり具合 0 で境目が先端の外、1 で根元の外になるように動かす
        float center = Mathf.Lerp(1f + ColorEdgeSoftness, -ColorEdgeSoftness, colorSpread);
        edgeStart = center - ColorEdgeSoftness * 0.5f;
        edgeEnd = center + ColorEdgeSoftness * 0.5f;
    }

    /// <summary>
    /// 流れの <paramref name="fromZ"/>〜<paramref name="toZ"/>（メートル）を描く線の色を、グラデーションに入れる。
    /// 色の境目の前後に目印を置くので、4つの目印で正確に表せる。
    /// </summary>
    private void FillGradient(Gradient gradient, GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys,
        float fromZ, float toZ, float whiten, float alpha)
    {
        float head = Mathf.Max(Mathf.Max(headDistance, toZ), 0.001f);
        float span = Mathf.Max(toZ - fromZ, 0.001f);

        GetColorEdge(out float edgeStart, out float edgeEnd);

        float u1 = Mathf.Clamp((edgeStart * head - fromZ) / span, 0.001f, 0.997f);
        float u2 = Mathf.Clamp((edgeEnd * head - fromZ) / span, u1 + 0.001f, 0.998f);

        SetColorKey(colorKeys, 0, 0f, fromZ, span, head, whiten);
        SetColorKey(colorKeys, 1, u1, fromZ, span, head, whiten);
        SetColorKey(colorKeys, 2, u2, fromZ, span, head, whiten);
        SetColorKey(colorKeys, 3, 1f, fromZ, span, head, whiten);

        alphaKeys[0] = new GradientAlphaKey(alpha, 0f);
        alphaKeys[1] = new GradientAlphaKey(alpha, 1f);

        gradient.SetKeys(colorKeys, alphaKeys);
    }

    private void SetColorKey(GradientColorKey[] keys, int index, float u, float fromZ, float span, float head, float whiten)
    {
        Color color = ColorAt((fromZ + u * span) / head);
        keys[index] = new GradientColorKey(Color.Lerp(color, Color.white, whiten), u);
    }

    // ------------------------------------------------------------
    // しぶき
    // ------------------------------------------------------------

    private ParticleSystem CreateDroplets()
    {
        var child = new GameObject("Droplets");
        child.transform.SetParent(transform, false);

        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 落ちたしずくがキャラクターについてこないよう、ワールド基準で動かす
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.2f;
        main.startSpeed = 0f;
        main.maxParticles = maxDroplets;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        var fadeOut = new Gradient();
        fadeOut.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = fadeOut;

        var material = new Material(lineMaterial);
        material.SetTexture("_BaseMap", LiquidTextures.Drop);
        material.SetColor("_BaseColor", Color.white * brightness);

        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }

    /// <summary>先端からしぶきを飛ばす。<paramref name="forward"/> なら前へ（飛んでいる途中）、そうでなければまわりへ散る。</summary>
    private void EmitHeadSpray(float deltaTime, float rate, bool forward)
    {
        int count = TakeCount(ref headSplashCarry, rate * deltaTime);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = StreamCenter(headDistance) + Random.insideUnitSphere * streamWidth * 0.5f;
            Vector3 velocity = forward
                ? new Vector3(0f, 0f, Random.Range(0.5f, 1.3f) * dropletSpeed) + Random.insideUnitSphere * dropletSpeed * 0.7f
                : Random.onUnitSphere * dropletSpeed * Random.Range(0.3f, 0.8f);

            EmitDroplet(position, velocity, 1f);
        }
    }

    /// <summary>流れのあちこちから、しずくを落とす。</summary>
    private void EmitDrips(float deltaTime, float perMeter)
    {
        float length = headDistance - tailDistance;

        if (length < 0.1f)
        {
            return;
        }

        int count = TakeCount(ref dripCarry, perMeter * length * deltaTime);

        for (int i = 0; i < count; i++)
        {
            float z = Random.Range(tailDistance, headDistance);
            Vector3 position = StreamCenter(z) + Random.insideUnitSphere * streamWidth * 0.4f;
            Vector3 velocity = new Vector3(0f, 0f, flowSpeed * Random.Range(0.05f, 0.2f)) + Random.insideUnitSphere * dropletSpeed * 0.25f;

            EmitDroplet(position, velocity, z / Mathf.Max(0.001f, headDistance));
        }
    }

    private void EmitDroplet(Vector3 localPosition, Vector3 localVelocity, float t)
    {
        Color color = Color.Lerp(ColorAt(t), Color.white, 0.2f);
        color.a = 0.9f;

        var parameters = new ParticleSystem.EmitParams
        {
            position = transform.TransformPoint(localPosition),
            velocity = transform.TransformDirection(localVelocity),
            startSize = Random.Range(dropletSize.x, dropletSize.y),
            startLifetime = Random.Range(dropletLife.x, dropletLife.y),
            startColor = color,
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
    // マテリアル
    // ------------------------------------------------------------

    /// <summary>マテリアルが指定されていないときの予備。URP の Particles/Unlit を半透明（アルファ合成）にする。</summary>
    public static Material CreateAlphaMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        SetupAlpha(material);
        return material;
    }

    /// <summary>URP の Particles/Unlit を「透明・アルファ合成」に設定する。</summary>
    public static void SetupAlpha(Material material)
    {
        material.SetFloat("_Surface", 1f);    // 透明
        material.SetFloat("_Blend", 0f);      // アルファ合成
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", Color.white);
    }

    // ------------------------------------------------------------
    // 流れのまわりに巻きつく水流1本
    // ------------------------------------------------------------

    /// <summary>
    /// 流れのまわりを回りながら、先端へ流れていく水流1本。
    /// 電撃のように作り直すのではなく、出てから消えるまで同じ1本が少しずつ動く。
    /// </summary>
    private class Ribbon
    {
        private readonly LiquidBeam owner;
        private readonly LiquidLine line;

        private readonly Vector3[] controlPoints = new Vector3[MaxControlPoints];
        private readonly Vector3[] points = new Vector3[LightningShape.PointCount(MaxControlPoints, SamplesPerSegment)];

        private bool alive;
        private bool firstSpawn = true;
        private float age;
        private float life;
        private float startZ;
        private float length;
        private float twist;
        private float direction;
        private float phase;
        private float radiusScale;

        public Ribbon(LiquidBeam owner, int index)
        {
            this.owner = owner;
            line = new LiquidLine(owner.transform, $"Ribbon_{index}", owner.lineMaterial, LiquidTextures.Tube, owner.brightness, 4);
        }

        public void Hide()
        {
            line.SetVisible(false);
            alive = false;
            firstSpawn = true;
        }

        public void Tick(float deltaTime, float fade)
        {
            float tail = owner.tailDistance;
            float head = owner.headDistance;

            if (!alive)
            {
                if (head - tail < 0.8f)
                {
                    line.SetVisible(false);
                    return;
                }

                Spawn(tail, head);
            }

            age += deltaTime;

            if (age >= life)
            {
                // 次のフレームで、別の場所に生まれ直す
                alive = false;
                line.SetVisible(false);
                return;
            }

            startZ += owner.ribbonFlowSpeed * deltaTime;
            phase += direction * owner.spinSpeed * deltaTime;

            // 流れの外にはみ出した部分は描かない
            float from = Mathf.Max(startZ, tail);
            float to = Mathf.Min(startZ + length, head);

            if (to - from < 0.2f)
            {
                line.SetVisible(false);
                return;
            }

            int controlCount = Mathf.Clamp(Mathf.CeilToInt((to - from) * twist * 4f) + 1, 4, MaxControlPoints);

            for (int i = 0; i < controlCount; i++)
            {
                float z = Mathf.Lerp(from, to, i / (float)(controlCount - 1));

                // 角度は「生まれた位置からの距離」で決めるので、はみ出しを切ってもらせんがずれない
                float angle = phase + direction * (z - startZ) * twist * Mathf.PI * 2f;
                float r = owner.radius * radiusScale * (1f + 0.15f * Mathf.Sin(z * 3f + owner.flowTime * 4f));

                Vector3 center = owner.StreamCenter(z);
                controlPoints[i] = new Vector3(center.x + Mathf.Cos(angle) * r, center.y + Mathf.Sin(angle) * r, z);
            }

            int count = LightningShape.SampleHermite(controlPoints, controlCount, SamplesPerSegment, points);

            // 出るときと消えるときは細くする
            float envelope = Mathf.Sin(Mathf.PI * age / life);

            line.SetColors(owner, from, to, 0.35f, owner.ribbonAlpha);
            line.SetPoints(points, count, owner.ribbonWidth * envelope * fade);
        }

        private void Spawn(float tail, float head)
        {
            alive = true;
            life = Random.Range(owner.ribbonLifeRange.x, owner.ribbonLifeRange.y);

            // 最初の1回は、全部が同時に出て同時に消えないよう、途中から始める
            age = firstSpawn ? Random.Range(0f, life * 0.6f) : 0f;
            firstSpawn = false;

            length = Mathf.Min(Random.Range(owner.ribbonLengthRange.x, owner.ribbonLengthRange.y), head - tail);
            startZ = Random.Range(tail - length * 0.3f, head - length * 0.8f);
            twist = Random.Range(owner.turnsPerMeter.x, owner.turnsPerMeter.y);
            direction = Random.value < 0.5f ? 1f : -1f;
            phase = Random.Range(0f, Mathf.PI * 2f);
            radiusScale = Random.Range(0.8f, 1.2f);
        }
    }

    // ------------------------------------------------------------
    // 液体の線1本の見た目
    // ------------------------------------------------------------

    /// <summary>
    /// 液体の線1本（LineRenderer）。色は頂点カラーのグラデーションで付けるので、
    /// 1本の線の途中で色を変えられる（先端から染まっていく演出に使う）。
    /// 明るさ（HDR）と質感のテクスチャは MaterialPropertyBlock で渡す。
    /// </summary>
    private class LiquidLine
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private readonly LineRenderer line;
        private readonly Gradient gradient = new Gradient();
        private readonly GradientColorKey[] colorKeys = new GradientColorKey[4];
        private readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];

        public LiquidLine(Transform parent, string name, Material material, Texture texture, float brightness, int capVertices)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = capVertices;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;

            // 両端を細くする（流れ本体は毎フレーム差し替える）
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.25f, 1f),
                new Keyframe(0.75f, 1f),
                new Keyframe(1f, 0.15f));

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, Color.white * brightness);
            block.SetTexture(BaseMapId, texture);
            line.SetPropertyBlock(block);

            line.enabled = false;
        }

        public void SetVisible(bool visible)
        {
            line.enabled = visible;
        }

        public void SetWidthCurve(AnimationCurve curve)
        {
            line.widthCurve = curve;
        }

        /// <summary>流れの <paramref name="fromZ"/>〜<paramref name="toZ"/> の色を付ける。<paramref name="whiten"/> は白に寄せる割合。</summary>
        public void SetColors(LiquidBeam owner, float fromZ, float toZ, float whiten, float alpha)
        {
            owner.FillGradient(gradient, colorKeys, alphaKeys, fromZ, toZ, whiten, alpha);
            line.colorGradient = gradient;
        }

        public void SetSolidColor(Color color, float alpha)
        {
            color.a = alpha;
            line.startColor = color;
            line.endColor = color;
        }

        public void SetPoints(Vector3[] positions, int count, float width)
        {
            line.positionCount = count;

            for (int i = 0; i < count; i++)
            {
                line.SetPosition(i, positions[i]);
            }

            line.widthMultiplier = width;
            line.enabled = true;
        }
    }

    // ------------------------------------------------------------
    // 質感のテクスチャ（実行時に作る。外部の素材は使わない）
    // ------------------------------------------------------------

    private static class LiquidTextures
    {
        private static Texture2D tube;
        private static Texture2D soft;
        private static Texture2D drop;

        /// <summary>
        /// 流れ本体用。線の幅方向（V）に、**ふちが濃く・真ん中がやや透ける**ようにして、筒（チューブ）っぽく見せる。
        /// </summary>
        public static Texture2D Tube => tube != null ? tube : tube = CreateAcross(d =>
            Mathf.Clamp01((1f - d) / 0.25f) * Mathf.Lerp(0.55f, 1f, d * d));

        /// <summary>明るい筋用。真ん中ほど濃い、やわらかい線。</summary>
        public static Texture2D Soft => soft != null ? soft : soft = CreateAcross(d =>
            Mathf.Pow(1f - d, 1.5f));

        /// <summary>しずく用の丸。ふちが少し濃い。</summary>
        public static Texture2D Drop => drop != null ? drop : drop = CreateDrop();

        /// <summary>線の幅方向（V）にだけ変化するテクスチャを作る。d は 0（真ん中）〜1（ふち）。</summary>
        private static Texture2D CreateAcross(System.Func<float, float> alpha)
        {
            const int height = 64;
            var texture = new Texture2D(4, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            for (int y = 0; y < height; y++)
            {
                float d = Mathf.Abs(y / (float)(height - 1) * 2f - 1f);
                var color = new Color(1f, 1f, 1f, alpha(d));

                for (int x = 0; x < 4; x++)
                {
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D CreateDrop()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - r) / 0.3f) * Mathf.Lerp(0.6f, 1f, r * r);

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
