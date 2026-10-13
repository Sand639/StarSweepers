using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **チームカメラ。自陣を画面の手前にしたまま、自分と味方がいつも画面に入るように映す。**（切り替えの名前は「チーム」）
///
/// 2026/10/7・小野田さんの依頼。「大まかな画面は自陣カメラで、常に2人が見えるようにしたい。
/// ただ、A が動いていないのに B が動くと A のカメラも動いてしまって気持ち悪い、という意見がある」。
///
/// ## 動き方（毎フレーム）
///
/// 1. **向き** … 自陣が手前に来る向き（<see cref="SpaceJunkCameraMode.OwnSideYaw"/>。自分カメラと同じ）
/// 2. **見る点** … 「チームの真ん中」と「自分」の間（どれだけ自分に寄せるかは「自分に寄せる割合」）
/// 3. **遊び** … 見る点のズレが「遊びの幅・奥行き」の中なら、**カメラは横に動かない**。はみ出した分だけ動く
/// 4. **ズーム** … 全員が画面に入る、いちばん近い距離まで下がる。**引くのは早く、寄るのはゆっくり・少し待ってから**
///
/// ## 「味方が動くと自分のカメラも動いて気持ち悪い」を減らす設定
///
/// | 設定 | 効き方 |
/// | --- | --- |
/// | 自分に寄せる割合 | 1 に近いほど、画面の真ん中が**自分**になる。味方が動いても横には動かず、**ズーム（引き・寄り）だけ**で入れる |
/// | 遊びの幅・奥行き | 広いほど、ちょっとした動きでは**カメラが止まったまま**。広すぎると、動き出したときにまとめて動く |
/// | 寄る前に待つ秒数・寄りのなめらかさ | 味方が近づいてきても、すぐには寄らない（行ったり来たりでズームが揺れない） |
/// | ズームしない差 | 必要な距離との差がこれより小さければ、ズームしない |
/// | 上下のなめらかさ | ジャンプ・段差での上下の揺れを抑える |
/// | 味方を映す最大の距離・この高さより下は映さない | 遠く離れた味方・落ちた味方は追わず、自分を優先する |
///
/// 組み合わせの例は `Documents/機能ドキュメント/宇宙ごみのチームカメラ.md` の「調整のしかた」。
/// 検証シーン `Assets/Scenes/Test/SpaceJunkTeamCameraTest.unity` で、通信なしで動きを試せる。
/// </summary>
public class SpaceJunkTeamGroupCamera : SpaceJunkCameraMode
{
    [Header("向き")]
    [Tooltip("見下ろす角度（度）。大きいほど真上から")]
    [Range(20f, 90f)]
    [SerializeField] private float pitch = 58f;

    [Tooltip("ON：自陣が画面の手前に来るように回す。OFF：全員北向き")]
    [SerializeField] private bool faceOwnSide = true;

    [Tooltip("向きが変わるときの回りのなめらかさ。大きいほどすぐ回る")]
    [SerializeField] private float turnSharpness = 6f;

    [Header("誰を映すか")]
    [Tooltip("画面の真ん中を、どれだけ自分に寄せるか。0＝チームの真ん中、1＝自分が真ん中（味方はズームだけで入れる）")]
    [Range(0f, 1f)]
    [SerializeField] private float selfWeight = 0.5f;

    [Tooltip("OFF にすると味方を映さない（自分だけのカメラになる。遊び・ズームの効き方を比べるとき用）")]
    [SerializeField] private bool includeTeammates = true;

    [Tooltip("自分からこれより離れた味方は映さない（メートル）。離れすぎた味方を追って、自分が小さくなりすぎないように")]
    [SerializeField] private float teammateMaxDistance = 40f;

    [Tooltip("この高さより下にいる人は映さない（落ちている味方を追わないように。メートル）")]
    [SerializeField] private float ignoreBelowHeight = -4f;

    [Tooltip("人のまわりに空ける余白（メートル）。人の大きさの分")]
    [SerializeField] private float memberPadding = 1.5f;

    [Tooltip("人の頭の高さ（メートル）。頭まで画面に入れる")]
    [SerializeField] private float memberHeight = 2f;

    [Header("遊び（この中のズレではカメラが横に動かない）")]
    [Tooltip("遊びの、画面の横方向の幅（メートル）。0 で遊び無し（いつも見る点を追う）")]
    [Min(0f)]
    [SerializeField] private float deadZoneWidth = 6f;

    [Tooltip("遊びの、画面の奥行き方向の長さ（メートル）")]
    [Min(0f)]
    [SerializeField] private float deadZoneDepth = 4f;

