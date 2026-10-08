using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった間隔で上がり下がりする床（エレベーター）。** STAGE_01 の土台のまわり（2026/10/8・大槻さんの設計図）。
///
/// ・下で Wait Bottom Seconds 待つ → Move Seconds かけて Rise Height だけ上がる →
///   上で Wait Top Seconds 待つ → Move Seconds かけて下がる、をくり返す
/// ・**置いた位置が「下にいるとき」の位置。** そこから上へ動く
/// ・**乗っているプレイヤーも一緒に上がり下がりする**（床の上で立っていれば運ばれる）
/// ・乗っている物資は、床の物理（動く Rigidbody）で押し上げられる
///
/// 地面より下まで伸びた柱として置くと、下にいるときは上面が地面と同じ高さになり、
/// 上にいるときも柱の下にすき間ができない（下にもぐり込まれない）。
///
/// ## オンラインのとき
///
/// 床そのものは通信しない。**全員で同期された時計（ServerTime）から、いまの高さを各PCが計算する**ので、
/// どのPCでも同じタイミングで動く（回転する床 <see cref="SpaceJunkRotatingFloor"/> と同じやり方）。
/// プレイヤーを一緒に運ぶのは、そのプレイヤーを動かしている本人のPCだけ。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SpaceJunkLiftFloor : MonoBehaviour
{
    [Header("動き方")]
    [Tooltip("下から上まで、何m上がるか")]
    [SerializeField] private float riseHeight = 1.5f;

    [Tooltip("上がる（下がる）のにかける秒数")]
    [SerializeField] private float moveSeconds = 1.5f;

    [Tooltip("下で止まっている秒数")]
    [SerializeField] private float waitBottomSeconds = 2f;

    [Tooltip("上で止まっている秒数")]
    [SerializeField] private float waitTopSeconds = 2f;

    [Tooltip("動くタイミングをずらす秒数。床ごとに変えると、上がる順番をずらせる")]
    [SerializeField] private float timeOffsetSeconds = 0f;

    [Header("乗っている人を運ぶ")]
    [Tooltip("床の上面から、どの高さまでにいる人を「乗っている」とみなすか（m）")]
    [SerializeField] private float carryHeight = 1.5f;

    private Rigidbody body;
    private Vector3 basePosition;
    private float currentHeight;
    private readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        basePosition = transform.position;
        currentHeight = 0f;
    }

    private void Update()
    {
        float target = TargetHeight();
        float delta = target - currentHeight;

        if (Mathf.Abs(delta) > 0.00001f)
        {
            CarryPlayers(delta);
        }

        currentHeight = target;
    }

    private void FixedUpdate()
    {
        // 物資が床と一緒に動くように、物理の床として動かす
        body.MovePosition(basePosition + Vector3.up * currentHeight);
    }

    /// <summary>いまの時刻での、置いた位置からの高さ（0 〜 Rise Height）。</summary>
    private float TargetHeight()
    {
        float move = Mathf.Max(0.01f, moveSeconds);
        float waitBottom = Mathf.Max(0f, waitBottomSeconds);
        float waitTop = Mathf.Max(0f, waitTopSeconds);
        double cycle = waitBottom + move + waitTop + move;

        double t = (SharedClock() - timeOffsetSeconds) % cycle;
        if (t < 0d)
        {
            t += cycle;
        }

        // 下で待つ → 上がる → 上で待つ → 下がる
        float progress;
        if (t < waitBottom)
        {
            progress = 0f;
        }
        else if (t < waitBottom + move)
        {
            progress = (float)((t - waitBottom) / move);
        }
        else if (t < waitBottom + move + waitTop)
        {
            progress = 1f;
        }
        else
        {
            progress = 1f - (float)((t - waitBottom - move - waitTop) / move);
        }

        // 動き出しはゆっくり、真ん中で速く、最後はゆっくり止まる
        return riseHeight * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
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

    /// <summary>床の上に立っている（自分のPCで動かしている）プレイヤーを、床と一緒に上下させる。</summary>
    private void CarryPlayers(float delta)
    {
        Vector3 size = transform.lossyScale;
        float top = basePosition.y + currentHeight + Mathf.Abs(size.y) * 0.5f;
        Vector3 halfExtents = new Vector3(Mathf.Abs(size.x) * 0.5f, carryHeight * 0.5f, Mathf.Abs(size.z) * 0.5f);
        Vector3 center = new Vector3(basePosition.x, top + carryHeight * 0.5f, basePosition.z);

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, transform.rotation,
            ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            CharacterController controller = overlapBuffer[i] as CharacterController;
            if (controller == null || !controller.enabled || controller.GetComponent<FishingPlayerController>() == null)
            {
                continue;
            }

            // **そのプレイヤーを動かしているPCだけが運ぶ。**（回転する床と同じ理由）
            // オンラインでは、プレイヤーの位置は本人のPCが決めて全員に配る（NetworkTransform は「動きは本人」）
            NetworkObject networkObject = controller.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
            {
                continue;
            }

            controller.Move(Vector3.up * delta);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 上に上がったときの床の位置を、黄色の枠で出す
        Vector3 origin = Application.isPlaying ? basePosition : transform.position;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(origin + Vector3.up * riseHeight, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(
            Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
