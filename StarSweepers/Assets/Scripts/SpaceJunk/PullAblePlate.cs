using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PullAblePlateDirection
{
    Front,
    FrontRight,
    Right,
    BackRight,
    Back,
    BackLeft,
    Left,
    FrontLeft
}

/// <summary>8方向から拉扯でき、上にいる Player も一緒に運ぶ移動床。</summary>
[RequireComponent(typeof(Rigidbody))]
public class PullAblePlate : MonoBehaviour
{
    [Header("拉扯移動")]
    [Min(0f)]
    [SerializeField] private float moveDistance = 2f;

    [Min(0f)]
    [SerializeField] private float moveDuration = 0.35f;

    [Header("有効な柱")]
    [SerializeField] private bool frontActive = true;
    [SerializeField] private bool frontRightActive = true;
    [SerializeField] private bool rightActive = true;
    [SerializeField] private bool backRightActive = true;
    [SerializeField] private bool backActive = true;
    [SerializeField] private bool backLeftActive = true;
    [SerializeField] private bool leftActive = true;
    [SerializeField] private bool frontLeftActive = true;

    [Header("柱の参照")]
    [SerializeField] private PullAblePlatePullPoint frontPullPoint;
    [SerializeField] private PullAblePlatePullPoint frontRightPullPoint;
    [SerializeField] private PullAblePlatePullPoint rightPullPoint;
    [SerializeField] private PullAblePlatePullPoint backRightPullPoint;
    [SerializeField] private PullAblePlatePullPoint backPullPoint;
    [SerializeField] private PullAblePlatePullPoint backLeftPullPoint;
    [SerializeField] private PullAblePlatePullPoint leftPullPoint;
    [SerializeField] private PullAblePlatePullPoint frontLeftPullPoint;

    private readonly Dictionary<Transform, CharacterController> passengers = new();
    private Rigidbody body;

    public bool IsMoving { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        RefreshPullPointStates();
    }

    private void OnValidate()
    {
        RefreshPullPointStates();
    }

    public bool Pull(PullAblePlateDirection direction)
    {
        return StartMove(CalculateDirection(direction, transform.rotation));
    }

    public bool PullToward(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        return StartMove(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero);
    }

    public void RegisterPassenger(Transform playerRoot, CharacterController controller)
    {
        if (playerRoot == null || controller == null)
        {
            return;
        }

        passengers[playerRoot] = controller;
    }

    public void UnregisterPassenger(Transform playerRoot)
    {
        if (playerRoot != null)
        {
            passengers.Remove(playerRoot);
        }
    }

    public bool IsPlayerOnPlate(Transform playerRoot)
    {
        return playerRoot != null && passengers.ContainsKey(playerRoot);
    }

    public bool IsDirectionActive(PullAblePlateDirection direction)
    {
        return direction switch
        {
            PullAblePlateDirection.Front => frontActive,
            PullAblePlateDirection.FrontRight => frontRightActive,
            PullAblePlateDirection.Right => rightActive,
            PullAblePlateDirection.BackRight => backRightActive,
            PullAblePlateDirection.Back => backActive,
            PullAblePlateDirection.BackLeft => backLeftActive,
            PullAblePlateDirection.Left => leftActive,
            PullAblePlateDirection.FrontLeft => frontLeftActive,
            _ => false
        };
    }

    public void SetPullPointActive(PullAblePlateDirection direction, bool active)
    {
        switch (direction)
        {
            case PullAblePlateDirection.Front:
                frontActive = active;
                break;
            case PullAblePlateDirection.FrontRight:
                frontRightActive = active;
                break;
            case PullAblePlateDirection.Right:
                rightActive = active;
                break;
            case PullAblePlateDirection.BackRight:
                backRightActive = active;
                break;
            case PullAblePlateDirection.Back:
                backActive = active;
                break;
            case PullAblePlateDirection.BackLeft:
                backLeftActive = active;
                break;
            case PullAblePlateDirection.Left:
                leftActive = active;
                break;
            case PullAblePlateDirection.FrontLeft:
                frontLeftActive = active;
                break;
        }

        ApplyPullPointState(GetPullPoint(direction), active);
    }

