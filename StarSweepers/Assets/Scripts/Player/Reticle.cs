using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面の中央に出す照準（レティクル）。
///
/// 「いま画面のどこを狙っているか」を示すための十字の印。
/// 一人称ではマウスカーソルを消しているため、これが無いと狙いが分からない。
///
/// 何かを狙っているときは色を変えて、「これに対して操作できる」と伝えられる。
/// 色を変えたいときは、外から <see cref="SetHighlight"/> を呼ぶ。
/// </summary>
public class Reticle : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("色を変える対象。十字を作っている画像を入れる")]
    [SerializeField] private List<Graphic> parts = new List<Graphic>();

    [Header("色")]
    [Tooltip("何も狙っていないときの色")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.75f);

    [Header("大きさ")]
    [Tooltip("狙っているものがあるときに、少しだけ大きくする倍率")]
    [SerializeField] private float highlightScale = 1.25f;

    [Tooltip("大きさが変わるときの滑らかさ。0にすると一瞬で変わる")]
    [SerializeField] private float scaleSmooth = 14f;

    /// <summary>いま塗っている色。同じ色を何度も塗り直さないために覚えておく。</summary>
    private Color currentColor;

    /// <summary>目指している大きさの倍率（1 = ふだん、highlightScale = 狙っているとき）。</summary>
    private float targetScale = 1f;

    private void Awake()
    {
        currentColor = normalColor;
        ApplyColor(normalColor);
    }

    private void Update()
    {
        if (scaleSmooth <= 0f)
        {
            transform.localScale = Vector3.one * targetScale;
            return;
        }

        // 縦横は同じ倍率なので、いまの大きさは x だけ見ればよい。
        // Exp を使うと、フレームレートが変わっても同じ速さで近づく
        float scale = Mathf.Lerp(
            transform.localScale.x,
            targetScale,
            1f - Mathf.Exp(-scaleSmooth * Time.deltaTime));

        transform.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// 狙っているものに合わせて見た目を変える。
    /// color に null を渡すと、通常の見た目に戻る。
    /// </summary>
    public void SetHighlight(Color? color)
    {
        Color next = color ?? normalColor;

        if (next != currentColor)
        {
            currentColor = next;
            ApplyColor(next);
        }

        targetScale = color.HasValue ? highlightScale : 1f;
    }

    /// <summary>十字を作っている画像すべてを、同じ色に塗る。</summary>
    private void ApplyColor(Color color)
    {
        foreach (Graphic part in parts)
        {
            if (part != null)
            {
                part.color = color;
            }
        }
    }
}
