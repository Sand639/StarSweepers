using Unity.Netcode;
using UnityEngine;

/// <summary>ラウンドがいまどの段階か。</summary>
public enum SpaceJunkRoundPhase
{
    /// <summary>集めている最中</summary>
    Playing,

    /// <summary>決着がついて結果を出している</summary>
    Result
}

/// <summary>ラウンドの勝ち方。</summary>
public enum SpaceJunkWinRule
{
    /// <summary>**時間いっぱい得点を競う**（2026/9/22 から。いまのルール）</summary>
    Score,

    /// <summary>素材3種類をそろえたチームが、その場で勝つ（2026/9/17 の最初のルール。コードだけ残してある）</summary>
    ThreeKinds
}

/// <summary>ラウンドの始まりの演出の種類。</summary>
public enum SpaceJunkIntroStyle
{
    /// <summary>ゴールエリアがプレイヤーを乗せて飛んできて、元の位置に設置される（2026/10/27〜）</summary>
    GoalFlyIn,

    /// <summary>カメラがマップを回って見せる（2026/10/7〜10/26 の演出。また使うかもしれないので、切り替えて使えるように残してある）</summary>
    CameraTour,

    /// <summary>
    /// **ステージを回って見せる間に、ゴールエリアがプレイヤーを乗せて飛んできて着地し、チームごとに別のカメラで順に映す**（2026/10/28 から試している演出）。
    /// 一番後ろに足したのは、前からある種類（GoalFlyIn・CameraTour）の番号を変えないため（保存された値がずれないように）
    /// </summary>
    LandingTour
}

/// <summary>
/// **1ラウンドぶんの進行役。** マップ（ステージ）のシーンに1つ置く。
///
/// 勝ち方は2つあり、<see cref="SpaceJunkSession"/> の Win Rule で切り替える（ロビーには出していない）。
///
/// ## 得点制（Score・いまのルール。2026/9/22・大槻さん）
///
/// - **時間いっぱい遊び、得点の高いチームがラウンドを取る**（途中で決着はつかない）
/// - 素材を自陣のゴールに入れると **1点**。同じ種類を2個目以降入れても1点ずつ入る
/// - **ラウンドの途中でイベントが起きる**（初期値は20秒後。<c>SpaceJunkRoundEvents.cs</c>）。
///   いまあるのは「お題セット」で、お題の素材を全部入れるとボーナスが入る
///   （2026/9/29。それまでの「同じ種類を3回続けると +5」はこれに置き換えた）
/// - **1位が同点なら引き分け**で、誰も取らずに次へ行く（全チーム0点も引き分け）
///
/// ## 3種類そろえたら勝ち（ThreeKinds・最初のルール。2026/9/17・大槻さん）
///
/// - **自陣のゴールに素材3種類を1個ずつ入れたチームが、その場でラウンド勝利**
/// - 誰もそろえられないまま最大時間に達したら、**集めた種類が多いチームがラウンドを取る**
/// - **1位が同数（引き分け）なら、そのラウンドは誰も取らずに次へ行く**
/// - 同じ種類を2個目入れても**何も起きない**（消えるだけ）
///
/// ## 誰が判定するか
///
/// **ホストだけ。** 参加者のPCは結果を受け取って出すだけで、一切数えていない
/// （それぞれが数えると、同じ素材で何度も数えてしまう）。
///
/// ## 勝ち数はここに持たない
///
/// ラウンドの勝ち数と設定は <see cref="SpaceJunkSession"/> が持っている。
/// こちらは**シーンと一緒に消える**ので、覚えておきたいものは置かない。
/// </summary>
public partial class SpaceJunkRound : NetworkBehaviour
{
    /// <summary>いま動いているラウンド。ゴールや画面表示から探すために持っている。</summary>
    public static SpaceJunkRound Current { get; private set; }

    /// <summary>
    /// いま動いてよいか。**結果が出たあとは動かせなくする**ために使う。
    /// ラウンドの進行役がいないシーン（釣りなど）では常に true。
    /// </summary>
    public static bool PlayAllowed => Current == null || Current.IsPlaying;

    [Header("最大時間（係がいないときの予備）")]
    [Tooltip("SpaceJunkSession が見つからないときに使う秒数。" +
             "**ふだんはホストがロビーで決めた値が使われる**ので、ここは効かない")]
    [SerializeField] private float fallbackSeconds = 60f;

    [Header("始まりの演出（2026/10/7。10/20 に流れを作り直した。いろいろ試すため、秒数と角度はここで変えられる）")]
    [Tooltip("OFF にすると演出をせず、すぐに始まる")]
    [SerializeField] private bool playIntro = true;

    [Tooltip("演出の種類。LandingTour：ステージを回る間にゴールエリアとプレイヤーが飛んできて着地し、チームごとに順に映す（2026/10/28〜）／" +
             "GoalFlyIn：ゴールエリアがプレイヤーを乗せて飛んでくる（2026/10/27）／" +
             "CameraTour：カメラがマップを回って見せる（2026/10/7〜10/26）")]
    [SerializeField] private SpaceJunkIntroStyle introStyle = SpaceJunkIntroStyle.LandingTour;

