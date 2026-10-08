using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **チャージ量と、スキルチェックのゲージを見せる簡単な表示。**
///
/// プロトタイプ用なので、画像は使わず四角を伸び縮みさせるだけ（仕様2・5の「簡単なUI/Visual」）。
///   ・チャージ … 押している間、下のバーが左から伸びる
///   ・スキルチェック … 引き寄せ中、マーカーが左から右へ**1回だけ**動く。
///     色の付いた枠が“当たり”で、**枠ごとに投げる方向が違う**
///
/// 値の受け渡し：チャージは <see cref="HookController"/>、
/// ゲージは <see cref="ThrowController"/> から呼ばれる。
///
/// **オンライン対戦では、プレイヤーごとに頭の上に出す**（<see cref="CreateOverhead"/>）。
/// 1台で複数人のときも、それぞれのゲージが自分の頭の上に出る。
/// </summary>
public class HookChargeUI : MonoBehaviour
{
    /// <summary>スキルチェックの枠1つぶんの見た目（外枠・良い・最適の3つ）。</summary>
    [System.Serializable]
    public class ZoneVisual
    {
        [Tooltip("枠のまとまり（出す/隠すの対象）")]
        public GameObject root;

        [Tooltip("『通常成功』の範囲を表す四角")]
        public RectTransform hitRect;

        [Tooltip("『強い力』の範囲を表す四角")]
        public RectTransform goodRect;

        [Tooltip("『非常に強い力』の範囲を表す四角")]
        public RectTransform perfectRect;
    }

