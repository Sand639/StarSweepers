using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出（プレイヤーが降りてきて、チームを順に映す）。**（2026/10/28・大槻さん）
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ⓪ 読み込み待ち | カメラは ① の回り始めの位置。プレイヤーは空の上で止まっている。マップの名前と「ラウンド ○」が出る | 1.5 秒 |
/// | ① 回って着地 | カメラが**ステージの中心を狙って、横に 30 度・上に 15 度回る**。その間に**プレイヤーが空から降りてきて、終わりに着地する** | 3 秒 |
/// | ② 青チーム | 着地したら、カメラが**青チームの中心を狙って、少し斜めから映す位置**へ移る（1 秒で移って、残りは止まる） | 3 秒 |
/// | ③ 赤チーム | 同じように赤チームを映す（3チーム以上なら、チームの数だけ続く） | 3 秒 |
/// | ④ カウントダウン | **ゲームの全体の画面に切り替えて**、3・2・1 → START!（ここから動ける） | 3 秒 |
///
/// プレイヤーが着地する場所は、ふだんの出てくる場所（<see cref="SpaceJunkPlayerSetup.TryGetGoalSpawnPosition"/>。ゴールエリアの上の決まった場所）。
/// その真上から降ろす。**全員のPCで全員ぶん**置く（自分のぶんはその位置が通信で配られ、
/// ほかの人のぶんは、通信の遅れで降り方がずれて見えないように、こちらで上書きする）。着地した時点で、ずれは 0 になる。
///
/// 秒数・角度・距離は、マップに置いた <see cref="SpaceJunkRound"/>（プレハブ）の「プレイヤーが降りてきてチームを順に映す演出」で変えられる。
/// **時間は全員で同期された時計（ServerTime）で決めている**ので、どのPCでも同じ場面になる。
/// <see cref="SpaceJunkRound"/> が、演出の種類が LandingTour のときに自分で付ける。
/// ほかの演出は <see cref="SpaceJunkGoalFlyIntro"/>（ゴールエリアが飛んでくる）と <see cref="SpaceJunkRoundIntro"/>（カメラが回る）。
///
/// `DefaultExecutionOrder(10000)` で、ほかの部品より**あとに** LateUpdate が動くようにしてある。
/// 通信の位置合わせ（NetworkTransform。LateUpdate より前に動く）が置いたほかの人の位置を、そのフレームのうちに上書きするため。
/// </summary>
[DefaultExecutionOrder(10000)]
public class SpaceJunkLandingIntro : MonoBehaviour
{
    /// <summary>START! の文字を出しておく秒数。</summary>
    private const float StartTextSeconds = 1f;

    /// <summary>②③ カメラが狙う高さ（チームの足もとから上へ。m）。乗っている人の胸のあたり。</summary>
    private const float TeamTargetHeight = 1f;

    /// <summary>
    /// ②③ カメラが移るときに、道筋を上へふくらませる量（移る水平の距離に対する割合）。
    /// まっすぐ移ると、チームからチームへ移るときに低いままステージの真ん中を通り、真ん中の物に突っ込んだ。
    /// 真上へふくらませるだけだと、途中で真下を向いて画面が回った（どちらも画像で確かめた）ので、
    /// ステージの中心のまわりを南側から回り込みつつ、少しだけ上へふくらませる（<see cref="SwingAround"/>）。
    /// </summary>
    private const float MoveArc = 0.2f;

    /// <summary>演出のカメラ。演出中だけ入る（名前のタグがこちらを向くために使う）。</summary>
    public static Camera ViewCamera { get; private set; }

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

    /// <summary>着地し終えたか。</summary>
    private bool landed;

    /// <summary>マップの大きさを、もう求めたか（求めるのは1回だけ）。</summary>
    private bool boundsReady;

    /// <summary>マップの大きさ（中央からいちばん遠いゴールまでの水平の距離＋4m。メートル）。① のカメラの距離に使う。</summary>
    private float radius = 20f;

    /// <summary>降りてくる間に切った、プレイヤーの当たり判定（着地したら戻す）。</summary>
    private readonly List<CharacterController> disabledControllers = new List<CharacterController>();

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

        // ① が始まってからの秒数（⓪ 読み込み待ちの間はマイナス）と、① の終わり・チームを映し終わる時刻
        float sinceIntro = (float)(now - round.IntroStartServerTime);
        float landEnd = round.LandingSeconds;
        float shotsEnd = landEnd + round.TeamShotSeconds * round.TeamShotCount;

        // START! の文字を出し終えた／ラウンドが終わった（結果の画面など）なら、片付けて終わる
        if (sinceStart > StartTextSeconds || round.Phase != SpaceJunkRoundPhase.Playing)
        {
            Finish();
            return;
        }

        EnsureCameraAndOverlay();
        PrepareBounds();

        if (!landed)
        {
            if (sinceIntro < landEnd)
            {
                DropPlayers(sinceIntro, landEnd);
            }
            else
            {
                Land();
            }
        }

        UpdateCamera(sinceIntro, landEnd, shotsEnd);

