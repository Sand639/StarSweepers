using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **リアクション1つぶんの中身。**（試作・2026/10/21）
/// <see cref="ReactionWheel"/> のインスペクターで8つ並べる。
/// </summary>
[Serializable]
public class ReactionEntry
{
    [Tooltip("ホイールと頭の上に出す文字（画像が無いときの仮の見た目）")]
    [SerializeField] private string label = "いいね";

    [Tooltip("丸の色")]
    [SerializeField] private Color color = Color.gray;

    [Tooltip("出す画像。空なら、色の丸に文字を出す")]
    [SerializeField] private Sprite icon;

    public ReactionEntry()
    {
    }

    public ReactionEntry(string label, Color color)
    {
        this.label = label;
        this.color = color;
    }

    public string Label => label;
    public Color Color => color;
    public Sprite Icon => icon;
}

/// <summary>
/// **リアクションの見た目を作るための小道具。** <see cref="ReactionWheel"/> と <see cref="ReactionBubble"/> が使う。
/// 画像を用意しなくて済むよう、丸の画像はここでその場で作る。
/// </summary>
public static class ReactionUi
{
    private static Sprite circleSprite;

    /// <summary>白い丸の画像（ふちをなめらかにしてある）。一度作ったら使い回す。</summary>
    public static Sprite CircleSprite
    {
        get
        {
            if (circleSprite == null)
            {
                circleSprite = CreateCircleSprite(256);
            }
            return circleSprite;
        }
    }

    /// <summary>画像を1つ作って親の下に置く。</summary>
    public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>真ん中ぞろえの日本語の文字を1つ作って親の下に置く。</summary>
    public static Text CreateText(string name, Transform parent, int fontSize, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = UiFont.Find(fontSize);
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>親いっぱいに広げる（inset の分だけ内側に縮める）。</summary>
    public static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>真ん中に、決めた大きさで置く。</summary>
    public static void Center(RectTransform rect, Vector2 position, float diameter)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(diameter, diameter);
    }

    private static Sprite CreateCircleSprite(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "ReactionCircle";

        float radius = resolution * 0.5f;
        Color32[] pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                // ふちの1ピクセルだけ半分すける（ギザギザにしないため）
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * resolution + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), 100f);
    }
}