    [Tooltip("全員の読み込みを待つ秒数（演出の前）。遅いPCでも最初から見られるように。この間は ① の始めの位置で止まる")]
    [Min(0f)]
    [SerializeField] private float introLeadSeconds = 1.5f;

    [Header("↓ ①〜⑥ はカメラが回る演出（Intro Style が CameraTour）のときだけ使う。一番下のカウントダウンの秒数は両方で使う")]
    [Tooltip("① マップをぐるっと見せる秒数")]
    [Min(0f)]
    [SerializeField] private float introFlyoverSeconds = 3f;

    [Tooltip("① マップの紹介で、カメラが向き続ける場所（マップの中央）")]
    [SerializeField] private Vector3 introLookCenter = Vector3.zero;

    [Tooltip("① マップの紹介で、真上（Y軸）から何度傾けた所から見るか。0 で真上、90 で真横")]
    [Range(0f, 90f)]
    [SerializeField] private float introFlyoverTilt = 45f;

    [Tooltip("① マップの紹介で、マップの中央を中心に回る角度（度）")]
    [SerializeField] private float introFlyoverDegrees = 180f;

    [Tooltip("② 自分のチームへカメラが寄る秒数")]
    [Min(0f)]
    [SerializeField] private float introZoomSeconds = 1f;

    [Tooltip("② 寄ったときの、チームの中央から後ろ（マップの外側）への距離（水平）。味方が離れているほど、自動で少し足す")]
    [Min(0f)]
    [SerializeField] private float introCloseDistance = 8f;

    [Tooltip("② 寄ったときの高さ（チームの中央から）")]
    [SerializeField] private float introCloseHeight = 3.5f;

    [Tooltip("③ 寄ったあと、止まっている秒数")]
    [Min(0f)]
    [SerializeField] private float introHoldBeforeOrbitSeconds = 0.5f;

    [Tooltip("④ ② の位置から、チームの中央を中心に回る秒数（0 で回らない）")]
    [Min(0f)]
    [SerializeField] private float introTeamOrbitSeconds = 1.5f;

    [Tooltip("④ チームの中央を中心に回る角度（度）。マイナスにすると逆向きに回る")]
    [SerializeField] private float introTeamOrbitDegrees = -120f;

    [Tooltip("④ 回り終わったときの、チームの中央からの距離（水平）。② の距離から少しずつ離れる")]
    [Min(0f)]
    [SerializeField] private float introOrbitEndDistance = 10f;

    [Tooltip("④ 回り終わったときの高さ。② の高さから少しずつ上がり、上からチームを映す")]
    [SerializeField] private float introOrbitEndHeight = 10f;

    [Tooltip("⑤ 回ったあと、止まっている秒数")]
    [Min(0f)]
    [SerializeField] private float introHoldAfterOrbitSeconds = 1f;

    [Tooltip("⑥ 全体カメラ（ふだんのカメラ）の位置へ移る秒数")]
    [Min(0f)]
    [SerializeField] private float introReturnSeconds = 1f;

