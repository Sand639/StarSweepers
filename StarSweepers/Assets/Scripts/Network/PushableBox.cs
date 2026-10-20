using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// みんなで押せる箱。
///
/// **物理の計算はホストだけが行い、その結果の位置を全員へ配る。**
/// 参加者のPCでは物理を止めてある（`isKinematic`）。
/// 全員がそれぞれ物理を回すと、**同じ入力でも少しずつ結果がずれて、
/// 気づくと箱の位置がバラバラになる**ため。
///
/// ## 「叩く」のではなく「押し続ける」
///
/// 最初は**ぶつかるたびに1発の力を加える**形にしていたが、
/// **重すぎて動かない／人によって効きが違う**という問題が出た。
///
/// 原因は2つ。
///
/// - **制限を箱ごとに1つしか持っていなかった。**
///   ホストが押した直後は、参加者の指示が捨てられていた（奪い合いになる）
/// - **1発ずつ叩く方式だと、通信のゆらぎで指示が落ちたときに差が出る。**
///   ホストは自分の指示が必ず届くので、参加者だけ不利になる
///
/// そこで、**「押している向き」を人ごとに預かって、
/// 押している間ずっと力を加え続ける**形に変えた。
/// 指示が1回落ちても、次の指示ですぐ埋まるので体感が揃う。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class PushableBox : NetworkBehaviour
{
    [Header("押す強さ")]
    [Tooltip("押されている間の加速の強さ（1秒あたりのメートル毎秒）。大きいほどキビキビ動く")]
    [Range(1f, 60f)]
    [SerializeField] private float pushAcceleration = 25f;

    [Tooltip("これ以上の速さでは動かない（1秒あたりのメートル）")]
    [Range(1f, 20f)]
    [SerializeField] private float maxSpeed = 5f;

    [Tooltip("上向きに飛ばないよう、水平方向だけに力を加える")]
    [SerializeField] private bool horizontalOnly = true;

    [Header("押している判定")]
    [Tooltip("指示が届いてから、何秒間「まだ押している」とみなすか。短すぎると途切れる")]
    [Range(0.1f, 1f)]
    [SerializeField] private float pushHoldSeconds = 0.3f;

    /// <summary>
    /// いま誰がどの向きに押しているか。**人ごとに預かる。**
    /// 箱ごとに1つしか持たないと、押している人同士で奪い合いになる。
    /// </summary>
    private readonly Dictionary<ulong, PushRequest> pushes = new Dictionary<ulong, PushRequest>();

    /// <summary>
    /// 期限切れを消すための一時置き場。
    /// Dictionary は foreach の途中で消せないので、いったん集めてから消す（毎回 new しないよう使い回す）。
    /// </summary>
    private readonly List<ulong> expiredKeys = new List<ulong>();

    private Rigidbody body;

    /// <summary>1人ぶんの「押している」。向きと、いつまで押しているとみなすか。</summary>
    private struct PushRequest
    {
        /// <summary>押している向き（長さ1）。</summary>
        public Vector3 Direction;

        /// <summary>この時刻（`Time.time`）を過ぎたら「押すのをやめた」とみなす。</summary>
        public float ExpireTime;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        // 物理を動かすのはホストだけ。参加者側は結果を受け取るだけにする
        body.isKinematic = !IsServer;

        Debug.Log($"[LAN] 押せる箱 {name} が同期されました（物理を回す：{IsServer}）");
    }

    private void FixedUpdate()
    {
        if (!IsServer || body.isKinematic)
        {
            return;
        }

        Vector3 total = CollectPushDirections();

        // 誰も押していない、または向かい合って押して打ち消し合っているときは、力を加えない
        if (total.sqrMagnitude > 0.0001f)
        {
            // 質量に関係なく同じ加速になるので、箱の重さを変えても押し心地が変わらない
            body.AddForce(total.normalized * pushAcceleration, ForceMode.Acceleration);
        }

        LimitSpeed();
    }

    /// <summary>
    /// いま有効な「押している向き」を足し合わせる。
    /// 期限が切れたものは捨てる（押すのをやめた人）。
    /// </summary>
    private Vector3 CollectPushDirections()
    {
        Vector3 total = Vector3.zero;
        expiredKeys.Clear();

        foreach (KeyValuePair<ulong, PushRequest> pair in pushes)
        {
            if (Time.time > pair.Value.ExpireTime)
            {
                expiredKeys.Add(pair.Key);
                continue;
            }

            total += pair.Value.Direction;
        }

        foreach (ulong key in expiredKeys)
        {
            pushes.Remove(key);
        }

        return total;
    }

    /// <summary>速くなりすぎたら頭打ちにする。押し続けても暴走しないように。</summary>
    private void LimitSpeed()
    {
        Vector3 velocity = body.linearVelocity;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        if (flat.magnitude > maxSpeed)
        {
            flat = flat.normalized * maxSpeed;
            body.linearVelocity = new Vector3(flat.x, velocity.y, flat.z);
        }
    }

    /// <summary>
    /// 押している人が、押している間くり返し呼ぶ。**中身はホストで動く。**
    ///
    /// `RequireOwnership = false` は「持ち主でなくても呼んでよい」という意味。
    /// 箱の持ち主はホストなので、これが無いと参加者から呼べない。
    ///
    /// **1回の呼び出しで力を加えるのではなく、「押している」という状態を預かる。**
    /// 実際に力を加えるのは <see cref="FixedUpdate"/>。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PushServerRpc(Vector3 direction, ServerRpcParams rpcParams = default)
    {
        // 物理を回していない（＝ホストではない）ときは何もしない。ServerRpc なのでふつうはホストで動くが、念のため
        if (body == null || body.isKinematic)
        {
            return;
        }

        if (horizontalOnly)
        {
            direction.y = 0f;
        }

        // 水平にしたら向きが無くなった（真上・真下から当たった）ときは、押していないとみなす
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // 誰からの指示かで分けて持つ。人ごとなので奪い合いにならない
        pushes[rpcParams.Receive.SenderClientId] = new PushRequest
        {
            Direction = direction.normalized,
            ExpireTime = Time.time + pushHoldSeconds,
        };
    }
}
