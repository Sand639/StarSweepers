using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>色帯がどちらへ扫けていくか。</summary>
public enum RoundWinWipeDirection
{
    /// <summary>左から右へ（画面の左の外から入ってきて、右の外へ出ていく）</summary>
    LeftToRight,

    /// <summary>右から左へ</summary>
    RightToLeft
}

/// <summary>扫けていく帯の、**縁（さきっぽ）の形**。</summary>
public enum RoundWinWipeEdge
{
    /// <summary>まっすぐな縦線（ふつうのワイプ）</summary>
    Straight,

    /// <summary>**水波。** 縁が上下にうねりながら進む</summary>
    Wave,

    /// <summary>**炎。** 縁がゆらめいて、燃えながら進むように見える</summary>
    Flame
}

/// <summary>炎の火舌が**どちらへ倒れるか**（風にあおられたような見えかた）。</summary>
public enum RoundWinWipeFlameDir
{
    /// <summary>掃けていく向きと同じ（赤＝右、青＝左）</summary>
    FollowSweep,

    /// <summary>どちらのチームでも右へ倒す</summary>
    Right,

    /// <summary>どちらのチームでも左へ倒す</summary>
    Left
}

/// <summary>**燃える範囲の大きさ**（火舌が帯の先へ何ぼ突き出るか）の档位。</summary>
public enum RoundWinWipeFlameRange
{
    /// <summary>ちいさく（0.05）</summary>
    Small,

    /// <summary>ふつう（0.10）</summary>
    Medium,

    /// <summary>おおきく（0.16）</summary>
    Large,

    /// <summary>とてもおおきく（0.24）</summary>
    Huge
}

/// <summary>
/// **1回の転場ぶんのかたまり。**
///
/// 「どの色の帯が」「どちらへ扫けて」「何と書いて」「扫けたあとの背景を何色にするか」と、
/// その回の縁の光・火星の色を持っている。<see cref="RoundWinWipe"/> の `steps` の中身で、
/// **インスペクターから自由に足したり並べ替えたりできる。**
/// </summary>
[System.Serializable]
public class RoundWinWipeStep
{
    [Tooltip("扫けていく帯の色。チームの色と同じにしておく（赤チーム＝(0.95, 0.45, 0.35)）")]
    public Color bandColor = new Color(0.95f, 0.45f, 0.35f);

    [Tooltip("帯がどちらへ扫けていくか")]
    public RoundWinWipeDirection direction = RoundWinWipeDirection.LeftToRight;

    [Tooltip("画面に出る文字（例：RED WIN）")]
    public string label = "RED WIN";

    [Tooltip("帯が画面をすっかり隠したあいだに差し替わる、次の背景の色")]
    public Color nextBackground = new Color(0.85f, 0.72f, 0.25f);

    [Tooltip("縁の光の色。炎なら「燃えている熱い縁」、水波なら「波頭の白い光」に見える")]
    public Color rimColor = new Color(1f, 0.78f, 0.35f);

    [Tooltip("炎の真ん中の層の色（外側の帯の色より明るい色。赤チームなら黄オレンジ）")]
    public Color flameMidColor = new Color(1f, 0.75f, 0.20f);

    [Tooltip("炎の芯の色（いちばん明るい色。赤チームならほぼ白の黄）")]
    public Color flameCoreColor = new Color(1f, 0.95f, 0.75f);

    [Tooltip("火星の色。退場のときに帯のあとへ散る光の点")]
    public Color sparkColor = new Color(1f, 0.55f, 0.20f);

    [Tooltip("炎の火舌が**どちらへ倒れるか**。Follow Sweep＝掃けていく向きと同じ（赤＝右・青＝左）")]
    public RoundWinWipeFlameDir flameDirection = RoundWinWipeFlameDir.FollowSweep;
}

/// <summary>転場がいまどの段階か。</summary>
public enum RoundWinWipePhase
{
    /// <summary>待機中。ボタン（またはスペース）を押せる</summary>
    Idle,

    /// <summary>① 帯が扫けてきて画面を隠していく</summary>
    SweepIn,

    /// <summary>② 帯が止まって文字だけ出している。**このあいだ帯は一切動かない**</summary>
    TextHold,

    /// <summary>③ 文字が消えて、帯がそのまま扫けて退場する。**このあいだだけ火星が出る**</summary>
    SweepOut
}