        // 文字：⓪① はマップの名前 → ②③ は何も出さない → 全体の画面に切り替えたら 3・2・1 → START!
        // 数字は START までの残り秒数を切り上げて出す（2.4 秒なら「3」）
        if (sinceIntro < landEnd)
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
    // プレイヤー
    // ------------------------------------------------------------

    /// <summary>
    /// ⓪① **全員を、着地する場所の真上に置く**（全員のPCで、全員ぶん）。
    /// 高さは Landing Drop Height から、① の終わりに 0 になる。だんだん速く落ちるようにする（落ちてくる感じを出すため）。
    /// 降りている間は当たり判定（CharacterController）を切る（ほかの人のぶんは元から切れている）。
    /// </summary>
    private void DropPlayers(float sinceIntro, float landEnd)
    {
        // 下の Mathf.Max(0.01f, …) は、秒数が 0 に設定されたときに 0 で割らないための保険。⓪ の間（マイナス）はいちばん上
        float t = Mathf.Clamp01(sinceIntro / Mathf.Max(0.01f, landEnd));
        float height = round.LandingDropHeight * (1f - t * t);
        PlacePlayers(height);
    }

    /// <summary>着地する。全員を着地する場所（高さ 0）に置いてから、当たり判定を戻す。</summary>
    private void Land()
    {
        landed = true;
        PlacePlayers(0f);
        RestoreControllers();
    }

    /// <summary>全員を、着地する場所から <paramref name="height"/> だけ上に置く（当たり判定は切る）。</summary>
    private void PlacePlayers(float height)
    {
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

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                disabledControllers.Add(controller);
            }

