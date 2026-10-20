using UnityEngine;

/// <summary>
/// **体のまわりを、らせん状にびりびり走る電撃**（フルカウル風）。
///
/// しくみ（1本の電撃ができるまで）：
/// 1. 体のまわりに、らせん状の「目印の点」を数個置く（半径・回る向き・回る量・高さは**毎回ランダム**）
/// 2. 目印の点のあいだを**エルミート曲線**でなめらかにつなぐ（これが電撃の「芯」になる）
/// 3. 芯の各点を、進む向きと直角の方向へランダムにずらして**ギザギザ**にする
/// 4. 短い間隔（0.03〜0.08秒くらい）で 1〜3 を**作り直す**。これで毎回形が変わり、ちらついて見える
/// 5. ときどき芯から**枝分かれ**を生やす
///
/// 見た目は「太くて薄い光（Glow）」と「細くて明るい芯（Core）」の2本の線を重ねて作る。
/// 明るさ（HDRの色）を 1 より大きくしているので、カメラのブルーム（光のにじみ）で光って見える。
///
/// このコンポーネントを付けたオブジェクトの**上方向を軸に**らせんを描き、一緒に動く。
/// </summary>
public class HelixLightning : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("加算合成（光を足す）のマテリアル。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    [Tooltip("太くて薄い光の色（HDR。1 より明るくするとブルームで光る）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColor = new Color(0.15f, 3f, 0.45f, 1f);

    [Tooltip("細くて明るい芯の色（HDR）")]
    [ColorUsage(true, true)]
    [SerializeField] private Color coreColor = new Color(0.9f, 4f, 1.2f, 1f);

    [Tooltip("光の太さ（メートル）")]
    [SerializeField] private float glowWidth = 0.16f;

    [Tooltip("芯の太さ（メートル）")]
    [SerializeField] private float coreWidth = 0.025f;

    [Header("本数")]
    [Tooltip("同時に出ている電撃の本数（ふだん）")]
    [SerializeField] private int boltCount = 5;

    [Tooltip("Burst（攻撃時など）で一時的に増やす本数")]
    [SerializeField] private int burstExtraBolts = 5;

    [Header("らせんの形")]
    [Tooltip("らせんの中心（足元からの高さ）")]
    [SerializeField] private float centerHeight = 1f;

    [Tooltip("らせんが走る範囲の高さ（体の高さ）")]
    [SerializeField] private float bodyHeight = 1.8f;

    [Tooltip("らせんの半径（メートル。体の中心から）")]
    [SerializeField] private float radius = 0.45f;

    [Tooltip("1本が何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsRange = new Vector2(0.4f, 1.3f);

    [Tooltip("1本の長さ（体の高さに対する割合。最小・最大）")]
    [SerializeField] private Vector2 lengthRange = new Vector2(0.35f, 0.8f);

    [Tooltip("目印の点の数。多いほど曲がりくねる")]
    [SerializeField] private int controlPointCount = 6;

    [Tooltip("目印の点1区間を何分割するか。多いほど細かいギザギザになる")]
    [SerializeField] private int samplesPerSegment = 5;

    [Header("びりびり")]
    [Tooltip("ギザギザの大きさ（メートル）")]
    [SerializeField] private float jitter = 0.05f;

    [Tooltip("作り直す間隔（秒。最小・最大）。短いほど激しくちらつく")]
    [SerializeField] private Vector2 regenerateInterval = new Vector2(0.03f, 0.08f);

    [Tooltip("作り直したときに見えている確率。1 未満にすると、ときどき消えて点滅する")]
    [Range(0f, 1f)]
    [SerializeField] private float visibleChance = 0.8f;

    [Header("枝分かれ")]
    [Tooltip("作り直すたびに、枝が生える確率")]
    [Range(0f, 1f)]
    [SerializeField] private float branchChance = 0.5f;

    [Tooltip("枝の長さ（メートル。最小・最大）")]
    [SerializeField] private Vector2 branchLengthRange = new Vector2(0.15f, 0.4f);

    [Header("光")]
    [Tooltip("まわりを照らすライト（空なら照らさない）")]
    [SerializeField] private Light flickerLight;

    [Tooltip("ライトの明るさ。ちらつかせ、Burst 中はさらに明るくなる")]
    [SerializeField] private float lightIntensity = 3f;

    [Header("Burst（一時的に激しくする）")]
    [Tooltip("Burst の最中、らせんの半径を何倍にするか")]
    [SerializeField] private float burstRadiusScale = 1.5f;

    [Tooltip("Burst の最中、ギザギザを何倍にするか")]
    [SerializeField] private float burstJitterScale = 1.8f;

    [Tooltip("Burst の最中、明るさを何倍にするか")]
    [SerializeField] private float burstBrightnessScale = 1.6f;

    /// <summary>枝1本の点の数（短い直線なので少なくてよい）。</summary>
    private const int BranchPointCount = 5;

    /// <summary>ふだんの本数 ＋ Burst 用の本数ぶん、最初に全部作っておく（途中で増やさない）。</summary>
    private Bolt[] bolts;

    /// <summary>Burst の残り秒数。0 以下ならふだんの状態。</summary>
    private float burstTimer;

    /// <summary>直近の Burst の長さ（秒）。残り秒数との割合で「強さ」を出すために控えておく。</summary>
    private float burstDuration;

    /// <summary>電撃を出すかどうか。false にすると全部消える。</summary>
    public bool IsOn { get; set; } = true;

    /// <summary>
    /// **一定時間だけ電撃を激しくする**（本数・太さ・広がりが増える）。攻撃やダッシュの瞬間に呼ぶ。
    /// </summary>
    public void Burst(float seconds = 0.4f)
    {
        burstTimer = seconds;
        burstDuration = seconds;
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

        int total = boltCount + burstExtraBolts;
        bolts = new Bolt[total];

        for (int i = 0; i < total; i++)
        {
            bolts[i] = new Bolt(this, i);
        }
    }

    private void Update()
    {
        if (burstTimer > 0f)
        {
            burstTimer -= Time.deltaTime;
        }

        // Burst の強さ（1 → 0 へ減っていく）
        float burst = burstDuration > 0f ? Mathf.Clamp01(burstTimer / burstDuration) : 0f;

        // 出す本数は、強さに応じてふだんの本数に Burst 用の本数を足す（弱まるにつれて減っていく）
        int activeCount = boltCount + Mathf.CeilToInt(burstExtraBolts * burst);

        for (int i = 0; i < bolts.Length; i++)
        {
            bool active = IsOn && i < activeCount;
            bolts[i].Tick(Time.deltaTime, active, burst);
        }

        if (flickerLight != null)
        {
            flickerLight.enabled = IsOn;
            // 光もちらつかせる
            flickerLight.intensity = lightIntensity * Random.Range(0.5f, 1f) * (1f + burst);
        }
    }

    // ------------------------------------------------------------
    // 1本の電撃
    // ------------------------------------------------------------

    /// <summary>
    /// 電撃1本（芯と、そこから生える枝1本の組）。設定値は親の <see cref="HelixLightning"/> のものをそのまま読む。
    /// 作り直す間隔はそれぞれが別々に持つので、本ごとにばらばらのタイミングでちらつく。
    /// </summary>
    private class Bolt
    {
        private readonly HelixLightning owner;
        private readonly LightningLine main;
        private readonly LightningLine branch;

        /// <summary>手順1で置く、らせん状の目印の点。</summary>
        private readonly Vector3[] controlPoints;

        /// <summary>手順2・3を経た、実際に描く芯の点。</summary>
        private readonly Vector3[] points;

        /// <summary>枝の点。</summary>
        private readonly Vector3[] branchPoints = new Vector3[BranchPointCount];

        /// <summary>次に作り直すまでの残り秒数。</summary>
        private float timer;

        public Bolt(HelixLightning owner, int index)
        {
            this.owner = owner;

            controlPoints = new Vector3[Mathf.Max(3, owner.controlPointCount)];
            points = new Vector3[LightningShape.PointCount(controlPoints.Length, owner.samplesPerSegment)];

            main = new LightningLine(owner.transform, $"Bolt_{index}", owner.lineMaterial);
            branch = new LightningLine(owner.transform, $"Bolt_{index}_Branch", owner.lineMaterial);
        }

        /// <summary>
        /// 手順4：毎フレーム呼ばれ、作り直す時間が来たときだけ 1〜3・5 をやり直す。
        /// <paramref name="active"/> が false なら消す。<paramref name="burst"/> は Burst の強さ（0〜1）。
        /// </summary>
        public void Tick(float deltaTime, bool active, float burst)
        {
            if (!active)
            {
                main.SetVisible(false);
                branch.SetVisible(false);
                timer = 0f;
                return;
            }

            timer -= deltaTime;

            if (timer > 0f)
            {
                return;
            }

            // 次に作り直すまでの時間もランダム（全部が同時に切り替わると機械的に見えるため）
            timer = Random.Range(owner.regenerateInterval.x, owner.regenerateInterval.y);

            // Burst 中は必ず見せる。ふだんはときどき消えて点滅する
            bool visible = Random.value < Mathf.Lerp(owner.visibleChance, 1f, burst);

            if (!visible)
            {
                main.SetVisible(false);
                branch.SetVisible(false);
                return;
            }

            // Burst の強さに応じて、ふだん（1 倍）と Burst の倍率の間を取る
            float radiusScale = Mathf.Lerp(1f, owner.burstRadiusScale, burst);
            float jitterScale = Mathf.Lerp(1f, owner.burstJitterScale, burst);
            float brightness = Mathf.Lerp(1f, owner.burstBrightnessScale, burst);

            BuildHelix(radiusScale);
            int count = LightningShape.SampleHermite(controlPoints, controlPoints.Length, owner.samplesPerSegment, points);
            LightningShape.Jag(points, count, owner.jitter * jitterScale);

            owner.SetLine(main, points, count, brightness, jitterScale);

            // 枝は芯より少し暗く（8 割）・細く（7 割）して、芯が主役に見えるようにする
            if (Random.value < owner.branchChance)
            {
                BuildBranch(count, owner.jitter * jitterScale);
                owner.SetLine(branch, branchPoints, BranchPointCount, brightness * 0.8f, 0.7f);
            }
            else
            {
                branch.SetVisible(false);
            }
        }

        /// <summary>手順1：らせん状に目印の点を置く。</summary>
        private void BuildHelix(float radiusScale)
        {
            // 体の高さの範囲（中心 ± 高さの半分）の中に、ランダムな長さで収まるように置く
            float length = owner.bodyHeight * Random.Range(owner.lengthRange.x, owner.lengthRange.y);
            float bottom = owner.centerHeight - owner.bodyHeight * 0.5f;
            float startY = bottom + Random.Range(0f, owner.bodyHeight - length);

            float turns = Random.Range(owner.turnsRange.x, owner.turnsRange.y);
            float direction = Random.value < 0.5f ? 1f : -1f;
            float phase = Random.Range(0f, Mathf.PI * 2f);

            // 上から下へ走るものも混ぜる
            bool downward = Random.value < 0.5f;

            for (int i = 0; i < controlPoints.Length; i++)
            {
                float t = i / (float)(controlPoints.Length - 1);
                float angle = phase + direction * t * turns * Mathf.PI * 2f;

                // 半径を点ごとに少しゆらす（きれいな円柱に沿いすぎると機械的に見えるため）
                float r = owner.radius * radiusScale * Random.Range(0.8f, 1.25f);
                float y = startY + (downward ? 1f - t : t) * length;

                // X・Z で円を描き、Y（上方向）に進む
                controlPoints[i] = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
            }
        }

        /// <summary>手順5：芯のどこかから、外向きに短い枝を生やす。</summary>
        private void BuildBranch(int mainCount, float jitterAmount)
        {
            // 芯の真ん中あたり（全体の 1/4〜3/4 の位置）から生やす。端は細く消えていくので枝が浮いて見えるため
            int from = Random.Range(mainCount / 4, mainCount * 3 / 4);
            Vector3 start = points[from];

            // 体の外側へ向かう方向 ＋ 少しランダム
            Vector3 outward = new Vector3(start.x, 0f, start.z).normalized;
            Vector3 direction = (outward + Random.insideUnitSphere * 0.8f).normalized;
            float length = Random.Range(owner.branchLengthRange.x, owner.branchLengthRange.y);

            for (int i = 0; i < BranchPointCount; i++)
            {
                float t = i / (float)(BranchPointCount - 1);
                Vector3 p = start + direction * (length * t);

                // 根元（i == 0）は芯にくっつけたまま、先に行くほど大きくずらす
                if (i > 0)
                {
                    p += Random.insideUnitSphere * (jitterAmount * t);
                }

                branchPoints[i] = p;
            }
        }
    }

    /// <summary>
    /// 線に点と色・太さを渡す。太さは毎回少しゆらす（作り直すたびに太さも変わって、よりちらついて見える）。
    /// <paramref name="widthScale"/> は光の太さにだけ掛ける（枝を細くするため）。
    /// </summary>
    private void SetLine(LightningLine line, Vector3[] positions, int count, float brightness, float widthScale)
    {
        line.Set(positions, count,
            glowColor * brightness,
            coreColor * brightness,
            glowWidth * widthScale * Random.Range(0.7f, 1.2f),
            coreWidth * Random.Range(0.8f, 1.3f));
    }
}