/// <summary>
/// **1ラウンド取ったときの、純色の帯が扫けていく転場。**（試作・2026/10/28・呉さんの注文）
///
/// ## 流れ（1回ぶん）
///
/// | 段階 | 何が起きるか | 長さ |
/// | --- | --- | --- |
/// | 待機 | いまの背景が見えている。帯は画面の外 | — |
/// | ① 扫ける | 帯が画面の外から入ってきて、画面をすっかり隠す | 1 ÷ `Sweep Speed` 秒 |
/// | （差し替え） | 帯に隠されて見えないあいだに、背景を次の色に変える | 0 秒 |
/// | ② 文字 | 「RED WIN」を出す。**このあいだ帯は動かない** | `Text Seconds` 秒 |
/// | ③ 退場 | 文字を消して、帯が同じ向きへ扫けていく。**火星を散らしながら**新しい背景が見える | 1 ÷ `Sweep Speed` 秒 |
///
/// ②で帯を止めるのは **「文字が出終わる前に色が動いてはいけない」** という注文のため。
/// 待機に戻ったら `steps` の次のかたまりへ進む（最後まで行ったら先頭に戻る）。
/// 初期値は 赤（左→右・背景を黄に）→ 青（右→左・背景を灰に）の2つが交互。
///
/// ## 縁の形（水波・炎）
///
/// **帯の画像そのものは動かしていない。** 画面いっぱいに置いたまま、
/// シェーダー（`Assets/Art/Shaders/UIWipeEdge.shader`）へ「境界はどこか」を毎フレーム伝えて描いている。
/// だから縁をまっすぐ以外の形にできる。`Edge Style` で切り替わる：
///
/// | 値 | 見た目 |
/// | --- | --- |
/// | Straight | まっすぐな縦線 |
/// | Wave | **水波。** 上下にうねりながら進む |
/// | Flame | **炎。** ゆらめいて燃えながら進む |
///
/// ゆれの大きさ・細かさ・速さ、縁の光の幅と強さは、ぜんぶインスペクターで変えられる
/// （**再生中に変えると次のフレームで反映される**）。
/// 縁の光と火星の**色**はかたまりごと（`steps` の中）に持つので、赤はオレンジ・青は水色にできる。
/// シェーダーが入っていない帯を渡されたときは、**画像を横へ動かすだけの古いやり方**（まっすぐな縁）に落ちる。
///
/// ## 火星
///
/// **退場（③）のあいだだけ**、帯が去った側の境界近くに光の点を散らす。
/// パーティクルではなくシェーダーで描いている。この画面は Overlay の Canvas なので、
/// **世界の空間に置いたパーティクルは UI の下に隠れて見えない**ため。
///
/// ## 調整のしかた（2通り）
///
/// - **インスペクター** … `Sweep Speed`（1秒間に画面何枚ぶん扫けるか）、`Text Seconds`（文字を出す秒数）、
///   `Edge Style` とその下の縁・火星の値ぜんぶ
/// - **再生中のキー** … `←` `→` 速度、`↑` `↓` 文字の秒数、`E` 縁の形を切り替え、`R` 最初に戻す。
///   **Shift を押しながらだと細かい刻み**。変わった値は画面の案内と Console に出る。
///   ※再生中の変更は保存されないので、気に入った値は停止してからインスペクターに書き写すこと
///
/// ## 通信は使っていない
///
/// 見た目だけの単体確認用。**ボタンとスペースキーだけで動く**ので、ホストをしなくても再生するだけで試せる。
/// 実際の試合に組み込むときは、`SpaceJunkRound.RoundWinner`（勝ったチーム。-1 なら引き分け）を見て
/// <see cref="Play"/> か <see cref="PlayStep"/> を呼ぶ形にする。
///
/// 文字は Unity 標準の物だと日本語が四角になるので、<see cref="UiFont"/> から借りてくる
/// （保存できないため、再生されたときに毎回入れる）。
/// </summary>
public class RoundWinWipe : MonoBehaviour
{
    /// <summary>縁を描くシェーダーの名前。これが付いていない帯は、まっすぐな縁になる。</summary>
    public const string WipeShaderName = "StarSweepers/UIWipeEdge";

    [Header("部品（インスペクターでドラッグして渡す）")]
    [Tooltip("背景の絵。帯が画面を隠しているあいだに色を差し替える")]
    [SerializeField] private Image background;

    [Tooltip("扫けていく純色の帯。**画面いっぱいに広がる大きさで置くこと。**\n" +
             "縁のシェーダー（UIWipeEdge）が入っていれば、再生されたときに自動的に画面いっぱいに広げられ、\n" +
             "画像を動かす代わりにシェーダーへ境界の位置が伝えられる")]
    [SerializeField] private Image band;

    [Tooltip("画面の真ん中に出る文字（例：RED WIN）")]
    [SerializeField] private Text labelText;

    [Tooltip("押すと転場が始まるボタン")]
    [SerializeField] private Button changeButton;

    [Tooltip("ボタンに書いた文字。**日本語の文字を入れ直すためだけ**に受け取る（動かすのはボタン本体）")]
    [SerializeField] private Text buttonText;

    [Tooltip("画面の隅に出る、いまの値と段階の案内（無ければ出さない）")]
    [SerializeField] private Text hintText;

    [Header("中身")]
    [Tooltip("転場のかたまり。押すたびにこの並びの次へ進む（最後まで行ったら先頭に戻る）")]
    [SerializeField] private RoundWinWipeStep[] steps = new RoundWinWipeStep[0];

    [Header("掃けかた")]
    [Tooltip("**扫ける速さ。1秒間に画面何枚ぶん進むか**（1.0 なら1秒で画面1枚ぶん）。大きいほど速い")]
    [SerializeField] private float sweepSpeed = 1.6f;

