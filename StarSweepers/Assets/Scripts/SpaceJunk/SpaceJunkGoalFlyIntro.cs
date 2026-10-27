using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出（ゴールエリアが飛んでくる）。**（2026/10/27・大槻さん）
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ⓪ 読み込み待ち | ゴールエリアは奥の飛び始めの位置で止まっている。マップの名前と「ラウンド ○」が出る | 1.5 秒 |
/// | ① 飛んでくる | 各チームのゴールエリアが、そのチームのプレイヤーを乗せ、横に並んで**マップの奥から手前へ**飛んでくる。
///   カメラはゴールエリアの手前の斜め上（45 度）から、ゴールエリア全部の中心を狙って一緒に下がる。
///   ゴールエリアより少しゆっくり下がるので、最初から乗っている人が映ったまま、だんだんこちらへ近づいて見える。
///   名前のタグを、カメラから見て左の人から順に1人ずつ強調する（① の秒数 ÷ 人数 ごとに、拡大と縮小をくり返す） | 3 秒 |
/// | ② 設置 | マップに近づいたら、それぞれ**元のゴールの位置へ飛んで設置される**。カメラはふだんのカメラへ移る | 1.5 秒 |
/// | ③ カウントダウン | 設置されたら 3・2・1 → START!（ここから動ける） | 3 秒 |
///
/// 飛んでいるのは**ゴールの見た目の写し**（当たり判定なし）。本物のゴールは動かさず、演出のあいだ見た目だけ隠す
/// （本物を動かすと、ボットが作る歩ける床の地図や、ゴールの判定がずれるため）。
/// 写すのは <see cref="SpaceJunkGoal.IntroVisual"/>（ふだんは色を塗る床。ゴールエリアを3Dモデルに替えたら、そのモデル）。
///
/// プレイヤーは、**設置されたときに立つ場所**（<see cref="SpaceJunkPlayerSetup.TryGetGoalSpawnPosition"/>。ゴールエリアの上の決まった場所）を
/// ゴールエリアと同じだけ動かした所に、**全員のPCで全員ぶん**置く。自分のぶんはその位置が通信で配られ、
/// ほかの人のぶんは、通信の遅れでゴールエリアからずれて見えないように、こちらで上書きする。設置された時点で、ずれは 0 になる。
///
/// 秒数・向き・高さは、マップに置いた <see cref="SpaceJunkRound"/>（プレハブ）の「ゴールが飛んでくる演出」で変えられる。
/// **時間は全員で同期された時計（ServerTime）で決めている**ので、どのPCでも同じ場面になる。
/// <see cref="SpaceJunkRound"/> が、演出の種類が GoalFlyIn のときに自分で付ける。前の演出（カメラが回る）は <see cref="SpaceJunkRoundIntro"/>。
///
/// `DefaultExecutionOrder(10000)` で、ほかの部品より**あとに** LateUpdate が動くようにしてある。
/// 通信の位置合わせ（NetworkTransform。LateUpdate より前に動く）が置いたほかの人の位置を、そのフレームのうちに上書きするため。
/// </summary>
[DefaultExecutionOrder(10000)]
public class SpaceJunkGoalFlyIntro : MonoBehaviour
{
    /// <summary>START! の文字を出しておく秒数。</summary>
    private const float StartTextSeconds = 1f;

    /// <summary>② 元の位置へ飛ぶときに、途中でふくらむ高さ（m）。まっすぐ降りるより「飛んでいく」感じにするため。</summary>
    private const float LandArcHeight = 1.5f;

    /// <summary>① のカメラが狙う高さ（ゴールエリアの中心から上へ。m）。乗っている人の胸のあたり。</summary>
    private const float CameraTargetHeight = 1f;

    /// <summary>① の終わりに画面の左右に残す余白（m）。</summary>
    private const float CameraSideMargin = 1.5f;

    /// <summary>見た目が無いゴールの、横に並べるときの幅（m）。</summary>
    private const float DefaultGoalWidth = 4f;

    /// <summary>演出のカメラ。演出中だけ入る（名前のタグがこちらを向くために使う）。</summary>
    public static Camera ViewCamera { get; private set; }

