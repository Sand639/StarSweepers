using UnityEngine;

/// <summary>
/// **「このデブリは特別」という見た目**（色を塗って明滅させ、まわりを光らせる）。
/// 特殊デブリ（重いデブリ。水色）のイベントで使う（2026/9/29 までは期間限定高価値デブリの金色にも使っていた）。
///
/// プレハブには付けない。<see cref="SpaceJunkRound"/> が、選ばれたデブリへ
/// **それぞれのPCで実行中に付けたり外したりする**（どれが選ばれたかは、ホストが一覧で全員に配っている）。
/// プレハブを変えずに済むようにするため。
///
/// 色は元のマテリアルを書き換えず、上から塗り重ねる（MaterialPropertyBlock）。
/// 外すと重ね塗りを消すだけで、元の色に戻る。
/// </summary>
public class SpaceJunkHighValueMark : MonoBehaviour
{
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private Light glow;
    private Color color = Color.yellow;

    /// <summary>いま塗っている色（レーダーの点の色に使う）。</summary>
    public Color MarkColor => color;
    private bool shown;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>色を塗って、光らせる。</summary>
    public void Show(Color markColor)
    {
        color = markColor;

        if (shown)
        {
            return;
        }

        shown = true;
        enabled = true;

        renderers = GetComponentsInChildren<MeshRenderer>();
        block ??= new MaterialPropertyBlock();

        if (glow == null)
        {
            // デブリの中心に点光源を1つ足す。光が届く範囲は 3m、影は作らない（軽くするため）
            var lightObject = new GameObject("HighValueGlow");
            lightObject.transform.SetParent(transform, false);
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 3f;
            glow.shadows = LightShadows.None;
        }

        glow.color = color;
        glow.enabled = true;
    }

    /// <summary>ふつうの見た目に戻す。</summary>
    public void Hide()
    {
        if (!shown)
        {
            return;
        }

        shown = false;
        enabled = false;

        if (renderers != null)
        {
            foreach (Renderer target in renderers)
            {
                if (target != null)
                {
                    target.SetPropertyBlock(null);
                }
            }
        }

        if (glow != null)
        {
            Destroy(glow.gameObject);
            glow = null;
        }
    }

    private void LateUpdate()
    {
        if (!shown || renderers == null)
        {
            return;
        }

        // ゆっくり明滅させて、ふつうのデブリと見分けやすくする
        // （明るさは 0.6〜1.0 の間を、およそ1秒に1往復。6 は1秒あたりの角度（ラジアン））
        float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 6f);
        Color tinted = new Color(color.r * pulse, color.g * pulse, color.b * pulse, 1f);

        foreach (Renderer target in renderers)
        {
            if (target == null)
            {
                continue;
            }

            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tinted);
            block.SetColor(ColorId, tinted);
            target.SetPropertyBlock(block);
        }

        if (glow != null)
        {
            // 光の強さも同じリズムで 2.1〜2.5 の間を往復させる
            glow.intensity = 1.5f + pulse;
        }
    }
}
