using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった間隔で、90度ずつ回る床（回転土台）。** STAGE_03 の真ん中の一本道（2026/10/6・大槻さん）。
///
/// ・Interval Seconds（初期値5秒）ごとに、Rotate Seconds（初期値1秒）かけて Step Angle（90度）回る
/// ・回る向きは Direction で選ぶ（**初期値は時計回り**。上から見て）
/// ・**乗っているプレイヤーも一緒に回る**（床の上で立っていれば、床と一緒に運ばれる）
/// ・乗っている物資は、床の物理（動く Rigidbody）で運ばれる
///
/// 置いたときの向きが「0回目」の向き。そこから回り始める。
///
/// ## オンラインのとき
///
/// 床そのものは通信しない。**全員で同期された時計（ServerTime）から、いまの角度を各PCが計算する**ので、
/// どのPCでも同じタイミングで回る（列車 <see cref="SpaceJunkTrain"/> と同じやり方）。
/// プレイヤーを一緒に回すのは、そのプレイヤーを動かしている本人のPCだけ。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SpaceJunkRotatingFloor : MonoBehaviour
{
    /// <summary>回る向き（上から見て）。</summary>
    public enum RotateDirection
    {
        /// <summary>時計回り</summary>
        Clockwise = 0,

        /// <summary>反時計回り</summary>
        CounterClockwise = 1,
    }

    [Header("回り方")]
    [Tooltip("回る向き（上から見て）。時計回り／反時計回り")]
    [SerializeField] private RotateDirection direction = RotateDirection.Clockwise;

    [Tooltip("何秒ごとに回るか（回り始めから次の回り始めまで）")]
    [SerializeField] private float intervalSeconds = 5f;

    [Tooltip("1回の回転にかける秒数。Interval Seconds より短くする")]
    [SerializeField] private float rotateSeconds = 1f;

    [Tooltip("1回に回る角度（度）")]
    [SerializeField] private float stepAngle = 90f;

    [Tooltip("回るタイミングをずらす秒数")]
    [SerializeField] private float timeOffsetSeconds = 0f;

    [Header("乗っている人を運ぶ")]
    [Tooltip("床の上面から、どの高さまでにいる人を「乗っている」とみなすか（m）")]
    [SerializeField] private float carryHeight = 1.5f;

    private Rigidbody body;
    private Quaternion baseRotation;
    private Quaternion currentRotation;
    private readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        baseRotation = transform.rotation;
        currentRotation = baseRotation;
    }

    private void Update()
    {
        Quaternion target = TargetRotation();
        Quaternion delta = target * Quaternion.Inverse(currentRotation);

        if (Quaternion.Angle(target, currentRotation) > 0.0001f)
        {
            CarryPlayers(delta);
        }

        currentRotation = target;
    }

    private void FixedUpdate()
    {
        // 物資が床と一緒に動くように、物理の床として回す
        body.MoveRotation(currentRotation);
    }

    /// <summary>いまの時刻での床の向き。</summary>
    private Quaternion TargetRotation()
    {
        double clock = SharedClock() - timeOffsetSeconds;
        float interval = Mathf.Max(0.1f, intervalSeconds);
        float duration = Mathf.Clamp(rotateSeconds, 0.01f, interval);

        long index = (long)System.Math.Floor(clock / interval);
        float sinceStart = (float)(clock - index * (double)interval);

        // 回り始めはゆっくり、真ん中で速く、最後はゆっくり止まる
        double progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sinceStart / duration));

        // 上から見て時計回り＝Y軸のプラス回り。角度が大きくなりすぎないよう 360 で割った余りにする
        double sign = direction == RotateDirection.Clockwise ? 1d : -1d;
        float angle = (float)((sign * stepAngle * (index + progress)) % 360d);

        return Quaternion.AngleAxis(angle, Vector3.up) * baseRotation;
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

    /// <summary>床の上に立っている（自分のPCで動かしている）プレイヤーを、床と一緒に回す。</summary>
    private void CarryPlayers(Quaternion delta)
    {
        Vector3 size = transform.lossyScale;
        Vector3 halfExtents = new Vector3(Mathf.Abs(size.x) * 0.5f, carryHeight * 0.5f, Mathf.Abs(size.z) * 0.5f);
        Vector3 center = transform.position + Vector3.up * (Mathf.Abs(size.y) * 0.5f + carryHeight * 0.5f);

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, currentRotation,
            ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            FishingPlayerController player = overlapBuffer[i].GetComponentInParent<FishingPlayerController>();
            if (player == null || !player.isActiveAndEnabled)
            {
                continue;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled || controller != overlapBuffer[i])
            {
                continue;
            }

            // 床の中心を軸に、回ったぶんだけ位置をずらす
            Vector3 offset = player.transform.position - transform.position;
            Vector3 moved = delta * offset - offset;
            moved.y = 0f;
            controller.Move(moved);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 乗っているとみなす範囲を水色の枠で出す
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Vector3 size = transform.lossyScale;
        Gizmos.matrix = Matrix4x4.TRS(
            transform.position + Vector3.up * (Mathf.Abs(size.y) * 0.5f + carryHeight * 0.5f),
            transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(Mathf.Abs(size.x), carryHeight, Mathf.Abs(size.z)));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