    /// <summary>名前のタグの大きさの倍率（強調している人だけ入る）。</summary>
    private static readonly Dictionary<FishingNetPlayer, float> emphasis = new Dictionary<FishingNetPlayer, float>();

    /// <summary>そのプレイヤーの名前のタグの大きさの倍率。強調していなければ 1。</summary>
    public static float NameEmphasisOf(FishingNetPlayer player)
    {
        return player != null && emphasis.TryGetValue(player, out float scale) ? scale : 1f;
    }

    /// <summary>飛んでくるゴールエリア1つぶん。</summary>
    private class FlyingGoal
    {
        /// <summary>元のゴール（動かさない）。</summary>
        public SpaceJunkGoal Goal;

        /// <summary>設置される場所（元のゴールの位置）。</summary>
        public Vector3 Final;

        /// <summary>① の始まり（奥）。</summary>
        public Vector3 Start;

        /// <summary>① の終わり（マップに近づいた所）。</summary>
        public Vector3 Approach;

        /// <summary>① で回しておく角度（度）。プレイヤーが並ぶ向きが、カメラから見て左右になるように回す。② で 0 に戻る。</summary>
        public float FlyYaw;

        /// <summary>横に並べるときの幅（m）。</summary>
        public float Width = DefaultGoalWidth;

        /// <summary>いまの位置（毎フレーム決める）。</summary>
        public Vector3 Position;

        /// <summary>いまの向き（毎フレーム決める）。</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>飛んでいる見た目の写し（当たり判定なし）。</summary>
        public GameObject Copy;

        /// <summary>演出のあいだ隠している、本物の見た目。</summary>
        public readonly List<Renderer> Hidden = new List<Renderer>();
    }

    /// <summary>同じ物に付いているラウンドの係。演出の時刻と設定はここから読む。</summary>
    private SpaceJunkRound round;

    /// <summary>演出のあいだだけ、ふだんのカメラの上に重ねるカメラ。</summary>
    private Camera introCamera;

    /// <summary>文字を出す画面（Canvas）。</summary>
    private GameObject overlay;

    /// <summary>真ん中の大きな文字（マップの名前・3・2・1・START!）。</summary>
    private Text bigText;

    /// <summary>その下の小さな文字（「ラウンド ○」）。</summary>
    private Text subText;

    /// <summary>演出が終わったか。終わったら、このラウンドの間は二度と動かない。</summary>
    private bool finished;

    /// <summary>飛んでくるゴールエリアを用意したか（用意するのは1回だけ）。</summary>
    private bool prepared;

    /// <summary>設置し終えたか。</summary>
    private bool landed;

    /// <summary>飛んでくるゴールエリア（カメラから見て左から順）。</summary>
    private readonly List<FlyingGoal> flying = new List<FlyingGoal>();

    /// <summary>① で飛んでくる元の向き（奥）。</summary>
    private Vector3 backward = Vector3.forward;

    /// <summary>カメラから見た右（奥を向いたときの右）。</summary>
    private Vector3 right = Vector3.right;

    /// <summary>① の終わりの、狙う所からカメラまでの距離（横に並んだゴールエリアが画面の幅にちょうど収まる距離。m）。</summary>
    private float cameraDistance = 10f;

    /// <summary>演出のカメラの画角（ふだんのカメラから写した値。① の間は変えない）。</summary>
    private float baseFieldOfView = 60f;

    /// <summary>ゴールエリアに乗せるために切った、プレイヤーの当たり判定（設置したら戻す）。</summary>
    private readonly List<CharacterController> disabledControllers = new List<CharacterController>();

    /// <summary>乗っているプレイヤーと、カメラから見た左右の位置（毎フレーム作り直す。名前のタグを強調する順番に使う）。</summary>
    private readonly List<(FishingNetPlayer player, float across)> riders = new List<(FishingNetPlayer, float)>();

