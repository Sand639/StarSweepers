using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **移動床（<see cref="PullAblePlate"/>）の上面に置くトリガー。「誰が床に乗っているか」を床に知らせる。**
/// プレハブでは床の子 PassengerArea に付いている。
///
/// ## 判定の仕方
///
/// トリガーに入っただけでは「乗っている」とは決めない（ジャンプ中や、端に引っかかっているだけかもしれない）。
/// トリガーに入っている間、毎回**プレイヤーの体の中心から真下へ短い Raycast を出し、最初に当たった床がこの床か**を確かめて、
/// 当たっていれば <see cref="PullAblePlate.RegisterPassenger"/>、外れていれば <see cref="PullAblePlate.UnregisterPassenger"/> を呼ぶ。
///
/// プレイヤー1人に当たり判定が複数あることがあるので、人ごとに「いくつ入っているか」を数え、
/// 全部出たときにだけ登録を外す（1つ出ただけで外すと、まだ乗っているのに降りた扱いになる）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlatePassengerArea : MonoBehaviour
{
    [Tooltip("この判定が付いている床。空なら親から自動で探す")]
    [SerializeField] private PullAblePlate plate;

    [Tooltip("足元の確認で、体の下端からさらに何m下まで床を探すか（段差や少しの浮きを許すため）")]
    [Min(0f)]
    [SerializeField] private float groundCheckMargin = 0.35f;

    [Tooltip("足元の確認で床とみなすレイヤー。初期値はすべて")]
    [SerializeField] private LayerMask groundMask = ~0;

    /// <summary>プレイヤーごとに、その人の当たり判定がいくつトリガーに入っているか。</summary>
    private readonly Dictionary<Transform, int> overlapCounts = new();

    private void Awake()
    {
        ResolvePlate();
    }

    private void OnValidate()
    {
        ResolvePlate();
        // 乗っている人を見つけるためだけの判定で、物をさえぎらない（トリガー）
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
            // 入った瞬間を取りこぼしていても（無効中に入った等）、中にいる間に数え始める
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

        // その人の当たり判定が全部出たときだけ「降りた」にする
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
        // 判定が止まる間は誰も乗っていない扱いにする（止まったまま登録が残ると、床だけ動いて人が置いていかれる）
        if (plate != null)
        {
            foreach (Transform playerRoot in overlapCounts.Keys)
            {
                plate.UnregisterPassenger(playerRoot);
            }
        }

        overlapCounts.Clear();
    }

    /// <summary>
    /// 足元を確かめて、床への登録を更新する。
    /// 体の中心から真下へ「体の半分の高さ＋Ground Check Margin」だけ Raycast し、
    /// 最初に当たった当たり判定がこの床（またはその子。PlateBody など）のものなら「乗っている」。
    /// トリガーは当たりに含めない（自分自身や柱に当たらないように）。
    /// </summary>
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

    /// <summary>床の参照が空なら、親から探して入れる（プレハブでは床の子に置いているため）。</summary>
    private void ResolvePlate()
    {
        if (plate == null)
        {
            plate = GetComponentInParent<PullAblePlate>();
        }
    }
}
