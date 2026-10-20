using UnityEngine;

/// <summary>
/// **まっすぐ飛ぶビームと、そのまわりにらせん状に巻きつく電撃。**
///
/// 使い方：ボタンを押した瞬間に <see cref="BeginCharge"/>、離した瞬間に <see cref="Release"/> を呼ぶ。
/// **押している時間が長いほど、ビームが遠くまで届く**（<see cref="minLength"/>〜<see cref="maxLength"/>）。
///
/// 流れ：
/// 1. **溜め**：押している間、届く距離の目安（うすい線）がのびていく
/// 2. **発射**：離すと、ビームの先端が前へ飛んでいく
/// 3. **維持**：届いたら少しのあいだ出たまま
/// 4. **消える**：だんだん細くなって消える
///
/// ビームのまわりの電撃は、体のまわりの電撃（<see cref="HelixLightning"/>）と同じ作り方
/// （らせん状の目印の点 → エルミート曲線 → ギザギザ → 短い間隔で作り直す）で、
/// らせんの軸が「上方向」ではなく「ビームの向き」になっている。
///
/// このコンポーネントを付けたオブジェクトの**前方向（青い矢印・Z）へ**飛ぶ。
/// </summary>
public class HelixBeam : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("加算合成（光を足す）のマテリアル。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    // 色はどれも HDR（1 より大きい値）。カメラのブルーム（光のにじみ）で光って見せるため

    [Tooltip("ビーム本体の、太くて薄い光の色（HDR）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color beamGlowColor = new Color(0.2f, 2.5f, 0.6f, 1f);

    [Tooltip("ビーム本体の、細くて明るい芯の色（HDR）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color beamCoreColor = new Color(2f, 5f, 2.5f, 1f);

    [Tooltip("ビーム本体の光の太さ（メートル）")]
    [SerializeField] private float beamGlowWidth = 0.45f;

    [Tooltip("ビーム本体の芯の太さ（メートル）")]
    [SerializeField] private float beamCoreWidth = 0.12f;

    [Tooltip("まわりの電撃の、太くて薄い光の色（HDR）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color boltGlowColor = new Color(0.15f, 3f, 0.45f, 1f);

    [Tooltip("まわりの電撃の、細くて明るい芯の色（HDR）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color boltCoreColor = new Color(0.9f, 4f, 1.2f, 1f);

    [Tooltip("まわりの電撃の光の太さ（メートル）")]
    [SerializeField] private float boltGlowWidth = 0.14f;

    [Tooltip("まわりの電撃の芯の太さ（メートル）")]
    [SerializeField] private float boltCoreWidth = 0.025f;

    [Header("溜め（長押しの時間で距離が決まる）")]
    [Tooltip("ちょっと押しただけのときの距離（メートル）")]
    [SerializeField] private float minLength = 3f;

    [Tooltip("いちばん溜めたときの距離（メートル）")]
    [SerializeField] private float maxLength = 15f;

    [Tooltip("いちばん遠くまで届くのに必要な長押しの時間（秒）")]
    [SerializeField] private float maxChargeSeconds = 1.5f;

    [Header("発射")]
    [Tooltip("ビームの先端が進む速さ（メートル／秒）")]
    [SerializeField] private float beamSpeed = 45f;

    [Tooltip("届いてから消え始めるまでの時間（秒）")]
    [SerializeField] private float holdSeconds = 0.35f;

    [Tooltip("消えるまでにかかる時間（秒）")]
    [SerializeField] private float fadeSeconds = 0.25f;

    [Header("まわりのらせん")]
    [Tooltip("ビームに巻きつく電撃の本数")]
    [SerializeField] private int boltCount = 9;

    [Tooltip("らせんの半径（ビームの中心から）")]
    [SerializeField] private float radius = 0.45f;

    [Tooltip("1本の電撃の長さ（メートル。最小・最大）")]
    [SerializeField] private Vector2 boltLengthRange = new Vector2(2f, 5f);

    [Tooltip("1メートルあたり何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsPerMeter = new Vector2(0.5f, 1.1f);

    [Tooltip("ギザギザの大きさ（メートル）")]
    [SerializeField] private float jitter = 0.06f;

    [Tooltip("作り直す間隔（秒。最小・最大）")]
    [SerializeField] private Vector2 regenerateInterval = new Vector2(0.03f, 0.07f);

    [Tooltip("作り直したときに見えている確率。1 未満にすると、ときどき消えて点滅する")]
    [Range(0f, 1f)]
    [SerializeField] private float visibleChance = 0.85f;

    [Header("光")]
    [Tooltip("ビームの先端についていくライト（空なら照らさない）")]
    [SerializeField] private Light headLight;

    [Tooltip("先端のライトの明るさ。ちらつかせ、ビームが消えるときは一緒に暗くなる")]
    [SerializeField] private float lightIntensity = 6f;

    /// <summary>
    /// いまビームがどの段階か。<see cref="Update"/> で、この順に進んで Idle に戻る。
    /// Idle のときだけ次の溜めを始められる。
    /// </summary>
    private enum State
    {
        /// <summary>何も出ていない（次の溜めを受け付ける）</summary>
        Idle,

        /// <summary>ボタンを押している間。届く距離の目安だけ出す</summary>
        Charging,

        /// <summary>離した直後。先端が目標の距離まで飛んでいく</summary>
        Firing,

        /// <summary>届いたあと、holdSeconds の間そのまま出す</summary>
        Holding,

        /// <summary>fadeSeconds かけて細くなり、消える</summary>
        Fading,
    }

    /// <summary>ビームの芯の点の数（まっすぐだが、少しだけ震わせる）。</summary>
    private const int BeamPointCount = 16;

    /// <summary>らせん1本の目印の点の最大数（長い電撃ほど多く使う）。</summary>
    private const int MaxControlPoints = 16;

    /// <summary>らせんの目印の点1区間を何分割するか。多いほど細かいギザギザになる。</summary>
    private const int SamplesPerSegment = 4;

    private State state = State.Idle;

    /// <summary>溜め始めてからの秒数。溜め具合（<see cref="Charge01"/>）の元。</summary>
    private float chargeTime;

    /// <summary>離した瞬間に決まった、届く距離（メートル）。</summary>
    private float targetLength;

    /// <summary>ビームの先端が根元から進んだ距離（メートル）。発射中に伸び、targetLength で止まる。</summary>
    private float headDistance;

    /// <summary>維持・消えるの段階で、残り何秒か。</summary>
    private float stateTimer;

    /// <summary>ビーム本体の線。</summary>
    private LightningLine beam;

    /// <summary>溜め中に出す、届く距離の目安のうすい線。</summary>
    private LightningLine preview;

    /// <summary>ビームに巻きつく電撃（boltCount 本）。</summary>
    private BeamBolt[] bolts;

    /// <summary>ビーム本体・目安の線の点の入れ物（毎フレーム作り直さず使い回す）。</summary>
    private readonly Vector3[] beamPoints = new Vector3[BeamPointCount];

    /// <summary>溜め具合（0〜1）。溜めていないときは 0。</summary>
    public float Charge01 => state == State.Charging ? Mathf.Clamp01(chargeTime / maxChargeSeconds) : 0f;

    /// <summary>溜め中か。</summary>
    public bool IsCharging => state == State.Charging;

    /// <summary>ビームが出ている（発射〜消えるまで）か。この間は次の溜めを始められない。</summary>
    public bool IsFiring => state == State.Firing || state == State.Holding || state == State.Fading;

    /// <summary>いま溜めを離したら、何メートル飛ぶか。</summary>
    public float CurrentLength => Mathf.Lerp(minLength, maxLength, Charge01);

    /// <summary>溜めを始める（ボタンを押した瞬間に呼ぶ）。ビームが出ている間は無視する。</summary>
    public void BeginCharge()
    {
        if (state != State.Idle)
        {
            return;
        }

        state = State.Charging;
        chargeTime = 0f;
    }

    /// <summary>ビームを撃つ（ボタンを離した瞬間に呼ぶ）。溜めた時間で距離が決まる。</summary>
    public void Release()
    {
        if (state != State.Charging)
        {
            return;
        }

        targetLength = CurrentLength;
        headDistance = 0f;
        state = State.Firing;
        preview.SetVisible(false);
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    private void Awake()
    {
        if (lineMaterial == null)
        {
            lineMaterial = LightningLine.CreateAdditiveMaterial();
        }

        // ビームの芯と溜めの目安は、根元から先まで同じ太さにする
        // （LightningLine の既定は両端が細くなる形。ここでは先端の 1 割だけを半分の太さに絞る）
        var flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0.5f));

        beam = new LightningLine(transform, "Beam", lineMaterial);
        beam.SetWidthCurve(flat);

        preview = new LightningLine(transform, "ChargePreview", lineMaterial);
        preview.SetWidthCurve(flat);

        bolts = new BeamBolt[boltCount];

        for (int i = 0; i < boltCount; i++)
        {
            bolts[i] = new BeamBolt(this, i);
        }

        if (headLight != null)
        {
            headLight.enabled = false;
        }
    }

    /// <summary>
    /// 段階ごとの流れ：溜め → 発射 → 維持 → 消える → 待機。
    /// 維持と消えるは <see cref="stateTimer"/> を減らして、0 になったら次へ進む。
    /// </summary>
    private void Update()
    {
        float deltaTime = Time.deltaTime;

        switch (state)
        {
            case State.Charging:
                chargeTime += deltaTime;
                DrawPreview();
                break;

            case State.Firing:
                headDistance += beamSpeed * deltaTime;

                if (headDistance >= targetLength)
                {
                    headDistance = targetLength;
                    state = State.Holding;
                    stateTimer = holdSeconds;
                }

                DrawBeam(1f, deltaTime);
                break;

            case State.Holding:
                stateTimer -= deltaTime;

                if (stateTimer <= 0f)
                {
                    state = State.Fading;
                    stateTimer = fadeSeconds;
                }

                DrawBeam(1f, deltaTime);
                break;

            case State.Fading:
                stateTimer -= deltaTime;

                if (stateTimer <= 0f)
                {
                    HideAll();
                    state = State.Idle;
                    break;
                }

                // 残り時間の割合（1 → 0）をそのまま「消え具合」にする
                DrawBeam(stateTimer / fadeSeconds, deltaTime);
                break;
        }
    }

    // ------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------

    /// <summary>溜め中：届く距離の目安をうすい線で出す。溜まるほど明るく、ちらつきも強くなる。</summary>
    private void DrawPreview()
    {
        float charge = Charge01;
        float length = CurrentLength;

        beamPoints[0] = Vector3.zero;
        beamPoints[1] = new Vector3(0f, 0f, length);

        // 溜めきったら点滅して「最大」と分かるようにする
        float flicker = charge >= 1f ? (Random.value < 0.5f ? 1f : 0.4f) : Random.Range(0.7f, 1f);

        // 明るさは本体の 8%（溜め始め）〜 30%（溜めきり）。目安なので本体より暗く
        float brightness = Mathf.Lerp(0.08f, 0.3f, charge) * flicker;

        // 太さも本体の 3 割にして、本体と見間違えないようにする
        preview.Set(beamPoints, 2,
            beamGlowColor * brightness,
            beamCoreColor * brightness,
            beamGlowWidth * 0.3f,
            beamCoreWidth * 0.3f);
    }

    /// <summary>ビーム本体と、まわりのらせんを描く。<paramref name="fade"/> は 1（そのまま）〜0（消える）。</summary>
    private void DrawBeam(float fade, float deltaTime)
    {
        // 芯：まっすぐだが、毎フレーム少しだけ震わせる（根元は動かさず、ほかは半径 2cm 以内でずらす）
        for (int i = 0; i < BeamPointCount; i++)
        {
            float z = headDistance * i / (BeamPointCount - 1);
            Vector2 shake = i == 0 ? Vector2.zero : Random.insideUnitCircle * 0.02f;
            beamPoints[i] = new Vector3(shake.x, shake.y, z);
        }

        // 太さを毎フレーム ±15% ゆらして、脈打っているように見せる
        float pulse = Random.Range(0.85f, 1.15f);

        beam.Set(beamPoints, BeamPointCount,
            beamGlowColor,
            beamCoreColor,
            beamGlowWidth * fade * pulse,
            beamCoreWidth * fade * pulse);

        for (int i = 0; i < bolts.Length; i++)
        {
            bolts[i].Tick(deltaTime, fade);
        }

        if (headLight != null)
        {
            headLight.enabled = true;
            headLight.transform.localPosition = new Vector3(0f, 0f, headDistance);
            headLight.intensity = lightIntensity * fade * Random.Range(0.6f, 1f);
        }
    }

    /// <summary>ビーム・目安・まわりの電撃・ライトを全部消す（消え終わったとき）。</summary>
    private void HideAll()
    {
        beam.SetVisible(false);
        preview.SetVisible(false);

        for (int i = 0; i < bolts.Length; i++)
        {
            bolts[i].Hide();
        }

        if (headLight != null)
        {
            headLight.enabled = false;
        }
    }

    // ------------------------------------------------------------
    // ビームに巻きつく電撃1本
    // ------------------------------------------------------------

    /// <summary>
    /// ビームに巻きつく電撃1本。設定値（色・太さ・半径など）は親の <see cref="HelixBeam"/> のものをそのまま読む。
    /// 作り方は <see cref="HelixLightning"/> の電撃と同じで、らせんの軸がビームの向き（Z）になっている。
    /// </summary>
    private class BeamBolt
    {
        private readonly HelixBeam owner;
        private readonly LightningLine line;

        /// <summary>らせん状に置いた目印の点（先頭から BuildHelix が返した数だけ使う）。</summary>
        private readonly Vector3[] controlPoints = new Vector3[MaxControlPoints];

        /// <summary>目印の点をなめらかにつないでギザギザにした、実際に描く点。</summary>
        private readonly Vector3[] points = new Vector3[LightningShape.PointCount(MaxControlPoints, SamplesPerSegment)];

        /// <summary>次に作り直すまでの残り秒数。</summary>
        private float timer;

        public BeamBolt(HelixBeam owner, int index)
        {
            this.owner = owner;
            line = new LightningLine(owner.transform, $"BeamBolt_{index}", owner.lineMaterial);
        }

        public void Hide()
        {
            line.SetVisible(false);
            timer = 0f;
        }

        /// <summary>
        /// 毎フレーム呼ばれ、作り直す時間が来たときだけ形を作り直す。
        /// <paramref name="fade"/> は 1（そのまま）〜0（消える）で、太さに掛ける。
        /// </summary>
        public void Tick(float deltaTime, float fade)
        {
            timer -= deltaTime;

            if (timer > 0f)
            {
                return;
            }

            timer = Random.Range(owner.regenerateInterval.x, owner.regenerateInterval.y);

            // ビームがまだ短すぎる（50cm 未満）ときや、ときどき消えて点滅させる
            if (owner.headDistance < 0.5f || Random.value > owner.visibleChance)
            {
                line.SetVisible(false);
                return;
            }

            int controlCount = BuildHelix();
            int count = LightningShape.SampleHermite(controlPoints, controlCount, SamplesPerSegment, points);
            LightningShape.Jag(points, count, owner.jitter);

            line.Set(points, count,
                owner.boltGlowColor,
                owner.boltCoreColor,
                owner.boltGlowWidth * fade * Random.Range(0.7f, 1.2f),
                owner.boltCoreWidth * fade * Random.Range(0.8f, 1.3f));
        }

        /// <summary>
        /// ビームの軸（Z）のまわりに、らせん状に目印の点を置く。置いた数を返す。
        /// ビームのどこに出るか・長さ・回る向き・回る量は毎回ランダム。
        /// </summary>
        private int BuildHelix()
        {
            // 長さはビームの現在の長さを超えないようにし、根元〜先端の間のどこかに置く
            float head = owner.headDistance;
            float length = Mathf.Min(Random.Range(owner.boltLengthRange.x, owner.boltLengthRange.y), head);
            float start = Random.Range(0f, head - length);

            float turns = length * Random.Range(owner.turnsPerMeter.x, owner.turnsPerMeter.y);
            float direction = Random.value < 0.5f ? 1f : -1f;
            float phase = Random.Range(0f, Mathf.PI * 2f);

            // 1周あたり目印を4つ置くと、きれいならせんになる
            int controlCount = Mathf.Clamp(Mathf.CeilToInt(turns * 4f) + 1, 4, MaxControlPoints);

            for (int i = 0; i < controlCount; i++)
            {
                float t = i / (float)(controlCount - 1);
                float angle = phase + direction * t * turns * Mathf.PI * 2f;

                // 半径を点ごとに少しゆらす（きれいな円柱に沿いすぎると機械的に見えるため）
                float r = owner.radius * Random.Range(0.75f, 1.3f);

                // X・Y で円を描き、Z（ビームの向き）に進む
                controlPoints[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, start + t * length);
            }

            return controlCount;
        }
    }
}
