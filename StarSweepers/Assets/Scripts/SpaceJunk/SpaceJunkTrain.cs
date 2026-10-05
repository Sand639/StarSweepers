using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった間隔で、ステージの真ん中を北から南へ高速で通り過ぎる列車。**
/// STAGE_02 の仕掛け（2026/10/6・大槻さん）。
///
/// ・Interval Seconds（初期値15秒）ごとに、**画面の外（北）に現れて、南の画面の外まで**一直線に走る
/// ・走っている間に当たったプレイヤーと物資（素材・爆弾）を**弾き飛ばす**。1回の通過で、同じ相手には1回だけ
/// ・走っていないときは見えない（当たり判定も無い）
///
/// 列車の見た目は子の「Body」。大きさは Train Size で変える（Body の Scale に反映される）。
///
/// ## オンラインのとき
///
/// 列車そのものは通信しない。**全員で同期された時計（ServerTime）から、いまどこにいるかを各PCが計算する**ので、
/// どのPCでも同じ場所を走る。
/// ・プレイヤー … **自分のPCが、自分の体を飛ばす**（ほかの人の体は、その人のPCが飛ばす）
/// ・物資 … **ホストが飛ばす**（物資の動きはホストが決めているため）
///
/// 時計を間隔で割った余りで動くので、ラウンド開始から最初の列車が来るまでの時間は 0〜15秒 のどこかになる。
/// </summary>
public class SpaceJunkTrain : MonoBehaviour
{
    [Header("走り方")]
    [Tooltip("何秒ごとに列車が来るか")]
    [SerializeField] private float intervalSeconds = 15f;

    [Tooltip("走る速さ（1秒あたりのメートル）。南へ向かって走る")]
    [SerializeField] private float moveSpeed = 45f;

    [Tooltip("現れる場所（北の画面の外）。このオブジェクトの位置からのずれではなく、ワールドの座標")]
    [SerializeField] private Vector3 startPosition = new Vector3(0f, 1.25f, 45f);

    [Tooltip("消える場所（南の画面の外）")]
    [SerializeField] private Vector3 endPosition = new Vector3(0f, 1.25f, -45f);

    [Header("大きさ（あとで調整）")]
    [Tooltip("列車の大きさ（X＝幅、Y＝高さ、Z＝長さ）。当たり判定もこの大きさ")]
    [SerializeField] private Vector3 trainSize = new Vector3(3f, 2.5f, 10f);

    [Header("弾き飛ばす強さ")]
    [Tooltip("プレイヤーを横（線路の外側）へ飛ばす速さ（m/s）")]
    [SerializeField] private float playerSideSpeed = 14f;

    [Tooltip("プレイヤーを列車の進む向きへ飛ばす速さ（m/s）")]
    [SerializeField] private float playerForwardSpeed = 8f;

    [Tooltip("プレイヤーを上へ飛ばす速さ（m/s）")]
    [SerializeField] private float playerLift = 6f;

    [Tooltip("物資を横へ飛ばす速さ（m/s）")]
    [SerializeField] private float objectSideSpeed = 14f;

    [Tooltip("物資を列車の進む向きへ飛ばす速さ（m/s）")]
    [SerializeField] private float objectForwardSpeed = 10f;

    [Tooltip("物資を上へ飛ばす速さ（m/s）")]
    [SerializeField] private float objectLift = 6f;

    [Header("見た目")]
    [Tooltip("列車の見た目（子の Body）。空なら子の1つ目を使う")]
    [SerializeField] private Transform body;

    /// <summary>この通過で、もう飛ばした相手（同じ通過で何度も飛ばさないため）。</summary>
    private readonly HashSet<GameObject> hitThisRun = new HashSet<GameObject>();

    /// <summary>いまの通過の番号。変わったら <see cref="hitThisRun"/> を空にする。</summary>
    private long runIndex = -1;

    private void Awake()
    {
        if (body == null && transform.childCount > 0)
        {
            body = transform.GetChild(0);
        }
        ApplySize();
    }

    private void OnValidate()
    {
        ApplySize();
    }

    private void ApplySize()
    {
        if (body != null)
        {
            body.localScale = trainSize;
        }
    }

    private void Update()
    {
        double clock = SharedClock();
        float interval = Mathf.Max(1f, intervalSeconds);

        long index = (long)System.Math.Floor(clock / interval);
        float sinceStart = (float)(clock - index * (double)interval);

        float distance = Vector3.Distance(startPosition, endPosition);
        float travelled = sinceStart * Mathf.Max(0.1f, moveSpeed);
        bool running = travelled <= distance;

        if (body != null && body.gameObject.activeSelf != running)
        {
            body.gameObject.SetActive(running);
        }

        if (!running)
        {
            return;
        }

        if (index != runIndex)
        {
            runIndex = index;
            hitThisRun.Clear();
        }

        Vector3 direction = (endPosition - startPosition).normalized;
        transform.position = startPosition + direction * travelled;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        HitAround(direction);
    }

    /// <summary>
    /// 全員で同じ時計。通信中は ServerTime、1人用のときはシーンが始まってからの秒数。
    /// </summary>
    private static double SharedClock()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening)
        {
            return network.ServerTime.Time;
        }
        return Time.timeSinceLevelLoad;
    }

    /// <summary>列車に重なっているプレイヤーと物資を飛ばす。</summary>
    private void HitAround(Vector3 direction)
    {
        Collider[] hits = Physics.OverlapBox(
            transform.position, trainSize * 0.5f, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            FishingPlayerController player = hit.GetComponentInParent<FishingPlayerController>();
            if (player != null)
            {
                if (hitThisRun.Add(player.gameObject))
                {
                    // ほかの人の体なら Launch は何もしない（動かしている本人のPCだけが受け取る）
                    player.Launch(KnockVelocity(player.transform.position, direction,
                        playerSideSpeed, playerForwardSpeed, playerLift));
                }
                continue;
            }

            HookableObject item = hit.GetComponentInParent<HookableObject>();
            if (item != null && !item.IsVanished && item.Body != null && hitThisRun.Add(item.gameObject))
            {
                KnockItem(item, KnockVelocity(item.Body.worldCenterOfMass, direction,
                    objectSideSpeed, objectForwardSpeed, objectLift));
            }
        }
    }

    /// <summary>線路の外側（列車の左右のうち、相手がいる側）＋進む向き＋上向きの勢い。</summary>
    private Vector3 KnockVelocity(Vector3 target, Vector3 direction, float side, float forward, float lift)
    {
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        float sign = Vector3.Dot(target - transform.position, right) >= 0f ? 1f : -1f;
        return right * (sign * side) + direction * forward + Vector3.up * lift;
    }

    private static void KnockItem(HookableObject item, Vector3 velocity)
    {
        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned)
        {
            // 物資を飛ばすのはホストだけ（参加者のPCでは中で何もしない）。引っ掛けられていたら外してから飛ばす
            netSupply.ServerApplyExplosion(velocity);
            return;
        }

        ThrowController.ReleaseTargetForExplosion(item);
        item.Body.isKinematic = false;
        item.Body.AddForce(velocity, ForceMode.VelocityChange);
    }

    private void OnDrawGizmosSelected()
    {
        // 走る道筋を黄色の線で、列車の大きさを現れる場所に出す
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(startPosition, endPosition);

        Vector3 direction = endPosition - startPosition;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Gizmos.matrix = Matrix4x4.TRS(startPosition, Quaternion.LookRotation(direction, Vector3.up), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, trainSize);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