            player.transform.position = spot + Vector3.up * height;
        }
    }

    /// <summary>切った当たり判定を戻す（着地したとき・途中で片付けるとき）。</summary>
    private void RestoreControllers()
    {
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
    // カメラ
    // ------------------------------------------------------------

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// ⓪① ステージの中心を狙って回る → ②③ チームを順に映す（前の映し方から、Team Shot Move Seconds で移ってから止まる）
    /// → 映し終えたら、重ねていたカメラを止めて、ふだんのカメラ（全体の画面）に切り替える。
    /// 移る間は、カメラの位置と「狙う所」をそれぞれなめらかに動かし、狙う所のほうを向かせる（向きだけを回すより、移る途中も狙いが外れない）。
    /// 位置は、ステージの中心のまわりを回り込むように動かす（<see cref="SwingAround"/>）。
    /// </summary>
    private void UpdateCamera(float sinceIntro, float landEnd, float shotsEnd)
    {
        if (introCamera == null || !introCamera.enabled)
        {
            return;
        }

        if (sinceIntro >= shotsEnd)
        {
            // 全体の画面に切り替える（文字はまだ出ていてよい）
            introCamera.enabled = false;
            ViewCamera = null;
            return;
        }

        Vector3 position;
        Vector3 target;

        if (sinceIntro < landEnd)
        {
            GetOrbitPose(sinceIntro / Mathf.Max(0.01f, landEnd), out position, out target);
        }
        else
        {
            // 何番目のチームを映しているか（0 から）と、そのチームを映し始めてからの秒数
            float shotSeconds = Mathf.Max(0.01f, round.TeamShotSeconds);
            int shot = Mathf.Min(round.TeamShotCount - 1, Mathf.FloorToInt((sinceIntro - landEnd) / shotSeconds));
            float inShot = sinceIntro - landEnd - shot * shotSeconds;

            // 前の映し方（1つ目のチームなら ① の終わり、それ以外は前のチーム）から移る
            Vector3 fromPosition;
            Vector3 fromTarget;
            if (shot == 0)
            {
                GetOrbitPose(1f, out fromPosition, out fromTarget);
            }
            else
            {
                GetTeamPose(shot - 1, out fromPosition, out fromTarget);
            }

            GetTeamPose(shot, out Vector3 toPosition, out Vector3 toTarget);
            float blend = Mathf.SmoothStep(0f, 1f, inShot / Mathf.Max(0.01f, round.TeamShotMoveSeconds));

            position = SwingAround(fromPosition, toPosition, blend);
            target = Vector3.Lerp(fromTarget, toTarget, blend);
        }

        introCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
    }

    /// <summary>
    /// 2つのカメラの位置の間を、**ステージの中心のまわりを回り込むように**移る（進み具合 0〜1）。
    /// 中心から見た水平の向き・中心からの距離・高さを、それぞれなめらかに変える。
    /// 回る向きは、**ふだんのカメラのある南側を通るほう**（北側を通ると、カメラが画面の奥側へ回り込んで見え方が逆になるため）。
    /// 高さは、真ん中で移る水平の距離の <see cref="MoveArc"/> 倍だけ上へふくらませる。
    /// </summary>
    private Vector3 SwingAround(Vector3 from, Vector3 to, float blend)
    {
        Vector3 fromOffset = Flat(from - Center);
        Vector3 toOffset = Flat(to - Center);

        // 上から見て東（+x）を 0 度とし、北（+z）へ向かって増える角度。まずは近いほうの回り方で、真ん中が北側なら反対回りにする
        float fromAngle = Mathf.Atan2(fromOffset.z, fromOffset.x) * Mathf.Rad2Deg;
        float turn = Mathf.DeltaAngle(fromAngle, Mathf.Atan2(toOffset.z, toOffset.x) * Mathf.Rad2Deg);
        if (Mathf.Sin((fromAngle + turn * 0.5f) * Mathf.Deg2Rad) > 0f)
        {
            turn -= Mathf.Sign(turn) * 360f;
        }

        float angle = (fromAngle + turn * blend) * Mathf.Deg2Rad;
        float distance = Mathf.Lerp(fromOffset.magnitude, toOffset.magnitude, blend);
        float arc = Flat(to - from).magnitude * MoveArc * Mathf.Sin(blend * Mathf.PI);

        return new Vector3(
            Center.x + Mathf.Cos(angle) * distance,
            Mathf.Lerp(from.y, to.y, blend) + arc,
            Center.z + Mathf.Sin(angle) * distance);
    }

    /// <summary>
    /// ⓪① ステージの中心を狙って回るカメラ（進み具合 0〜1。始めと終わりをゆっくりにする）。
    /// ふだんのカメラと同じ南側を真ん中にして、横に Landing Orbit Degrees（左右に半分ずつ）回り、
    /// 見下ろす角度は Landing Start Pitch から Landing Orbit Rise Degrees だけ上がる。
    /// </summary>
    private void GetOrbitPose(float progress, out Vector3 position, out Vector3 target)
    {
        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        float yaw = Mathf.Lerp(-0.5f, 0.5f, eased) * round.LandingOrbitDegrees;
        float pitch = Mathf.Clamp(round.LandingStartPitch + round.LandingOrbitRiseDegrees * eased, 1f, 89f) * Mathf.Deg2Rad;

        Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
        float distance = radius * round.LandingCameraDistanceScale;

        target = Center;
        position = Center + outward * (distance * Mathf.Cos(pitch)) + Vector3.up * (distance * Mathf.Sin(pitch));
    }

    /// <summary>
    /// ②③ そのチームを映すカメラ。チームの中心を狙い、**ステージの中心の側（プレイヤーが向いている側）から、
    /// Team Shot Angle だけ横に回り込んだ斜め**から、Team Shot Pitch で見下ろす。
    /// </summary>
    private void GetTeamPose(int team, out Vector3 position, out Vector3 target)
    {
        GetTeamCenter(team, out Vector3 center, out float spread);
        target = center + Vector3.up * TeamTargetHeight;

        Vector3 inward = Flat(Center - center);
        inward = inward.sqrMagnitude < 0.01f ? Vector3.back : inward.normalized;
        Vector3 direction = Quaternion.Euler(0f, round.TeamShotAngle, 0f) * inward;

        float pitch = round.TeamShotPitch * Mathf.Deg2Rad;
        float distance = round.TeamShotDistance + spread;
        position = target + direction * (distance * Mathf.Cos(pitch)) + Vector3.up * (distance * Mathf.Sin(pitch));
    }

    /// <summary>
    /// そのチームの中心（プレイヤー全員の足もとの平均）と、中心からいちばん遠い味方までの水平の距離。
    /// そのチームに誰もいなければ、そのチームのゴールの位置（それも無ければマップの中央）。
    /// </summary>
    private void GetTeamCenter(int team, out Vector3 center, out float spread)
    {
        center = Vector3.zero;
        spread = 0f;

        SpaceJunkSession session = SpaceJunkSession.Current;
        int count = 0;

        if (session != null)
        {
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player != null && player.IsSpawned && session.TeamOf(player.PlayerKey) == team)
                {
                    center += player.transform.position;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            SpaceJunkGoal goal = SpaceJunkGoal.FirstOwnedBy(team, gameObject.scene);
            center = goal != null ? goal.transform.position : Center;
            return;
        }

        center /= count;

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && session.TeamOf(player.PlayerKey) == team)
            {
                spread = Mathf.Max(spread, Flat(player.transform.position - center).magnitude);
            }
        }
    }

    /// <summary>
    /// マップの大きさ（中央からの半径）を決める。**このマップのゴールの位置**から求める（ゴールはマップの端にあるため）。
    /// 8〜80m に収める。
    /// </summary>
    private void PrepareBounds()
    {
        if (boundsReady)
        {
            return;
        }

        boundsReady = true;
        radius = 0f;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == gameObject.scene)
            {
                radius = Mathf.Max(radius, Flat(goal.transform.position - Center).magnitude);
            }
        }

        radius = Mathf.Clamp(radius + 4f, 8f, 80f);
    }

    /// <summary>高さを 0 にした（地面と平行にした）ベクトル。</summary>
    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    // ------------------------------------------------------------
    // 文字（ほかの演出と同じ見た目）
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
    /// 途中で終わっても（結果の画面へ移った・シーンが変わった）、切った当たり判定は戻す。
    /// </summary>
    private void Cleanup()
    {
        RestoreControllers();

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