    [Header("追いかけ")]
    [Tooltip("遊びからはみ出したときに、追いかけるなめらかさ。大きいほどキビキビ")]
    [SerializeField] private float followSharpness = 5f;

    [Tooltip("上下（ジャンプ・段差）への付いていき方。小さいほど上下に揺れない")]
    [SerializeField] private float heightSharpness = 1.5f;

    [Header("ズーム（カメラの距離）")]
    [Tooltip("いちばん近いときの、見る点からカメラまでの距離（メートル）。1人のときはこの距離")]
    [SerializeField] private float minDistance = 18f;

    [Tooltip("いちばん遠いときの距離（メートル）。これでも入らない味方は、画面の外に出る")]
    [SerializeField] private float maxDistance = 36f;

    [Tooltip("画面の端に空けるすき間（画面の幅を1としたときの割合）。大きいほど早めに引く")]
    [Range(0f, 0.4f)]
    [SerializeField] private float screenMargin = 0.12f;

    [Tooltip("引く（遠ざかる）ときのなめらかさ。大きいほどすぐ引く（味方が画面から出ないよう、速めがよい）")]
    [SerializeField] private float zoomOutSharpness = 4f;

    [Tooltip("寄る（近づく）ときのなめらかさ。小さいほどゆっくり寄る")]
    [SerializeField] private float zoomInSharpness = 1f;

    [Tooltip("寄れるようになってから、実際に寄り始めるまで待つ秒数")]
    [Min(0f)]
    [SerializeField] private float zoomInDelay = 0.8f;

    [Tooltip("必要な距離との差が、いまの距離のこの割合より小さければ寄らない（小さな寄り引きを繰り返さないように）")]
    [Range(0f, 0.5f)]
    [SerializeField] private float zoomInDeadBand = 0.1f;

    [Header("調整用")]
    [Tooltip("シーンビューに、見る点・遊びの範囲・映している人を線で描く（再生中だけ）")]
    [SerializeField] private bool drawGizmos = true;

    protected override string DefaultName => "チーム";

    /// <summary>いまカメラが向いている水平の角度（度）。北向きが 0。</summary>
    public float Yaw { get; private set; }

    /// <summary>いまの見る点（地面の上のカメラの注目点）。</summary>
    public Vector3 Focus => focus;

    /// <summary>いまの、見る点からカメラまでの距離。</summary>
    public float Distance => distance;

    private readonly List<Transform> teammates = new List<Transform>();
    private readonly List<Vector3> members = new List<Vector3>();
    private readonly List<Vector3> fitPoints = new List<Vector3>();

    private Vector3 focus;
    private Vector3 desired;
    private float distance;
    private float zoomInTimer;
    private bool zoomingIn;

    public override bool TryGetPose(float deltaTime, out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;

        Transform self = LocalTarget();
        if (self == null)
        {
            return false;
        }

        CollectMembers(self);

        // ① 向き
        float goalYaw = faceOwnSide ? OwnSideYaw() : 0f;
        Yaw = SnapNext ? goalYaw : Mathf.LerpAngle(Yaw, goalYaw, Smooth(turnSharpness, deltaTime));
        rotation = Quaternion.Euler(pitch, Yaw, 0f);

        Quaternion flat = Quaternion.Euler(0f, Yaw, 0f);
        Vector3 right = flat * Vector3.right;
        Vector3 forward = flat * Vector3.forward;

        // ② 見る点：チームの真ん中（画面の向きで見た範囲の真ん中）と自分の間
        desired = Vector3.Lerp(TeamCenter(right, forward), self.position, selfWeight);

        // ③ 遊び：はみ出した分だけ追う
        if (SnapNext)
        {
            focus = desired;
        }
        else
        {
            Vector3 gap = desired - focus;
            float side = Excess(Vector3.Dot(gap, right), deadZoneWidth * 0.5f);
            float depth = Excess(Vector3.Dot(gap, forward), deadZoneDepth * 0.5f);
            Vector3 goal = focus + right * side + forward * depth;

            float follow = Smooth(followSharpness, deltaTime);
            focus.x = Mathf.Lerp(focus.x, goal.x, follow);
            focus.z = Mathf.Lerp(focus.z, goal.z, follow);
            focus.y = Mathf.Lerp(focus.y, desired.y, Smooth(heightSharpness, deltaTime));
        }

        // ④ ズーム：全員が入るいちばん近い距離
        BuildFitPoints(right, forward);
        float need = FitDistance(focus, rotation, fitPoints, screenMargin, minDistance, Mathf.Max(minDistance, maxDistance));
        UpdateDistance(need, deltaTime);

        SnapNext = false;
        position = focus + rotation * Vector3.back * distance;
        return true;
    }

