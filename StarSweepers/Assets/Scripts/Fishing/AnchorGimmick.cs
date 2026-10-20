using UnityEngine;

/// <summary>
/// 釣りフックを引っ掛けると、物ではなくプレイヤーを引き寄せる固定アンカー。
/// 見た目はこの部品を付けたゲームオブジェクトから独立しているため、モデルに差し替えても使える。
///
/// ## 引っ掛けたあとの流れ（<see cref="ThrowController"/> が進める）
///
/// 1. フックが当たると、チャージ量を <see cref="BeginPull"/> に渡して今回の速さを決める
/// 2. 毎フレーム <see cref="PullPlayer"/> でプレイヤーをこちらへ動かす（アンカー自身は一切動かない）
/// 3. true が返ったら（着いた・壁で進めない）引っ張りを終え、フックが手元へ戻る
///
/// 引っ張っている間、プレイヤーの入力移動と重力は
/// <see cref="FishingPlayerController.SetAnchorPulling"/> で止めてある。
///
/// 検証シーン：`Assets/Scenes/Test/FishingAnchorTest.unity`
/// （`Tools > StarSweepers > 釣りのアンカー検証シーンを作る` で作れる）。
/// 詳しくは `Documents/機能ドキュメント/釣りの固定アンカー.md`。
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

    /// <summary>今回の引っ張りで使う速さ（メートル／秒）。<see cref="BeginPull"/> でチャージ量から決まる。</summary>
    private float activePullSpeed;

    [Tooltip("この距離まで近づいたら引っ張りを終える。プレイヤーが当たり判定に重ならないよう、必要なら距離を広げる")]
    [SerializeField] private float stopDistance = 1.2f;

    /// <summary>プレイヤーを引っ張る先。見た目を変えても空オブジェクトを指定すれば同じ位置を保てる。</summary>
    public Vector3 PullPosition => pullPoint != null ? pullPoint.position : transform.position;

    /// <summary>
    /// Unity がエディタでこの部品を付けた瞬間（とインスペクターの Reset）に呼ぶ。
    /// 置いた時点から Rigidbody を固定にしておき、再生前に物理で動く設定のまま残らないようにする。
    /// </summary>
    private void Reset()
    {
        MakeBodyFixed();
    }

    private void Awake()
    {
        MakeBodyFixed();

        // BeginPull が呼ばれる前に PullPlayer が来ても、最低速度で動けるようにしておく
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

        // 高さの差は無視して、水平方向だけ寄せる（引っ張り中は重力も止めてあるので上下には動かない）
        Vector3 direction = PullPosition - player.transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        float endDistance = GetSafeStopDistance(player, direction, distance);

        if (distance <= endDistance)
        {
            return true;
        }

        // 1フレームで進む距離。行き過ぎて反対側へ抜けないよう、残り距離で頭打ちにする
        float moveDistance = Mathf.Min(activePullSpeed * Time.deltaTime, distance - endDistance);
        Vector3 previousPosition = player.transform.position;
        CollisionFlags collisions = player.Move(direction / distance * moveDistance);
        Vector3 remaining = PullPosition - player.transform.position;
        remaining.y = 0f;

        // 横に何かに当たっていて、しかも 1mm（0.001m）も近づけていない ＝ 壁に阻まれている
        if ((collisions & CollisionFlags.Sides) != 0
            && remaining.magnitude >= distance - 0.001f)
        {
            // 壁などで一歩も進めない場合は引っ張りを終え、操作不能のまま残さない。
            return true;
        }

        // 念のため、押し戻しなどで位置が変わらなかった場合も操作を返す。
        // （sqrMagnitude なので 0.000001 は「1mm の2乗」＝ 1mm も動いていない、の意味）
        if ((player.transform.position - previousPosition).sqrMagnitude <= 0.000001f
            && remaining.magnitude > endDistance)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// **プレイヤーがアンカーにめり込まずに止まれる距離**を求める。
    ///
    /// Stop Distance をそのまま使うと、アンカーの当たり判定が大きいときにプレイヤーが
    /// 当たり判定の中で止まり、着いたあと離れられなくなる。
    /// そこで「アンカーの当たり判定の手前の面」＋「プレイヤーの半径ぶん」まで広げた距離と、
    /// Stop Distance の**大きいほう**を使う。
    /// </summary>
    /// <param name="direction">プレイヤーからアンカーへの水平方向（正規化していない）。</param>
    /// <param name="distance"><paramref name="direction"/> の長さ。</param>
    private float GetSafeStopDistance(CharacterController player, Vector3 direction, float distance)
    {
        float endDistance = Mathf.Max(0f, stopDistance);
        Collider anchorCollider = GetComponent<Collider>();
        if (anchorCollider == null || distance <= Mathf.Epsilon)
        {
            return endDistance;
        }

        // アンカーからプレイヤーへ向かう向き（長さ1）
        Vector3 awayDirection = -direction / distance;
        Bounds bounds = anchorCollider.bounds;

        // プレイヤーが向かってくる方向で見た、当たり判定の「半径」。
        // 箱の半分の幅（extents）を向きの成分で按分する（真正面なら箱の半分の幅、斜めなら少し広め）
        float anchorRadius = Mathf.Abs(awayDirection.x) * bounds.extents.x
            + Mathf.Abs(awayDirection.z) * bounds.extents.z;

        // 引っ張る先（Pull Point）が当たり判定の中心からずれているぶんの補正
        float centerOffset = Vector3.Dot(bounds.center - PullPosition, awayDirection);

        // プレイヤー側の厚み：半径＋表面の余裕（skinWidth）＋念のための余白 0.05m
        float playerClearance = player.radius + player.skinWidth + 0.05f;

        return Mathf.Max(endDistance, centerOffset + anchorRadius + playerClearance);
    }

    /// <summary>
    /// アンカーが物理で動かないようにする。
    /// キネマティック＋重力なし＋全方向固定にして、プレイヤーや物資がぶつかっても押されないようにする。
    /// </summary>
    private void MakeBodyFixed()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
    }
}
