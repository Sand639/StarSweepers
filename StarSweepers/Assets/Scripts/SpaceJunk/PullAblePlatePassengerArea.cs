using System.Collections.Generic;
using UnityEngine;

/// <summary>Plate の上面にいる Player を、短い足元 Raycast で判定する Trigger。</summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlatePassengerArea : MonoBehaviour
{
    [SerializeField] private PullAblePlate plate;
    [Min(0f)]
    [SerializeField] private float groundCheckMargin = 0.35f;
    [SerializeField] private LayerMask groundMask = ~0;

    private readonly Dictionary<Transform, int> overlapCounts = new();

    private void Awake()
    {
        ResolvePlate();
    }

    private void OnValidate()
    {
        ResolvePlate();
        Collider area = GetComponent<Collider>();
        if (area != null)
        {
            area.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller == null)
        {
            return;
        }

        Transform playerRoot = controller.transform;
        overlapCounts.TryGetValue(playerRoot, out int count);
        overlapCounts[playerRoot] = count + 1;
        RefreshPassenger(playerRoot, controller);
    }

    private void OnTriggerStay(Collider other)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller != null)
        {
            Transform playerRoot = controller.transform;
            if (!overlapCounts.ContainsKey(playerRoot))
            {
                overlapCounts[playerRoot] = 1;
            }

            RefreshPassenger(playerRoot, controller);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller == null)
        {
            return;
        }

        Transform playerRoot = controller.transform;
        if (!overlapCounts.TryGetValue(playerRoot, out int count) || count <= 1)
        {
            overlapCounts.Remove(playerRoot);
            plate?.UnregisterPassenger(playerRoot);
            return;
        }

        overlapCounts[playerRoot] = count - 1;
    }

    private void OnDisable()
    {
        if (plate != null)
        {
            foreach (Transform playerRoot in overlapCounts.Keys)
            {
                plate.UnregisterPassenger(playerRoot);
            }
        }

        overlapCounts.Clear();
    }

    private void RefreshPassenger(Transform playerRoot, CharacterController controller)
    {
        if (plate == null)
        {
            return;
        }

        Bounds bounds = controller.bounds;
        float distance = bounds.extents.y + groundCheckMargin;
        bool onThisPlate = Physics.Raycast(
            bounds.center,
            Vector3.down,
            out RaycastHit hit,
            distance,
            groundMask,
            QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<PullAblePlate>() == plate;

        if (onThisPlate)
        {
            plate.RegisterPassenger(playerRoot, controller);
        }
        else
        {
            plate.UnregisterPassenger(playerRoot);
        }
    }

    private void ResolvePlate()
    {
        if (plate == null)
        {
            plate = GetComponentInParent<PullAblePlate>();
        }
    }
}
