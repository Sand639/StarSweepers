using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった間隔で、線路の上を高速で通り過ぎる列車。** STAGE_02 の仕掛け（2026/10/6・大槻さん）。
///
/// ・**置いた場所が線路の真ん中**になる。線路は、このオブジェクトの奥行き（青い矢印 Z）の向きに伸びる。
///   何も回していなければ、**奥（＋Z）が北、手前（−Z）が南**
/// ・Interval Seconds（初期値15秒）ごとに、線路の端（画面の外）に現れて、反対の端まで一直線に走る
/// ・走る向きは Direction で選ぶ（北→南／南→北／交互／ランダム）。**Move Speed をマイナスにしても逆向きになる**
/// ・走っている間に当たったプレイヤーと物資（素材・爆弾）を**弾き飛ばす**。1回の通過で、同じ相手には1回だけ
/// ・走っていないときは見えない（当たり判定も無い）
///
/// 列車の見た目は子の「Body」。大きさは Train Size で変える（Body の Scale に反映される）。
/// **線路を2本にしたいときは、プレハブを2つ置いて左右にずらす。** 同時に来させたくなければ Time Offset Seconds をずらす。
///
/// ## オンラインのとき
///
/// 列車そのものは通信しない。**全員で同期された時計（ServerTime）から、いまどこにいるか・どちら向きかを各PCが計算する**ので、
/// どのPCでも同じ場所を走る（ランダムの向きも、全員で同じになるように決めている）。
/// ・プレイヤー … **自分のPCが、自分の体を飛ばす**（ほかの人の体は、その人のPCが飛ばす）
/// ・物資 … **ホストが飛ばす**（物資の動きはホストが決めているため）
///
/// 時計を間隔で割った余りで動くので、ラウンド開始から最初の列車が来るまでの時間は 0〜間隔 のどこかになる。
/// </summary>
public class SpaceJunkTrain : MonoBehaviour
{
    /// <summary>走る向き。</summary>
    public enum TrainDirection
    {
        /// <summary>北（奥）から南（手前）へ</summary>
        NorthToSouth = 0,

        /// <summary>南（手前）から北（奥）へ</summary>
        SouthToNorth = 1,

        /// <summary>1回ごとに向きを変える</summary>
        Alternate = 2,

        /// <summary>毎回ランダム（全員で同じ向きになる）</summary>
        Random = 3,
    }

    [Header("走り方")]
    [Tooltip("走る向き。北→南／南→北／交互／ランダム")]
    [SerializeField] private TrainDirection direction = TrainDirection.NorthToSouth;

    [Tooltip("何秒ごとに列車が来るか")]
    [SerializeField] private float intervalSeconds = 15f;

    [Tooltip("来るタイミングをずらす秒数（列車を2つ置いて、同時に来させたくないとき）")]
    [SerializeField] private float timeOffsetSeconds = 0f;

    [Tooltip("走る速さ（1秒あたりのメートル）。マイナスにすると Direction と逆向きに走る")]
    [SerializeField] private float moveSpeed = 45f;

    [Tooltip("置いた場所から、線路の端（現れる・消える場所）までの距離（m）。画面の外になるようにする")]
    [SerializeField] private float runHalfLength = 45f;

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
    private long runIndex = long.MinValue;

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
        if (body == null)
        {
            return;
        }

        double clock = SharedClock() - timeOffsetSeconds;
        float interval = Mathf.Max(1f, intervalSeconds);

        long index = (long)System.Math.Floor(clock / interval);
        float sinceStart = (float)(clock - index * (double)interval);

        float halfLength = Mathf.Max(1f, runHalfLength);
        float travelled = sinceStart * Mathf.Max(0.1f, Mathf.Abs(moveSpeed));
        bool running = travelled <= halfLength * 2f;

        if (body.gameObject.activeSelf != running)
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

        // 北（＋Z）から南へ走るなら +1 → -1 へ、南から北なら -1 → +1 へ
        float sign = GoesSouth(index) ? -1f : 1f;
        float along = -sign * halfLength + sign * travelled;

        Vector3 runDirection = transform.forward * sign;
        body.position = transform.position + transform.forward * along;
        body.rotation = Quaternion.LookRotation(runDirection, Vector3.up);

        HitAround(runDirection);
    }

    /// <summary>この回の列車が、北から南へ走るか。</summary>
    private bool GoesSouth(long index)
    {
        bool south;
        switch (direction)
        {
            case TrainDirection.SouthToNorth:
                south = false;
                break;
            case TrainDirection.Alternate:
                south = (index & 1L) == 0L;
                break;
            case TrainDirection.Random:
                // 回の番号から決める（全員で同じ結果になる）
                unchecked
                {
                    ulong hash = (ulong)index * 0x9E3779B97F4A7C15UL;
                    south = ((hash >> 32) & 1UL) == 0UL;
                }
                break;
            default:
                south = true;
                break;
        }

        // 速さがマイナスなら逆向き
        return moveSpeed >= 0f ? south : !south;
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
    private void HitAround(Vector3 runDirection)
    {
        Collider[] hits = Physics.OverlapBox(
            body.position, trainSize * 0.5f, body.rotation, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            FishingPlayerController player = hit.GetComponentInParent<FishingPlayerController>();
            if (player != null)
            {
                if (hitThisRun.Add(player.gameObject))
                {
                    // ほかの人の体なら Launch は何もしない（動かしている本人のPCだけが受け取る）
                    player.Launch(KnockVelocity(player.transform.position, runDirection,
                        playerSideSpeed, playerForwardSpeed, playerLift));
                }
                continue;
            }

            HookableObject item = hit.GetComponentInParent<HookableObject>();
            if (item != null && !item.IsVanished && item.Body != null && hitThisRun.Add(item.gameObject))
            {
                KnockItem(item, KnockVelocity(item.Body.worldCenterOfMass, runDirection,
                    objectSideSpeed, objectForwardSpeed, objectLift));
            }
        }
    }

    /// <summary>線路の外側（列車の左右のうち、相手がいる側）＋進む向き＋上向きの勢い。</summary>
    private Vector3 KnockVelocity(Vector3 target, Vector3 runDirection, float side, float forward, float lift)
    {
        Vector3 right = Vector3.Cross(Vector3.up, runDirection).normalized;
        float sign = Vector3.Dot(target - body.position, right) >= 0f ? 1f : -1f;
        return right * (sign * side) + runDirection * forward + Vector3.up * lift;
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

    private void OnDrawGizmos()
    {
        // 線路を黄色の線で、両端に列車の大きさの枠を出す（選んでいなくても見えるように）
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        float halfLength = Mathf.Max(1f, runHalfLength);
        Vector3 north = transform.position + transform.forward * halfLength;
        Vector3 south = transform.position - transform.forward * halfLength;
        Gizmos.DrawLine(north, south);

        Gizmos.matrix = Matrix4x4.TRS(north, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, trainSize);
        Gizmos.matrix = Matrix4x4.TRS(south, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, trainSize);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
