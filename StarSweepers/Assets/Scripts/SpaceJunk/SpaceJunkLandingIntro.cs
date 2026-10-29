using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出（ゴールエリアと一緒に着地する）。**（2026/10/28・大槻さん。10/29 から、ラウンドの間（2ラウンド目から）に使う）
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ⓪ 読み込み待ち | カメラは ① の回り始めの位置。ゴールエリアとプレイヤーは奥の飛び始めの位置で止まっている。マップの名前と「ラウンド ○」が出る | 1.5 秒 |
/// | ① 回って着地 | カメラが**ステージの中心を狙って、全体カメラより少し低い角度（50 度）から、横に 45 度・上に 10 度回る**。
///   その間に、**ゴールエリアがプレイヤーを乗せて奥から飛んできて、元の位置に設置（着地）する**（ゴールが飛んでくる演出と同じ動きを、この秒数に縮める） | 3 秒 |
/// | ②③ チームを映す | Team Shot Seconds が 0 より大きいときだけ。チームごとの別のカメラに切り替えて、青 → 赤 …の順に少し斜めから映す。
///   **初期値は 0 で、映さない**（2026/10/29・大槻さん「着地したら画面中央にカメラを切り替えてカウントダウンのコンパクトに」。前は 3 秒ずつ） | 0 秒 |
/// | ④ カウントダウン | 着地したら**全体の画面（ふだんのカメラ）に切り替えて**、3・2・1 → START!（ここから動ける） | 3 秒 |
///
/// ゴールエリアを飛ばしてプレイヤーを乗せる動きは <see cref="SpaceJunkGoalCarrier"/>（ゴールが飛んでくる演出 <see cref="SpaceJunkGoalFlyIntro"/> と共用）。
/// カメラは、① 用・チームごとに、ふだんのカメラの上に重ねる別のカメラを作り、使うものだけを ON にして切り替える
/// （大槻さん「別のカメラに切り替える」）。全部 OFF にすると、ふだんのカメラ（全体の画面）が見える。
///
/// 秒数・角度・距離は、マップに置いた <see cref="SpaceJunkRound"/>（プレハブ）の「ゴールエリアと一緒に着地してチームを順に映す演出」で変えられる。
/// **時間は全員で同期された時計（ServerTime）で決めている**ので、どのPCでも同じ場面になる。
/// <see cref="SpaceJunkRound"/> が、演出の種類が LandingTour のときに自分で付ける。
///
/// `DefaultExecutionOrder(10000)` で、ほかの部品より**あとに** LateUpdate が動くようにしてある。
/// 通信の位置合わせ（NetworkTransform。LateUpdate より前に動く）が置いたほかの人の位置を、そのフレームのうちに上書きするため
/// （<see cref="SpaceJunkGoalCarrier"/> の説明）。
/// </summary>
[DefaultExecutionOrder(10000)]
public class SpaceJunkLandingIntro : MonoBehaviour
{
    /// <summary>START! の文字を出しておく秒数。</summary>
    private const float StartTextSeconds = 1f;

    /// <summary>②③ カメラが狙う高さ（チームの足もとから上へ。m）。立っている人の胸のあたり。</summary>
    private const float TeamTargetHeight = 1f;

    /// <summary>演出のカメラのうち、いま映しているもの。演出中だけ入る（名前のタグがこちらを向くために使う）。</summary>
    public static Camera ViewCamera { get; private set; }

    /// <summary>同じ物に付いているラウンドの係。演出の時刻と設定はここから読む。</summary>
    private SpaceJunkRound round;

    /// <summary>ゴールエリアを飛ばしてプレイヤーを乗せる係。</summary>
    private SpaceJunkGoalCarrier carrier;

    /// <summary>① ステージを回って映すカメラ。</summary>
    private Camera stageCamera;

    /// <summary>②③ チームを映すカメラ（チームの番号ごと。映す番が来たときに作って置く）。</summary>
    private readonly Dictionary<int, Camera> teamCameras = new Dictionary<int, Camera>();

    /// <summary>文字を出す画面（Canvas）。</summary>
    private GameObject overlay;

    /// <summary>真ん中の大きな文字（マップの名前・3・2・1・START!）。</summary>
    private Text bigText;

    /// <summary>その下の小さな文字（「ラウンド ○」）。</summary>
    private Text subText;

    /// <summary>演出が終わったか。終わったら、このラウンドの間は二度と動かない。</summary>
    private bool finished;

    /// <summary>マップの大きさを、もう求めたか（求めるのは1回だけ）。</summary>
    private bool boundsReady;

    /// <summary>マップの大きさ（中央からいちばん遠いゴールまでの水平の距離＋4m。メートル）。① のカメラの距離に使う。</summary>
    private float radius = 20f;