    [Tooltip("**文字を出しておく秒数。** この秒数が終わるまで帯は動かない")]
    [SerializeField] private float textSeconds = 1.2f;

    [Header("縁の形（再生中に変えてもすぐ反映される）")]
    [Tooltip("**縁の形。** まっすぐ／水波／炎。`E` キーでも切り替わる")]
    [SerializeField] private RoundWinWipeEdge edgeStyle = RoundWinWipeEdge.Flame;

    [Tooltip("縁のゆれの大きさ（画面の横幅に対する割合）。0 にすると縁がまっすぐになる")]
    [SerializeField] private float edgeAmplitude = 0.06f;

    [Tooltip("縁のぼかし幅。大きいほど境界がふわっとする")]
    [SerializeField] private float edgeSoftness = 0.012f;

    [Tooltip("縁のゆれの細かさ。大きいほど細かい揺れになる")]
    [SerializeField] private float edgeDetail = 8f;

    [Tooltip("縁のゆれの速さ（水波のうねりが進む速さ）")]
    [SerializeField] private float edgeSpeed = 1.5f;

    [Header("炎（Balatro の炎のような、とがった火舌の3枚重ね）")]
    [Tooltip("**火舌の長さ**（画面の横幅に対する割合）。大きいほど炎が帯の先へ突き出る")]
    [SerializeField] private float flameHeight = 0.10f;

    [Tooltip("火舌の本数の細かさ（縦に何本並ぶか）。大きいほど細かい舌になる")]
    [SerializeField] private float flameDetail = 6f;

    [Tooltip("火舌のちらつく速さ")]
    [SerializeField] private float flameSpeed = 2.2f;

    [Tooltip("真ん中の層の大きさ（外側の火舌の何割か）")]
    [SerializeField] private float flameMidScale = 0.62f;

    [Tooltip("芯の層の大きさ（外側の火舌の何割か）")]
    [SerializeField] private float flameCoreScale = 0.32f;

    [Tooltip("縁の光の幅。炎の「熱い縁」や波頭の「白い光」の太さ")]
    [SerializeField] private float rimWidth = 0.03f;

    [Tooltip("縁の光の強さ。**初期値は 0（光らせない）。** 水波の波頭を白くしたいときだけ上げる")]
    [SerializeField] private float rimStrength = 0f;

    [Header("火星（退場のときだけ出る）")]
    [Tooltip("退場のときに火星を出すか")]
    [SerializeField] private bool sparksOnExit = true;

    [Tooltip("火星の細かさ。大きいほど細かい点になる")]
    [SerializeField] private float sparkDensity = 24f;

    [Tooltip("火星が帯のあとへ散る距離（画面の横幅に対する割合）")]
    [SerializeField] private float sparkSpread = 0.18f;

    [Tooltip("火星のちらつく速さ")]
    [SerializeField] private float sparkSpeed = 2f;

    [Tooltip("火星の少なさ。小さいほど多く出る")]
    [SerializeField] private float sparkThreshold = 0.55f;

    [Tooltip("退場が始まってから火星がいっぱいになるまでに、退場全体の何割を使うか")]
    [SerializeField] private float sparkRamp = 0.25f;

    [Header("キーボード")]
    [Tooltip("← → で速度を変えたときの刻み（画面/秒）")]
    [SerializeField] private float speedStep = 0.2f;

    [Tooltip("↑ ↓ で文字の秒数を変えたときの刻み（秒）")]
    [SerializeField] private float textStep = 0.1f;

    [Tooltip("再生中にキーボード（スペース・矢印・E・R）でも操作するか。OFF にするとボタンだけになる")]
    [SerializeField] private bool allowKeyboard = true;

    [Header("変更できる範囲")]
    [Tooltip("掃ける速さの下限（これより遅くできない）")]
    [SerializeField] private float minSpeed = 0.1f;

    [Tooltip("掃ける速さの上限")]
    [SerializeField] private float maxSpeed = 8f;

    [Tooltip("文字の秒数の下限")]
    [SerializeField] private float minTextSeconds = 0f;

    [Tooltip("文字の秒数の上限")]
    [SerializeField] private float maxTextSeconds = 10f;

