using UnityEngine;

/// <summary>
/// **カートを後ろから追いかけるカメラ。**
///
/// マウスでは動かさない。**進む向きの後ろに回り込む**だけの、レースにありがちな見せ方。
/// 遅れて付いてくるので、曲がるときに車体が見える。
///
/// 追う相手が入っていなければ、**自分が操作しているカートを自分で探す**
/// （<see cref="RaceRacer.All"/> の中から）。
/// </summary>
public class RaceCamera : MonoBehaviour
{
    [Tooltip("追いかける相手。空なら、自分が操作しているカートを探す")]
    [SerializeField] private Transform target;

    [Tooltip("後ろに下がる距離（メートル）")]
    [SerializeField] private float distance = 7.5f;

    [Tooltip("上に上がる高さ（メートル）")]
    [SerializeField] private float height = 3.4f;

    [Tooltip("見る位置を、相手のどれだけ上にするか")]
    [SerializeField] private float lookHeight = 1.2f;

    [Tooltip("付いていく滑らかさ。大きいほどぴったり付く")]
    [SerializeField] private float positionSmooth = 7f;

    [Tooltip("向きを合わせる滑らかさ。大きいほど早く向く")]
    [SerializeField] private float rotationSmooth = 10f;

    private void LateUpdate()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        if (target == null)
        {
            target = FindLocalKart();

            if (target == null)
            {
                return;
            }

            // 見つけた最初のフレームは、いきなり後ろへ回り込ませる
            SnapBehind();
            return;
        }

        // 「1 - exp(-勢い × 経過秒)」は、フレームの間隔が変わっても同じ速さで追いつく割合。
        // 毎フレーム残りの距離の一定割合を詰めるので、近づくほどゆっくりになる
        float positionBlend = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, BehindTarget(), positionBlend);

        Vector3 direction = LookPoint() - transform.position;

        // 相手と重なっていて向きが決められないときは、向きを触らない
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float rotationBlend = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(direction), rotationBlend);
    }

    /// <summary>追う相手を入れ替える。通信で自分のカートが出てきたときに使う。</summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        SnapBehind();
    }

    /// <summary>間を置かずに、相手の後ろへ移動する。</summary>
    public void SnapBehind()
    {
        if (target == null)
        {
            return;
        }

        transform.position = BehindTarget();
        transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
    }

    /// <summary>カメラが落ち着く先。相手の後ろ <see cref="distance"/> メートル、上 <see cref="height"/> メートル。</summary>
    private Vector3 BehindTarget()
    {
        return target.position - target.forward * distance + Vector3.up * height;
    }

    /// <summary>カメラが見つめる点。相手の <see cref="lookHeight"/> メートル上。</summary>
    private Vector3 LookPoint()
    {
        return target.position + Vector3.up * lookHeight;
    }

    /// <summary>自分が操作しているカートを探す。見つからなければ、いる中の先頭（1人で遊ぶとき用）。</summary>
    private static Transform FindLocalKart()
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            if (RaceRacer.All[i].IsLocalPlayer)
            {
                return RaceRacer.All[i].transform;
            }
        }

        return RaceRacer.All.Count > 0 ? RaceRacer.All[0].transform : null;
    }
}
