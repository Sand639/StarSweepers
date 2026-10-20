using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **レース中の画面表示。**
///
/// | 場所 | 出るもの |
/// | --- | --- |
/// | 左上 | **周回**（1/1） |
/// | 右上 | **タイム** |
/// | 中央 | **カウントダウン**（3・2・1・スタート！）と、**ゴールの結果** |
/// | 右下 | 速度 |
/// | 下 | やり直しの案内 |
///
/// **画面はコードで組み立てている。**
/// Unityに最初から入っている文字は日本語を持っておらず、
/// **パソコンのフォントを借りる**必要があるため（<see cref="UiFont"/>）。
/// </summary>
public class RaceHud : MonoBehaviour
{
    [Tooltip("映す相手。空なら、自分が操作している人を探す")]
    [SerializeField] private RaceRacer racer;

    [Tooltip("文字の色")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("文字を読みやすくする、後ろの帯の色")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.45f);

    [Tooltip("カウントダウンなど、中央に出る大きな文字の色")]
    [SerializeField] private Color bigTextColor = new Color(1f, 0.92f, 0.35f);

    [Tooltip("重力が変わるときなど、気をつけてほしいときの色")]
    [SerializeField] private Color warningColor = new Color(1f, 0.55f, 0.2f);

    [Tooltip("画面の左下に出す、操作の案内。**乗り物が変わったらここを書き換える**")]
    [SerializeField] private string hintText = "W / S  進む・戻る　　A / D  ハンドル　　R  やり直し";

    // 画面の部品。すべて Build() で作る。
    // 角の文字（lap / time / speed / gravity）は「帯（親）＋文字（子）」の2段になっていて、
    // 出し入れは帯のほう（transform.parent）で行う
    private Font font;
    private Text lapText;
    private Text timeText;
    private Text speedText;
    private Text gravityText;
    private GameObject gravityPanel;
    private Text bigText;
    private Text resultText;
    private GameObject resultPanel;

    private void Awake()
    {
        font = UiFont.Find(32);
        Build();
    }

    private void Update()
    {
        RaceManager race = RaceManager.Current;

        // 重力はレースが無いシーンでも出す（割れるガラス床の検証シーンなど）
        UpdateGravity();

        // **レースが無いシーンでは、周回・タイム・速度は出さない**
        SetRacePanelsVisible(race != null);

        if (race == null)
        {
            return;
        }

        if (racer == null)
        {
            racer = FindLocalRacer();
        }

        UpdateBigText(race);
        UpdateCorners(race);
        UpdateResult(race);
    }

    /// <summary>周回・タイム・速度・中央の大きな文字を、まとめて出す・隠す。隠すときはゴールの結果も隠す。</summary>
    private void SetRacePanelsVisible(bool visible)
    {
        SetActiveIfChanged(lapText.transform.parent.gameObject, visible);
        SetActiveIfChanged(timeText.transform.parent.gameObject, visible);
        SetActiveIfChanged(speedText.transform.parent.gameObject, visible);
        SetActiveIfChanged(bigText.gameObject, visible);

        if (!visible)
        {
            SetActiveIfChanged(resultPanel, false);
        }
    }

    /// <summary>変わるときだけ出し入れする（毎フレーム SetActive を呼ぶと無駄が多いため）。</summary>
    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    /// <summary>
    /// **いまの重力を出す。**
    /// 重力を変える係（<see cref="GravityShifter"/>）がいないシーンでは、何も出さない。
    /// </summary>
    private void UpdateGravity()
    {
        GravityShifter shifter = GravityShifter.Current;
        bool show = shifter != null;

        SetActiveIfChanged(gravityPanel, show);

        if (!show)
        {
            return;
        }

        if (shifter.IsChanging)
        {
            gravityText.text = $"重力が変わっている…　{shifter.CurrentStrength:0.0}";
            gravityText.color = warningColor;

            return;
        }

        if (shifter.IsWarning)
        {
            // 点滅させて気づかせる（0.5秒周期で、前半の0.25秒だけ明るい色）
            bool bright = Mathf.Repeat(Time.time, 0.5f) < 0.25f;

            gravityText.text = "まもなく重力が変わる！";
            gravityText.color = bright ? warningColor : textColor;

            return;
        }

        gravityText.text = $"重力  {shifter.CurrentStrength:0.0}　（{shifter.StrengthLabel}）";
        gravityText.color = textColor;
    }

    /// <summary>中央の大きな文字（3・2・1・スタート！）。何を出すかは進行役に聞く。</summary>
    private void UpdateBigText(RaceManager race)
    {
        string message = race.BigMessage();

        bigText.text = message;
        bigText.color = bigTextColor;

        // カウントダウンの数字は、消える直前に少し小さくなる
        // （数字が切り替わった直後は 1.15倍、次の数字に変わる直前は 0.75倍）
        float scale = 1f;

        if (race.State == RaceManager.Phase.Countdown)
        {
            // 残り秒数の小数部分（例：2.3秒 → 0.3）。1に近いほど切り替わった直後
            float fraction = race.CountdownRemaining - Mathf.Floor(race.CountdownRemaining);
            scale = Mathf.Lerp(0.75f, 1.15f, fraction);
        }

        bigText.rectTransform.localScale = Vector3.one * scale;
    }