    // シェーダーへ値を渡すときの名前（文字で探しに行くと遅いので、番号に変えて覚えておく）
    private static readonly int IdBoundary = Shader.PropertyToID("_Boundary");
    private static readonly int IdCoverSign = Shader.PropertyToID("_CoverSign");
    private static readonly int IdSoftness = Shader.PropertyToID("_Softness");
    private static readonly int IdEdgeStyle = Shader.PropertyToID("_EdgeStyle");
    private static readonly int IdEdgeAmplitude = Shader.PropertyToID("_EdgeAmplitude");
    private static readonly int IdEdgeDetail = Shader.PropertyToID("_EdgeDetail");
    private static readonly int IdEdgeSpeed = Shader.PropertyToID("_EdgeSpeed");
    private static readonly int IdFlameHeight = Shader.PropertyToID("_FlameHeight");
    private static readonly int IdFlameDetail = Shader.PropertyToID("_FlameDetail");
    private static readonly int IdFlameSpeed = Shader.PropertyToID("_FlameSpeed");
    private static readonly int IdFlameMidScale = Shader.PropertyToID("_FlameMidScale");
    private static readonly int IdFlameCoreScale = Shader.PropertyToID("_FlameCoreScale");
    private static readonly int IdFlameMidColor = Shader.PropertyToID("_FlameMidColor");
    private static readonly int IdFlameCoreColor = Shader.PropertyToID("_FlameCoreColor");
    private static readonly int IdScreenAspect = Shader.PropertyToID("_ScreenAspect");
    private static readonly int IdRimColor = Shader.PropertyToID("_RimColor");
    private static readonly int IdRimWidth = Shader.PropertyToID("_RimWidth");
    private static readonly int IdRimStrength = Shader.PropertyToID("_RimStrength");
    private static readonly int IdSparkColor = Shader.PropertyToID("_SparkColor");
    private static readonly int IdSparkAmount = Shader.PropertyToID("_SparkAmount");
    private static readonly int IdSparkDensity = Shader.PropertyToID("_SparkDensity");
    private static readonly int IdSparkSpread = Shader.PropertyToID("_SparkSpread");
    private static readonly int IdSparkSpeed = Shader.PropertyToID("_SparkSpeed");
    private static readonly int IdSparkThreshold = Shader.PropertyToID("_SparkThreshold");

    /// <summary>いまの段階。</summary>
    public RoundWinWipePhase CurrentPhase => (RoundWinWipePhase)(int)phase;

    /// <summary>次に押したときに使われる、かたまりの番号。</summary>
    public int StepIndex => stepIndex;

    /// <summary>縁の形。代入すると画面の案内も作り直される。</summary>
    public RoundWinWipeEdge EdgeStyle
    {
        get => edgeStyle;
        set
        {
            edgeStyle = value;
            hintDirty = true;
        }
    }

    /// <summary>掃ける速さ（1秒間に画面何枚ぶん）。代入すると範囲に収まる。</summary>
    public float SweepSpeed
    {
        get => sweepSpeed;
        set
        {
            sweepSpeed = Mathf.Clamp(value, minSpeed, maxSpeed);
            hintDirty = true;
        }
    }

    /// <summary>文字を出しておく秒数。代入すると範囲に収まる。</summary>
    public float TextSeconds
    {
        get => textSeconds;
        set
        {
            textSeconds = Mathf.Clamp(value, minTextSeconds, maxTextSeconds);
            hintDirty = true;
        }
    }

    /// <summary>転場が動いているあいだか（待機中なら false）。</summary>
    public bool IsPlaying => phase != Phase.Idle;

    /// <summary>縁をシェーダーで描けているか（false ならまっすぐな縁に落ちている）。</summary>
    public bool UsingShaderEdge => shaderEdge;

    /// <summary>内部で使う段階。<see cref="RoundWinWipePhase"/> と同じ並びなので、外へは番号を写して返す。</summary>
    private enum Phase { Idle, SweepIn, TextHold, SweepOut }

    private Phase phase = Phase.Idle;

    /// <summary>次に使う、かたまりの番号。</summary>
    private int stepIndex;

    /// <summary>いま動いているかたまり（待機中は null）。</summary>
    private RoundWinWipeStep step;

    /// <summary>①③ の進み具合（0〜1）。1秒間に <see cref="sweepSpeed"/> ぶん増える</summary>
    private float sweep;

    /// <summary>② で文字を出してからの秒数。</summary>
    private float hold;

    /// <summary>背景の最初の色。再生が始まったときのインスペクターの値を覚えておく</summary>
    private Color startBackground = Color.gray;

    /// <summary>帯のマテリアル（この帯だけの写し）。縁のシェーダーが入っていれば、ここに値を渡す</summary>
    private Material bandMaterial;

    /// <summary>縁をシェーダーで描くか。描けないときは帯を横へ動かす古いやり方に落ちる</summary>
    private bool shaderEdge;

    /// <summary>画面の案内を作り直す必要があるか（毎フレーム文字を組み立てないため）</summary>
    private bool hintDirty = true;

    /// <summary>さっき案内に出した文字（同じ内容なら入れ直さない）</summary>
    private string hintCache = string.Empty;

    /// <summary>案内を見直してからの秒数（毎フレーム文字を組み立てないため）</summary>
    private float hintTimer;

    private void Awake()
    {
        // 日本語の文字は保存できないので、ここで入れ直す（入れないと四角になる）
        ApplyFont(labelText, 150);
        ApplyFont(hintText, 26);
        ApplyFont(buttonText, 32);

        if (changeButton != null)
        {
            changeButton.onClick.RemoveListener(Play);
            changeButton.onClick.AddListener(Play);
        }

        if (background != null)
        {
            startBackground = background.color;
        }

        PrepareBand();

        // つなぎ忘れ（または、古い作りのシーンをそのまま開いた）ときの気づきやすさのため
        if (labelText == null)
        {
            Debug.LogWarning("[WINWIPE] 文字の部品（Label Text）がつながっていないので、**文字は出ません**。\n" +
                             "　古い作りの検証シーンを開いているのかもしれません。" +
                             "`Tools > StarSweepers > 勝利の転場 > 検証シーンを作る（作り直す）` で作り直してください。");
        }

        stepIndex = 0;
        phase = Phase.Idle;
        ShowText(false);
        ApplyIdleVisual();
        hintDirty = true;
    }

