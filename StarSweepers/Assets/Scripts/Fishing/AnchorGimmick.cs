using UnityEngine;

/// <summary>
/// 釣りフックを引っ掛けると、物ではなくプレイヤーを引き寄せる固定アンカー。
/// 見た目はこの部品を付けたゲームオブジェクトから独立しているため、モデルに差し替えても使える。
/// </summary>
[RequireComponent(typeof(HookableObject))]
[RequireComponent(typeof(Rigidbody))]
public class AnchorGimmick : MonoBehaviour
{
    [Header("引っ張る先")]
    [Tooltip("プレイヤーがここまで近づいたら引っ張りを終える。空ならオブジェクトの中心を使う")]
    [SerializeField] private Transform pullPoint;

    [Tooltip("チャージ0のときの、1秒あたりにプレイヤーを引っ張る距離")]
    [SerializeField] private float minPullSpeed = 4f;

    [Tooltip("チャージ最大のときの、1秒あたりにプレイヤーを引っ張る距離")]
    [SerializeField] private float maxPullSpeed = 16f;

    private float activePullSpeed;

    [Tooltip("この距離まで近づいたら引っ張りを終える。プレイヤーが当たり判定に重ならないよう、必要なら距離を広げる")]
    [SerializeField] private float stopDistance = 1.2f;

    /// <summary>プレイヤーを引っ張る先。見た目を変えても空オブジェクトを指定すれば同じ位置を保てる。</summary>
    public Vector3 PullPosition => pullPoint != null ? pullPoint.position : transform.position;

    private void Reset()
    {
        MakeBodyFixed();
    }

    private void Awake()
    {
        MakeBodyFixed();
        BeginPull(0f);
    }

    /// <summary>フックのチャージ量に応じて、今回使う引っ張る速さを決める。</summary>
    public void BeginPull(float charge)
    {
        float strength = Mathf.Clamp01(charge);
        activePullSpeed = Mathf.Lerp(minPullSpeed, maxPullSpeed, strength);
    }

    /// <summary>
    /// プレイヤーをアンカーの方向へ1フレームぶん動かす。
    /// CharacterController を使うため、壁にぶつかったときも通常のプレイヤー移動と同じ扱いになる。
    /// </summary>
    /// <returns>十分近づいて引っ張りが終わったとき true。</returns>
    public bool PullPlayer(CharacterController player)
    {
        if (player == null)
        {
            return true;
        }

        Vector3 direction = PullPosition - player.transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        float endDistance = GetSafeStopDistance(player, direction, distance);

        if (distance <= endDistance)
        {
            return true;
        }

        float moveDistance = Mathf.Min(activePullSpeed * Time.deltaTime, distance - endDistance);
        Vector3 previousPosition = player.transform.position;
        CollisionFlags collisions = player.Move(direction / distance * moveDistance);
        Vector3 remaining = PullPosition - player.transform.position;
        remaining.y = 0f;
        if ((collisions & CollisionFlags.Sides) != 0
            && remaining.magnitude >= distance - 0.001f)
        {
            // 壁などで一歩も進めない場合は引っ張りを終え、操作不能のまま残さない。
            return true;
        }

        // 念のため、押し戻しなどで位置が変わらなかった場合も操作を返す。
        if ((player.transform.position - previousPosition).sqrMagnitude <= 0.000001f
            && remaining.magnitude > endDistance)
        {
            return true;
        }

        return false;
    }

    private float GetSafeStopDistance(CharacterController player, Vector3 direction, float distance)
    {
        float endDistance = Mathf.Max(0f, stopDistance);
        Collider anchorCollider = GetComponent<Collider>();
        if (anchorCollider == null || distance <= Mathf.Epsilon)
        {
            return endDistance;
        }

        Vector3 awayDirection = -direction / distance;
        Bounds bounds = anchorCollider.bounds;
        float anchorRadius = Mathf.Abs(awayDirection.x) * bounds.extents.x
            + Mathf.Abs(awayDirection.z) * bounds.extents.z;
        float centerOffset = Vector3.Dot(bounds.center - PullPosition, awayDirection);
        float playerClearance = player.radius + player.skinWidth + 0.05f;

        return Mathf.Max(endDistance, centerOffset + anchorRadius + playerClearance);
    }

    private void MakeBodyFixed()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
    }
}
