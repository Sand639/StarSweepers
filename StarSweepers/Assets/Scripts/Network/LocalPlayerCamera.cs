using UnityEngine;

/// <summary>
/// 自分が操作しているキャラクターを、後ろから追いかけるカメラ。
///
/// 通信でつながると、自分のキャラクターは**あとから生まれる**ので、
/// 生まれた側から <see cref="SetTarget"/> で「これを追いかけて」と伝える形にしている。
///
/// 向きは固定で、マウスでは回らない。
/// 通信の土台を確かめるための最小限の作りにしてある。
/// </summary>
public class LocalPlayerCamera : MonoBehaviour
{
    [Tooltip("追いかける相手から見た、カメラの位置（後ろと上）")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -8f);

    [Tooltip("見る高さ。相手の足元からどれだけ上を見るか（メートル）")]
    [SerializeField] private float lookHeight = 1.2f;

    [Tooltip("追いつく速さ。大きいほどぴったり付いてくる")]
    [Range(1f, 30f)]
    [SerializeField] private float followSmooth = 8f;

    /// <summary>追いかける相手。null の間は動かない。</summary>
    private Transform target;

    /// <summary>追いかける相手を決める。null を渡すと止まる。</summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
            // 参加した瞬間に遠くから飛んでこないよう、最初は一気に寄せる
            transform.position = target.position + offset;
            transform.LookAt(target.position + Vector3.up * lookHeight);
        }
    }

    /// <summary>LateUpdate ＝ プレイヤーが動いた**あと**に追いかける（Update だと順番しだいで1フレーム遅れて揺れる）。</summary>
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 wanted = target.position + offset;

        // フレームレートが変わっても同じ速さで追いつく計算（1 - e^(-速さ×経過秒)）
        transform.position = Vector3.Lerp(
            transform.position, wanted, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));

        transform.LookAt(target.position + Vector3.up * lookHeight);
    }
}
