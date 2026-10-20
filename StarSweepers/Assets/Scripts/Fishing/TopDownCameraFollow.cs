using UnityEngine;

/// <summary>
/// **見下ろしカメラがプレイヤーを追いかける。**
///
/// カメラをプレイヤーの子にすると、プレイヤーが向きを変えるたびに
/// カメラごと回ってしまう。だから親子にせず、位置だけを追わせる。
/// カメラの角度（見下ろす傾き）は、シーンでカメラに付けた回転をそのまま使う。
///
/// 使い方：カメラに付けて、Target にプレイヤーを入れる。
/// </summary>
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("追いかける相手（プレイヤー）")]
    [SerializeField] private Transform target;

    [Header("位置")]
    [Tooltip("相手から見た、カメラを置く相対位置。Y を上げるほど高く、Z をマイナスにするほど後ろから見る")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 16f, -9f);

    [Tooltip("追従のなめらかさ。大きいほどキビキビ、小さいほどゆっくり付いてくる")]
    [SerializeField] private float followSharpness = 10f;

    /// <summary>
    /// 追いかける相手を後から決める。
    /// **オンラインでは、自分のプレイヤーが生まれた時点で渡される**
    /// （シーンに置いた時点では、まだ誰も生まれていないため）。
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
            // 最初の1回はなめらかにせず、いきなりその場所へ飛ばす
            transform.position = target.position + offset;
        }
    }

    /// <summary>
    /// プレイヤーの移動（Update）が終わったあとに追いかける。
    /// Update で追うと、同じフレームの移動前の位置を見てしまい、カメラがガタつく。
    /// </summary>
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 goal = target.position + offset;

        // 「1フレームで残りの距離の何割を詰めるか」を deltaTime から求める。
        // 単に Lerp(…, followSharpness * deltaTime) とするより、フレームレートが変わっても同じ速さで寄っていく
        float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, goal, blend);
    }
}
