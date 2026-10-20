using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **狙っている物の色を一時的に変えて、元に戻す**ための小さな道具。
///
/// 「持てる物」と「ロープ」で同じことをするので、ここにまとめてある。
/// ゲームオブジェクトに付ける部品ではなく、**中で使うだけの入れ物**。
///
/// ## 気をつけること
///
/// `renderer.material` に一度でも触ると、**その物だけのマテリアルが作られ、
/// `sharedMaterial` もそちらを指すようになる。**
/// そのため、**色を変える前に元の色を控えておく**必要がある。
/// （控えずに「あとで共有マテリアルから読み直す」ことはできない）
/// </summary>
public class InteractHighlight
{
    /// <summary>
    /// 色を持つ材質かどうかを見分けるための、色の項目の名前。
    /// このプロジェクトの材質（URP の Lit）は、本体の色をこの名前で持っている。
    /// これが無い材質（粒子やUIなど）は色を変えない。
    /// </summary>
    private const string BaseColorProperty = "_BaseColor";

    /// <summary>いま色を変えている見た目。<see cref="originalColors"/> と同じ並び順。</summary>
    private readonly List<Renderer> renderers = new List<Renderer>();

    /// <summary>変える前の色。<see cref="Clear"/> で戻すときに使う。</summary>
    private readonly List<Color> originalColors = new List<Color>();

    /// <summary>いま色を変えている最中か。</summary>
    public bool IsActive => renderers.Count > 0;

    /// <summary>
    /// `target` とその子の色を `color` に近づける。
    /// strength は混ぜる強さ（0で変化なし、1で色そのもの）。
    /// </summary>
    public void Apply(GameObject target, Color color, float strength)
    {
        Clear();

        if (target == null)
        {
            return;
        }

        target.GetComponentsInChildren(renderers);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.material == null
                || !renderer.material.HasProperty(BaseColorProperty))
            {
                // 色を持たない見た目でも、数を合わせるために控えておく
                // （renderers[i] と originalColors[i] が同じ物を指すようにする）
                originalColors.Add(Color.white);
                continue;
            }

            Color original = renderer.material.color;
            originalColors.Add(original);

            renderer.material.color = Color.Lerp(original, color, strength);
        }
    }

    /// <summary>色を元に戻す。</summary>
    public void Clear()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer == null || i >= originalColors.Count)
            {
                continue;
            }

            renderer.material.color = originalColors[i];
        }

        renderers.Clear();
        originalColors.Clear();
    }
}