    private void Awake()
    {
        round = GetComponent<SpaceJunkRound>();
        carrier = new SpaceJunkGoalCarrier(round);
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

        EnsureOverlay();
        PrepareBounds();
        carrier.Prepare();

        // ① の間に、ゴールが飛んでくる演出と同じ動き（飛ぶ → 設置）を入れる。
        // 飛ぶ秒数と設置の秒数の割合はそのままにして、合計を ① の秒数に縮める（初期値なら 3 秒と 1.5 秒 → 2 秒と 1 秒）
        float motion = round.FlyInSeconds + round.FlyInLandSeconds;
        float scale = motion > 0.01f ? landEnd / motion : 0f;
        carrier.Update(sinceIntro, round.FlyInSeconds * scale, round.FlyInLandSeconds * scale);

        UpdateCameras(sinceIntro, landEnd, shotsEnd);

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
    // カメラ
    // ------------------------------------------------------------

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// いまの場面のカメラだけを ON にする。⓪① ステージを回るカメラ（毎フレーム動かす）→ ②③ そのチームのカメラ（置いたまま動かない）
    /// → 映し終えたら全部 OFF（ふだんのカメラ＝全体の画面が見える）。どれもパッと切り替える。
    /// </summary>
    private void UpdateCameras(float sinceIntro, float landEnd, float shotsEnd)
    {
        Camera active = null;

        if (sinceIntro < landEnd)
        {
            if (stageCamera == null)
            {
                stageCamera = CreateCamera("RoundIntroCamera_Stage");
            }

            GetOrbitPose(sinceIntro / Mathf.Max(0.01f, landEnd), out Vector3 position, out Vector3 target);
            stageCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            active = stageCamera;
        }
        else if (sinceIntro < shotsEnd)
        {
            // 何番目のチームを映しているか（0 から。青＝0、赤＝1 …）
            int team = Mathf.Min(round.TeamShotCount - 1,
                Mathf.FloorToInt((sinceIntro - landEnd) / Mathf.Max(0.01f, round.TeamShotSeconds)));
            active = TeamCamera(team);
        }

        SetActiveCamera(active);
    }

    /// <summary>そのチームを映すカメラ。まだ無ければ作り、そのチームを映す位置に置く（置いたあとは動かさない）。</summary>
    private Camera TeamCamera(int team)
    {
        if (!teamCameras.TryGetValue(team, out Camera camera) || camera == null)
        {
            camera = CreateCamera($"RoundIntroCamera_Team{team}");
            GetTeamPose(team, out Vector3 position, out Vector3 target);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            teamCameras[team] = camera;
        }

        return camera;
    }

    /// <summary>演出のカメラのうち <paramref name="active"/> だけを ON にし、ほかは OFF にする（null なら全部 OFF）。</summary>
    private void SetActiveCamera(Camera active)
    {
        if (stageCamera != null)
        {
            stageCamera.enabled = stageCamera == active;
        }

        foreach (Camera camera in teamCameras.Values)
        {
            if (camera != null)
            {
                camera.enabled = camera == active;
            }
        }

        ViewCamera = active;
    }

    /// <summary>
    /// ⓪① ステージの中心を狙って回るカメラ（進み具合 0〜1。始めと終わりをゆっくりにする）。
    /// ふだんのカメラと同じ南側を真ん中にして、横に Landing Orbit Degrees（左右に半分ずつ）回り、
    /// 見下ろす角度は Landing Start Pitch（全体カメラより少し低い）から Landing Orbit Rise Degrees だけ上がる。
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
    /// ②③ そのチームを映すカメラの位置。チームの中心を狙い、**ステージの中心の側（プレイヤーが向いている側）から、
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

    /// <summary>
    /// ふだんのカメラの上に重ねる、演出用のカメラを1つ作る（ふだんのカメラの動きには手を出さない）。
    /// 映し方の設定（画角・背景・映す層など）はふだんのカメラから写し、depth を 10 大きくして、
    /// ふだんのカメラより後に描かせる（上に重なって、こちらの映像が見える）。
    /// </summary>
    private Camera CreateCamera(string cameraName)
    {
        GameObject cameraObject = new GameObject(cameraName);
        cameraObject.transform.SetParent(transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();

        Camera main = Camera.main;
        if (main != null)
        {
            camera.CopyFrom(main);
            camera.depth = main.depth + 10f;
        }
        else
        {
            camera.depth = 10f;
        }

        return camera;
    }

    /// <summary>文字の画面を、まだ無ければ作る（毎フレーム呼んでよい）。</summary>
    private void EnsureOverlay()
    {
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
    /// 途中で終わっても（結果の画面へ移った・シーンが変わった）、ゴールエリアの見た目と切った当たり判定は戻す。
    /// </summary>
    private void Cleanup()
    {
        carrier?.Restore();

        if (ViewCamera != null && (ViewCamera == stageCamera || teamCameras.ContainsValue(ViewCamera)))
        {
            ViewCamera = null;
        }

        if (stageCamera != null)
        {
            Destroy(stageCamera.gameObject);
            stageCamera = null;
        }

        foreach (Camera camera in teamCameras.Values)
        {
            if (camera != null)
            {
                Destroy(camera.gameObject);
            }
        }

        teamCameras.Clear();

        if (overlay != null)
        {
            Destroy(overlay);
            overlay = null;
        }
    }
}
