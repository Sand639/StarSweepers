using UnityEngine;

/// <summary>
/// **床の外（場外）に置く引っ張り先。移動床（<see cref="PullAblePlate"/>）に乗っている人がここを引っ張ると、
/// 乗っている床がこのアンカーの向きへ1回ぶん動く**（床の上から自分の乗っている床を動かす手段）。
///
/// ・引っ掛けられるのは**床に乗っている人だけ**。乗っていない人のフックは素通りする
/// ・引っ張り終わった時点でどの床に乗っているかをもう一度調べる。途中で降りていたら何もしない
/// ・動く向きは「床の中心 → アンカー」の水平方向。アンカーの高さは関係ない
///
/// プレハブは `Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlateAnchor.prefab`。
/// どの床に付くものでもなく、場外に何個でも置ける（置いた位置がそのまま動く向きになる）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlateAnchor : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    public Component HookComponent => this;

    /// <summary>フックの線がつながる場所（アンカーの位置）。</summary>
    public Vector3 HookAnchorPoint => transform.position;

    /// <summary>いま誰かのフックが掛かっているか。</summary>
    public bool IsHooked { get; private set; }

    /// <summary>いま引っ掛けられるか（誰が撃ったかまで見るのは <see cref="CanBeHookedBy"/>）。</summary>
    public bool CanBeHooked => isActiveAndEnabled && gameObject.activeInHierarchy && !IsHooked;

    private void OnValidate()
    {
        // フックが「触れた」ことだけ分かればよく、物をさえぎらない（トリガー）
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    /// <summary>そのプレイヤーが引っ掛けられるか。**どれかの床に乗っている人だけ。**</summary>
    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanBeHooked && PullAblePlate.FindPlateCarrying(playerRoot) != null;
    }

    /// <summary>フックが触れた／外れたときにフック側が呼ぶ。掛けられる状態のときだけ「掛かった」になる。</summary>
    public void SetHooked(bool hooked)
    {
        IsHooked = hooked && CanBeHooked;
    }

    /// <summary>
    /// 引っ張りのゲージがたまり切ったときにフック側が呼ぶ。
    /// 撃った人がいま乗っている床を探し、その床をこのアンカーへ向けて動かす。降りていれば何もしない。
    /// </summary>
    public void CompletePull(HookPullContext context)
    {
        if (IsHooked)
        {
            PullAblePlate plate = PullAblePlate.FindPlateCarrying(context.PlayerRoot);
            plate?.PullToward(transform.position);
        }

        IsHooked = false;
    }
}
