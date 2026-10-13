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

    private SpaceJunkRound round;
    private Camera introCamera;
    private GameObject overlay;
    private Text bigText;
    private Text subText;
    private bool finished;

    private bool boundsReady;
    private Vector3 center;
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

        double now = round.ServerNow;
        float sinceStart = (float)(now - playStart);

        if (sinceStart > Mathf.Max(StartTextSeconds, BlendOutSeconds) || round.Phase != SpaceJunkRoundPhase.Playing)
        {
            Finish();
            return;
        }

        EnsureCameraAndOverlay();
        PrepareBounds();

        float t = (float)(now - round.IntroStartServerTime);
        float flyover = round.IntroFlyoverSeconds;
        float zoom = round.IntroZoomSeconds;

        Transform player = LocalPlayer();
        GetClosePose(player, out Vector3 closePosition, out Quaternion closeRotation);

        Vector3 position;
        Quaternion rotation;

        if (t < flyover)
        {
            // ① マップをぐるっと見せる（読み込み待ちの間は、回り始めの位置で止めておく）
            GetFlyoverPose(Mathf.Clamp01(t / Mathf.Max(0.01f, flyover)), player, out position, out rotation);
            ShowTexts(MapName(), RoundText());
        }
        else if (t < flyover + zoom)
        {
            // ② 自分のプレイヤーへ寄る
            GetFlyoverPose(1f, player, out Vector3 fromPosition, out Quaternion fromRotation);
            float k = Mathf.SmoothStep(0f, 1f, (t - flyover) / Mathf.Max(0.01f, zoom));
            position = Vector3.Lerp(fromPosition, closePosition, k);
            rotation = Quaternion.Slerp(fromRotation, closeRotation, k);
            ShowTexts(string.Empty, string.Empty);
        }
        else if (sinceStart < 0f)
        {
            // ③ 3・2・1
            position = closePosition;
            rotation = closeRotation;
            ShowTexts(Mathf.CeilToInt(-sinceStart).ToString(), string.Empty);
        }
        else
        {
            // ④ START!（ふだんのカメラへ戻る）
            Camera main = Camera.main;
            float k = Mathf.SmoothStep(0f, 1f, sinceStart / BlendOutSeconds);
            position = main != null ? Vector3.Lerp(closePosition, main.transform.position, k) : closePosition;
            rotation = main != null ? Quaternion.Slerp(closeRotation, main.transform.rotation, k) : closeRotation;
            ShowTexts(sinceStart < StartTextSeconds ? "START!" : string.Empty, string.Empty);

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

        radius = Mathf.Clamp(radius, 8f, 80f);
        boundsReady = true;
    }

    /// <summary>
    /// マップの上をぐるっと回るカメラの位置（0〜1）。**最後に自分のプレイヤーの側に来る**ように回り始めを決める。
    /// </summary>
    private void GetFlyoverPose(float progress, Transform player, out Vector3 position, out Quaternion rotation)
    {
        Vector3 toPlayer = player != null ? Flat(player.position - center) : Vector3.back;
        if (toPlayer.sqrMagnitude < 0.01f)
        {
            toPlayer = Vector3.back;
        }

        float endAngle = Mathf.Atan2(toPlayer.z, toPlayer.x) * Mathf.Rad2Deg;
        float angle = endAngle - FlyoverAngle * (1f - Mathf.SmoothStep(0f, 1f, progress));
        Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

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

        Vector3 outward = Flat(player.position - center);
        outward = outward.sqrMagnitude < 0.01f ? Vector3.back : outward.normalized;

        Vector3 focus = player.position + Vector3.up * 1f - outward * 2f;
        position = player.position + outward * 6f + Vector3.up * 3.5f;
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

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

    private static string MapName()
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;
        string scene = SceneManager.GetActiveScene().name;
        return lobby != null ? lobby.LabelOf(scene) : scene;
    }

    private static string RoundText()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        return session != null && session.CurrentRound > 0 ? $"ラウンド {session.CurrentRound}" : string.Empty;
    }

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

    private void EnsureCameraAndOverlay()
    {
        if (introCamera == null)
        {
            // ふだんのカメラの上に重ねる、演出用のカメラ（ふだんのカメラの動きには手を出さない）
            GameObject cameraObject = new GameObject("RoundIntroCamera");
            cameraObject.transform.SetParent(transform, false);
            introCamera = cameraObject.AddComponent<Camera>();

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

    private void BuildOverlay()
    {
        overlay = new GameObject("RoundIntroOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(transform, false);

        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85; // タイトル画面（90）・ポーズ画面（100）より後ろ

        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = UiFont.Find(64);
        bigText = CreateText("Big", font, 160, new Vector2(0f, 60f), new Vector2(1800f, 260f));
        subText = CreateText("Sub", font, 56, new Vector2(0f, -110f), new Vector2(1800f, 90f));
    }

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
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(5f, -5f);

        return text;
    }

    private void Finish()
    {
        finished = true;
        Cleanup();
    }

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