    private void OnDestroy()
    {
        if (changeButton != null)
        {
            changeButton.onClick.RemoveListener(Play);
        }
    }

    private void Update()
    {
        ReadKeyboard();

        switch (phase)
        {
            case Phase.Idle:
                break;

            case Phase.SweepIn:
                sweep += Time.deltaTime * Mathf.Max(0.01f, sweepSpeed);

                if (sweep >= 1f)
                {
                    sweep = 1f;
                    ApplyVisual(sweep);

                    // 帯が画面をすっかり隠したこの瞬間に、背景を差し替える（隠れているので切り替わりが見えない）
                    if (background != null && step != null)
                    {
                        background.color = step.nextBackground;
                    }

                    ShowText(true);
                    hold = 0f;
                    SetPhase(Phase.TextHold);
                }
                break;

            case Phase.TextHold:
                // **文字の秒数が終わるまで、帯は一切動かさない。** ここでは経過時間だけ数える
                hold += Time.deltaTime;

                if (hold >= Mathf.Max(0f, textSeconds))
                {
                    ShowText(false);
                    sweep = 0f;
                    SetPhase(Phase.SweepOut);
                    ApplyVisual(sweep);
                }
                break;

            case Phase.SweepOut:
                sweep += Time.deltaTime * Mathf.Max(0.01f, sweepSpeed);

                if (sweep >= 1f)
                {
                    // 退場し終わった。次のかたまりへ進んで待機に戻る
                    stepIndex = steps != null && steps.Length > 0 ? (stepIndex + 1) % steps.Length : 0;
                    step = null;
                    SetPhase(Phase.Idle);
                    ApplyIdleVisual();
                }
                break;
        }

        if (phase == Phase.SweepIn || phase == Phase.SweepOut)
        {
            ApplyVisual(sweep);
        }

        UpdateHint();
    }

    // ------------------------------------------------------------
    // 外から呼ぶ入口
    // ------------------------------------------------------------

    /// <summary>
    /// **転場を1回再生する。** 待機中のときだけ効く（動いているあいだに押しても無視する）。
    /// ボタンのクリックとスペースキーの両方から呼ばれる。
    /// </summary>
    public void Play()
    {
        if (phase != Phase.Idle)
        {
            return;
        }

        if (steps == null || steps.Length == 0)
        {
            Debug.LogWarning("[WINWIPE] 転場のかたまり（Steps）が空なので、何も起きません。インスペクターで足してください。");
            return;
        }

        PlayStep(steps[Mathf.Clamp(stepIndex, 0, steps.Length - 1)]);
    }

    /// <summary>
    /// **かたまりを1つ渡して、その場で再生する。** 待機中なら必ず動く。
    /// 試合側に組み込むときは、勝ったチームからこれを作って渡すのが分かりやすい。
    /// </summary>
    public void PlayStep(RoundWinWipeStep target)
    {
        if (phase != Phase.Idle || target == null)
        {
            return;
        }

        step = target;
        sweep = 0f;
        hold = 0f;

        if (band != null)
        {
            band.color = step.bandColor;
            band.enabled = true;
        }

        ShowText(false);

        // **先に段階を ① にしてから見た目を入れる**（入口側か出口側かは段階を見て決めるため）
        SetPhase(Phase.SweepIn);
        ApplyVisual(0f);

        Debug.Log($"[WINWIPE] 転場を始めます：{step.label}　{DirectionName(step.direction)}　縁＝{EdgeName(edgeStyle)}　" +
                  $"掃く速さ {sweepSpeed:0.00} 画面/秒・文字 {textSeconds:0.00} 秒" +
                  (shaderEdge ? string.Empty : "　※縁のシェーダーが使えないので、まっすぐな縁で動かしています"));
    }

    /// <summary>
    /// **待機の状態に戻す**（背景を再生前の色に戻して、帯と文字を隠す）。
    /// 何度も見られるように、テストシーンでは `R` キーでも呼ばれる。
    /// </summary>
    public void ResetToIdle()
    {
        step = null;
        stepIndex = 0;
        sweep = 0f;
        hold = 0f;
        phase = Phase.Idle;

        if (background != null)
        {
            background.color = startBackground;
        }

        ShowText(false);
        ApplyIdleVisual();
        hintDirty = true;

        Debug.Log("[WINWIPE] 待機に戻しました（背景も最初の色に戻しています）。");
    }

    // ------------------------------------------------------------
    // 見た目（帯の位置・縁・火星）
    // ------------------------------------------------------------

