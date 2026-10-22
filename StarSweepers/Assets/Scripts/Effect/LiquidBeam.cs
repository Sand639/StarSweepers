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
    [Tooltip("らせん（水流）の本数。始まったとき（Awake）に作るので、再生中に変えても反映されない")]
    [SerializeField] private int ribbonCount = 8;

    [Tooltip("らせんの半径（流れの中心から）")]
    [SerializeField] private float radius = 0.32f;

    [Tooltip("らせん1本の長さ（メートル。最小・最大）")]
    [SerializeField] private Vector2 ribbonLengthRange = new Vector2(1.5f, 4f);

    [Tooltip("1メートルあたり何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsPerMeter = new Vector2(0.35f, 0.8f);

    [Tooltip("らせん1本が出てから消えるまでの時間（秒。最小・最大）")]
    [SerializeField] private Vector2 ribbonLifeRange = new Vector2(0.3f, 0.7f);

    [Tooltip("らせん1本の太さ（メートル）。出るときと消えるときは、これより細くなる")]
    [SerializeField] private float ribbonWidth = 0.08f;

    [Tooltip("らせんの濃さ（0 で透明、1 で不透明）")]
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

    [Tooltip("しぶきの粒を同時に出せる最大数。超えたぶんは出ない（重くなりすぎないように）。Awake で決まり、再生中に変えても反映されない")]
    [SerializeField] private int maxDroplets = 400;

    [Header("色の変わり方")]
    [Tooltip("ChangeColorFromTip() で、先端から根元まで染まりきるのにかかる時間（秒）")]
    [SerializeField] private float colorSpreadSeconds = 0.35f;

    [Header("光")]
    [Tooltip("流れの先端についていくライト（空なら照らさない）。色は液体の色に合わせる")]
    [SerializeField] private Light headLight;

    [Tooltip("先端のライトの明るさ。消えていくときは、流れと一緒にだんだん弱くなる")]
    [SerializeField] private float lightIntensity = 3f;

    [Tooltip("先端のライトを使うか。オフにすると、Head Light が入っていても照らさない")]
    [SerializeField] private bool useHeadLight = true;

    /// <summary>
    /// **すべての液体のビームのライトを、まとめてオン／オフする**（デバッグ用。初期値はオン）。
    /// 例：<c>LiquidBeam.LightsEnabled = false;</c> で、いま出ている物も含めて全部のライトが消える。
    /// 1本ずつ切り替えたいときは <see cref="UseHeadLight"/>。
    /// </summary>
    public static bool LightsEnabled { get; set; } = true;

    /// <summary>このビームの先端のライトを使うか。<see cref="LightsEnabled"/> がオフなら、こちらがオンでも照らさない。</summary>
    public bool UseHeadLight
    {
        get => useHeadLight;
        set => useHeadLight = value;
    }

    /// <summary>いまライトを点けてよいか。</summary>
    private bool LightOn => headLight != null && useHeadLight && LightsEnabled;

    /// <summary>
    /// ビームの状態。ふつうは 待機 → 溜め → 発射 → 出たまま → 消える → 待機 と進む。
    /// Follow() を呼ばれている間だけは「外から両端を決めてもらう」状態になる。
    /// </summary>
    private enum State
    {
        /// <summary>何も出ていない。BeginCharge() または Follow() を待っている。</summary>
        Idle,
        /// <summary>ボタンを押している間。口もとに玉がふくらみ、届く距離の目安が出る。</summary>
        Charging,
        /// <summary>先端が目標の距離へ向かって進んでいる途中。</summary>
        Firing,
        /// <summary>先端が届いて、出たままになっている（holdSeconds 秒、または Stop() まで）。</summary>
        Holding,
        /// <summary>根元から切れて、先端へ流れ去りながら消えていく途中。</summary>
        Fading,
        Following,  // 外から両端を決めてもらっている（フックの糸の代わり。Follow()）
    }

    /// <summary>流れの芯の点の数。</summary>
    private const int StreamPointCount = 40;

    /// <summary>太さのムラを表す目印の数（先端のふくらみの2つを含む）。</summary>
    private const int WidthKeyCount = 10;

    /// <summary>らせん1本の目印の点の最大数。</summary>
    private const int MaxControlPoints = 16;

    /// <summary>らせんの目印の点と点のあいだを、何個の点でなめらかにつなぐか。多いほどなめらかだが重い。</summary>
    private const int SamplesPerSegment = 5;

    /// <summary>色が切り替わる境目のぼかし幅（流れの長さに対する割合）。</summary>
    private const float ColorEdgeSoftness = 0.12f;

    private State state = State.Idle;

    /// <summary>溜めを始めてから押し続けている時間（秒）。</summary>
    private float chargeTime;

    /// <summary>今回の発射で先端が届く距離（メートル）。離した瞬間の溜め具合で決まる。</summary>
    private float targetLength;

    /// <summary>根元（このオブジェクトの位置）から先端までの距離（メートル・前方向）。</summary>
    private float headDistance;

    /// <summary>根元から、流れの切れ目（いちばん根元側の端）までの距離（メートル）。消えるときに先端へ向かって増える。</summary>
    private float tailDistance;

    /// <summary>「出たまま」「消える」の残り時間（秒）。</summary>
    private float stateTimer;

    /// <summary>始まってからの経過時間（秒）。波やゆれを流すのに使う。</summary>
    private float flowTime;

    /// <summary>インスペクターで決めた最初の色。ResetColor() で戻す先。</summary>
    private Color defaultColor;

    /// <summary>色を変えている途中の、元の色（根元側に残っている色）。</summary>
    private Color fromColor;

    /// <summary>色を変えている途中の、新しい色（先端から染まっていく色）。変えていなければ今の色。</summary>
    private Color toColor;

    /// <summary>先端から何割まで新しい色に染まったか（0〜1）。1 なら全部 <see cref="toColor"/>。</summary>
    private float colorSpread = 1f;

    /// <summary>先端のしぶき・流れのしずくの「1個に満たない端数」。次のフレームに持ち越す（<see cref="TakeCount"/>）。</summary>
    private float headSplashCarry;
    private float dripCarry;

    /// <summary>Follow() で、先端が前のフレームより先へ伸びたか（しぶきを前へ飛ばすかどうか）。</summary>
    private bool followGrowing;

    // 見た目の部品。どれも Awake で子オブジェクトとして作る
    private LiquidLine body;        // 流れ本体
    private LiquidLine highlight;   // 流れの真ん中の明るい筋
    private LiquidLine preview;     // 溜め中の、届く距離の目安（うすい線）
    private LiquidLine nozzleBlob;  // 溜め中に口もとでふくらむ玉
    private Ribbon[] ribbons;       // まわりに巻きつくらせん
    private ParticleSystem droplets; // しぶき・しずく

    private readonly Vector3[] streamPoints = new Vector3[StreamPointCount];
    private readonly Keyframe[] widthKeys = new Keyframe[WidthKeyCount];
    private readonly AnimationCurve widthCurve = new AnimationCurve();

    /// <summary>溜め具合（0〜1）。溜めていないときは 0。</summary>
    public float Charge01 => state == State.Charging ? Mathf.Clamp01(chargeTime / maxChargeSeconds) : 0f;

    /// <summary>溜め中か。</summary>
    public bool IsCharging => state == State.Charging;

    /// <summary>流れが出ている（発射〜消えるまで）か。この間は次の溜めを始められない。</summary>
    public bool IsFiring => state == State.Firing || state == State.Holding || state == State.Fading || state == State.Following;

    /// <summary>先端が届ききって、出たままになっているか。</summary>
    public bool IsHolding => state == State.Holding;

    /// <summary>いま溜めを離したら、何メートル飛ぶか。</summary>
    public float CurrentLength => Mathf.Lerp(minLength, maxLength, Charge01);

    /// <summary>流れの先端の位置（ワールド座標）。物資をくっつけるときなどに使う。</summary>
    public Vector3 HeadPosition => transform.TransformPoint(new Vector3(0f, 0f, headDistance));

    /// <summary>液体の色（変えている途中なら、変えた先の色）。</summary>
    public Color LiquidColor => toColor;

    /// <summary>流れを描いているマテリアル。<see cref="LiquidWrap"/>（物資を囲う枠）に同じものを使ってもらう。</summary>
    public Material LineMaterial => lineMaterial;

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

        if (state == State.Firing || state == State.Holding || state == State.Following)
        {
            BeginFade();
        }
    }

    /// <summary>
    /// **両端を外から決めて、流れを出し続ける**（フックの糸の代わりに使うとき。<c>HookLine</c> が毎フレーム呼ぶ）。
    /// 溜めや距離の計算はせず、<paramref name="start"/>（根元）から <paramref name="end"/>（先端）まで流れを描く。
    /// やめるときは <see cref="Stop"/>（根元から切れて消える）。
    /// </summary>
    public void Follow(Vector3 start, Vector3 end)
    {
        Vector3 toEnd = end - start;
        float length = toEnd.magnitude;

        transform.position = start;

        if (length > 0.001f)
        {
            Vector3 direction = toEnd / length;

            // 真上・真下を向くとき（物資を頭の上へ引き抜くときなど）は、上の向きの基準を変える
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            transform.rotation = Quaternion.LookRotation(direction, up);
        }

        if (state != State.Following)
        {
            preview.SetVisible(false);
            nozzleBlob.SetVisible(false);
            state = State.Following;
            headDistance = 0f;
        }

        followGrowing = length > headDistance + 0.001f;
        headDistance = length;
        tailDistance = 0f;
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

    // LateUpdate にしているのは、Follow() で両端を決めてもらったあとに描くため
    // （Update だと、呼ばれる順番しだいで先端が1フレーム遅れる）
    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// 無効にされたら、出ている物をすべて隠して待機に戻す（次に有効にしたとき、途中の状態から始まらないように）。
    /// Awake より前に呼ばれた場合（部品がまだ無い）は何もしない。
    /// </summary>
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

                // 届いたあとは、先端のしぶきを飛んでいる途中の 35% に減らし、まわりへ散らす
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

                // 太さは平方根を使って細くなるのを遅らせ、消える直前まで見えるようにする。
                // 切れた流れからは、しずくをふだんの8倍こぼす
                DrawStream(Mathf.Sqrt(fade), deltaTime);
                EmitDrips(deltaTime, dripsPerMeter * 8f);
                break;

            case State.Following:
                // 先端が伸びているときは前へ勢いよく、止まっているときは控えめにまわりへ散らす
                DrawStream(1f, deltaTime);
                EmitHeadSpray(deltaTime, followGrowing ? headSplashRate : headSplashRate * 0.35f, followGrowing);
                EmitDrips(deltaTime, dripsPerMeter);
                break;
        }
    }

    /// <summary>消え始める。fadeSeconds 秒かけて根元から切れていき、終わったら待機に戻る。</summary>
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

        // 溜めきったら点滅して「最大」と分かるようにする（1秒に8回、明るさ 100% と 45% を行き来する）
        float flicker = charge >= 1f ? (Mathf.Repeat(flowTime * 8f, 1f) < 0.5f ? 1f : 0.45f) : 1f;

        preview.SetSolidColor(toColor, Mathf.Lerp(0.1f, 0.35f, charge) * flicker);
        preview.SetPoints(streamPoints, 2, streamWidth * 0.2f);

        // 口もとの玉：ごく短い線を丸い端で描くと、丸く見える。ぷるぷる震わせる
        // （速さの違う2つの揺れを重ねて、規則的に見えないようにする。大きさは ±13% ほど）
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

        if (LightOn)
        {
            headLight.enabled = true;
            headLight.transform.localPosition = new Vector3(0f, 0f, head);
            headLight.color = ColorAt(1f);
            headLight.intensity = lightIntensity * fade;
        }
        else if (headLight != null)
        {
            // 途中でオフにされたときも、すぐ消えるように
            headLight.enabled = false;
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

        // 残りの2つで先端のふくらみを作る（先端の少し手前がいちばん太く、先端でやや細くなる）
        widthKeys[bodyKeys] = new Keyframe(1f - headSpan * 0.4f, headBlobScale);
        widthKeys[bodyKeys + 1] = new Keyframe(1f, headBlobScale * 0.75f);

        widthCurve.keys = widthKeys;
    }

    /// <summary>流れ・らせん・溜めの表示・ライトをすべて隠し、長さを 0 に戻す（しぶきの粒は自然に消えるのを待つ）。</summary>
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

    /// <summary>
    /// 線の上の位置 <paramref name="u"/>（0〜1）にあたる流れの場所の色を、グラデーションの目印にする。
    /// 線が流れの一部（らせんなど）でも、流れ全体での位置に直してから色を決めるので、本体と色がそろう。
    /// </summary>
    private void SetColorKey(GradientColorKey[] keys, int index, float u, float fromZ, float span, float head, float whiten)
    {
        Color color = ColorAt((fromZ + u * span) / head);
        keys[index] = new GradientColorKey(Color.Lerp(color, Color.white, whiten), u);
    }

    // ------------------------------------------------------------
    // しぶき
    // ------------------------------------------------------------

    /// <summary>
    /// しぶき用のパーティクルを子として作る。
    /// 自動では出さず（Emission はオフ）、<see cref="EmitDroplet"/> で1粒ずつ位置・速さ・色を決めて出す。
    /// 重力で落ち、だんだん小さく・薄くなって消える。
    /// </summary>
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

    /// <summary>
    /// しぶきを1粒出す。位置と速さはこのオブジェクト基準で受け取り、ワールドに直して出す。
    /// <paramref name="t"/> は流れの上の位置（0＝根元、1＝先端）で、その場所の液体の色を少し白っぽくして使う。
    /// </summary>
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

        /// <summary>いま出ているか。false なら、次のフレームで別の場所に生まれ直す。</summary>
        private bool alive;

        /// <summary>流れが出てから最初の1本目か（全部が同時に出て同時に消えないよう、最初だけ途中から始める）。</summary>
        private bool firstSpawn = true;

        private float age;          // 生まれてからの時間（秒）
        private float life;         // 消えるまでの時間（秒）
        private float startZ;       // らせんの根元側の端の位置（根元からのメートル）。先端へ流れていく
        private float length;       // らせん1本の長さ（メートル）
        private float twist;        // 1メートルあたり何周回るか
        private float direction;    // 回る向き（1 か -1）
        private float phase;        // らせんの回りはじめの角度（ラジアン）。時間とともに回る
        private float radiusScale;  // 半径のばらつき（0.8〜1.2 倍）。全部が同じ太さの筒に見えないように

        public Ribbon(LiquidBeam owner, int index)
        {
            this.owner = owner;
            line = new LiquidLine(owner.transform, $"Ribbon_{index}", owner.lineMaterial, LiquidTextures.Tube, owner.brightness, 4);
        }

        /// <summary>隠して、次に流れが出たときに「最初の1本目」から始め直す。</summary>
        public void Hide()
        {
            line.SetVisible(false);
            alive = false;
            firstSpawn = true;
        }

        /// <summary>
        /// 1フレームぶん、先端へ流しながら回して描き直す。寿命が来たら消え、次のフレームで別の場所に生まれ直す。
        /// <paramref name="fade"/> は流れ本体と同じ消え具合（1＝そのまま、0＝消える）。
        /// </summary>
        public void Tick(float deltaTime, float fade)
        {
            float tail = owner.tailDistance;
            float head = owner.headDistance;

            if (!alive)
            {
                // 流れが短すぎる（0.8m 未満）うちは、巻きつく余地が無いので出さない
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

            // 目印の点は、1周あたり4つ置く（少ないと、らせんが角ばって見える）。4〜MaxControlPoints の間に収める
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

            // 目印の点のあいだを、電撃のビームと同じ道具でなめらかな曲線につなぐ
            int count = LightningShape.SampleHermite(controlPoints, controlCount, SamplesPerSegment, points);

            // 出るときと消えるときは細くする
            float envelope = Mathf.Sin(Mathf.PI * age / life);

            line.SetColors(owner, from, to, 0.35f, owner.ribbonAlpha);
            line.SetPoints(points, count, owner.ribbonWidth * envelope * fade);
        }

        /// <summary>流れの上のどこかに、長さ・回り方・向き・寿命をばらつかせて新しく生まれる。</summary>
        private void Spawn(float tail, float head)
        {
            alive = true;
            life = Random.Range(owner.ribbonLifeRange.x, owner.ribbonLifeRange.y);

            // 最初の1回は、全部が同時に出て同時に消えないよう、途中から始める
            age = firstSpawn ? Random.Range(0f, life * 0.6f) : 0f;
            firstSpawn = false;

            // 流れより長くはしない。根元側は少しはみ出してもよい（はみ出した所は描かない）ので、
            // 根元のすぐそばからも巻きついて見える
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

        /// <summary>
        /// 子オブジェクトに LineRenderer を作る。最初は隠しておく。
        /// <paramref name="capVertices"/> は線の端を丸めるための頂点の数（多いほど丸い。口もとの玉は 10 で丸く見せている）。
        /// </summary>
        public LiquidLine(Transform parent, string name, Material material, Texture texture, float brightness, int capVertices)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            line = child.AddComponent<LineRenderer>();
            // 点はビームのオブジェクト基準で渡す（ビームを動かせば線もそのまま付いてくる）。
            // 線はいつもカメラの方を向く平たい帯で描く
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

        /// <summary>線全体を1色にする（溜め中の表示用。色の変わり目は付けない）。</summary>
        public void SetSolidColor(Color color, float alpha)
        {
            color.a = alpha;
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>
        /// 線の点を、配列の先頭 <paramref name="count"/> 個で入れ替え、太さ（<paramref name="width"/>、太さの形に掛ける倍率）を決めて表示する。
        /// 配列は使い回すので、count より後ろの値は使わない。
        /// </summary>
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

    /// <summary>
    /// 液体の見た目に使うテクスチャ。初めて使われたときに1回だけ作り、すべてのビームで使い回す。
    /// <see cref="LiquidWrap"/>（つかんだ物資の縁取り）でも同じものを使う。
    /// </summary>
    internal static class LiquidTextures
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
            // 長さ方向は変化しないので幅は 4 ピクセルで足りる。幅方向（V）だけ 64 段で細かく作る
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

        /// <summary>32×32 ピクセルの丸。中心からの距離 r（0〜1）で、ふちの手前が濃く、外側（r が 1 に近い所）でなめらかに透ける。</summary>
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
