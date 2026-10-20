using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出。**（2026/10/7・大槻さん「スプラトゥーンみたいに、マップを一通り紹介して、
/// プレイヤーの近くにカメラが寄って、3・2・1・Start で始まるようにしてほしい」）
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ① マップの紹介 | カメラがマップの上をぐるっと回る。マップの名前と「ラウンド ○」が出る | 4 秒 |
/// | ② 自分に寄る | カメラが自分のプレイヤーの後ろへ寄る | 1.5 秒 |
/// | ③ カウントダウン | 「3」「2」「1」 | 3 秒 |
/// | ④ START! | 動けるようになり、カメラがふだんの位置へ戻る | 0.7 秒で戻る |
///
/// 長さは、マップに置いた <see cref="SpaceJunkRound"/> の「始まりの演出」で変えられる（演出を切ることもできる）。
/// **時間は全員で同期された時計（ServerTime）で決めている**ので、どのPCでも同じタイミングで START になる。
/// 演出中は誰も動けず、素材も出ない（<see cref="SpaceJunkRound.IsIntro"/>）。残り時間とイベントは START から数える。
///
/// カメラは、ふだんのカメラとは**別のカメラを一時的に重ねて**動かしている（ふだんのカメラの動きには手を出さない）。
/// <see cref="SpaceJunkRound"/> が開始時に自分で付けるので、マップに置く必要はない。
///
/// `DefaultExecutionOrder(10000)` で、ほかの部品より**あとに** LateUpdate が動くようにしてある。
/// ④でふだんのカメラの位置へなめらかに戻すとき、そのフレームのふだんのカメラの位置が決まってから読みたいため。
/// </summary>
[DefaultExecutionOrder(10000)]
public class SpaceJunkRoundIntro : MonoBehaviour
{
    /// <summary>START! の文字を出しておく秒数。</summary>
    private const float StartTextSeconds = 1f;

    /// <summary>START! のあと、ふだんのカメラへ戻るまでの秒数。</summary>
    private const float BlendOutSeconds = 0.7f;

    /// <summary>マップをぐるっと回る角度（度）。</summary>
    private const float FlyoverAngle = 160f;

    /// <summary>同じ物に付いているラウンドの係。演出の時刻（いつ始まり、いつ START か）はここから読む。</summary>
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

    /// <summary>マップの真ん中と大きさを、もう求めたか（求めるのは1回だけ）。</summary>
    private bool boundsReady;

    /// <summary>マップの真ん中。①でカメラはここを見ながら回る。</summary>
    private Vector3 center;

    /// <summary>マップの大きさ（真ん中から端までの水平の距離。メートル）。①で回る輪の大きさに使う。</summary>
    private float radius = 20f;

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

        // START! の文字も、ふだんのカメラへ戻るのも終わった／ラウンドが終わった（結果の画面など）なら、片付けて終わる
        if (sinceStart > Mathf.Max(StartTextSeconds, BlendOutSeconds) || round.Phase != SpaceJunkRoundPhase.Playing)
        {
            Finish();
            return;
        }

        EnsureCameraAndOverlay();
        PrepareBounds();

        // 演出が始まってからの秒数。① → ② → ③ → ④ のどこにいるかを、これで決める
        float sinceIntro = (float)(now - round.IntroStartServerTime);
        float flyover = round.IntroFlyoverSeconds;
        float zoom = round.IntroZoomSeconds;

        // ②の行き先・③の置き場所・④の戻り始めは、どれもこの「自分の近く」の位置
        Transform player = LocalPlayer();
        GetClosePose(player, out Vector3 closePosition, out Quaternion closeRotation);

        Vector3 position;
        Quaternion rotation;

        // 下の Mathf.Max(0.01f, …) は、秒数が 0 に設定されたときに 0 で割らないための保険
        if (sinceIntro < flyover)
        {
            // ① マップをぐるっと見せる（読み込み待ちの間は、回り始めの位置で止めておく）
            GetFlyoverPose(Mathf.Clamp01(sinceIntro / Mathf.Max(0.01f, flyover)), player, out position, out rotation);
            ShowTexts(MapName(), RoundText());
        }
        else if (sinceIntro < flyover + zoom)
        {
            // ② 自分のプレイヤーへ寄る（①の終わりの位置から、自分の近くの位置へ。始めと終わりをゆっくりにする）
            GetFlyoverPose(1f, player, out Vector3 fromPosition, out Quaternion fromRotation);
            float blend = Mathf.SmoothStep(0f, 1f, (sinceIntro - flyover) / Mathf.Max(0.01f, zoom));
            position = Vector3.Lerp(fromPosition, closePosition, blend);
            rotation = Quaternion.Slerp(fromRotation, closeRotation, blend);
            ShowTexts(string.Empty, string.Empty);
        }
        else if (sinceStart < 0f)
        {
            // ③ 3・2・1（START までの残り秒数を切り上げて出す。2.4 秒なら「3」）
            position = closePosition;
            rotation = closeRotation;
            ShowTexts(Mathf.CeilToInt(-sinceStart).ToString(), string.Empty);
        }
        else
        {
            // ④ START!（ふだんのカメラへ戻る）
            // START のあとはプレイヤーが動き出し、ふだんのカメラも動くので、毎フレームその位置を読み直して、そこへ寄せていく
            Camera main = Camera.main;
            float blend = Mathf.SmoothStep(0f, 1f, sinceStart / BlendOutSeconds);
            position = main != null ? Vector3.Lerp(closePosition, main.transform.position, blend) : closePosition;
            rotation = main != null ? Quaternion.Slerp(closeRotation, main.transform.rotation, blend) : closeRotation;
            ShowTexts(sinceStart < StartTextSeconds ? "START!" : string.Empty, string.Empty);

            // 戻りきったら、重ねていたカメラを止めて、ふだんのカメラの映像に任せる（文字はまだ出ていてよい）
            if (sinceStart >= BlendOutSeconds && introCamera != null)
            {
                introCamera.enabled = false;
            }
        }

