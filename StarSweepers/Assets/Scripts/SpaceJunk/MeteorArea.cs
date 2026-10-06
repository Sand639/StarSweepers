using UnityEngine;

/// <summary>
/// 隕石を落としてよい床の範囲。
/// BoxCollider の内側から、攻撃円全体がはみ出さない落下点を返す。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class MeteorArea : MonoBehaviour
{
    [Tooltip("隕石を落としてよい範囲。空なら同じ物のBoxColliderを使う")]
    [SerializeField] private BoxCollider areaCollider;

    public bool IsAvailable =>
        isActiveAndEnabled && gameObject.activeInHierarchy &&
        AreaCollider != null && AreaCollider.enabled;

    private BoxCollider AreaCollider
    {
        get
        {
            if (areaCollider == null)
            {
                areaCollider = GetComponent<BoxCollider>();
            }
            return areaCollider;
        }
    }

    private void Reset()
    {
        areaCollider = GetComponent<BoxCollider>();
        areaCollider.isTrigger = true;
    }

    private void OnValidate()
    {
        if (areaCollider == null)
        {
            areaCollider = GetComponent<BoxCollider>();
        }

        if (areaCollider != null)
        {
            areaCollider.isTrigger = true;
        }
    }

    /// <summary>範囲内からランダムな落下点を返す。</summary>
    public bool TryGetRandomPoint(float radius, out Vector3 point)
    {
        return TryGetPoint(radius, Random.value, Random.value, out point);
    }

    /// <summary>
    /// 0～1の座標を使って落下点を返す。テストや決まった位置を選びたい場合にも使える。
    /// </summary>
    public bool TryGetPoint(
        float radius,
        float normalizedX,
        float normalizedZ,
        out Vector3 point)
    {
        point = default;
        BoxCollider box = AreaCollider;
        if (box == null)
        {
            return false;
        }

        float worldPerLocalX = transform.TransformVector(Vector3.right).magnitude;
        float worldPerLocalZ = transform.TransformVector(Vector3.forward).magnitude;
        if (worldPerLocalX <= 0.0001f || worldPerLocalZ <= 0.0001f)
        {
            return false;
        }

        float safeRadius = Mathf.Max(0f, radius);
        float availableHalfX = box.size.x * 0.5f - safeRadius / worldPerLocalX;
        float availableHalfZ = box.size.z * 0.5f - safeRadius / worldPerLocalZ;

        // ちょうど同じ大きさでは、丸め誤差で床の外へ出やすいため使わない。
        if (availableHalfX <= 0f || availableHalfZ <= 0f)
        {
            return false;
        }

        float localX = box.center.x + Mathf.Lerp(
            -availableHalfX, availableHalfX, Mathf.Clamp01(normalizedX));
        float localZ = box.center.z + Mathf.Lerp(
            -availableHalfZ, availableHalfZ, Mathf.Clamp01(normalizedZ));
        float localY = box.center.y + box.size.y * 0.5f;

        point = transform.TransformPoint(new Vector3(localX, localY, localZ));
        return true;
    }

    private void OnDrawGizmos()
    {
        BoxCollider box = AreaCollider;
        if (box == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
