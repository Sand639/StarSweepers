using System.Collections;
using UnityEngine;

/// <summary>拉扯時，方塊要用哪種方式決定移動方向。</summary>
public enum MovableBlockMoveMode
{
    TowardPlayer,
    FourDirections
}

/// <summary>方塊四周可被拉扯的位置。</summary>
public enum MovableBlockPullSide
{
    Front,
    Back,
    Left,
    Right
}

/// <summary>可由玩家拉動的大型方塊。</summary>
[RequireComponent(typeof(Rigidbody))]
public class MovableBlock : MonoBehaviour
{
    [Header("拉扯移動")]
    [SerializeField] private MovableBlockMoveMode moveMode = MovableBlockMoveMode.FourDirections;

    [Min(0f)]
    [SerializeField] private float moveDistance = 2f;

    [Min(0f)]
    [SerializeField] private float moveDuration = 0.35f;

    private Rigidbody body;

    public bool IsMoving { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    /// <summary>指定された拉點から、方塊を一回移動させる。</summary>
    public bool Pull(Vector3 playerPosition, MovableBlockPullSide side)
    {
        if (IsMoving)
        {
            return false;
        }

        Vector3 direction = CalculateMoveDirection(
            moveMode, transform.rotation, side, transform.position, playerPosition);
        if (direction.sqrMagnitude <= 0.0001f || moveDistance <= 0f)
        {
            return false;
        }

        Vector3 destination = transform.position + direction * moveDistance;
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (moveDuration <= 0f || !Application.isPlaying)
        {
            body.position = destination;
            transform.position = destination;
            return true;
        }

        StartCoroutine(MoveTo(destination));
        return true;
    }

    private IEnumerator MoveTo(Vector3 destination)
    {
        IsMoving = true;
        Vector3 start = body.position;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            body.MovePosition(Vector3.Lerp(start, destination, t));
        }

        body.MovePosition(destination);
        IsMoving = false;
    }

    /// <summary>依目前模式計算水平移動方向。</summary>
    public static Vector3 CalculateMoveDirection(
        MovableBlockMoveMode mode,
        Quaternion blockRotation,
        MovableBlockPullSide side,
        Vector3 blockPosition,
        Vector3 playerPosition)
    {
        if (mode == MovableBlockMoveMode.TowardPlayer)
        {
            Vector3 towardPlayer = playerPosition - blockPosition;
            towardPlayer.y = 0f;
            return towardPlayer.sqrMagnitude > 0.0001f ? towardPlayer.normalized : Vector3.zero;
        }

        Vector3 localDirection;
        switch (side)
        {
            case MovableBlockPullSide.Back:
                localDirection = Vector3.back;
                break;
            case MovableBlockPullSide.Left:
                localDirection = Vector3.left;
                break;
            case MovableBlockPullSide.Right:
                localDirection = Vector3.right;
                break;
            default:
                localDirection = Vector3.forward;
                break;
        }

        Vector3 worldDirection = blockRotation * localDirection;
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.zero;
    }
}
