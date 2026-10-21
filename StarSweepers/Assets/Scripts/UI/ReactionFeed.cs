using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **画面の端に「名前＋スタンプ」を並べて出す。**（試作・2026/10/21）
///
/// マリオパーティのように、誰かがリアクションを選ぶと、画面の右端に
///
///     yziku100  （ありがと）
///
/// のような札が横から滑り込み、少しの間出て消える。新しいものほど上に積む。
/// 試合中は全体を映すカメラになり、頭の上のアイコン（<see cref="ReactionBubble"/>）が見づらいので、こちらで見せる。
///
/// 見た目は再生したときにこのスクリプトが作る。出すのは <see cref="ReactionRelay"/>。
/// </summary>
public class ReactionFeed : MonoBehaviour
{
    [Tooltip("札を出しておく秒数")]
    [SerializeField] private float displaySeconds = 3.5f;

    [Tooltip("一度に並べる札の数（多いと古いものから消す）")]
    [SerializeField] private int maxCards = 5;

    [Tooltip("札が滑り込む・出ていく秒数")]
    [SerializeField] private float slideSeconds = 0.2f;

    [Header("置き場所（1920x1080 の画面での大きさ）")]
    [Tooltip("いちばん上の札の位置（画面の右端からの距離・画面の上下の真ん中からの高さ）")]
    [SerializeField] private Vector2 firstCardOffset = new Vector2(24f, 160f);

    [Tooltip("札の大きさ")]
    [SerializeField] private Vector2 cardSize = new Vector2(360f, 96f);

    [Tooltip("札と札のすき間")]
    [SerializeField] private float spacing = 10f;

    [SerializeField] private Color cardColor = new Color(0.05f, 0.07f, 0.1f, 0.8f);

    /// <summary>出ている札1枚ぶん。</summary>
    private class Card
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public float ShownAt;
        public float CurrentY;
    }

    private readonly List<Card> cards = new List<Card>();
    private RectTransform area;

    private void Awake()
    {
        GameObject canvasObject = new GameObject("ReactionFeedCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject areaObject = new GameObject("Area", typeof(RectTransform));
        areaObject.transform.SetParent(canvasObject.transform, false);
        area = (RectTransform)areaObject.transform;
        ReactionUi.Stretch(area, 0f);
    }

    private void OnDisable()
    {
        foreach (Card card in cards)
        {
            if (card.Rect != null)
            {
                Destroy(card.Rect.gameObject);
            }
        }
        cards.Clear();
    }

    /// <summary>**札を1枚出す。** いちばん上に積み、多すぎたら古いものを消す。</summary>
    public void Show(string playerName, ReactionEntry entry)
    {
        if (entry == null || area == null)
        {
            return;
        }

        Card card = CreateCard(playerName, entry);
        cards.Insert(0, card);

        while (cards.Count > Mathf.Max(1, maxCards))
        {
            Card oldest = cards[cards.Count - 1];
            cards.RemoveAt(cards.Count - 1);
            Destroy(oldest.Rect.gameObject);
        }

        // 新しい札は、いちばん上の位置から出す（ほかの札は Update でなめらかに下がる）
        card.CurrentY = TargetY(0);
        Update();
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        for (int i = cards.Count - 1; i >= 0; i--)
        {
            Card card = cards[i];
            float age = now - card.ShownAt;
            if (age >= displaySeconds)
            {
                cards.RemoveAt(i);
                Destroy(card.Rect.gameObject);
            }
        }

        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            float age = now - card.ShownAt;

            // 入るときと出るとき：右の画面の外から滑る
            float slideIn = slideSeconds > 0f ? Mathf.Clamp01(age / slideSeconds) : 1f;
            float slideOut = slideSeconds > 0f ? Mathf.Clamp01((displaySeconds - age) / slideSeconds) : 1f;
            float shown = EaseOut(Mathf.Min(slideIn, slideOut));
            float x = -firstCardOffset.x + (1f - shown) * (cardSize.x + firstCardOffset.x);

            card.CurrentY = Mathf.Lerp(card.CurrentY, TargetY(i), 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
            card.Rect.anchoredPosition = new Vector2(x, card.CurrentY);
            card.Group.alpha = shown;
        }
    }

    /// <summary>上から i 番目の札の高さ（画面の上下の真ん中から）。</summary>
    private float TargetY(int index)
    {
        return firstCardOffset.y - index * (cardSize.y + spacing);
    }

    private static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    /// <summary>札（黒い板・名前・スタンプ）を作る。右端ぞろえ。</summary>
    private Card CreateCard(string playerName, ReactionEntry entry)
    {
        GameObject cardObject = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
        cardObject.transform.SetParent(area, false);
        RectTransform rect = (RectTransform)cardObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = cardSize;

        CanvasGroup group = cardObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Image background = ReactionUi.CreateImage("Background", rect, null, cardColor);
        ReactionUi.Stretch(background.rectTransform, 0f);

        // 右側にスタンプ（白いふち＋色の丸＋文字か画像）
        float stampSize = cardSize.y - 12f;
        GameObject stamp = new GameObject("Stamp", typeof(RectTransform));
        stamp.transform.SetParent(rect, false);
        RectTransform stampRect = (RectTransform)stamp.transform;
        stampRect.anchorMin = stampRect.anchorMax = new Vector2(1f, 0.5f);
        stampRect.pivot = new Vector2(1f, 0.5f);
        stampRect.anchoredPosition = new Vector2(-6f, 0f);
        stampRect.sizeDelta = new Vector2(stampSize, stampSize);

        Image border = ReactionUi.CreateImage("Border", stampRect, ReactionUi.CircleSprite, Color.white);
        ReactionUi.Stretch(border.rectTransform, 0f);
        Image circle = ReactionUi.CreateImage("Circle", stampRect, ReactionUi.CircleSprite, entry.Color);
        ReactionUi.Stretch(circle.rectTransform, stampSize * 0.05f);

        if (entry.Icon != null)
        {
            Image icon = ReactionUi.CreateImage("Icon", stampRect, entry.Icon, Color.white);
            icon.preserveAspect = true;
            ReactionUi.Stretch(icon.rectTransform, stampSize * 0.15f);
        }
        else
        {
            Text stampText = ReactionUi.CreateText("Label", stampRect, 20, Color.white);
            ReactionUi.Stretch(stampText.rectTransform, 0f);
            stampText.text = entry.Label;
        }

        // 左側に名前
        Text nameText = ReactionUi.CreateText("Name", rect, 30, Color.white);
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = new Vector2(18f, 0f);
        nameRect.offsetMax = new Vector2(-(stampSize + 18f), 0f);
        nameText.text = playerName;

        return new Card { Rect = rect, Group = group, ShownAt = Time.unscaledTime };
    }
}