    [Header("チャージ表示")]
    [Tooltip("チャージ表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject chargeRoot;

    [Tooltip("伸びる部分。左端を軸にして横方向の拡大率を変える")]
    [SerializeField] private RectTransform chargeFill;

    [Header("スキルチェック表示")]
    [Tooltip("スキルチェック表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject timingRoot;

    [Tooltip("左から右へ動くマーカー")]
    [SerializeField] private RectTransform timingMarker;

    [Tooltip("枠の見た目。ThrowController の枠の数だけ用意しておく")]
    [SerializeField] private ZoneVisual[] zoneVisuals;

    [Tooltip("ゲージの幅（ピクセル）。マーカーと枠の位置計算に使う")]
    [SerializeField] private float barWidth = 420f;

    [Header("頭の上に出すとき（オンライン対戦）")]
    [Tooltip("足元から、どれだけ上に出すか（m）")]
    [SerializeField] private float overheadHeight = 2.2f;

    [Tooltip("頭の上に出すときの大きさ（1 = 画面の下に出すときと同じ）")]
    [SerializeField] private float overheadScale = 0.5f;

    /// <summary>画面の下のゲージどうしの縦の間（チャージ 70px・スキルチェック 120px の差）。</summary>
    private const float TimingGap = 50f;

    /// <summary>頭の上に出すときに追いかける相手。null なら画面の下に出す（今までどおり）。</summary>
    private Transform followTarget;

    /// <summary><see cref="CreateOverhead"/> で作った複製か。</summary>
    private bool isOverheadCopy;

    private Canvas canvas;

    /// <summary>
    /// **<paramref name="target"/> の頭の上に出すゲージを返す。**
    /// 同じ相手のぶんがもうあればそれを返し、無ければ作る。
    /// シーンに置いてあるゲージがあれば見た目をそのまま複製し、無いマップでは同じ見た目を作る。
    /// 作ったゲージは今いるシーンに置かれるので、シーンを移ると消える（移った先でまた呼ぶ）。
    /// </summary>
    public static HookChargeUI CreateOverhead(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        HookChargeUI template = null;
        foreach (HookChargeUI ui in FindObjectsByType<HookChargeUI>(FindObjectsSortMode.None))
        {
            if (ui.isOverheadCopy)
            {
                if (ui.followTarget == target)
                {
                    return ui;
                }
            }
            else if (template == null)
            {
                template = ui;
            }
        }

        HookChargeUI copy;
        if (template != null && template.GetComponent<Canvas>() != null)
        {
            // 置いてあるゲージは使わなくなるので隠しておく
            template.ShowCharge(false);
            template.ShowTiming(false);
            copy = Instantiate(template.gameObject).GetComponent<HookChargeUI>();
        }
        else
        {
            copy = BuildDefault();
        }

        copy.name = $"HookUI ({target.name})";
        copy.BeginFollow(target);
        return copy;
    }

    private void BeginFollow(Transform target)
    {
        isOverheadCopy = true;
        followTarget = target;
        canvas = GetComponent<Canvas>();

        ShowCharge(false);
        ShowTiming(false);

        PrepareContainer(chargeRoot);
        PrepareContainer(timingRoot);
        FollowNow();
    }

    /// <summary>まとまりを画面の真ん中基準にして、小さくする（位置は毎フレーム決める）。</summary>
    private void PrepareContainer(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one * overheadScale;
    }

    private void LateUpdate()
    {
        if (isOverheadCopy)
        {
            FollowNow();
        }
    }

    /// <summary>追いかける相手の頭の上の、画面での位置にゲージを動かす。</summary>
    private void FollowNow()
    {
        if (followTarget == null)
        {
            // プレイヤーが消えたら、ゲージも片付ける
            Destroy(gameObject);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null || canvas == null)
        {
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(followTarget.position + Vector3.up * overheadHeight);

        // カメラの後ろにいるときは出さない
        canvas.enabled = screen.z > 0f;
        if (!canvas.enabled)
        {
            return;
        }

        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)canvas.transform, screen, uiCamera, out Vector2 local);

        if (chargeRoot != null)
        {
            ((RectTransform)chargeRoot.transform).anchoredPosition = local;
        }

        if (timingRoot != null)
        {
            ((RectTransform)timingRoot.transform).anchoredPosition =
                local + new Vector2(0f, TimingGap * overheadScale);
        }
    }

    /// <summary>
    /// シーンにゲージが置かれていないマップ用に、同じ見た目のゲージを作る
    /// （<c>FishingSceneBuilder.CreateUI</c> と同じ形・同じ色）。
    /// </summary>
    private static HookChargeUI BuildDefault()
    {
        const float width = 420f;
        const int zoneCount = 2;

        GameObject canvasObject = new GameObject("HookUI",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Color back = new Color(0.12f, 0.12f, 0.12f, 0.8f);

        GameObject chargeRoot = MakeContainer("ChargeRoot", canvasObject.transform);
        MakeImage("ChargeBG", chargeRoot.transform, new Vector2(width, 24f), back, Vector2.zero);
        Image chargeFill = MakeImage("ChargeFill", chargeRoot.transform,
            new Vector2(width, 24f), new Color(0.3f, 0.8f, 1f, 1f), new Vector2(-width * 0.5f, 0f));
        chargeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        chargeFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);

        GameObject timingRoot = MakeContainer("TimingRoot", canvasObject.transform);
        MakeImage("TimingBG", timingRoot.transform, new Vector2(width, 28f), back, Vector2.zero);

        // 前半＝後ろへ投げる枠は青、後半＝前へ投げる枠は橙
        Color[] zoneTints =
        {
            new Color(0.35f, 0.6f, 1f, 0.35f),
            new Color(1f, 0.6f, 0.25f, 0.35f)
        };

        ZoneVisual[] zones = new ZoneVisual[zoneCount];
        for (int i = 0; i < zoneCount; i++)
        {
            GameObject zoneRoot = new GameObject($"Zone{i + 1}", typeof(RectTransform));
            zoneRoot.transform.SetParent(timingRoot.transform, false);
            RectTransform zoneRect = zoneRoot.GetComponent<RectTransform>();
            zoneRect.sizeDelta = Vector2.zero;

            zones[i] = new ZoneVisual
            {
                root = zoneRoot,
                hitRect = MakeImage("Hit", zoneRoot.transform,
                    new Vector2(90f, 28f), zoneTints[i], Vector2.zero).rectTransform,
                goodRect = MakeImage("Good", zoneRoot.transform,
                    new Vector2(50f, 28f), new Color(1f, 0.85f, 0.2f, 0.55f), Vector2.zero).rectTransform,
                perfectRect = MakeImage("Perfect", zoneRoot.transform,
                    new Vector2(18f, 28f), new Color(0.3f, 1f, 0.45f, 0.8f), Vector2.zero).rectTransform
            };
        }

        // マーカーは一番上に出したいので、最後に作る
        Image marker = MakeImage("Marker", timingRoot.transform, new Vector2(6f, 44f), Color.white, Vector2.zero);

        chargeRoot.SetActive(false);
        timingRoot.SetActive(false);

        HookChargeUI ui = canvasObject.AddComponent<HookChargeUI>();
        ui.chargeRoot = chargeRoot;
        ui.chargeFill = chargeFill.rectTransform;
        ui.timingRoot = timingRoot;
        ui.timingMarker = marker.rectTransform;
        ui.zoneVisuals = zones;
        ui.barWidth = width;
        return ui;
    }

    private static GameObject MakeContainer(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.zero;
        return go;
    }

    private static Image MakeImage(string name, Transform parent, Vector2 size, Color color, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>チャージ表示を出す／隠す。</summary>
    public void ShowCharge(bool visible)
    {
        if (chargeRoot != null)
        {
            chargeRoot.SetActive(visible);
        }
    }

    /// <summary>チャージ量（0〜1）を反映する。</summary>
    public void SetCharge(float amount)
    {
        if (chargeFill != null)
        {
            chargeFill.localScale = new Vector3(Mathf.Clamp01(amount), 1f, 1f);
        }
    }

    /// <summary>スキルチェック表示を出す／隠す。</summary>
    public void ShowTiming(bool visible)
    {
        if (timingRoot != null)
        {
            timingRoot.SetActive(visible);
        }
    }

    /// <summary>使う枠の数を伝える。余った見た目は隠す。</summary>
    public void SetZoneCount(int count)
    {
        if (zoneVisuals == null)
        {
            return;
        }

        for (int i = 0; i < zoneVisuals.Length; i++)
        {
            if (zoneVisuals[i] != null && zoneVisuals[i].root != null)
            {
                zoneVisuals[i].root.SetActive(i < count);
            }
        }

        if (count > zoneVisuals.Length)
        {
            Debug.LogWarning(
                $"{name}: 枠が {count} 個ありますが、表示は {zoneVisuals.Length} 個分しかありません。" +
                "検証シーンを作り直すか、Zone Visuals を足してください。", this);
        }
    }

    /// <summary>
    /// 枠1つの位置と広さを決める。
    /// <paramref name="center"/> は 0〜1、広さは中心からの片側の広さ（0〜1）。
    /// </summary>
    public void SetZone(int index, float center, float hit, float good, float perfect)
    {
        if (zoneVisuals == null || index < 0 || index >= zoneVisuals.Length)
        {
            return;
        }

        ZoneVisual visual = zoneVisuals[index];
        if (visual == null)
        {
            return;
        }

        float x = (center - 0.5f) * barWidth;

        PlaceRect(visual.hitRect, x, hit);
        PlaceRect(visual.goodRect, x, good);
        PlaceRect(visual.perfectRect, x, perfect);
    }

    private void PlaceRect(RectTransform rect, float x, float halfWidth01)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(halfWidth01 * 2f * barWidth, rect.sizeDelta.y);
    }

    /// <summary>マーカーの位置（0〜1）を反映する。</summary>
    public void SetMarker(float position)
    {
        if (timingMarker != null)
        {
            timingMarker.anchoredPosition =
                new Vector2((Mathf.Clamp01(position) - 0.5f) * barWidth, 0f);
        }
    }
}