        if (introCamera != null && introCamera.enabled)
        {
            introCamera.transform.SetPositionAndRotation(position, rotation);
        }
    }

    // ------------------------------------------------------------
    // カメラの位置
    // ------------------------------------------------------------

    /// <summary>
    /// マップの真ん中と大きさを決める。**このマップのゴールの位置**から求める（ゴールはマップの端にあるため）。
    /// ゴールが無ければ、このシーンの見える物の範囲から求める。
    /// </summary>
    private void PrepareBounds()
    {
        if (boundsReady)
        {
            return;
        }

        Scene scene = gameObject.scene;
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene)
            {
                sum += goal.transform.position;
                count++;
            }
        }

        // ゴールが2つ以上あれば … 真ん中＝ゴールの位置の平均、大きさ＝いちばん遠いゴールまでの水平の距離＋4m の余白
        // （ゴールが1つだと、真ん中がそのゴールの上になってしまうので使わない）
        if (count >= 2)
        {
            center = sum / count;
            radius = 0f;
            foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
            {
                if (goal != null && goal.gameObject.scene == scene)
                {
                    radius = Mathf.Max(radius, Vector3.Distance(Flat(goal.transform.position), Flat(center)));
                }
            }
            radius += 4f;
        }
        else
        {
            // ゴールが足りなければ … このシーンの見える物を全部囲む箱から求める
            // （大きさは箱の横・奥行きの半分から求めた斜めの長さ。小さすぎるときは 10m）。
            // 見える物が1つも無ければ、この部品の位置を真ん中にする
            Bounds bounds = new Bounds(transform.position, Vector3.one);
            bool any = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            center = bounds.center;
            radius = Mathf.Max(10f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
        }

        // 回る輪が小さすぎ・大きすぎにならないよう、8〜80m に収める
        radius = Mathf.Clamp(radius, 8f, 80f);
        boundsReady = true;
    }

    /// <summary>
    /// マップの上をぐるっと回るカメラの位置（0〜1）。**最後に自分のプレイヤーの側に来る**ように回り始めを決める。
    /// </summary>
    private void GetFlyoverPose(float progress, Transform player, out Vector3 position, out Quaternion rotation)
    {
        // マップの真ん中から見た、自分のプレイヤーの方向（水平）。分からない・真ん中にいるときは南（手前）
        Vector3 toPlayer = player != null ? Flat(player.position - center) : Vector3.back;
        if (toPlayer.sqrMagnitude < 0.01f)
        {
            toPlayer = Vector3.back;
        }

        // 角度は、上から見て東（+x）を 0 度とし、北（+z）へ向かって増える（反時計回り）。
        // 終わりの角度＝プレイヤーの方向。そこから FlyoverAngle だけ手前の角度から回り始め、
        // 進み具合 0→1 に合わせて（始めと終わりをゆっくりにして）終わりの角度まで回る
        float endAngle = Mathf.Atan2(toPlayer.z, toPlayer.x) * Mathf.Rad2Deg;
        float angle = endAngle - FlyoverAngle * (1f - Mathf.SmoothStep(0f, 1f, progress));
        Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

        // 真ん中から、マップの大きさの 1.3 倍外側・0.9 倍の高さに置き、真ん中を見下ろす
        position = center + direction * (radius * 1.3f) + Vector3.up * (radius * 0.9f);
        rotation = Quaternion.LookRotation(center - position, Vector3.up);
    }

    /// <summary>自分のプレイヤーの後ろ（マップの外側）から、マップの真ん中のほうを向いて見る位置。</summary>
    private void GetClosePose(Transform player, out Vector3 position, out Quaternion rotation)
    {
        if (player == null)
        {
            GetFlyoverPose(1f, null, out position, out rotation);
            return;
        }

        // outward … マップの真ん中から見て、プレイヤーの外側への向き（長さ 1）。真ん中にいるときは南（手前）
        Vector3 outward = Flat(player.position - center);
        outward = outward.sqrMagnitude < 0.01f ? Vector3.back : outward.normalized;

        // 見る点：プレイヤーの 1m 上から、2m だけ真ん中寄り（プレイヤーとその先のマップが一緒に映る）
        // カメラ：プレイヤーの 6m 外側・3.5m 上
        Vector3 focus = player.position + Vector3.up * 1f - outward * 2f;
        position = player.position + outward * 6f + Vector3.up * 3.5f;
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    /// <summary>高さを 0 にした（地面と平行にした）ベクトル。</summary>
    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    /// <summary>このPCで操作しているプレイヤー（まだ生まれていなければ null）。</summary>
    private static Transform LocalPlayer()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            // 1台で複数人なら、そのPCの1人目に寄る（カメラはそのPCの1人目に付いているため）
            if (player != null && player.IsOwner && player.LocalSeat == 0)
            {
                return player.transform;
            }
        }

        return null;
    }

    // ------------------------------------------------------------
    // 文字
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

    /// <summary>文字を変える。毎フレーム呼ばれるので、中身が変わったときだけ入れ直す（作り直しの手間を省くため）。</summary>
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
        if (introCamera == null)
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

    /// <summary>作ったカメラと文字の画面を消す（演出が終わったとき・この部品が消えるとき）。</summary>
    private void Cleanup()
    {
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