    private void Awake()
    {
        round = GetComponent<SpaceJunkRound>();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void LateUpdate()
    {
        if (finished || round == null || !round.IsSpawned)
        {
            return;
        }

        double playStart = round.PlayStartServerTime;
        if (playStart <= 0d)
        {
            // 演出なしのラウンド
            Finish();
            return;
        }

        // 時刻はすべて、全員で同期された時計（ServerTime）で測る。どのPCでも同じ場面になるように
        double now = round.ServerNow;

        // START からの秒数。START 前はマイナス（-3 なら、あと3秒で START）
        float sinceStart = (float)(now - playStart);

        // ① が始まってからの秒数（⓪ 読み込み待ちの間はマイナス）と、①② の終わりの時刻
        float sinceIntro = (float)(now - round.IntroStartServerTime);
        float flyEnd = round.FlyInSeconds;
        float landEnd = flyEnd + round.FlyInLandSeconds;

        // START! の文字を出し終えた／ラウンドが終わった（結果の画面など）なら、片付けて終わる
        if (sinceStart > StartTextSeconds || round.Phase != SpaceJunkRoundPhase.Playing)
        {
            Finish();
            return;
        }

        EnsureCameraAndOverlay();
        Prepare();

        if (!landed)
        {
            if (sinceIntro < landEnd)
            {
                MoveGoals(sinceIntro, flyEnd);
                RidePlayers();
            }
            else
            {
                Land();
            }
        }

        UpdateEmphasis(sinceIntro, flyEnd);
        UpdateCamera(sinceIntro, flyEnd, landEnd);

        // 文字：⓪ はマップの名前 → ①② は何も出さない → 設置されたら 3・2・1 → START!
        // 数字は START までの残り秒数を切り上げて出す（2.4 秒なら「3」）
        if (sinceIntro < 0f)
        {
            ShowTexts(MapName(), RoundText());
        }
        else
        {
            string big = sinceStart >= 0f ? "START!"
                : -sinceStart <= round.IntroCountdownSeconds ? Mathf.CeilToInt(-sinceStart).ToString()
                : string.Empty;
            ShowTexts(big, string.Empty);
        }
    }

    // ------------------------------------------------------------
    // ゴールエリア
    // ------------------------------------------------------------

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// **飛んでくるゴールエリアを用意する**（1回だけ）。どこかのチームのゴール（閉ざされていないもの）を、
    /// カメラから見て左から順に横に並べ、① の始まりと終わりの位置・見た目の写し・① のカメラの位置を決める。
    /// </summary>
    private void Prepare()
    {
        if (prepared)
        {
            return;
        }

        prepared = true;

        backward = Flat(round.FlyInFrom);
        backward = backward.sqrMagnitude < 0.01f ? Vector3.forward : backward.normalized;
        right = Vector3.Cross(Vector3.up, backward);

        Scene scene = gameObject.scene;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene && !goal.IsClosed)
            {
                flying.Add(new FlyingGoal { Goal = goal, Final = goal.transform.position });
            }
        }

        // カメラから見て左から順（同じ所にあれば番号順。どのPCでも同じ並びになるように）
        flying.Sort((a, b) =>
        {
            int compare = Vector3.Dot(a.Final, right).CompareTo(Vector3.Dot(b.Final, right));
            return compare != 0 ? compare : a.Goal.GoalIndex.CompareTo(b.Goal.GoalIndex);
        });

        // マップの大きさ … 中央からいちばん遠いゴールまでの水平の距離＋4m（8〜80m に収める）
        float radius = 0f;
        foreach (FlyingGoal goal in flying)
        {
            radius = Mathf.Max(radius, Flat(goal.Final - Center).magnitude);
        }
        radius = Mathf.Clamp(radius + 4f, 8f, 80f);

        float total = round.FlyInGap * Mathf.Max(0, flying.Count - 1);
        foreach (FlyingGoal goal in flying)
        {
            BuildCopy(goal);
            goal.FlyYaw = FlyYawOf(goal.Final);
            total += goal.Width;
        }

        // ① の終わり … マップの奥の端の少し先に、横に並ぶ。① の始まりは、そこからさらに奥
        Vector3 approachCenter = Center + backward * radius;
        float cursor = -total * 0.5f;

        foreach (FlyingGoal goal in flying)
        {
            float across = cursor + goal.Width * 0.5f;
            cursor += goal.Width + round.FlyInGap;

            goal.Approach = new Vector3(approachCenter.x, goal.Final.y + round.FlyInHeight, approachCenter.z) + right * across;
            goal.Start = goal.Approach + backward * round.FlyInDistance;
            goal.Position = goal.Start;
        }

