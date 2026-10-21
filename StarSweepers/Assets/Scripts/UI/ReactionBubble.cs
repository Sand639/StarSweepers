using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **プレイヤーの頭の上に、リアクションのアイコンを少しの間だけ出す。**（試作・2026/10/21）
///
/// プレイヤー（体）に付けておき、<see cref="Show"/> を呼ぶと出る。出すのは <see cref="ReactionWheel"/>。
/// 見た目（頭の上の板）は再生したときにこのスクリプトが作るので、シーンやプレハブに何か置く必要はない。
///
/// - 画像（Icon）が入っていればそれを、無ければ**色付きの丸に文字**を出す（仮の画像）
/// - いつもカメラの方を向く
/// - 出るときに少し弾み、消える前にうすくなる
///
/// > **自分の画面にしか出ない。** ほかの人の画面に出すには、通信で知らせる仕組みが別に要る（まだ作っていない）。
/// </summary>
public class ReactionBubble : MonoBehaviour
{
    [Tooltip("体の足元から、アイコンを出す高さ（m）")]
    [SerializeField] private float headHeight = 2.3f;

    [Tooltip("アイコンの大きさ（m）")]
    [SerializeField] private float size = 0.9f;

    [Tooltip("アイコンを出しておく秒数")]
    [SerializeField] private float displaySeconds = 2.5f;

    [Tooltip("出るときに弾む秒数")]
    [SerializeField] private float popSeconds = 0.2f;

    [Tooltip("消える前に、うすくなっていく秒数")]
    [SerializeField] private float fadeSeconds = 0.4f;

    /// <summary>板の1辺の大きさ（Canvas の中の単位）。size（m）になるよう縮めて使う。</summary>
    private const float CanvasUnits = 100f;

    private Transform bubbleRoot;
    private CanvasGroup group;
    private Image circle;
    private Image icon;
    private Text label;
    private float shownAt = -999f;

    private void Awake()
    {
        BuildBubble();
        bubbleRoot.gameObject.SetActive(false);
    }

    /// <summary>**リアクションを出す。** 出ている途中なら、新しいほうに差し替えて出し直す。</summary>
    public void Show(ReactionEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        bool hasIcon = entry.Icon != null;
        icon.enabled = hasIcon;
        icon.sprite = entry.Icon;
        circle.color = entry.Color;
        label.enabled = !hasIcon;
        label.text = entry.Label;

        shownAt = Time.time;
        bubbleRoot.gameObject.SetActive(true);
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (!bubbleRoot.gameObject.activeSelf)
        {
            return;
        }

        float age = Time.time - shownAt;
        if (age >= displaySeconds)
        {
            bubbleRoot.gameObject.SetActive(false);
            return;
        }

        // 体が回っても、アイコンはいつも頭の真上でカメラの方を向く
        bubbleRoot.position = transform.position + Vector3.up * headHeight;
        Camera view = Camera.main;
        if (view != null)
        {
            bubbleRoot.rotation = view.transform.rotation;
        }

        // 出るとき：0 → 少し大きく → ちょうどの大きさ
        float pop = popSeconds > 0f ? Mathf.Clamp01(age / popSeconds) : 1f;
        float scale = pop < 0.7f ? Mathf.Lerp(0f, 1.15f, pop / 0.7f) : Mathf.Lerp(1.15f, 1f, (pop - 0.7f) / 0.3f);
        bubbleRoot.localScale = Vector3.one * (size / CanvasUnits * scale);

        // 消える前：うすくする
        float left = displaySeconds - age;
        group.alpha = fadeSeconds > 0f ? Mathf.Clamp01(left / fadeSeconds) : 1f;
    }

    /// <summary>頭の上の板（白いふち・色の丸・画像・文字）を作る。</summary>
    private void BuildBubble()
    {
        GameObject root = new GameObject("ReactionBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        bubbleRoot = root.transform;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        // 小さく縮めて使うので、文字がぼやけないよう細かく描かせる
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        ((RectTransform)root.transform).sizeDelta = new Vector2(CanvasUnits, CanvasUnits);
        group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Image border = ReactionUi.CreateImage("Border", root.transform, ReactionUi.CircleSprite, Color.white);
        ReactionUi.Stretch(border.rectTransform, 0f);

        circle = ReactionUi.CreateImage("Circle", root.transform, ReactionUi.CircleSprite, Color.gray);
        ReactionUi.Stretch(circle.rectTransform, CanvasUnits * 0.06f);

        icon = ReactionUi.CreateImage("Icon", root.transform, null, Color.white);
        icon.preserveAspect = true;
        ReactionUi.Stretch(icon.rectTransform, CanvasUnits * 0.16f);

        label = ReactionUi.CreateText("Label", root.transform, 22, Color.white);
        ReactionUi.Stretch(label.rectTransform, CanvasUnits * 0.08f);
    }
}
