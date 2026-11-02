using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出（ゴールエリアが飛んでくる）。**（2026/10/27・大槻さん。10/29 から、ロビーから1ラウンド目に入るときに使う）
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ⓪ 読み込み待ち | ゴールエリアは奥の飛び始めの位置で止まっている。マップの名前と「ラウンド ○」が出る | 1.5 秒 |
/// | ① 飛んでくる | 各チームのゴールエリアが、そのチームのプレイヤーを乗せ、横に並んで**マップの奥から手前へ**飛んでくる。
///   カメラはゴールエリアの手前の斜め上（45 度）から、ゴールエリア全部の中心を狙って一緒に下がる。
///   ゴールエリアより少しゆっくり下がるので、最初から乗っている人が映ったまま、だんだんこちらへ近づいて見える。
///   名前のタグを、カメラから見て左の人から順に1人ずつ強調する（① の秒数 ÷ 人数 ずつ、自分の番の間だけ大きくなる） | 3 秒 |
/// | ② 設置 | マップに近づいたら、それぞれ**元のゴールの位置へ飛んで設置される**。カメラはふだんのカメラへ移る | 1.5 秒 |
/// | ③ カウントダウン | 設置されたら 3・2・1 → START!（ここから動ける） | 3 秒 |
///
/// ゴールエリアを飛ばしてプレイヤーを乗せる動きは <see cref="SpaceJunkGoalCarrier"/> が受け持つ（2026/10/28 に分けた。
/// プレイヤーが降りてくる演出 <see cref="SpaceJunkLandingIntro"/> でも同じ動きを使うため）。ここはカメラ・名前のタグの強調・文字。
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

    /// <summary>① のカメラが狙う高さ（ゴールエリアの中心から上へ。m）。乗っている人の胸のあたり。</summary>
    private const float CameraTargetHeight = 1f;

    /// <summary>① の終わりに画面の左右に残す余白（m）。</summary>
    private const float CameraSideMargin = 1.5f;

    /// <summary>名前のタグが、番の始めに大きくなる・番の終わりに元へ戻るのにかける秒数。</summary>
    private const float EmphasisRampSeconds = 0.15f;

    /// <summary>演出のカメラ。演出中だけ入る（名前のタグがこちらを向くために使う）。</summary>
    public static Camera ViewCamera { get; private set; }

    /// <summary>名前のタグの大きさの倍率（強調している人だけ入る）。</summary>
    private static readonly Dictionary<FishingNetPlayer, float> emphasis = new Dictionary<FishingNetPlayer, float>();

    /// <summary>そのプレイヤーの名前のタグの大きさの倍率。強調していなければ 1。</summary>
    public static float NameEmphasisOf(FishingNetPlayer player)
    {
        return player != null && emphasis.TryGetValue(player, out float scale) ? scale : 1f;
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

    /// <summary>ゴールエリアを飛ばしてプレイヤーを乗せる係。</summary>
    private SpaceJunkGoalCarrier carrier;

    /// <summary>ゴールエリアを用意して、① の終わりのカメラの距離を決めたか（1回だけ）。</summary>
    private bool cameraReady;

    /// <summary>① の終わりの、狙う所からカメラまでの距離（横に並んだゴールエリアが画面の幅にちょうど収まる距離。m）。</summary>
    private float cameraDistance = 10f;

    /// <summary>演出のカメラの画角（ふだんのカメラから写した値。① の間は変えない）。</summary>
    private float baseFieldOfView = 60f;

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
        PrepareCarrier();
        carrier.Update(sinceIntro, flyEnd, round.FlyInLandSeconds);

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
    // ゴールエリア（動かすのは SpaceJunkGoalCarrier）
    // ------------------------------------------------------------

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// 飛んでくるゴールエリアを用意し（<see cref="SpaceJunkGoalCarrier.Prepare"/>）、① の終わりのカメラの距離を決める（1回だけ）。
    /// 距離は、横に並んだゴールエリア全部（＋余白）が画面の幅にちょうど収まる距離。画角はふだんのカメラから写したものを使い、① の間は変えない。
    /// </summary>
    private void PrepareCarrier()
    {
        if (cameraReady)
        {
            return;
        }

        cameraReady = true;
        carrier.Prepare();

        if (introCamera != null)
        {
            baseFieldOfView = introCamera.fieldOfView;
        }

        float aspect = introCamera != null ? Mathf.Max(0.1f, introCamera.aspect) : 16f / 9f;
        float sideTan = Mathf.Tan(baseFieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
        cameraDistance = Mathf.Max(6f, (carrier.TotalWidth * 0.5f + CameraSideMargin) / Mathf.Max(0.01f, sideTan));
    }

    // ------------------------------------------------------------
    // 名前のタグの強調
    // ------------------------------------------------------------

    /// <summary>
    /// ① の間、**カメラから見て左の人から順に、① の秒数 ÷ 人数 ごとに1人ずつ**名前のタグを強調する。
    /// **自分の番が来たら Name Emphasis Scale 倍まで大きくなり、番の間は大きいまま、番の終わりに元の大きさへ戻る**
    /// （2026/10/27・大槻さん「拡大するだけにして、自分の番が終わったら元のサイズに戻る感じ」。前は拡大と縮小をくり返していた）。
    /// 大きくなる・戻るは、パッと切り替えず <see cref="EmphasisRampSeconds"/> 秒でなめらかに変える。
    /// </summary>
    private void UpdateEmphasis(float sinceIntro, float flyEnd)
    {
        emphasis.Clear();

        if (carrier.Landed || carrier.Riders.Count == 0 || sinceIntro < 0f || sinceIntro >= flyEnd)
        {
            return;
        }

        // 同じ位置なら人ごとの番号順（どのPCでも同じ順番になるように）
        carrier.Riders.Sort((a, b) =>
        {
            int compare = a.across.CompareTo(b.across);
            return compare != 0 ? compare : a.player.PlayerKey.CompareTo(b.player.PlayerKey);
        });

        float window = flyEnd / carrier.Riders.Count;
        int index = Mathf.Min(carrier.Riders.Count - 1, Mathf.FloorToInt(sinceIntro / window));
        float elapsed = sinceIntro - index * window;
        float remaining = window - elapsed;

        // 番の始めに大きくなり、番の終わりに戻る（人数が多くて番が短いときは、番の 1/4 で変える）
        float ramp = Mathf.Max(0.001f, Mathf.Min(EmphasisRampSeconds, window * 0.25f));
        float grow = Mathf.SmoothStep(0f, 1f, Mathf.Min(elapsed, remaining) / ramp);
        emphasis[carrier.Riders[index].player] = Mathf.Lerp(1f, round.NameEmphasisScale, grow);
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
        if (carrier.Goals.Count > 0)
        {
            target = Vector3.zero;
            foreach (SpaceJunkGoalCarrier.FlyingGoal goal in carrier.Goals)
            {
                target += atApproach ? goal.Approach : goal.Position;
            }
            target = target / carrier.Goals.Count + Vector3.up * CameraTargetHeight;
        }

        // 離れ方：飛び始めは Start Scale 倍、① の終わりで 1 倍（ゴールエリアと同じく、終わりに向かってゆっくりにする）
        float t = atApproach ? 1f : Mathf.Clamp01(sinceIntro / Mathf.Max(0.01f, flyEnd));
        float eased = 1f - (1f - t) * (1f - t);
        float distance = cameraDistance * Mathf.Lerp(round.FlyInCameraStartScale, 1f, eased);

        // 狙う所から、手前（飛んでくる先）へ倒した向きに離れて、見下ろす
        float pitch = round.FlyInCameraPitch * Mathf.Deg2Rad;
        Vector3 position = target - carrier.Backward * (distance * Mathf.Cos(pitch)) + Vector3.up * (distance * Mathf.Sin(pitch));
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
        if (introCamera == null && !carrier.Landed)
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
        carrier?.Restore();
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
