using UnityEngine;

/// <summary>
/// **フックの先端。** 飛んでいる「かぎ針」そのもの。
///
/// このスクリプトは考えない。動かすのは <see cref="HookController"/> の役目で、
/// ここは「物資に当たったよ」と報告する当たり判定だけを持つ。
/// 分けておくと、フックの見た目や当たり方だけを後で差し替えやすい。
///
/// 必要な部品：トリガーにした Collider と、キネマティックな Rigidbody。
/// （Rigidbody が無いと、動かしたときにトリガーの通知が出ないことがある）
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class HookProjectile : MonoBehaviour
{
    /// <summary>物資に触れた瞬間に呼ばれる。相手の <see cref="HookableObject"/> を渡す。</summary>
    public System.Action<HookableObject> HookableTouched;

    /// <summary>大型の拉扯対象に触れた瞬間に呼ばれる。</summary>
    public System.Action<IHookPullable> PullableTouched;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        // 当たり判定はすり抜け（トリガー）にしておく。押し返さず、触れたことだけ拾う
        foreach (Collider col in GetComponents<Collider>())
        {
            col.isTrigger = true;
        }
    }

    /// <summary>フックの位置を動かす。HookController から毎フレーム呼ばれる。</summary>
    public void MoveTo(Vector3 worldPosition)
    {
        // 物理の位置（Rigidbody）と見た目の位置（Transform）の両方に入れる。
        // 片方だけだと、次の物理更新までもう片方がずれたままになり、当たり判定と見た目が食い違う
        body.position = worldPosition;
        transform.position = worldPosition;
    }

    /// <summary>
    /// 何かに触れたら「触れたよ」と伝えるだけ。**本当に引っ掛けてよいか**
    /// （投げ用のフックではないか、横取りできる状況か など）は、受け取った HookController が決める。
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // 大型物件（IHookPullable。動かせるブロックや移動床）を先に見る。
        // 同じ物に HookableObject も付いていた場合は、大型物件として扱う
        IHookPullable pullable = HookController.FindPullable(other);
        if (pullable != null && pullable.CanBeHooked)
        {
            PullableTouched?.Invoke(pullable);
            return;
        }

        HookableObject hookable = other.GetComponentInParent<HookableObject>();

        // すでに誰かが引っ掛けている物資・消えている最中の物資は伝えない
        if (hookable != null && !hookable.IsHooked && !hookable.IsVanished)
        {
            HookableTouched?.Invoke(hookable);
        }
    }
}
