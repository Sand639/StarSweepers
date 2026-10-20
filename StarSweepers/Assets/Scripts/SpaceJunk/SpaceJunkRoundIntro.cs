using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンドの始まりの演出。**（2026/10/7・大槻さん「スプラトゥーンみたいに、マップを一通り紹介して、
/// プレイヤーの近くにカメラが寄って、3・2・1・Start で始まるようにしてほしい」）
///
/// 2026/10/20 に流れを作り直した（大槻さん「まだどんな演出がいいか分かっていないので、色々試していきたい」）。
///
/// | 順番 | 何が起きるか | 長さ（初期値） |
/// | --- | --- | --- |
/// | ⓪ 読み込み待ち | ① の回り始めの位置で止まったまま。マップの名前と「ラウンド ○」が出る | 1.5 秒 |
/// | ① マップの紹介 | マップの中央（0,0,0）を向いたまま、真上から 45 度傾けた所を 180 度回る | 3 秒 |
/// | ② 自分のチームに寄る | 自分のチームの後ろ（マップの外側、少し上）へ寄る | 1 秒 |
/// | ③ 止まる | ② の位置で止まる | 0.5 秒 |
/// | ④ チームの周りを回る | チームの中央を中心に -120 度回りながら、少しずつ離れて上がり、最後は上からチームを映す | 1.5 秒 |
/// | ⑤ 止まる | 回り終わった位置で止まる | 1 秒 |
/// | ⑥ 全体カメラへ移る | ふだんのカメラ（全体カメラ）の位置へ移る | 1 秒 |
///
/// 文字：④ が始まって 1 秒たったら「3」「2」「1」（3 秒）。終わったら「START!」を 1 秒出し、ここから動ける。
/// （初期値では ⑥ が終わった 0.5 秒あとに START になる）
///
/// 秒数と角度は、マップに置いた <see cref="SpaceJunkRound"/> の「始まりの演出」で変えられる（演出を切ることもできる）。
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

    private SpaceJunkRound round;
    private Camera introCamera;
    private GameObject overlay;
    private Text bigText;
    private Text subText;
    private bool finished;

    private bool boundsReady;
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

        // 各段階の終わりの時刻（演出の始まりから数えた秒数）。カウントダウンはカメラの動きとは別に、START から逆算する
        float t = (float)(now - round.IntroStartServerTime);
        float flyoverEnd = round.IntroFlyoverSeconds;
        float zoomEnd = flyoverEnd + round.IntroZoomSeconds;
        float holdEnd = zoomEnd + round.IntroHoldBeforeOrbitSeconds;
        float orbitEnd = holdEnd + round.IntroTeamOrbitSeconds;
        float holdAfterEnd = orbitEnd + round.IntroHoldAfterOrbitSeconds;
        float returnEnd = holdAfterEnd + round.IntroReturnSeconds;
        float countdown = round.IntroCountdownSeconds;

        if ((sinceStart > StartTextSeconds && t >= returnEnd) || round.Phase != SpaceJunkRoundPhase.Playing)
        {
            Finish();
            return;
        }

        EnsureCameraAndOverlay();
        PrepareBounds();

        bool hasTeam = TryGetTeam(out Vector3 team, out float spread);
        GetClosePose(hasTeam, team, spread, out Vector3 closePosition, out Quaternion closeRotation);

        Vector3 position = closePosition;
        Quaternion rotation = closeRotation;

        if (t < flyoverEnd)
        {
            // ⓪① マップをぐるっと見せる（読み込み待ちの間は、回り始めの位置で止めておく）
            GetFlyoverPose(Mathf.Clamp01(t / Mathf.Max(0.01f, round.IntroFlyoverSeconds)), hasTeam, team,
                out position, out rotation);
            ShowTexts(MapName(), RoundText());
        }
        else if (t < zoomEnd)
        {
            // ② 自分のチームへ寄る
            GetFlyoverPose(1f, hasTeam, team, out Vector3 fromPosition, out Quaternion fromRotation);
            float k = Mathf.SmoothStep(0f, 1f, (t - flyoverEnd) / Mathf.Max(0.01f, round.IntroZoomSeconds));
            position = Vector3.Lerp(fromPosition, closePosition, k);
            rotation = Quaternion.Slerp(fromRotation, closeRotation, k);
            ShowTexts(string.Empty, string.Empty);
        }
        else
        {
            // 文字：カウントダウンの前は何も出さない → 3・2・1 → START!（カメラの動きとは別に、START の時刻で切り替える）
            string big = sinceStart >= 0f ? "START!"
                : -sinceStart <= countdown ? Mathf.CeilToInt(-sinceStart).ToString()
                : string.Empty;
            ShowTexts(big, string.Empty);

            if (t < holdEnd)
            {
                // ③ 寄った位置で止まる
            }
            else if (t < orbitEnd)
            {
                // ④ ② と同じ距離・高さのまま、チームの中央を中心に回る
                if (hasTeam)
                {
                    GetOrbitPose((t - holdEnd) / Mathf.Max(0.01f, round.IntroTeamOrbitSeconds), team, spread,
                        out position, out rotation);
                }
            }
            else if (t < returnEnd)
            {
                // ⑤ 回り終わった位置で止まる → ⑥ 全体カメラ（ふだんのカメラ）へ移る
                if (hasTeam)
                {
                    GetOrbitPose(1f, team, spread, out position, out rotation);
                }

                if (t >= holdAfterEnd)
                {
                    Camera main = Camera.main;
                    float k = Mathf.SmoothStep(0f, 1f, (t - holdAfterEnd) / Mathf.Max(0.01f, round.IntroReturnSeconds));
                    if (main != null)
                    {
                        position = Vector3.Lerp(position, main.transform.position, k);
                        rotation = Quaternion.Slerp(rotation, main.transform.rotation, k);
                    }
                }
            }
            else if (introCamera != null)
            {
                // 移り終わったので、演出用のカメラを止める（文字だけ残す）
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

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// マップの大きさ（中央からの半径）を決める。**このマップのゴールの位置**から求める（ゴールはマップの端にあるため）。
    /// ゴールが無ければ、このシーンの見える物の範囲から求める。
    /// </summary>
    private void PrepareBounds()
    {
        if (boundsReady)
        {
            return;
        }

        Scene scene = gameObject.scene;
        int count = 0;
        radius = 0f;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene)
            {
                radius = Mathf.Max(radius, Vector3.Distance(Flat(goal.transform.position), Flat(Center)));
                count++;
            }
        }

        if (count > 0)
        {
            radius += 4f;
        }
        else
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = renderer.bounds;
                    radius = Mathf.Max(radius,
                        Vector3.Distance(Flat(b.min), Flat(Center)),
                        Vector3.Distance(Flat(b.max), Flat(Center)));
                }
            }
        }

        radius = Mathf.Clamp(radius, 8f, 80f);
        boundsReady = true;
    }

    /// <summary>
    /// ① マップの中央を向いたまま回るカメラの位置（0〜1）。真上（Y軸）から <see cref="SpaceJunkRound.IntroFlyoverTilt"/> 度傾けた所を、
    /// <see cref="SpaceJunkRound.IntroFlyoverDegrees"/> 度回る。**最後に自分のチームの側に来る**ように回り始めを決める。
    /// </summary>
    private void GetFlyoverPose(float progress, bool hasTeam, Vector3 team, out Vector3 position, out Quaternion rotation)
    {
        Vector3 toTeam = hasTeam ? Flat(team - Center) : Vector3.back;
        if (toTeam.sqrMagnitude < 0.01f)
        {
            toTeam = Vector3.back;
        }

        float endAngle = Mathf.Atan2(toTeam.z, toTeam.x) * Mathf.Rad2Deg;
        float angle = endAngle - round.IntroFlyoverDegrees * (1f - Mathf.SmoothStep(0f, 1f, progress));
        Vector3 horizontal = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

        float tilt = round.IntroFlyoverTilt * Mathf.Deg2Rad;
        float distance = radius * 1.6f;
        position = Center + (horizontal * Mathf.Sin(tilt) + Vector3.up * Mathf.Cos(tilt)) * distance;

        // 真上から見るときは「上」が決まらないので、回っている向きを画面の上にする
        rotation = Quaternion.LookRotation(Center - position, tilt < 0.01f ? -horizontal : Vector3.up);
    }

    /// <summary>② 自分のチームの後ろ（マップの外側、少し上）から、マップの中央のほうを向いて見る位置。</summary>
    private void GetClosePose(bool hasTeam, Vector3 team, float spread, out Vector3 position, out Quaternion rotation)
    {
        if (!hasTeam)
        {
            GetFlyoverPose(1f, false, team, out position, out rotation);
            return;
        }

        GetTeamViewPose(team, spread, Outward(team), out position, out rotation);
    }

    /// <summary>
    /// ④ チームの中央を中心に回るカメラの位置（0〜1）。② で寄った位置から回り始め、
    /// **回りながら少しずつ離れ・上がって、最後は上からチームを映す**（<see cref="SpaceJunkRound.IntroOrbitEndDistance"/>・
    /// <see cref="SpaceJunkRound.IntroOrbitEndHeight"/>）。
    /// </summary>
    private void GetOrbitPose(float progress, Vector3 team, float spread, out Vector3 position, out Quaternion rotation)
    {
        Vector3 outward = Outward(team);
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));

        float startAngle = Mathf.Atan2(outward.z, outward.x) * Mathf.Rad2Deg;
        float angle = startAngle + round.IntroTeamOrbitDegrees * k;
        Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

        float distance = Mathf.Lerp(round.IntroCloseDistance, round.IntroOrbitEndDistance, k);
        float height = Mathf.Lerp(round.IntroCloseHeight, round.IntroOrbitEndHeight, k);

        // 見る所も、チームの少し奥から、チームの中央そのものへ移す（上から見下ろす形になる）
        float lookAhead = Mathf.Lerp(2f, 0f, k);
        GetTeamViewPose(team, spread, direction, distance, height, lookAhead, out position, out rotation);
    }

    /// <summary>② 寄った位置（チームの後ろ、少し上から、チームの少し奥を見る）。</summary>
    private void GetTeamViewPose(Vector3 team, float spread, Vector3 direction, out Vector3 position, out Quaternion rotation)
    {
        GetTeamViewPose(team, spread, direction, round.IntroCloseDistance, round.IntroCloseHeight, 2f,
            out position, out rotation);
    }

    /// <summary>
    /// チームの中央から <paramref name="direction"/>（水平）の向きへ <paramref name="distance"/> 離れ、
    /// <paramref name="height"/> の高さからチームを見る位置。見る所は、チームの中央から <paramref name="lookAhead"/> だけ奥（カメラと反対側）。
    /// </summary>
    private static void GetTeamViewPose(Vector3 team, float spread, Vector3 direction, float distance, float height,
        float lookAhead, out Vector3 position, out Quaternion rotation)
    {
        Vector3 focus = team + Vector3.up * 1f - direction * lookAhead;
        position = team + direction * (distance + spread) + Vector3.up * height;
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    /// <summary>マップの中央からチームの中央へ向かう水平の向き（マップの外側）。</summary>
    private Vector3 Outward(Vector3 team)
    {
        Vector3 outward = Flat(team - Center);
        return outward.sqrMagnitude < 0.01f ? Vector3.back : outward.normalized;
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    /// <summary>
    /// **自分のチームの中央**と、中央からいちばん遠い味方までの距離（水平）。
    /// チームが分からない（1人で試しているなど）ときは、自分のプレイヤーだけで求める。
    /// </summary>
    private static bool TryGetTeam(out Vector3 center, out float spread)
    {
        center = Vector3.zero;
        spread = 0f;

        Transform self = LocalPlayer();
        SpaceJunkSession session = SpaceJunkSession.Current;
        NetworkManager manager = NetworkManager.Singleton;

        Vector3 sum = Vector3.zero;
        int count = 0;

        if (session != null && manager != null)
        {
            int myTeam = session.TeamOf(manager.LocalClientId);
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player != null && session.TeamOf(player.OwnerClientId) == myTeam)
                {
                    sum += player.transform.position;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            if (self == null)
            {
                return false;
            }

            sum = self.position;
            count = 1;
        }

        center = sum / count;

        if (session != null && manager != null)
        {
            int myTeam = session.TeamOf(manager.LocalClientId);
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player != null && session.TeamOf(player.OwnerClientId) == myTeam)
                {
                    spread = Mathf.Max(spread, Vector3.Distance(Flat(player.transform.position), Flat(center)));
                }
            }
        }

        return true;
    }

    private static Transform LocalPlayer()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            // 1台で複数人なら、そのPCの1人目（カメラはそのPCの1人目に付いているため）
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