    /// <summary>
    /// 帯の準備。**縁のシェーダー（<see cref="WipeShaderName"/>）が入っていれば、
    /// 帯を画面いっぱいに広げて、以降は画像を動かさずにシェーダーへ境界の位置を伝える。**
    /// 入っていなければ、画像を横へ動かすだけの古いやり方（まっすぐな縁）に落ちる。
    /// </summary>
    private void PrepareBand()
    {
        shaderEdge = false;
        bandMaterial = null;

        if (band == null)
        {
            return;
        }

        Material material = band.material; // この帯だけの写し（マテリアルの資産そのものは汚さない）
        Shader shader = material != null ? material.shader : null;

        if (shader == null || shader.name != WipeShaderName)
        {
            Debug.LogWarning($"[WINWIPE] 帯（{band.name}）に縁のシェーダー（{WipeShaderName}）が入っていないので、" +
                             "まっすぐな縁で動かします。水波・炎・火星は出ません。\n" +
                             "　出すには：`Assets/Art/Materials/UIWipeEdge.mat` を帯の Image の Material に入れるか、\n" +
                             "　`Tools > StarSweepers > 勝利の転場 > 検証シーンを作る（作り直す）` でシーンを作り直してください。");
            return;
        }

        if (!shader.isSupported)
        {
            Debug.LogError($"[WINWIPE] 縁のシェーダー（{WipeShaderName}）がこの機種で使えないので、まっすぐな縁に落とします。" +
                           "シェーダーのコンパイルに失敗しているかもしれません（Console に赤い文字が出ていないか確認してください）。");
            return;
        }

        bandMaterial = material;
        shaderEdge = true;

        // シェーダー方式では帯の画像を動かさない。画面いっぱいに広げておく
        RectTransform rect = band.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        Debug.Log($"[WINWIPE] 縁のシェーダー（{WipeShaderName}）を使います。いまの縁の形＝{EdgeName(edgeStyle)}。");
    }

    /// <summary>待機中の見た目（帯は画面の外、火星は無し）。</summary>
    private void ApplyIdleVisual()
    {
        if (shaderEdge)
        {
            // 境界を画面の外（入口側）へ置く。縁のゆれが出ても画面に入ってこないだけの余白を取ってある
            PushMaterial(-EdgeMargin(), 1f, 0f, null);

            if (band != null)
            {
                band.enabled = true;
            }
            return;
        }

        ParkBand();
    }

    /// <summary>
    /// 進み具合（0〜1）に合わせて帯を描く。
    /// **0＝帯が画面の外（入口側）、1＝帯がすっかり扫け切った（出口側）。**
    /// </summary>
    private void ApplyVisual(float progress)
    {
        if (band == null)
        {
            return;
        }

        float p = Mathf.Clamp01(progress);
        bool leftToRight = step == null || step.direction == RoundWinWipeDirection.LeftToRight;

        if (shaderEdge)
        {
            float margin = EdgeMargin();

            // 境界は -余白 → 1+余白 まで動く（右→左のときは逆向き）
            float boundary = leftToRight
                ? Mathf.Lerp(-margin, 1f + margin, p)
                : Mathf.Lerp(1f + margin, -margin, p);

            // ① は「境界より入口側」を塗る。③ は帯が去ったあとなので、反対側を塗る
            bool sweepIn = phase == Phase.SweepIn;
            float coverSign = leftToRight == sweepIn ? 1f : -1f;

            PushMaterial(boundary, coverSign, SparkAmount(p), step);
            return;
        }

        ApplyBandOffset(p, leftToRight);
    }

    /// <summary>
    /// 縁のゆれ（<see cref="edgeAmplitude"/>）が出ても画面の中に残らないための余白。
    /// ② で帯が画面をすっかり隠していなければならないので、**ゆれの大きさより必ず大きくする。**
    /// </summary>
    private float EdgeMargin()
    {
        return Mathf.Max(0.15f, Mathf.Abs(edgeAmplitude) * 1.5f + 0.05f);
    }