    [Tooltip("④ が始まってから、3・2・1 のカウントダウンが始まるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float introCountdownDelaySeconds = 1f;

    [Tooltip("3・2・1 のカウントダウンの秒数。終わったら START!（ここから動ける）。どちらの演出でも使う")]
    [Min(0f)]
    [SerializeField] private float introCountdownSeconds = 3f;

    [Header("ゴールが飛んでくる演出（Intro Style が GoalFlyIn のとき。2026/10/27）")]
    [Tooltip("① ゴールエリアがプレイヤーを乗せて、マップの奥から手前へ飛んでくる秒数。" +
             "名前のタグは、この秒数を人数で割った間隔で1人ずつ順に強調する")]
    [Min(0f)]
    [SerializeField] private float flyInSeconds = 3f;

    [Tooltip("① どちらの奥から飛んでくるか（水平の向き）。(0, 0, 1) で北＝ふだんのカメラの画面の奥")]
    [SerializeField] private Vector3 flyInFrom = Vector3.forward;

    [Tooltip("① 飛び始めの位置が、マップに近づいた位置（① の終わり）よりどれだけ奥か（m）")]
    [Min(0f)]
    [SerializeField] private float flyInDistance = 45f;

    [Tooltip("① 飛んでいるときの高さ（元のゴールの高さから。m）")]
    [SerializeField] private float flyInHeight = 4f;

    [Tooltip("① ゴールエリアどうしを横に並べて飛ぶときの、すき間（m）")]
    [Min(0f)]
    [SerializeField] private float flyInGap = 3f;

    [Tooltip("① カメラが見下ろす角度（度）。カメラはゴールエリアの手前（飛んでくる先）の斜め上から、乗っている人を映したまま一緒に下がる")]
    [Range(5f, 85f)]
    [SerializeField] private float flyInCameraPitch = 45f;

    [Tooltip("① 飛び始めのとき、カメラが ① の終わりのときの何倍離れているか。" +
             "1 より大きいほど、カメラがゴールエリアよりゆっくり下がり、ゴールエリアがこちらへ近づいてくるように見える")]
    [Min(1f)]
    [SerializeField] private float flyInCameraStartScale = 1.8f;

    [Tooltip("② マップに近づいたあと、元のゴールの位置へ飛んで設置されるまでの秒数。設置されたらカウントダウンが始まる")]
    [Min(0f)]
    [SerializeField] private float flyInLandSeconds = 1.5f;

    [Tooltip("① 名前のタグを強調するときの倍率。自分の番の間はこの大きさになり、番が終わると元の大きさに戻る")]
    [Min(1f)]
    [SerializeField] private float nameEmphasisScale = 1.8f;

    [Header("ゴールエリアと一緒に着地してチームを順に映す演出（Intro Style が LandingTour のとき。2026/10/28）")]
    [Tooltip("① ステージの中心を狙ってカメラが回る秒数。この間に、ゴールエリアがプレイヤーを乗せて飛んできて、終わりに着地（設置）する。" +
             "飛び方は「ゴールが飛んでくる演出」と同じ（Fly In From・Fly In Distance・Fly In Height・Fly In Gap を使い、" +
             "Fly In Seconds と Fly In Land Seconds の割合のまま、この秒数に縮めて入れる）")]
    [Min(0f)]
    [SerializeField] private float landingSeconds = 3f;

    [Tooltip("① カメラが横に回る角度（度）。ふだんのカメラと同じ南側を真ん中にして、左右に半分ずつ回る")]
    [SerializeField] private float landingOrbitDegrees = 45f;

    [Tooltip("① 回り始めの、カメラが見下ろす角度（度）。全体カメラ（ステージのふだんのカメラ。約 60 度）より少し低くして始める")]
    [Range(5f, 80f)]
    [SerializeField] private float landingStartPitch = 50f;

    [Tooltip("① 回る間に、カメラが上へ回る角度（度）。回り始めの角度にこれを足した角度で終わる")]
    [SerializeField] private float landingOrbitRiseDegrees = 10f;

    [Tooltip("① カメラとステージの中心の距離が、ステージの大きさ（中央からいちばん遠いゴール＋4m）の何倍か。全体カメラは約 1.7 倍")]
    [Min(0.5f)]
    [SerializeField] private float landingCameraDistanceScale = 1.5f;

    [Tooltip("②③ チームを1つ映す秒数（青 → 赤 …の順に、チームの数だけ映す）。チームごとの別のカメラに切り替えて映す。" +
             "全部映し終えたら、全体の画面（ふだんのカメラ）に切り替わってカウントダウン")]
    [Min(0f)]
    [SerializeField] private float teamShotSeconds = 3f;

    [Tooltip("②③ チームの中心からカメラまでの距離（m）。味方が離れているほど、自動で少し足す")]
    [Min(1f)]
    [SerializeField] private float teamShotDistance = 7f;

    [Tooltip("②③ チームを映すときに見下ろす角度（度）")]
    [Range(0f, 80f)]
    [SerializeField] private float teamShotPitch = 25f;

    [Tooltip("②③ チームを映すときの斜めの角度（度）。0 でチームの真正面（ステージの中心の側）から、大きいほど横に回り込む")]
    [SerializeField] private float teamShotAngle = 25f;

    /// <summary>演出の種類。</summary>
    public SpaceJunkIntroStyle IntroStyle => introStyle;

    // ↓ ゴールが飛んでくる演出の設定を、演出の部品 SpaceJunkGoalFlyIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① ゴールエリアが飛んでくる秒数。</summary>
    public float FlyInSeconds => flyInSeconds;

    /// <summary>① どちらの奥から飛んでくるか（水平の向き）。</summary>
    public Vector3 FlyInFrom => flyInFrom;

    /// <summary>① 飛び始めの位置が、マップに近づいた位置よりどれだけ奥か。</summary>
    public float FlyInDistance => flyInDistance;

    /// <summary>① 飛んでいるときの高さ。</summary>
    public float FlyInHeight => flyInHeight;

    /// <summary>① ゴールエリアどうしのすき間。</summary>
    public float FlyInGap => flyInGap;

    /// <summary>① カメラが見下ろす角度（度）。</summary>
    public float FlyInCameraPitch => flyInCameraPitch;

    /// <summary>① 飛び始めのとき、カメラが ① の終わりのときの何倍離れているか。</summary>
    public float FlyInCameraStartScale => flyInCameraStartScale;

    /// <summary>② 元の位置へ飛んで設置されるまでの秒数。</summary>
    public float FlyInLandSeconds => flyInLandSeconds;

    /// <summary>① 名前のタグを強調するときの倍率。</summary>
    public float NameEmphasisScale => nameEmphasisScale;

    // ↓ ゴールエリアと一緒に着地する演出の設定を、演出の部品 SpaceJunkLandingIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① カメラが回り、ゴールエリアがプレイヤーを乗せて飛んできて着地するまでの秒数。</summary>
    public float LandingSeconds => landingSeconds;

    /// <summary>① カメラが横に回る角度。</summary>
    public float LandingOrbitDegrees => landingOrbitDegrees;

    /// <summary>① 回り始めの、カメラが見下ろす角度。</summary>
    public float LandingStartPitch => landingStartPitch;

    /// <summary>① 回る間に、カメラが上へ回る角度。</summary>
    public float LandingOrbitRiseDegrees => landingOrbitRiseDegrees;

    /// <summary>① カメラとステージの中心の距離（ステージの大きさの何倍か）。</summary>
    public float LandingCameraDistanceScale => landingCameraDistanceScale;

    /// <summary>②③ チームを1つ映す秒数。</summary>
    public float TeamShotSeconds => teamShotSeconds;

    /// <summary>②③ チームの中心からカメラまでの距離。</summary>
    public float TeamShotDistance => teamShotDistance;

    /// <summary>②③ チームを映すときに見下ろす角度。</summary>
    public float TeamShotPitch => teamShotPitch;

    /// <summary>②③ チームを映すときの斜めの角度。</summary>
    public float TeamShotAngle => teamShotAngle;

    /// <summary>②③ 映すチームの数（遊んでいるチーム数。係がいなければ 1）。どのPCでも同じ値になる。</summary>
    public int TeamShotCount => SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

    // ↓ 演出の設定を、演出の部品 SpaceJunkRoundIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① マップをぐるっと見せる秒数（演出の部品 <see cref="SpaceJunkRoundIntro"/> が読む）。</summary>
    public float IntroFlyoverSeconds => introFlyoverSeconds;

    /// <summary>① カメラが向き続ける場所（マップの中央）。</summary>
    public Vector3 IntroLookCenter => introLookCenter;

    /// <summary>① 真上から何度傾けた所から見るか。</summary>
    public float IntroFlyoverTilt => introFlyoverTilt;

    /// <summary>① マップの中央を中心に回る角度。</summary>
    public float IntroFlyoverDegrees => introFlyoverDegrees;

    /// <summary>② 自分のチームへカメラが寄る秒数。</summary>
    public float IntroZoomSeconds => introZoomSeconds;

    /// <summary>② 寄ったときの、チームの中央から後ろへの距離。</summary>
    public float IntroCloseDistance => introCloseDistance;

    /// <summary>② 寄ったときの高さ。</summary>
    public float IntroCloseHeight => introCloseHeight;

    /// <summary>③ 寄ったあと止まっている秒数。</summary>
    public float IntroHoldBeforeOrbitSeconds => introHoldBeforeOrbitSeconds;

    /// <summary>④ チームの周りを回る秒数。</summary>
    public float IntroTeamOrbitSeconds => introTeamOrbitSeconds;

    /// <summary>④ チームの周りを回る角度（マイナスで逆向き）。</summary>
    public float IntroTeamOrbitDegrees => introTeamOrbitDegrees;

    /// <summary>④ 回り終わったときの、チームの中央からの距離。</summary>
    public float IntroOrbitEndDistance => introOrbitEndDistance;

    /// <summary>④ 回り終わったときの高さ。</summary>
    public float IntroOrbitEndHeight => introOrbitEndHeight;

    /// <summary>⑤ 回ったあと止まっている秒数。</summary>
    public float IntroHoldAfterOrbitSeconds => introHoldAfterOrbitSeconds;

    /// <summary>⑥ 全体カメラ（ふだんのカメラ）へ移る秒数。</summary>
    public float IntroReturnSeconds => introReturnSeconds;

    /// <summary>④ が始まってから、カウントダウンが始まるまでの秒数。</summary>
    public float IntroCountdownDelaySeconds => introCountdownDelaySeconds;

    /// <summary>3・2・1 のカウントダウンの秒数。</summary>
    public float IntroCountdownSeconds => introCountdownSeconds;

    /// <summary>
    /// 演出の長さ（① 〜START まで。読み込み待ちは入れない）。
    /// ・プレイヤーが降りてくる演出 … ① 回って着地 ＋ チームを映す秒数 × チーム数 ＋ カウントダウン
    /// ・ゴールが飛んでくる演出 … ① 飛んでくる ＋ ② 設置 ＋ カウントダウン
    /// ・カメラが回る演出 … カウントダウンは ④ の途中から数えるので、④⑤⑥ のカメラの動きはこの中に入ったり、START をまたいだりする
    /// </summary>
    private float IntroTotalSeconds
    {
        get
        {
            switch (introStyle)
            {
                case SpaceJunkIntroStyle.LandingTour:
                    return landingSeconds + teamShotSeconds * TeamShotCount + introCountdownSeconds;
                case SpaceJunkIntroStyle.GoalFlyIn:
                    return flyInSeconds + flyInLandSeconds + introCountdownSeconds;
                default:
                    return introFlyoverSeconds + introZoomSeconds + introHoldBeforeOrbitSeconds + introCountdownDelaySeconds +
                           introCountdownSeconds;
            }
        }
    }

    /// <summary>
    /// **動き出してよい時刻**（全員で同期された時計 ServerTime）。これより前は始まりの演出中で、誰も動けない。
    /// 0 なら演出なし。
    /// </summary>
    private readonly NetworkVariable<double> playStartServerTime = new NetworkVariable<double>(0d);

    /// <summary>動き出してよい時刻（ServerTime）。0 なら演出なし。</summary>
    public double PlayStartServerTime => playStartServerTime.Value;

    /// <summary>始まりの演出が始まる時刻（ServerTime）。</summary>
    public double IntroStartServerTime =>
        playStartServerTime.Value - IntroTotalSeconds;

    /// <summary>いまの時刻（全員で同期された時計）。</summary>
    public double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

    /// <summary>**始まりの演出中か**（マップの紹介〜START）。この間は誰も動けず、素材も出ない。</summary>
    public bool IsIntro =>
        phase.Value == SpaceJunkRoundPhase.Playing && playStartServerTime.Value > 0d && ServerNow < playStartServerTime.Value;

    /// <summary>前のフレームで演出中だったか（演出が終わった瞬間に操作を戻すため）。</summary>
    private bool wasIntro;

    /// <summary>
    /// チームごとの、集めた素材の種類。**1ビットが1種類**（装甲板=1 / 回路基板=2 / 燃料タンク=4）。
    /// 長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。
    /// </summary>
    private readonly NetworkList<int> collected = new NetworkList<int>();

    /// <summary>いまの段階。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<SpaceJunkRoundPhase> phase =
        new NetworkVariable<SpaceJunkRoundPhase>(SpaceJunkRoundPhase.Playing);

    /// <summary>
    /// ラウンドが終わる時刻。**全員で同期された時計（ServerTime）で持つ**ので、
    /// どのPCでも同じ残り時間が出る。
    /// </summary>
    private readonly NetworkVariable<double> endServerTime = new NetworkVariable<double>(0d);

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    private readonly NetworkVariable<int> roundWinner = new NetworkVariable<int>(-1);

    /// <summary>このラウンドの勝ち方。**ホストが開始時に決めて全員に配る。**</summary>
    private readonly NetworkVariable<SpaceJunkWinRule> rule =
        new NetworkVariable<SpaceJunkWinRule>(SpaceJunkWinRule.Score);

    /// <summary>チームごとの得点（得点制のとき）。長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。</summary>
    private readonly NetworkList<int> scores = new NetworkList<int>();

    /// <summary>素材1個を入れたときの得点。</summary>
    public const int PointPerItem = 1;

    /// <summary>このラウンドの勝ち方。</summary>
    public SpaceJunkWinRule Rule => rule.Value;

    /// <summary>そのチームの得点（得点制のとき）。</summary>
    public int ScoreOf(int team)
    {
        return team >= 0 && team < scores.Count ? scores[team] : 0;
    }

    /// <summary>集めている最中か（始まりの演出中は false）。</summary>
    public bool IsPlaying => phase.Value == SpaceJunkRoundPhase.Playing && !IsIntro;

    /// <summary>いまの段階。</summary>
    public SpaceJunkRoundPhase Phase => phase.Value;

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    public int RoundWinner => roundWinner.Value;

    /// <summary>残り秒数。どのPCでも同じ値になる。</summary>
    public float RemainingSeconds
    {
        get
        {
            if (NetworkManager == null)
            {
                return 0f;
            }

            // **始まりの演出中（3・2・1 の間も）は、決めた制限時間のまま止めておく。** START から減り始める（2026/10/7）。
            // そのまま引き算すると、演出の残りのぶん多く出る（60秒なのに 63秒 など）
            if (IsIntro)
            {
                return Mathf.Max(0f, (float)(endServerTime.Value - playStartServerTime.Value));
            }

            return Mathf.Max(0f, (float)(endServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>そのチームが、その種類をもう持っているか。</summary>
    public bool HasKind(int team, SpaceJunkMaterialKind kind)
    {
        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        return (collected[team] & KindBit(kind)) != 0;
    }

    /// <summary>そのチームが集めた種類の数（0〜3）。</summary>
    public int KindCountOf(int team)
    {
        if (team < 0 || team >= collected.Count)
        {
            return 0;
        }

        int mask = collected[team];
        int count = 0;

        for (int i = 0; i < SpaceJunkMaterials.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>種類をビットに直す。</summary>
    private static int KindBit(SpaceJunkMaterialKind kind)
    {
        return 1 << (int)kind;
    }

    /// <summary>そのマップで出る種類が全部そろった状態のビット（3種類のマップなら 0b111）。</summary>
    private static int FullMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < SpaceJunkMaterials.Count; i++)
            {
                if (SpaceJunkMaterials.IsUsed(SpaceJunkMaterials.FromIndex(i)))
                {
                    mask |= 1 << i;
                }
            }
            return mask;
        }
    }

    // ------------------------------------------------------------
    // 出入り
    // ------------------------------------------------------------

    /// <summary>
    /// 全員のPCで、ラウンドの係が現れたとき（マップのシーンが読み込まれた直後）。
    /// ホストはラウンドの中身を決めて配り（<see cref="ServerInitRound"/>）、
    /// **全員のPCで操作の止め／戻し・始まりの演出の準備・ゴールの持ち主の割り当てをする。**
    /// </summary>
    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            ServerInitRound();
        }

        phase.OnValueChanged += OnPhaseChanged;

        // **前のラウンドで止めた操作を、ここで戻す。**（演出中なら、まだ止めておく。演出が終わったら Update で戻す）
        // プレイヤーはシーンをまたいで生き続けるので、止めたままだと次のラウンドで動けなくなる
        wasIntro = IsIntro;
        SetLocalPlayerControlEnabled(!wasIntro);

        // 始まりの演出を、全員のPCで出す。演出の種類で部品を選ぶ（使っていない種類も、切り替えて試せるように残してある）
        if (introStyle == SpaceJunkIntroStyle.LandingTour)
        {
            if (GetComponent<SpaceJunkLandingIntro>() == null)
            {
                gameObject.AddComponent<SpaceJunkLandingIntro>();
            }
        }
        else if (introStyle == SpaceJunkIntroStyle.GoalFlyIn)
        {
            if (GetComponent<SpaceJunkGoalFlyIntro>() == null)
            {
                gameObject.AddComponent<SpaceJunkGoalFlyIntro>();
            }
        }
        else if (GetComponent<SpaceJunkRoundIntro>() == null)
        {
            gameObject.AddComponent<SpaceJunkRoundIntro>();
        }

        ApplyGoalOwners();

        if (IsServer)
        {
            CheckGoalsArePlayable();
        }
    }

    /// <summary>
    /// **ホストがラウンドの中身を決めて配る。** 得点と集めた種類を 0 に戻し、勝ち方と最大時間は試合の係（ロビーの設定）から取る。
    /// 動き出す時刻は始まりの演出のぶん後ろへずらし、終わる時刻とイベントの時刻はそこから数える。
    /// </summary>
    private void ServerInitRound()
    {
        collected.Clear();
        scores.Clear();

        for (int i = 0; i < SpaceJunkTeams.MaxTeams; i++)
        {
            collected.Add(0);
            scores.Add(0);
        }

        rule.Value = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.WinRule
            : SpaceJunkWinRule.Score;

        phase.Value = SpaceJunkRoundPhase.Playing;
        roundWinner.Value = -1;

        float seconds = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.RoundSeconds
            : fallbackSeconds;

        // **始まりの演出のぶん、動き出す時刻を後ろにずらす。** 残り時間とイベントは、動き出してから数える
        double now = NetworkManager.ServerTime.Time;
        double playStart = playIntro
            ? now + introLeadSeconds + IntroTotalSeconds
            : 0d;
        playStartServerTime.Value = playStart;

        endServerTime.Value = (playStart > 0d ? playStart : now) + seconds;

        ServerInitEvents(seconds);
    }

    /// <summary>
    /// **このマップで、全チームがちゃんと戦えるかを見る。**
    ///
    /// ゴールが足りないマップだと、**あるチームは素材を入れる場所が無く、
    /// 絶対にラウンドを取れない**。遊んでいる最中は気づきにくいので、開始時に知らせる。
    ///
    /// （2026/9/20：`SpaceJunkMap03` はコピー元の `FishingMap03` にゴールが1つしか
    /// 置かれておらず、2チーム以上だと片方が何もできない状態だった）
    /// </summary>
    private void CheckGoalsArePlayable()
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

        // ゴールの数そのものは見ない（1対1のマップはゴールが2つ。2026/9/30）。
        // 大事なのは「遊んでいる全チームが自分のゴールを持てるか」なので、下でそれを確かめる。
        // （ロビーでも、遊べないマップはチーム数に合わせて候補から外している）

        // 遊んでいるチームに、持ち場のゴールがあるかを1つずつ見る
        for (int team = 0; team < teamCount; team++)
        {
            bool hasGoal = false;

            foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
            {
                if (goal != null && goal.OwnerTeam == team)
                {
                    hasGoal = true;
                    break;
                }
            }

            if (!hasGoal)
            {
                Debug.LogError(
                    $"[JUNK] {SpaceJunkTeams.TeamName(team)} のゴールが、このマップにありません。" +
                    "**このチームは素材を入れる場所が無く、ラウンドを取れません。**");
            }
        }
    }

    /// <summary>係が消えるとき（次のマップやロビーへ移る）。</summary>
    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;

        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>段階が変わったとき（全員のPCで動く。ホストが変えた値が届いたタイミング）。</summary>
    private void OnPhaseChanged(SpaceJunkRoundPhase before, SpaceJunkRoundPhase after)
    {
        // 結果が出たら動けなくする（結果を見る時間になる）
        SetLocalPlayerControlEnabled(after == SpaceJunkRoundPhase.Playing);
    }

    /// <summary>
    /// 毎フレーム。全員のPCでは重いデブリの見た目と演出の終わりを見て、
    /// **ホストだけが**イベントの進行と最大時間の決着を見る。
    /// </summary>
    private void Update()
    {
        // 重いデブリの見た目は、全員のPCで付け外しする
        UpdateHeavyMarks();

        // **始まりの演出が終わった瞬間に、操作を戻す**（全員のPC。START! の合図と同時に動ける）
        bool intro = IsIntro;
        if (wasIntro && !intro && phase.Value == SpaceJunkRoundPhase.Playing)
        {
            SetLocalPlayerControlEnabled(true);
        }
        wasIntro = intro;

        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing || intro)
        {
            return;
        }

        ServerUpdateEvents();

        // 最大時間に達した
        if (NetworkManager.ServerTime.Time >= endServerTime.Value)
        {
            if (rule.Value == SpaceJunkWinRule.Score)
            {
                FinishByScore();
            }
            else
            {
                FinishByCount();
            }
        }
    }

    // ------------------------------------------------------------
    // 素材を受け取る（**ホストだけが呼ぶ**）
    // ------------------------------------------------------------

    /// <summary>
    /// **ゴールに素材が入ったことを受け取る。** <see cref="SpaceJunkGoal"/> から呼ばれる。
    ///
    /// 得点制なら点を足す。3種類ルールなら、すでに持っている種類のときは何もしない（素材は消えるだけ）。
    /// </summary>
    /// <returns>数えたら true（ゴールの床が光る）。</returns>
    public bool ServerCollect(int team, SpaceJunkMaterialKind kind, NetworkObject item = null)
    {
        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return false;
        }

        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        // **誰もいないチームのぶんは数えない。**
        // 数えてしまうと、投げ間違いが積み重なるだけで
        // 「誰もいないチーム」がラウンドを取ってしまう
        if (SpaceJunkSession.Current != null && !SpaceJunkSession.Current.HasPlayers(team))
        {
            return false;
        }

        if (rule.Value == SpaceJunkWinRule.Score)
        {
            ServerAddScore(team, kind, item);
            return true;
        }

        return ServerCollectKind(team, kind);
    }