    /// <summary>左上の周回、右上のタイム、右下の速度。</summary>
    private void UpdateCorners(RaceManager race)
    {
        // 記録の Lap は「走り終えた周回数」なので、画面には +1 して「今は何周目か」を出す。
        // ゴール後は最終周のまま止めておく
        int lap = racer != null ? Mathf.Clamp(racer.Lap + 1, 1, race.LapsToFinish) : 1;

        if (racer != null && racer.Finished)
        {
            lap = race.LapsToFinish;
        }

        lapText.text = $"周回  {lap} / {race.LapsToFinish}";

        // ゴール後は、ゴールした時点のタイムで止める
        float shown = racer != null && racer.Finished ? racer.FinishTime : race.ElapsedTime;
        timeText.text = RaceManager.FormatTime(shown);

        // 速さは位置の変化から出しているので、カートでも走る人でも同じように出せる
        // （m/s × 3.6 = km/h）
        speedText.text = racer != null ? $"{racer.CurrentSpeed * 3.6f:0} km/h" : string.Empty;
    }

    /// <summary>ゴールしたら、中央にタイムと順位を出す。</summary>
    private void UpdateResult(RaceManager race)
    {
        bool show = racer != null && racer.Finished;

        SetActiveIfChanged(resultPanel, show);

        if (!show)
        {
            return;
        }

        resultText.text = $"ゴール！\n\nタイム  {RaceManager.FormatTime(racer.FinishTime)}\n" +
                          $"順位  {racer.Rank} 位\n\nR でもう一度";
    }

    /// <summary>自分が操作している人を探す。見つからなければ、いる中の先頭（1人で遊ぶとき用）。</summary>
    private static RaceRacer FindLocalRacer()
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            if (RaceRacer.All[i].IsLocalPlayer)
            {
                return RaceRacer.All[i];
            }
        }

        return RaceRacer.All.Count > 0 ? RaceRacer.All[0] : null;
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    /// <summary>
    /// 画面を組み立てる。キャンバスを1枚作り、四隅の文字・中央の大きな文字・結果の板を乗せる。
    /// 位置や大きさは 1920×1080 を基準にした数字（px）。画面の大きさが変わっても同じ見え方になるよう拡大縮小される。
    /// </summary>
    private void Build()
    {
        GameObject root = new GameObject("RaceCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // ポーズ画面（100）より後ろに出す
        canvas.sortingOrder = 50;

        // 画面の大きさに合わせて全体を拡大縮小する。幅と高さの中間（0.5）に合わせる
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 四隅の文字。引数は順に「貼る角（0,1＝左上・1,1＝右上・1,0＝右下・0,0＝左下）、角からのずれ、帯の大きさ、文字の大きさ、寄せ」
        lapText = CreateCornerText(root.transform, "Lap", new Vector2(0f, 1f),
            new Vector2(40f, -40f), new Vector2(300f, 64f), 36, TextAnchor.MiddleLeft);

        timeText = CreateCornerText(root.transform, "Time", new Vector2(1f, 1f),
            new Vector2(-40f, -40f), new Vector2(300f, 64f), 36, TextAnchor.MiddleRight);

        speedText = CreateCornerText(root.transform, "Speed", new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(300f, 56f), 30, TextAnchor.MiddleRight);

        CreateCornerText(root.transform, "Hint", new Vector2(0f, 0f),
            new Vector2(40f, 40f), new Vector2(560f, 48f), 24, TextAnchor.MiddleLeft)
            .text = hintText;

        gravityText = CreateCornerText(root.transform, "Gravity", new Vector2(0.5f, 1f),
            new Vector2(0f, -40f), new Vector2(460f, 56f), 30, TextAnchor.MiddleCenter);
        gravityPanel = gravityText.transform.parent.gameObject;
        gravityPanel.SetActive(false);

        // 中央の大きな文字。帯は付けず、画面の中央より少し上（120px）に置く
        bigText = CreateText(root.transform, "BigMessage", string.Empty, 140, TextAnchor.MiddleCenter);
        RectTransform bigRect = bigText.rectTransform;
        bigRect.anchorMin = new Vector2(0.5f, 0.5f);
        bigRect.anchorMax = new Vector2(0.5f, 0.5f);
        bigRect.sizeDelta = new Vector2(900f, 240f);
        bigRect.anchoredPosition = new Vector2(0f, 120f);

        BuildResultPanel(root.transform);
    }

    /// <summary>ゴールの結果を出す板。中央に置いて、ふだんは隠しておく。</summary>
    private void BuildResultPanel(Transform parent)
    {
        resultPanel = new GameObject("Result", typeof(RectTransform), typeof(Image));
        resultPanel.transform.SetParent(parent, false);
        resultPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform rect = resultPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(560f, 380f);
        rect.anchoredPosition = Vector2.zero;

        resultText = CreateText(resultPanel.transform, "Text", string.Empty, 40, TextAnchor.MiddleCenter);
        Stretch(resultText.rectTransform);

        resultPanel.SetActive(false);
    }

    /// <summary>
    /// 帯（半透明の四角）と、その上の文字を1組作って、文字のほうを返す。
    /// <paramref name="anchor"/> は画面のどこに貼るか（0〜1）。帯の位置の基準（pivot）も同じ角にそろえる。
    /// </summary>
    private Text CreateCornerText(Transform parent, string name, Vector2 anchor,
        Vector2 offset, Vector2 size, int fontSize, TextAnchor alignment)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = panelColor;

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;

        // 文字は帯いっぱいに広げて、左右に 16px の余白を取る
        Text text = CreateText(panel.transform, "Text", string.Empty, fontSize, alignment);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(16f, 0f);
        text.rectTransform.offsetMax = new Vector2(-16f, 0f);

        return text;
    }

    /// <summary>文字を1つ作る。はみ出しても切らず、クリックの邪魔もしない設定にしてある。</summary>
    private Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.color = textColor;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>親いっぱいに広げる。</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
