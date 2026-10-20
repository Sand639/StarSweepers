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
/// | ① マップの紹介 | マップの中央（0,0,0）を向いたまま、真上から 45 度傾けた所を 180 度回る | 5 秒 |
/// | ② 自分のチームに寄る | 自分のチームの後ろ（マップの外側、少し上）へ寄る | 1.5 秒 |
/// | ③ カウントダウン | 「3」「2」「1」。**始まると同時に**、下の 1.・2. のカメラの動きをする | 3 秒 |
/// | 1. チームの周りを回る | ② の位置から、チームの中央を中心に 220 度回る（内側・少し下へ移りながら） | 2 秒 |
/// | 2. 全体カメラへ戻る | ふだんのカメラ（全体カメラ）の位置へ戻る。START をまたいで続く | 1.7 秒 |
/// | ④ START! | 動けるようになる。「START!」を出す | 1 秒表示 |
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

    /// <summary>② で寄ったときの、チームの中央から後ろへの距離（水平）。</summary>
    private const float CloseBack = 6f;

    /// <summary>② で寄ったときの高さ。</summary>
    private const float CloseHeight = 3.5f;

    /// <summary>チームの周りを回り始めてから、寄った位置の距離と高さから回るときの距離と高さへ移り終わるまでの割合。</summary>
    private const float OrbitSettle = 0.3f;

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

        float t = (float)(now - round.IntroStartServerTime);
        float flyoverEnd = round.IntroFlyoverSeconds;
        float zoomEnd = flyoverEnd + round.IntroZoomSeconds;

        // ③ のカウントダウンが始まると同時に、チームの周りを回り → 全体カメラへ戻る（START をまたいでもよい）
        float orbitEnd = zoomEnd + round.IntroTeamOrbitSeconds;
        float returnEnd = orbitEnd + round.IntroReturnSeconds;

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
            // ③ 3・2・1 → ④ START!（文字はカメラとは別に、START の時刻で切り替える）
            ShowTexts(sinceStart < 0f ? Mathf.CeilToInt(-sinceStart).ToString() : "START!", string.Empty);

            if (t < orbitEnd)
            {
                // 1. チームの周りを回る（② の位置から、内側・少し下へ）
                if (hasTeam)
                {
                    GetOrbitPose((t - zoomEnd) / Mathf.Max(0.01f, round.IntroTeamOrbitSeconds), team, spread,
                        out position, out rotation);
                }
            }
            else if (t < returnEnd)
            {
                // 2. 全体カメラ（ふだんのカメラ）へ戻る
                Vector3 fromPosition = closePosition;
                Quaternion fromRotation = closeRotation;
                if (hasTeam)
                {
                    GetOrbitPose(1f, team, spread, out fromPosition, out fromRotation);
                }

                Camera main = Camera.main;
                float k = Mathf.SmoothStep(0f, 1f, (t - orbitEnd) / Mathf.Max(0.01f, round.IntroReturnSeconds));
                position = main != null ? Vector3.Lerp(fromPosition, main.transform.position, k) : fromPosition;
                rotation = main != null ? Quaternion.Slerp(fromRotation, main.transform.rotation, k) : fromRotation;
            }
            else if (introCamera != null)
            {
                // 戻り終わったので、演出用のカメラを止める（文字だけ残す）
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

        Vector3 outward = Outward(team);
        Vector3 focus = team + Vector3.up * 1f - outward * 2f;
        position = team + outward * (CloseBack + spread) + Vector3.up * CloseHeight;
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    /// <summary>
    /// 1. チームの中央を中心に回るカメラの位置（0〜1）。② で寄った位置から回り始め、
    /// 回りながら少し下（<see cref="SpaceJunkRound.IntroTeamOrbitHeight"/>）へ下がる。
    /// </summary>
    private void GetOrbitPose(float progress, Vector3 team, float spread, out Vector3 position, out Quaternion rotation)
    {
        progress = Mathf.Clamp01(progress);
        Vector3 outward = Outward(team);

        float startAngle = Mathf.Atan2(outward.z, outward.x) * Mathf.Rad2Deg;
        float angle = startAngle + round.IntroTeamOrbitDegrees * Mathf.SmoothStep(0f, 1f, progress);
        Vector3 horizontal = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

        // 寄った位置の距離と高さから、回るときの距離と高さへなめらかに移す（いきなり飛ばないように）
        float settle = Mathf.SmoothStep(0f, 1f, progress / OrbitSettle);
        float distance = Mathf.Lerp(CloseBack + spread, round.IntroTeamOrbitDistance + spread, settle);
        float height = Mathf.Lerp(CloseHeight, round.IntroTeamOrbitHeight, settle);

        Vector3 closeFocus = team + Vector3.up * 1f - outward * 2f;
        Vector3 focus = Vector3.Lerp(closeFocus, team + Vector3.up * 1f, settle);

        position = team + horizontal * distance + Vector3.up * height;
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