        // ① の終わりのカメラの距離 … 横に並んだゴールエリア全部（＋余白）が、画面の幅にちょうど収まる距離。
        // 画角はふだんのカメラから写したものを使い、① の間は変えない
        if (introCamera != null)
        {
            baseFieldOfView = introCamera.fieldOfView;
        }

        float aspect = introCamera != null ? Mathf.Max(0.1f, introCamera.aspect) : 16f / 9f;
        float sideTan = Mathf.Tan(baseFieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
        cameraDistance = Mathf.Max(6f, (total * 0.5f + CameraSideMargin) / Mathf.Max(0.01f, sideTan));
    }

    /// <summary>
    /// **ゴールの見た目の写しを作り、本物の見た目を隠す。** 当たり判定は写さない。
    /// 色を塗る床の材質は本物と同じもの（チームの色を塗った物）を使うので、色もそのまま出る。
    /// </summary>
    private static void BuildCopy(FlyingGoal goal)
    {
        goal.Copy = new GameObject($"FlyingGoal_{goal.Goal.name}");
        goal.Copy.transform.position = goal.Final;

        Transform root = goal.Goal.IntroVisual;
        if (root == null)
        {
            return;
        }

        if (goal.Goal.TryGetIntroVisualBounds(out Bounds bounds))
        {
            goal.Width = Mathf.Max(DefaultGoalWidth, bounds.size.x, bounds.size.z);
        }

        foreach (MeshRenderer source in root.GetComponentsInChildren<MeshRenderer>())
        {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (!source.enabled || filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            // 写しの親は元のゴールの位置にあり、回転も大きさも無いので、世界での位置・向き・大きさをそのまま入れればよい
            GameObject part = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(goal.Copy.transform, false);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;
            part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            part.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;

            source.enabled = false;
            goal.Hidden.Add(source);
        }
    }

    /// <summary>
    /// ① で回しておく角度（度）。ゴールの「横」（ゴールから中心へ向かう向きと直角。プレイヤーが並ぶ向き。
    /// <see cref="SpaceJunkPlayerSetup.TryGetGoalSpawnPosition"/> と同じ求め方）が、カメラから見て左右になる角度。
    /// 回す量が小さいほう（±90 度以内）を選ぶ。
    /// </summary>
    private float FlyYawOf(Vector3 final)
    {
        Vector3 toCenter = new Vector3(-final.x, 0f, -final.z);
        if (toCenter.sqrMagnitude < 0.01f)
        {
            toCenter = Vector3.forward;
        }

        Vector3 side = Vector3.Cross(Vector3.up, toCenter.normalized);
        float angle = Vector3.SignedAngle(side, right, Vector3.up);

        if (angle > 90f)
        {
            angle -= 180f;
        }
        else if (angle < -90f)
        {
            angle += 180f;
        }

        return angle;
    }

    /// <summary>
    /// ゴールエリアの、いまの位置と向きを決めて写しを動かす。
    /// ⓪① 奥から、マップに近づいた所まで（近づくにつれてゆっくりに）→ ② 元のゴールの位置へ飛んで設置される。
    /// </summary>
    private void MoveGoals(float sinceIntro, float flyEnd)
    {
        foreach (FlyingGoal goal in flying)
        {
            Quaternion flyRotation = Quaternion.Euler(0f, goal.FlyYaw, 0f);

            // 下の Mathf.Max(0.01f, …) は、秒数が 0 に設定されたときに 0 で割らないための保険
            if (sinceIntro < flyEnd)
            {
                // ⓪① ⓪ の間（マイナス）は飛び始めの位置で止まる。終わりに向かってゆっくりにする
                float t = Mathf.Clamp01(sinceIntro / Mathf.Max(0.01f, flyEnd));
                float eased = 1f - (1f - t) * (1f - t);
                goal.Position = Vector3.Lerp(goal.Start, goal.Approach, eased);
                goal.Rotation = flyRotation;
            }
            else
            {
                // ② 元のゴールの位置へ（途中で少しふくらむ）。向きも元に戻す
                float t = Mathf.Clamp01((sinceIntro - flyEnd) / Mathf.Max(0.01f, round.FlyInLandSeconds));
                float eased = Mathf.SmoothStep(0f, 1f, t);
                goal.Position = Vector3.Lerp(goal.Approach, goal.Final, eased) + Vector3.up * (LandArcHeight * Mathf.Sin(t * Mathf.PI));
                goal.Rotation = Quaternion.Slerp(flyRotation, Quaternion.identity, eased);
            }

            if (goal.Copy != null)
            {
                goal.Copy.transform.SetPositionAndRotation(goal.Position, goal.Rotation);
            }
        }
    }

    /// <summary>
    /// **全員を、自分のチームのゴールエリアに乗せる**（全員のPCで、全員ぶん）。
    /// 設置されたときに立つ場所を、ゴールエリアと同じだけ動かして回した所に置く。
    /// 落ちたりぶつかったりしないよう、乗っている間は当たり判定（CharacterController）を切る。
    /// </summary>
    private void RidePlayers()
    {
        riders.Clear();

        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null)
        {
            return;
        }

        Scene active = SceneManager.GetActiveScene();

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsSpawned)
            {
                continue;
            }