    public static Vector3 CalculateDirection(PullAblePlateDirection direction, Quaternion rotation)
    {
        Vector3 localDirection = direction switch
        {
            PullAblePlateDirection.Front => Vector3.forward,
            PullAblePlateDirection.FrontRight => new Vector3(1f, 0f, 1f),
            PullAblePlateDirection.Right => Vector3.right,
            PullAblePlateDirection.BackRight => new Vector3(1f, 0f, -1f),
            PullAblePlateDirection.Back => Vector3.back,
            PullAblePlateDirection.BackLeft => new Vector3(-1f, 0f, -1f),
            PullAblePlateDirection.Left => Vector3.left,
            PullAblePlateDirection.FrontLeft => new Vector3(-1f, 0f, 1f),
            _ => Vector3.zero
        };

        Vector3 worldDirection = rotation * localDirection;
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.zero;
    }

    private bool StartMove(Vector3 direction)
    {
        if (IsMoving || direction.sqrMagnitude <= 0.0001f || moveDistance <= 0f)
        {
            return false;
        }

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        Vector3 destination = body.position + direction.normalized * moveDistance;
        if (moveDuration <= 0f || !Application.isPlaying)
        {
            ApplyMoveStep(destination);
            return true;
        }

        StartCoroutine(MoveTo(destination));
        return true;
    }

    private IEnumerator MoveTo(Vector3 destination)
    {
        IsMoving = true;
        Vector3 start = body.position;
        Vector3 current = start;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            Vector3 next = Vector3.Lerp(start, destination, t);
            ApplyMoveStep(next, current);
            current = next;
        }

        ApplyMoveStep(destination, current);
        IsMoving = false;
    }

    private void ApplyMoveStep(Vector3 nextPosition)
    {
        ApplyMoveStep(nextPosition, body.position);
        transform.position = nextPosition;
    }

    private void ApplyMoveStep(Vector3 nextPosition, Vector3 currentPosition)
    {
        Vector3 delta = nextPosition - currentPosition;
        body.MovePosition(nextPosition);
        MovePassengers(delta);
    }

    private void MovePassengers(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0f)
        {
            return;
        }

        List<Transform> invalid = null;
        foreach (KeyValuePair<Transform, CharacterController> passenger in passengers)
        {
            if (passenger.Key == null || passenger.Value == null)
            {
                invalid ??= new List<Transform>();
                invalid.Add(passenger.Key);
                continue;
            }

            passenger.Value.Move(delta);
        }

        if (invalid == null)
        {
            return;
        }

        foreach (Transform playerRoot in invalid)
        {
            passengers.Remove(playerRoot);
        }
    }

    private void RefreshPullPointStates()
    {
        foreach (PullAblePlateDirection direction in System.Enum.GetValues(typeof(PullAblePlateDirection)))
        {
            ApplyPullPointState(GetPullPoint(direction), IsDirectionActive(direction));
        }
    }

    private PullAblePlatePullPoint GetPullPoint(PullAblePlateDirection direction)
    {
        return direction switch
        {
            PullAblePlateDirection.Front => frontPullPoint,
            PullAblePlateDirection.FrontRight => frontRightPullPoint,
            PullAblePlateDirection.Right => rightPullPoint,
            PullAblePlateDirection.BackRight => backRightPullPoint,
            PullAblePlateDirection.Back => backPullPoint,
            PullAblePlateDirection.BackLeft => backLeftPullPoint,
            PullAblePlateDirection.Left => leftPullPoint,
            PullAblePlateDirection.FrontLeft => frontLeftPullPoint,
            _ => null
        };
    }

    private static void ApplyPullPointState(PullAblePlatePullPoint pullPoint, bool active)
    {
        if (pullPoint != null && pullPoint.gameObject.activeSelf != active)
        {
            pullPoint.gameObject.SetActive(active);
        }
    }
}
