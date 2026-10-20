using UnityEngine;

/// <summary>
/// **三人称のカメラが、壁の向こうへ抜けないようにする。**
///
/// カメラとキャラクターの間に壁や物があると、そのままでは
/// **カメラが壁の中に入り、壁の裏側が見えてしまう。**
///
/// そこで、**間に物があるときだけカメラをキャラクターの近くへ寄せる。**
/// よくある三人称ゲームと同じ動き。
///
/// **向き（角度）は変えない。** 寄せるのは距離だけなので、
/// マウスで見ている方向はそのまま保たれる。
///
/// ## 付ける場所
///
/// **カメラそのもの**に付ける。
/// カメラの**親**（首振りの中心）からカメラへ向かって調べるので、
/// 「キャラクター → カメラ」の間が対象になる。
///
/// ## 動かし方
///
/// 自分では毎フレーム動かない。**視点を切り替える側から <see cref="Apply"/> を呼んでもらう。**
/// カメラの位置を決めたすぐあとに呼ぶ決まりにしてある。
///
/// 自分で `LateUpdate` を持つと、**カメラの位置を決める処理とどちらが先か決まらず**、
/// 1フレームおきに戻ったり寄ったりしてガタつくため。
///
/// ## 戻るときは自然に戻る
///
/// **寄るのは一瞬**（壁の向こうが見えてはいけないので）。
/// **戻るのはゆっくり**だが、これはこの部品ではなく、
/// 呼び出し側が元々持っている「なめらかに動かす処理」がそのまま効いている。
/// </summary>
public class CameraObstacleAvoid : MonoBehaviour
{
    [Header("当たり方")]
    [Tooltip("カメラの太さ（メートル）。太いほど壁の手前で早めに止まる")]
    [Range(0.05f, 1f)]
    [SerializeField] private float radius = 0.25f;

    [Tooltip("壁からどれだけ手前で止まるか（メートル）。0にすると壁にくっつく")]
    [Range(0f, 1f)]
    [SerializeField] private float padding = 0.12f;

    [Tooltip("どれだけ寄っても、これより近づかない（メートル）。体の中に入るのを防ぐ")]
    [Range(0.05f, 3f)]
    [SerializeField] private float minDistance = 0.4f;

    [Tooltip("ぶつかったと見なす物の種類。普通は初期値のままでよい")]
    [SerializeField] private LayerMask blockMask = ~0;

    [Tooltip("**無視する物のまとまり。** 空なら、この物の一番上の親（自分のキャラクター）を無視する")]
    [SerializeField] private Transform ignoreRoot;

    /// <summary>
    /// 間に何があるかを調べるときの入れ物。毎フレーム作り直さないよう使い回している。
    /// 一度に数えるのは16個まで（カメラと体の間にそれ以上の物が重なることはまず無い）。
    /// </summary>
    private readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>いま何かに遮られて、カメラを寄せているか。UIの表示などに使える。</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>
    /// **カメラの位置を決めたあとに呼ぶ。**
    /// 間に物があれば、その手前まで寄せる。無ければ何もしない。
    /// </summary>
    public void Apply()
    {
        Transform pivot = transform.parent;

        if (pivot == null)
        {
            return;
        }

        // カメラは首振りの中心（親）からの相対位置で置かれているので、
        // その長さ＝「本来居たい距離」、その向き＝「調べる方向」になる
        Vector3 local = transform.localPosition;
        float desired = local.magnitude;

        // もともと十分近い（一人称など）なら、調べる必要がない
        if (desired <= minDistance)
        {
            IsBlocked = false;
            return;
        }

        // 回転だけを使う。親の大きさに影響されないようにするため
        Vector3 direction = pivot.rotation * (local / desired);

        float safe = GetSafeDistance(pivot.position, direction, desired);

        IsBlocked = safe < desired;

        if (IsBlocked)
        {
            // **向きはそのまま、長さだけを縮める**
            transform.localPosition = local * (safe / desired);
        }
    }

    /// <summary>
    /// キャラクターからカメラまでの間を調べて、**入ってよい一番遠い距離**を返す。
    ///
    /// 線ではなく**球を飛ばして**調べている（`SphereCast`）。
    /// 細い線だと、柱のきわなどで**すり抜けて**しまうため。
    /// </summary>
    private float GetSafeDistance(Vector3 origin, Vector3 direction, float desired)
    {
        Transform root = ignoreRoot != null ? ignoreRoot : transform.root;

        int count = Physics.SphereCastNonAlloc(
            origin, radius, direction, hits, desired, blockMask, QueryTriggerInteraction.Ignore);

        float safe = desired;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];

            if (hit.collider == null)
            {
                continue;
            }

            // **自分の体は無視する。** カメラは体の中から出ているので、
            // 無視しないと常に自分にぶつかってしまう
            if (root != null && hit.collider.transform.IsChildOf(root))
            {
                continue;
            }

            // 始点がすでに物の中にある場合、当たった場所は当てにならないので使わない
            if (hit.distance <= 0f)
            {
                continue;
            }

            // **ガラスのように透けて見える物は、壁として扱わない。**
            // 向こう側が見えているのにカメラだけ寄ると、見え方と食い違う
            if (SeeThrough.Is(hit.collider))
            {
                continue;
            }

            safe = Mathf.Min(safe, hit.distance);
        }

        return Mathf.Clamp(safe - padding, minDistance, desired);
    }
}