            SpaceJunkPlayerSetup setup = player.GetComponent<SpaceJunkPlayerSetup>();
            if (setup == null || !setup.TryGetGoalSpawnPosition(out Vector3 spot))
            {
                continue;
            }

            FlyingGoal ride = FindFlying(SpaceJunkGoal.FirstOwnedBy(session.TeamOf(player.PlayerKey), active));
            if (ride == null)
            {
                continue;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                disabledControllers.Add(controller);
            }

            Vector3 position = ride.Position + ride.Rotation * (spot - ride.Final);
            player.transform.position = position;
            riders.Add((player, Vector3.Dot(position, right)));
        }
    }

    /// <summary>そのゴールの飛んでくるゴールエリア（無ければ null）。</summary>
    private FlyingGoal FindFlying(SpaceJunkGoal goal)
    {
        foreach (FlyingGoal flyingGoal in flying)
        {
            if (flyingGoal.Goal == goal)
            {
                return flyingGoal;
            }
        }

        return null;
    }

    /// <summary>
    /// **設置する。** 全員を設置された場所（ずれ 0）に置いてから、写しを消して本物の見た目を出し、当たり判定を戻す。
    /// </summary>
    private void Land()
    {
        landed = true;

        foreach (FlyingGoal goal in flying)
        {
            goal.Position = goal.Final;
            goal.Rotation = Quaternion.identity;
        }

        RidePlayers();
        RestoreGoalsAndPlayers();
    }

    /// <summary>写しを消して本物の見た目を出し、切った当たり判定を戻す（設置したとき・途中で片付けるとき）。</summary>
    private void RestoreGoalsAndPlayers()
    {
        foreach (FlyingGoal goal in flying)
        {
            if (goal.Copy != null)
            {
                Destroy(goal.Copy);
                goal.Copy = null;
            }

            foreach (Renderer hidden in goal.Hidden)
            {
                if (hidden != null)
                {
                    hidden.enabled = true;
                }
            }

            goal.Hidden.Clear();
        }

        foreach (CharacterController controller in disabledControllers)
        {
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        disabledControllers.Clear();
    }

    // ------------------------------------------------------------
    // 名前のタグの強調
    // ------------------------------------------------------------

    /// <summary>
    /// ① の間、**カメラから見て左の人から順に、① の秒数 ÷ 人数 ごとに1人ずつ**名前のタグを強調する。
    /// 強調の間に、拡大と縮小を Name Emphasis Pulses 回くり返す。
    /// </summary>
    private void UpdateEmphasis(float sinceIntro, float flyEnd)
    {
        emphasis.Clear();

        if (landed || riders.Count == 0 || sinceIntro < 0f || sinceIntro >= flyEnd)
        {
            return;
        }

        // 同じ位置なら人ごとの番号順（どのPCでも同じ順番になるように）
        riders.Sort((a, b) =>
        {
            int compare = a.across.CompareTo(b.across);
            return compare != 0 ? compare : a.player.PlayerKey.CompareTo(b.player.PlayerKey);
        });

        float window = flyEnd / riders.Count;
        int index = Mathf.Min(riders.Count - 1, Mathf.FloorToInt(sinceIntro / window));
        float local = (sinceIntro - index * window) / window;

        // 1 → 最大 → 1 を Name Emphasis Pulses 回くり返す
        float wave = 0.5f - 0.5f * Mathf.Cos(local * round.NameEmphasisPulses * 2f * Mathf.PI);
        emphasis[riders[index].player] = Mathf.Lerp(1f, round.NameEmphasisScale, wave);
    }

    // ------------------------------------------------------------
    // カメラ
    // ------------------------------------------------------------

    /// <summary>
    /// ⓪① ゴールエリアの手前（飛んでくる先）の斜め上から、Fly In Camera Pitch（45 度）で見下ろして、ゴールエリア全部の中心を狙う。
    /// ゴールエリアと一緒に下がるが、離れ方を Fly In Camera Start Scale 倍 → 1 倍に縮めていくので、
    /// **最初から乗っている人が映ったまま、だんだんこちらへ近づいてくるように見える**（2026/10/27・大槻さん）。
    /// ② ① の終わりの見え方から、ふだんのカメラの位置へ移る（② のゴールエリアは追わない。カメラの真下や後ろへ回り込むので、
    /// 追うとカメラが振り返って、移る途中で斜めを向いてしまった）。設置し終えたら、重ねていたカメラを止める。
    /// </summary>
    private void UpdateCamera(float sinceIntro, float flyEnd, float landEnd)
    {
        if (introCamera == null || !introCamera.enabled)
        {
            return;
        }

        if (sinceIntro >= landEnd)
        {
            // ふだんのカメラへ移り終わったので、重ねていたカメラを止めて、ふだんのカメラの映像に任せる（文字はまだ出ていてよい）
            introCamera.enabled = false;
            ViewCamera = null;
            return;
        }

        // ② の間は、① の終わり（マップに近づいた位置）で止めて考える
        bool atApproach = sinceIntro >= flyEnd;

        Vector3 target = Center;
        if (flying.Count > 0)
        {
            target = Vector3.zero;
            foreach (FlyingGoal goal in flying)
            {
                target += atApproach ? goal.Approach : goal.Position;
            }
            target = target / flying.Count + Vector3.up * CameraTargetHeight;
        }

        // 離れ方：飛び始めは Start Scale 倍、① の終わりで 1 倍（ゴールエリアと同じく、終わりに向かってゆっくりにする）
        float t = atApproach ? 1f : Mathf.Clamp01(sinceIntro / Mathf.Max(0.01f, flyEnd));
        float eased = 1f - (1f - t) * (1f - t);
        float distance = cameraDistance * Mathf.Lerp(round.FlyInCameraStartScale, 1f, eased);

        // 狙う所から、手前（飛んでくる先）へ倒した向きに離れて、見下ろす
        float pitch = round.FlyInCameraPitch * Mathf.Deg2Rad;
        Vector3 position = target - backward * (distance * Mathf.Cos(pitch)) + Vector3.up * (distance * Mathf.Sin(pitch));
        Quaternion rotation = Quaternion.LookRotation(target - position, Vector3.up);
        float fieldOfView = baseFieldOfView;

        if (atApproach)
        {
            // ② ふだんのカメラへ移る。ふだんのカメラが動いていても追えるよう、毎フレームその位置を読み直して、そこへ寄せていく
            Camera main = Camera.main;
            float blend = Mathf.SmoothStep(0f, 1f, (sinceIntro - flyEnd) / Mathf.Max(0.01f, round.FlyInLandSeconds));
            if (main != null)
            {
                position = Vector3.Lerp(position, main.transform.position, blend);
                rotation = Quaternion.Slerp(rotation, main.transform.rotation, blend);
                fieldOfView = Mathf.Lerp(fieldOfView, main.fieldOfView, blend);
            }
        }

        introCamera.transform.SetPositionAndRotation(position, rotation);
        introCamera.fieldOfView = fieldOfView;
    }

    /// <summary>高さを 0 にした（地面と平行にした）ベクトル。</summary>
    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    // ------------------------------------------------------------
    // 文字（前の演出 SpaceJunkRoundIntro と同じ見た目）
    // ------------------------------------------------------------

    /// <summary>いまのマップの、ロビーに出している名前（マップの一覧に無ければシーンの名前）。</summary>
    private static string MapName()
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;
        string scene = SceneManager.GetActiveScene().name;
        return lobby != null ? lobby.LabelOf(scene) : scene;
    }

    /// <summary>「ラウンド ○」。何ラウンド目か分からなければ空。</summary>
    private static string RoundText()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        return session != null && session.CurrentRound > 0 ? $"ラウンド {session.CurrentRound}" : string.Empty;
    }

    /// <summary>文字を変える。毎フレーム呼ばれるので、中身が変わったときだけ入れ直す。</summary>
    private void ShowTexts(string big, string sub)
    {
        if (bigText != null && bigText.text != big)
        {
            bigText.text = big;
        }

        if (subText != null && subText.text != sub)
        {
            subText.text = sub;
        }
    }

    // ------------------------------------------------------------
    // 作る・片付ける
    // ------------------------------------------------------------

    /// <summary>演出用のカメラと文字の画面を、まだ無ければ作る（毎フレーム呼んでよい）。</summary>
    private void EnsureCameraAndOverlay()
    {
        if (introCamera == null && !landed)
        {
            // ふだんのカメラの上に重ねる、演出用のカメラ（ふだんのカメラの動きには手を出さない）
            GameObject cameraObject = new GameObject("RoundIntroCamera");
            cameraObject.transform.SetParent(transform, false);
            introCamera = cameraObject.AddComponent<Camera>();

            // 映し方の設定（画角・背景・映す層など）はふだんのカメラから写し、
            // depth を 10 大きくして、ふだんのカメラより後に描かせる（上に重なって、こちらの映像が見える）
            Camera main = Camera.main;
            if (main != null)
            {
                introCamera.CopyFrom(main);
                introCamera.depth = main.depth + 10f;
            }
            else
            {
                introCamera.depth = 10f;
            }

            ViewCamera = introCamera;
        }

        if (overlay == null)
        {
            BuildOverlay();
        }
    }

    /// <summary>
    /// 文字を出す画面を、コードだけで組み立てる（プレハブを用意しなくて済むように）。
    /// 位置と大きさは 1920×1080 の画面での値で、窓の大きさに合わせて伸び縮みする。
    /// </summary>
    private void BuildOverlay()
    {
        overlay = new GameObject("RoundIntroOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(transform, false);

        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85; // タイトル画面（90）・ポーズ画面（100）より後ろ

        // 1920×1080 を基準に、幅と高さの中間（0.5）で合わせて伸び縮みさせる
        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 大きな文字は画面の真ん中より 60 上、小さな文字は 110 下（文字の大きさは 160 と 56）
        Font font = UiFont.Find(64);
        bigText = CreateText("Big", font, 160, new Vector2(0f, 60f), new Vector2(1800f, 260f));
        subText = CreateText("Sub", font, 56, new Vector2(0f, -110f), new Vector2(1800f, 90f));
    }

    /// <summary>
    /// 画面の真ん中を基準にした、黒い縁取りつきの太字を1つ作る。
    /// <paramref name="position"/> は真ん中からのずれ、<paramref name="boxSize"/> は文字の枠の大きさ。
    /// </summary>
    private Text CreateText(string name, Font font, int size, Vector2 position, Vector2 boxSize)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(overlay.transform, false);

        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        // 長いマップの名前でも折り返さず、枠からはみ出して出す。
        // raycastTarget を切って、文字がマウスのクリックを横取りしないようにする
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        // 縁取り：半透明の黒を、右下へ 5 ずらして重ねる（明るい景色の上でも読めるように）
        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(5f, -5f);

        return text;
    }

    /// <summary>演出を終わりにする。このあとは LateUpdate が何もしなくなる。</summary>
    private void Finish()
    {
        finished = true;
        Cleanup();
    }

    /// <summary>
    /// 作ったものを片付ける（演出が終わったとき・この部品が消えるとき）。
    /// 途中で終わっても（結果の画面へ移った・シーンが変わった）、隠した見た目と切った当たり判定は戻す。
    /// </summary>
    private void Cleanup()
    {
        RestoreGoalsAndPlayers();
        emphasis.Clear();

        if (ViewCamera == introCamera)
        {
            ViewCamera = null;
        }

        if (introCamera != null)
        {
            Destroy(introCamera.gameObject);
            introCamera = null;
        }

        if (overlay != null)
        {
            Destroy(overlay);
            overlay = null;
        }
    }
}
