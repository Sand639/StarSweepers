using UnityEngine;

/// <summary>
/// **「この物は重い」という目印。** 付いている物資は、フックで**投げられず、引きずることしかできない**。
///
/// 宇宙ごみ集めのイベント「特殊デブリ」で使う（2026/9/29・大槻さん）。
/// プレハブには付けない。イベントの係が、重いデブリに選ばれた物へ**それぞれのPCで実行中に付ける**
/// （どれが重いか・重さはいくつかは、ホストが全員へ配っている）。
///
/// 重さ（<see cref="Weight"/>）で変わること：
///
///   ・**引きずれる距離** … 重いほど短い（<see cref="ThrowController"/> が見る）
///   ・**ゲージの速さ** … 重いほど速い（同上）
///   ・**見た目の大きさ** … 重いほど大きい（ここで変える）
///
/// 引っ掛けたあとの動きは <see cref="ThrowController"/>（宇宙ごみ式のときだけ効く）、
/// 右クリック（投げる）で撃ったフックが引っ掛からないのは <see cref="HookController"/> が見ている。
/// </summary>
public class HeavyHookable : MonoBehaviour
{
    /// <summary>重さ。1 が基準。引きずれる距離は「引っ張る強さ ÷ 重さ」で決まる（<see cref="ThrowController"/>）。</summary>
    public float Weight { get; private set; } = 1f;

    /// <summary>重くする前の大きさ。<see cref="Remove"/> で戻すために、最初の <see cref="Apply"/> で控える。</summary>
    private Vector3 baseScale;

    /// <summary>重くする前の物理の質量。Rigidbody が無ければ 1 として扱う。</summary>
    private float baseMass;

    /// <summary>いま重い物になっているか（<see cref="Apply"/> 済みで、まだ <see cref="Remove"/> されていない）。</summary>
    private bool applied;

    /// <summary>その物資が、いま重い物として扱われているか。重さも返す。</summary>
    public static bool TryGetWeight(Component item, out float weight)
    {
        weight = 1f;

        // Remove() のあとも部品自体は付いたまま残るので、「付いているか」だけでは判断しない。
        // enabled と applied の両方が立っているときだけ「いま重い」とみなす
        if (item == null || !item.TryGetComponent(out HeavyHookable heavy) || !heavy.enabled || !heavy.applied)
        {
            return false;
        }

        weight = heavy.Weight;
        return true;
    }

    /// <summary>
    /// 重い物にする。<paramref name="scale"/> 倍の大きさにし、重さに合わせて物理の質量も重くする。
    /// 何度呼んでもよい（元の大きさは最初の1回で覚える）。
    /// </summary>
    public void Apply(float weight, float scale)
    {
        // 2回目以降は控え直さない。控え直すと「すでに大きくした大きさ」を元の大きさだと思ってしまう
        if (!applied)
        {
            baseScale = transform.localScale;
            baseMass = TryGetComponent(out Rigidbody body) ? body.mass : 1f;
            applied = true;
        }

        enabled = true;

        // 0 や負の値が来ても、割り算（引っ張る強さ ÷ 重さ）や大きさが壊れないよう下限を 0.1 にする
        Weight = Mathf.Max(0.1f, weight);
        transform.localScale = baseScale * Mathf.Max(0.1f, scale);

        if (TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.mass = baseMass * Weight;
        }
    }

    /// <summary>ふつうの物に戻す（大きさと質量も元に戻す）。</summary>
    public void Remove()
    {
        if (applied)
        {
            transform.localScale = baseScale;

            if (TryGetComponent(out Rigidbody rigidbody))
            {
                rigidbody.mass = baseMass;
            }
        }

        applied = false;
        Weight = 1f;
        enabled = false;
    }
}
