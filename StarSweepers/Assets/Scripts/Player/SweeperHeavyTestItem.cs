using UnityEngine;

/// <summary>
/// **検証シーン用：置いた物資を、再生したときに重い物（<see cref="HeavyHookable"/>）にする。**（2026/11/5・小野田さんの依頼）
///
/// 宇宙ごみの試合では、重いデブリはイベントの係が再生中に付ける（<c>SpaceJunkRoundEvents</c>）。
/// 通信の無い検証シーン（`SweeperPlayerTest`）にはその係がいないので、代わりにこれが付ける。
/// 宇宙ごみの試合のシーンには置かない。
/// </summary>
public class SweeperHeavyTestItem : MonoBehaviour
{
    [Tooltip("重さ（1 が基準）。重いほど、1回で引きずれる距離が短く、ゆっくりになる")]
    [Min(0.1f)]
    [SerializeField] private float weight = 2f;

    [Tooltip("見た目の大きさの倍率")]
    [Min(0.1f)]
    [SerializeField] private float scale = 1.6f;

    private void Start()
    {
        HeavyHookable heavy = GetComponent<HeavyHookable>();
        if (heavy == null)
        {
            heavy = gameObject.AddComponent<HeavyHookable>();
        }

        heavy.Apply(weight, scale);
    }
}