    /// <summary>退場（③）のときの火星の量。0〜1（退場が始まってから <see cref="sparkRamp"/> の割合いっぱいになる）。</summary>
    private float SparkAmount(float progress)
    {
        if (!sparksOnExit || phase != Phase.SweepOut)
        {
            return 0f;
        }

        float ramp = Mathf.Max(0.01f, sparkRamp);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / ramp));
    }

    /// <summary>
    /// 縁・火星の値をまとめてシェーダーへ渡す。
    /// **再生中にインスペクターを変えると、ここで拾われて次のフレームで反映される。**
    /// </summary>
    private void PushMaterial(float boundary, float coverSign, float spark, RoundWinWipeStep source)
    {
        if (bandMaterial == null)
        {
            return;
        }

        RectTransform rect = band != null ? band.rectTransform : null;
        float aspect = rect != null && rect.rect.height > 0.01f ? rect.rect.width / rect.rect.height : 1.777f;

        bandMaterial.SetFloat(IdBoundary, boundary);
        bandMaterial.SetFloat(IdCoverSign, coverSign);
        bandMaterial.SetFloat(IdSoftness, Mathf.Max(0.0005f, edgeSoftness));
        bandMaterial.SetFloat(IdEdgeStyle, (float)edgeStyle);
        bandMaterial.SetFloat(IdEdgeAmplitude, edgeAmplitude);
        bandMaterial.SetFloat(IdEdgeDetail, Mathf.Max(0.1f, edgeDetail));
        bandMaterial.SetFloat(IdEdgeSpeed, edgeSpeed);
        bandMaterial.SetFloat(IdFlameHeight, Mathf.Max(0f, flameHeight));
        bandMaterial.SetFloat(IdFlameDetail, Mathf.Max(0.5f, flameDetail));
        bandMaterial.SetFloat(IdFlameSpeed, Mathf.Max(0f, flameSpeed));
        bandMaterial.SetFloat(IdFlameMidScale, Mathf.Clamp01(flameMidScale));
        bandMaterial.SetFloat(IdFlameCoreScale, Mathf.Clamp01(flameCoreScale));
        bandMaterial.SetColor(IdFlameMidColor, source != null ? source.flameMidColor : Color.white);
        bandMaterial.SetColor(IdFlameCoreColor, source != null ? source.flameCoreColor : Color.white);
        bandMaterial.SetFloat(IdScreenAspect, aspect);

        bandMaterial.SetColor(IdRimColor, source != null ? source.rimColor : Color.white);
        bandMaterial.SetFloat(IdRimWidth, Mathf.Max(0.0005f, rimWidth));
        bandMaterial.SetFloat(IdRimStrength, rimStrength);

        bandMaterial.SetColor(IdSparkColor, source != null ? source.sparkColor : Color.white);
        bandMaterial.SetFloat(IdSparkAmount, spark);
        bandMaterial.SetFloat(IdSparkDensity, Mathf.Max(1f, sparkDensity));
        bandMaterial.SetFloat(IdSparkSpread, Mathf.Max(0.001f, sparkSpread));
        bandMaterial.SetFloat(IdSparkSpeed, sparkSpeed);
        bandMaterial.SetFloat(IdSparkThreshold, Mathf.Clamp01(sparkThreshold));
    }

    /// <summary>
    /// シェーダーが使えないときの代わり：**帯の画像そのものを横へ動かす**（縁はまっすぐになる）。
    /// ①（扫けてくる）は 0＝画面の外の入口側 → 1＝画面をすっぽり隠した、
    /// ③（退場）は 0＝すっぽり隠した → 1＝画面の外の出口側。
    /// </summary>
    private void ApplyBandOffset(float progress, bool leftToRight)
    {
        RectTransform rect = band.rectTransform;
        float width = Mathf.Max(1f, rect.rect.width);

        // すっぽり隠した位置を 0 とする。左からなら入口は -1（画面の左の外）、出口は +1（右の外）
        float entry = leftToRight ? -1f : 1f;
        float exit = -entry;

        float unit = phase == Phase.SweepIn
            ? Mathf.Lerp(entry, 0f, progress)
            : Mathf.Lerp(0f, exit, progress);

        Vector2 anchored = rect.anchoredPosition;
        anchored.x = unit * width;
        rect.anchoredPosition = anchored;
    }

    /// <summary>シェーダーが使えないとき、帯を画面の外（次に扫けてくる側の外）へ退避して描かないようにする。</summary>
    private void ParkBand()
    {
        if (band == null)
        {
            return;
        }

        RectTransform rect = band.rectTransform;
        float width = Mathf.Max(1f, rect.rect.width);

        RoundWinWipeStep next = NextStep();
        bool leftToRight = next == null || next.direction == RoundWinWipeDirection.LeftToRight;

        Vector2 anchored = rect.anchoredPosition;
        anchored.x = (leftToRight ? -1f : 1f) * width;
        rect.anchoredPosition = anchored;

        band.enabled = false;
    }

    /// <summary>次に押したときに使うかたまり。無ければ null。</summary>
    private RoundWinWipeStep NextStep()
    {
        if (steps == null || steps.Length == 0)
        {
            return null;
        }

        return steps[Mathf.Clamp(stepIndex, 0, steps.Length - 1)];
    }

    // ------------------------------------------------------------
    // 文字
    // ------------------------------------------------------------

    /// <summary>文字を出す（<paramref name="show"/> が false なら隠す）。中身はそのときに入れ直す。</summary>
    private void ShowText(bool show)
    {
        if (labelText == null)
        {
            return;
        }

        string label = show && step != null ? step.label : string.Empty;

        if (labelText.text != label)
        {
            labelText.text = label;
        }

        labelText.enabled = show;
    }

    /// <summary>日本語を出せる文字を入れる。大きさは <paramref name="size"/> を基準にする。</summary>
    private static void ApplyFont(Text text, int size)
    {
        if (text != null && text.font == null)
        {
            text.font = UiFont.Find(size);
        }
    }

    // ------------------------------------------------------------
    // キーボード
    // ------------------------------------------------------------

    /// <summary>スペース＝再生、矢印＝速度と文字の秒数、E＝縁の形、R＝待機に戻す。</summary>
    private void ReadKeyboard()
    {
        if (!allowKeyboard)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            Play();
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            ResetToIdle();
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            CycleEdgeStyle();
        }

        // Shift を押していると細かい刻み（5分の1）
        bool fine = keyboard.shiftKey.isPressed;
        float speedDelta = fine ? speedStep * 0.2f : speedStep;
        float textDelta = fine ? textStep * 0.2f : textStep;

        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            SweepSpeed = sweepSpeed - speedDelta;
        }
        else if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            SweepSpeed = sweepSpeed + speedDelta;
        }

        if (keyboard.downArrowKey.wasPressedThisFrame)
        {
            TextSeconds = textSeconds - textDelta;
        }
        else if (keyboard.upArrowKey.wasPressedThisFrame)
        {
            TextSeconds = textSeconds + textDelta;
        }
    }

    /// <summary>縁の形を「まっすぐ → 水波 → 炎 → まっすぐ…」の順に切り替える（`E` キー）。</summary>
    private void CycleEdgeStyle()
    {
        RoundWinWipeEdge[] all = { RoundWinWipeEdge.Straight, RoundWinWipeEdge.Wave, RoundWinWipeEdge.Flame };
        int index = 0;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == edgeStyle)
            {
                index = (i + 1) % all.Length;
                break;
            }
        }

        EdgeStyle = all[index];

        Debug.Log($"[WINWIPE] 縁の形：{EdgeName(edgeStyle)}　掃く速さ {sweepSpeed:0.00} 画面/秒・文字 {textSeconds:0.00} 秒" +
                  (shaderEdge ? string.Empty : "　※縁のシェーダーが使えないので、どれにしてもまっすぐなままです"));
    }

    // ------------------------------------------------------------
    // 画面の案内
    // ------------------------------------------------------------

    private void SetPhase(Phase next)
    {
        phase = next;
        hintDirty = true;
    }

    /// <summary>
    /// いまの段階・次のかたまり・速度・秒数を画面に出す。
    /// **毎フレームは組み立てない**（文字の組み立てはゴミが出るので、
    /// 値が変わった合図があるときか、4分の1秒に1回だけ見直す）。
    /// 4分の1秒ごとに見るのは、再生中にインスペクターで値を直接いじった場合も追いつくようにするため。
    /// </summary>
    private void UpdateHint()
    {
        if (hintText == null)
        {
            return;
        }

        hintTimer += Time.unscaledDeltaTime;

        if (!hintDirty && hintTimer < 0.25f)
        {
            return;
        }

        hintTimer = 0f;
        hintDirty = false;

        string next = BuildHint();

        if (next == hintCache)
        {
            return;
        }

        hintCache = next;
        hintText.text = next;
    }

    private string BuildHint()
    {
        RoundWinWipeStep next = NextStep();
        int count = steps != null ? steps.Length : 0;

        string label = next == null ? "（かたまり無し）" : next.label;
        string arrow = next == null ? "―" : DirectionName(next.direction);

        return
            $"[段階] {PhaseName(phase)}　[次] {label}（{arrow}）　{stepIndex + 1}/{count} 番目\n" +
            $"[掃く速さ] {sweepSpeed:0.00} 画面/秒（1回 {OneSweepSeconds():0.00} 秒）　← → で変更\n" +
            $"[文字の秒数] {textSeconds:0.00} 秒（このあいだ帯は動かない）　↑ ↓ で変更\n" +
            $"[縁の形] {EdgeName(edgeStyle)}　ゆれ {edgeAmplitude:0.000}・細かさ {edgeDetail:0.0}・速さ {edgeSpeed:0.00}" +
            $"　炎の舌 {flameHeight:0.000}・本数 {flameDetail:0.0}・ちらつき {flameSpeed:0.00}" +
            $"　火星 {(sparksOnExit ? "あり" : "なし")}　E で切り替え\n" +
            (shaderEdge
                ? "[操作] ボタン or スペース＝再生　R＝最初に戻す　Shift＋矢印＝細かい刻み　※再生中の変更は保存されません"
                : "[注意] 縁のシェーダーが使えないので、まっすぐな縁で動いています（Console を見てください）");
    }

    /// <summary>①または③の1回にかかる秒数（速さから求める。案内の表示用）。</summary>
    private float OneSweepSeconds()
    {
        return 1f / Mathf.Max(0.01f, sweepSpeed);
    }

    private static string DirectionName(RoundWinWipeDirection direction)
    {
        return direction == RoundWinWipeDirection.LeftToRight ? "左→右" : "右→左";
    }

    private static string EdgeName(RoundWinWipeEdge style)
    {
        switch (style)
        {
            case RoundWinWipeEdge.Wave: return "水波";
            case RoundWinWipeEdge.Flame: return "炎";
            default: return "まっすぐ";
        }
    }

    private static string PhaseName(Phase value)
    {
        switch (value)
        {
            case Phase.SweepIn: return "① 掃けてくる";
            case Phase.TextHold: return "② 文字（帯は停止）";
            case Phase.SweepOut: return "③ 掃けて退場（火星）";
            default: return "待機";
        }
    }
}