    /// <summary>
    /// **3種類ルールの受け取り方。** 新しい種類なら印を付け、そのマップで出る種類が全部そろったらその場でラウンド勝利。
    /// もう持っている種類なら何も起きない（false）。
    /// </summary>
    private bool ServerCollectKind(int team, SpaceJunkMaterialKind kind)
    {
        int bit = KindBit(kind);

        if ((collected[team] & bit) != 0)
        {
            // もう持っている種類。**何も起きない**
            return false;
        }

        collected[team] = collected[team] | bit;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を集めました" +
                  $"（{KindCountOf(team)} / {SpaceJunkMaterials.Count} 種類）。");

        // そのマップで出る種類が全部そろったら、その場でラウンド勝利
        if ((collected[team] & FullMask) == FullMask)
        {
            FinishRound(team);
        }

        return true;
    }

    /// <summary>
    /// **得点制の点の足し方。** 1個で1点。イベントが起きていれば、そのぶんのボーナスも足す
    /// （お題セットなら、お題がそろった1個でボーナスが入る）。
    /// </summary>
    private void ServerAddScore(int team, SpaceJunkMaterialKind kind, NetworkObject item)
    {
        // 集めた種類の印も付けておく（画面の表示に使う）
        collected[team] = collected[team] | KindBit(kind);

        // ステージに置いた重いデブリなら、その得点（イベントのボーナスとは重ねない）
        int bonus = ServerTryStageHeavyCollect(team, item, out int heavyBonus)
            ? heavyBonus
            : ServerEventBonusOnCollect(team, kind, item);

        // 真ん中の特殊デブリ（SpaceJunkBonusDebris）なら、その点数も足す
        if (item != null && item.TryGetComponent(out SpaceJunkBonusDebris bonusDebris))
        {
            bonus += bonusDebris.BonusPoints;
        }

        int gained = PointPerItem + bonus;

        scores[team] = scores[team] + gained;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を入れました" +
                  $"（+{gained} 点{(bonus > 0 ? $"。ボーナス {bonus} 点込み" : string.Empty)}／" +
                  $"合計 {scores[team]} 点）。");
    }

    // ------------------------------------------------------------
    // 決着（**ホストだけが判定する**）
    // ------------------------------------------------------------

    /// <summary>
    /// 得点制で、最大時間に達したときの決着。**得点の高いチームがラウンドを取る。**
    /// 1位が同点なら引き分けで、誰も取らない（全チーム0点も引き分け）。
    /// </summary>
    private void FinishByScore()
    {
        FinishRound(LeadingTeamOrTie(ScoreOf));
    }

    /// <summary>
    /// 最大時間に達したときの決着。**集めた種類が多いチームがラウンドを取る。**
    /// 1位が同数なら引き分けで、誰も取らない。
    /// </summary>
    private void FinishByCount()
    {
        // 1位が同数なら引き分け。**誰も集めていない（全員0種類）ときも引き分け**
        FinishRound(LeadingTeamOrTie(KindCountOf));
    }

    /// <summary>
    /// 遊んでいるチームのうち、<paramref name="valueOf"/>（得点、または集めた種類の数）が一番大きいチーム。
    /// **1位が並んでいる、または1位の値が 0 以下なら -1（引き分け）。**
    /// 得点制と3種類ルールで同じ比べ方をするので、ここにまとめてある。
    /// </summary>
    private int LeadingTeamOrTie(System.Func<int, int> valueOf)
    {
        int bestTeam = -1;
        int bestValue = -1;
        bool tied = false;

        int teams = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.TeamCount
            : SpaceJunkTeams.MaxTeams;

        for (int team = 0; team < teams; team++)
        {
            int value = valueOf(team);

            if (value > bestValue)
            {
                bestValue = value;
                bestTeam = team;
                tied = false;
            }
            else if (value == bestValue)
            {
                tied = true;
            }
        }

        return tied || bestValue <= 0 ? -1 : bestTeam;
    }

    /// <summary>ラウンドを終わらせて、試合の係へ結果を渡す。</summary>
    private void FinishRound(int winnerTeam)
    {
        if (phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return;
        }

        roundWinner.Value = winnerTeam;
        phase.Value = SpaceJunkRoundPhase.Result;

        Debug.Log(winnerTeam < 0
            ? "[JUNK] ラウンド終了。引き分け（誰もラウンドを取りません）。"
            : $"[JUNK] ラウンド終了。{SpaceJunkTeams.TeamName(winnerTeam)} がラウンドを取りました。");

        if (SpaceJunkSession.Current != null)
        {
            SpaceJunkSession.Current.ServerReportRoundResult(winnerTeam);
        }
        else
        {
            Debug.LogWarning("[JUNK] 試合の係（SpaceJunkSession）がいないので、次のラウンドへ進めません。");
        }
    }

    // ------------------------------------------------------------
    // ゴールの持ち主と、操作の止め方
    // ------------------------------------------------------------

    /// <summary>
    /// **そのゴールが、どのチームのものか。**
    ///
    /// 全員が同じ計算（チーム数 → 割り当て）をするので、通信で送る必要はない。
    /// ゴールの側からも呼べるようにしてある（**生まれる順番がどちらでも取りこぼさないため**）。
    /// </summary>
    public int OwnerTeamOfGoal(int goalIndex)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

        // **このマップにあるゴールも見て決める**（東西だけのマップなら、2チームは東西を使う。2026/9/30）
        int[] owners = SpaceJunkTeams.GoalOwners(teamCount, GoalMaskOfThisMap());

        return owners[Mathf.Clamp(goalIndex, 0, owners.Length - 1)];
    }

    /// <summary>
    /// **このマップにあるゴールの番号**（北=1 / 東=2 / 南=4 / 西=8 を足したもの）。
    /// シーンの切り替え途中に前のマップのゴールが残っていることがあるので、**このシーンのゴールだけ**数える。
    /// </summary>
    private int GoalMaskOfThisMap()
    {
        int mask = 0;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == gameObject.scene &&
                goal.GoalIndex >= 0 && goal.GoalIndex < SpaceJunkTeams.GoalCount)
            {
                mask |= 1 << goal.GoalIndex;
            }
        }

        return mask;
    }

    /// <summary>シーンにあるゴールへ、どのチームのものかをまとめて配る。</summary>
    private void ApplyGoalOwners()
    {
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null)
            {
                goal.ApplyOwnerTeam(OwnerTeamOfGoal(goal.GoalIndex));
            }
        }
    }

    /// <summary>
    /// **このPCで操作しているプレイヤーの、移動とフックを止める／戻す。**
    ///
    /// 釣りでは <c>FishingMatch.PlayAllowed</c> がこの役目をしているが、
    /// 宇宙ごみのシーンには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）。
    /// **釣り側のファイルを一切変えずに済ませるために、ここで直接止めている。**
    /// </summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }

            // **オートエイムも一緒に戻す。** ロビーの設定端末は、開いている間これを止める。
            // ホストは設定を開いたまま「ゲーム開始」を押すので、ここで戻さないと
            // **ホストだけマップでオートエイムが効かない**ままになる
            HookAimAssist aimAssist = player.GetComponent<HookAimAssist>();
            if (aimAssist != null)
            {
                aimAssist.enabled = enabledState;
            }
        }
    }
}
