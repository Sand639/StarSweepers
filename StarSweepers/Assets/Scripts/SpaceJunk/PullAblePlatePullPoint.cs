using UnityEngine;

/// <summary>PullAblePlate の各方向に置く Hook 用 Trigger。</summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlatePullPoint : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    [SerializeField] private PullAblePlateDirection direction;
    [SerializeField] private PullAblePlate plate;

    public PullAblePlateDirection Direction => direction;
    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked { get; private set; }
    public bool CanBeHooked => isActiveAndEnabled
        && gameObject.activeInHierarchy
        && plate != null
        && plate.IsDirectionActive(direction)
        && !plate.IsMoving
        && !IsHooked;

    private void Awake()
    {
        ResolvePlate();
    }

    private void OnValidate()
    {
        ResolvePlate();
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanBeHooked && !plate.IsPlayerOnPlate(playerRoot);
    }

    public void SetHooked(bool hooked)
    {
        IsHooked = hooked && CanBeHooked;
    }

    public void CompletePull(HookPullContext context)
    {
        if (IsHooked && plate != null)
        {
            plate.Pull(direction);
        }

        IsHooked = false;
    }

    private void ResolvePlate()
    {
        if (plate == null)
        {
            plate = GetComponentInParent<PullAblePlate>();
        }
    }
}
