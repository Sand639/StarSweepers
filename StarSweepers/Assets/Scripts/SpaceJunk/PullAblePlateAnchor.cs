using UnityEngine;

/// <summary>
/// 場外に置く拉扯点。Hook した Player が乗っている Plate を、この位置へ向けて動かす。
/// </summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlateAnchor : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked { get; private set; }
    public bool CanBeHooked => isActiveAndEnabled && gameObject.activeInHierarchy && !IsHooked;

    private void OnValidate()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanBeHooked && PullAblePlate.FindPlateCarrying(playerRoot) != null;
    }

    public void SetHooked(bool hooked)
    {
        IsHooked = hooked && CanBeHooked;
    }

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