    /// <summary>映す人（自分＋映せる味方）の位置を集める。</summary>
    private void CollectMembers(Transform self)
    {
        members.Clear();
        members.Add(self.position);

        if (!includeTeammates)
        {
            return;
        }

        GatherTeammates(self, teammates);

        foreach (Transform mate in teammates)
        {
            if (mate == null || !mate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 p = mate.position;
            if (p.y < ignoreBelowHeight)
            {
                continue;
            }

            Vector3 apart = p - self.position;
            apart.y = 0f;
            if (apart.magnitude > teammateMaxDistance)
            {
                continue;
            }

            members.Add(p);
        }
    }

    /// <summary>映す人全員を囲む範囲（画面の横・奥行きの向きで測る）の真ん中。高さは平均。</summary>
    private Vector3 TeamCenter(Vector3 right, Vector3 forward)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        float height = 0f;

        foreach (Vector3 p in members)
        {
            float x = Vector3.Dot(p, right);
            float z = Vector3.Dot(p, forward);
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
            height += p.y;
        }

        Vector3 center = right * ((minX + maxX) * 0.5f) + forward * ((minZ + maxZ) * 0.5f);
        center.y = height / members.Count;
        return center;
    }

    /// <summary>画面に入れたい点（人の足元と頭のまわり）。</summary>
    private void BuildFitPoints(Vector3 right, Vector3 forward)
    {
        fitPoints.Clear();

        foreach (Vector3 p in members)
        {
            Vector3 head = p + Vector3.up * memberHeight;
            fitPoints.Add(p + right * memberPadding);
            fitPoints.Add(p - right * memberPadding);
            fitPoints.Add(p + forward * memberPadding);
            fitPoints.Add(p - forward * memberPadding);
            fitPoints.Add(head + forward * memberPadding);
        }
    }

    /// <summary>距離を必要な距離へ寄せる。引くのは早く、寄るのはゆっくり・少し待ってから。</summary>
    private void UpdateDistance(float need, float deltaTime)
    {
        if (SnapNext)
        {
            distance = need;
            zoomInTimer = 0f;
            zoomingIn = false;
            return;
        }

        if (need > distance)
        {
            // 引く：入らない人が出ないように、すぐ
            distance = Mathf.Lerp(distance, need, Smooth(zoomOutSharpness, deltaTime));
            zoomInTimer = 0f;
            zoomingIn = false;
        }
        else if (zoomingIn || need < distance * (1f - zoomInDeadBand))
        {
            // 寄る：しばらく寄れる状態が続いてから、ゆっくり。寄り始めたら、必要な距離まで寄りきる
            zoomInTimer += deltaTime;
            if (zoomInTimer >= zoomInDelay)
            {
                zoomingIn = true;
                distance = Mathf.Lerp(distance, need, Smooth(zoomInSharpness, deltaTime));

                if (distance - need < 0.05f)
                {
                    zoomingIn = false;
                    zoomInTimer = 0f;
                }
            }
        }
        else
        {
            zoomInTimer = 0f;
        }

        distance = Mathf.Clamp(distance, minDistance, Mathf.Max(minDistance, maxDistance));
    }

    /// <summary>遊びの半分の幅 <paramref name="half"/> をはみ出した分（はみ出していなければ 0）。</summary>
    private static float Excess(float value, float half)
    {
        if (value > half)
        {
            return value - half;
        }

        if (value < -half)
        {
            return value + half;
        }

        return 0f;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
        {
            return;
        }

        Quaternion flat = Quaternion.Euler(0f, Yaw, 0f);
        Vector3 right = flat * Vector3.right * (deadZoneWidth * 0.5f);
        Vector3 forward = flat * Vector3.forward * (deadZoneDepth * 0.5f);

        // 遊びの範囲（黄色の四角）。見る点の目標（水色）がこの外に出ると、カメラが動く
        Gizmos.color = Color.yellow;
        Vector3 a = focus - right - forward;
        Vector3 b = focus + right - forward;
        Vector3 c = focus + right + forward;
        Vector3 d = focus - right + forward;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
        Gizmos.DrawSphere(focus, 0.3f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(desired, 0.3f);

        // 映している人（緑の線）
        Gizmos.color = Color.green;
        foreach (Vector3 p in members)
        {
            Gizmos.DrawLine(focus, p);
        }
    }
}
