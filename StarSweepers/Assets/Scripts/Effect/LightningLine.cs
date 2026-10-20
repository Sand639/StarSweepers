using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **電撃1本ぶんの線の見た目**。「太くて薄い光（Glow）」と「細くて明るい芯（Core）」の2本の LineRenderer を重ねて描く。
///
/// 色は HDR（1 より大きい値）で渡すので、カメラのブルーム（光のにじみ）で光って見える。
/// 線は親オブジェクト基準（<c>useWorldSpace = false</c>）なので、親が動けば一緒に動く。
/// </summary>
public class LightningLine
{
    /// <summary>マテリアルの色のプロパティ名（URP の Particles/Unlit）。毎回名前で引かず、番号にしておく。</summary>
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>太くて薄い光の線。</summary>
    private readonly LineRenderer glow;

    /// <summary>細くて明るい芯の線。</summary>
    private readonly LineRenderer core;

    /// <summary>
    /// 色を線ごとに変えるための入れ物。マテリアルは全部の線で1つを共有しているので、
    /// `material.color` を変えると全部の線の色が変わってしまう。これを使えば線ごとに色を上書きできる。
    /// </summary>
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

    /// <summary><paramref name="parent"/> の子に、「名前_Glow」「名前_Core」の2本の線を作る（最初は非表示）。</summary>
    public LightningLine(Transform parent, string name, Material material)
    {
        glow = CreateLine(parent, name + "_Glow", material);
        core = CreateLine(parent, name + "_Core", material);
    }

    /// <summary>点（先頭から <paramref name="count"/> 個）と色・太さを渡して、2本とも表示する。</summary>
    public void Set(Vector3[] positions, int count, Color glowColor, Color coreColor, float glowWidth, float coreWidth)
    {
        SetLine(glow, positions, count, glowColor, glowWidth);
        SetLine(core, positions, count, coreColor, coreWidth);
    }

    /// <summary>2本まとめて出す／消す。</summary>
    public void SetVisible(bool visible)
    {
        glow.enabled = visible;
        core.enabled = visible;
    }

    private void SetLine(LineRenderer line, Vector3[] positions, int count, Color color, float width)
    {
        line.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            line.SetPosition(i, positions[i]);
        }

        line.widthMultiplier = width;

        block.SetColor(BaseColorId, color);
        line.SetPropertyBlock(block);

        line.enabled = true;
    }

    /// <summary>線1本ぶんの子オブジェクトを作る。光なので影は付けず・受けず、最初は非表示にしておく。</summary>
    private static LineRenderer CreateLine(Transform parent, string name, Material material)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);

        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;

        // 端を少し丸める（0 だと角張った端になる）
        line.numCapVertices = 2;

        // 線の面を常にカメラへ向ける（どこから見ても太さが変わらない）
        line.alignment = LineAlignment.View;

        // 両端を細くする
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.3f, 1f),
            new Keyframe(0.7f, 1f),
            new Keyframe(1f, 0.2f));

        line.enabled = false;
        return line;
    }

    /// <summary>
    /// 線の太さの変わり方を差し替える（ビームの芯のように「根元から先まで同じ太さ」にしたいとき）。
    /// </summary>
    public void SetWidthCurve(AnimationCurve curve)
    {
        glow.widthCurve = curve;
        core.widthCurve = curve;
    }

    // ------------------------------------------------------------
    // マテリアル
    // ------------------------------------------------------------

    /// <summary>マテリアルが指定されていないときの予備。URP の Particles/Unlit を加算合成にする。</summary>
    public static Material CreateAdditiveMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        SetupAdditive(material);
        return material;
    }

    /// <summary>
    /// URP の Particles/Unlit を「透明・加算合成」に設定する。
    /// 加算合成＝背景の色に線の色を**足す**描き方。重なるほど明るくなるので、光らしく見える。
    /// `_Surface`・`_Blend` はインスペクターの表示用で、実際の合成は `_SrcBlend`〜`_DstBlendAlpha` で決まる。
    /// </summary>
    public static void SetupAdditive(Material material)
    {
        material.SetFloat("_Surface", 1f);    // 透明
        material.SetFloat("_Blend", 2f);      // 加算
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);

        // 深度を書かない（透明な物どうしが前後関係で欠けないように）
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", Color.white);
    }
}
