using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// **決まった場所に物（爆弾・特殊デブリ）を置き、無くなったら同じ場所にまた出すスポナー。**
///
/// 子オブジェクト1つが「物を置く場所」1か所になる（4つ置けば4か所）。
/// 置いた物が**その場所から動かされた**か**消えた**（爆発・ゴール・場外）ら、
/// **Respawn Seconds（初期値3秒）後に、同じ場所へ新しく出す。**
///
/// ・動かされた物はそのまま残る（投げたり、爆発させたりできる）
/// ・**Respawn When Moved を OFF にすると、消えたときだけ出し直す**（真ん中の特殊デブリ用。動かすたびに増えないように）
/// ・**One Random Point を ON にすると、子の場所のうち毎回ランダムな1か所にだけ出す**（STAGE_03 の特殊デブリ）
/// ・**ラウンドの結果が出たら、もう出さない**
///
/// ## オンラインのとき
///
/// **出すのはホストだけ。** ホストが出して Spawn() すると、全員の画面に同じ物が現れる。
/// プレハブは `DefaultNetworkPrefabs` に登録してあるもの
/// （`FishingOnlineBomb` や `KnockBackBoom`、`SpaceJunkSpecial`）を使うこと。
/// </summary>
public class SpaceJunkBombPoints : MonoBehaviour
{
    [Header("出す物")]
    [Tooltip("出す物のプレハブ。オンラインで使うなら NetworkObject が付いたもの（FishingOnlineBomb / KnockBackBoom / SpaceJunkSpecial）")]
    [FormerlySerializedAs("bombPrefab")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("出し直し")]
    [Tooltip("動かされた・消えてから、同じ場所に次を出すまでの秒数")]
    [SerializeField] private float respawnSeconds = 3f;

    [Tooltip("ON：動かされたら出し直す（爆弾）。OFF：消えたときだけ出し直す（特殊デブリ）")]
    [SerializeField] private bool respawnWhenMoved = true;

    [Tooltip("置いた場所から横にこれ以上離れたら「動かされた」とみなす（m）")]
    [SerializeField] private float moveThreshold = 1f;

    [Tooltip("ON：子の場所のうち、毎回ランダムな1か所にだけ出す（同時に1つだけ。STAGE_03 の特殊デブリの上・中・下）。" +
             "OFF：子の場所すべてに1つずつ出す")]
    [SerializeField] private bool oneRandomPoint = false;

    [Tooltip("始まってから最初の物を出すまでの秒数（シーン切り替えが落ち着くのを待つ）")]
    [SerializeField] private float startDelaySeconds = 0.5f;

    /// <summary>1か所分の状態。</summary>
    private class Point
    {
        public Transform place;
        public HookableObject bomb;
        public float waitTimer = -1f; // 0以上なら、出し直しを待っている
    }

    private Point[] points;
    private float startTimer;
    private bool started;

    private void Awake()
    {
        if (oneRandomPoint)
        {
            // 1か所分の状態だけ持ち、出すたびに場所を選び直す
            points = new Point[] { new Point { place = RandomChild() } };
            return;
        }

        points = new Point[transform.childCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Point { place = transform.GetChild(i) };
        }
    }

    private void Update()
    {
        if (!CanSpawnOnThisPC() || spawnPrefab == null)
        {
            return;
        }

        if (!started)
        {
            startTimer += Time.deltaTime;
            if (startTimer < startDelaySeconds)
            {
                return;
            }

            started = true;
            foreach (Point point in points)
            {
                SpawnAt(point);
            }
            return;
        }

        foreach (Point point in points)
        {
            UpdatePoint(point);
        }
    }

    private void UpdatePoint(Point point)
    {
        if (point.waitTimer >= 0f)
        {
            point.waitTimer += Time.deltaTime;
            if (point.waitTimer >= respawnSeconds)
            {
                SpawnAt(point);
            }
            return;
        }

        if (HasLeft(point))
        {
            // この爆弾はもう追いかけない。動かされた爆弾はそのまま残る
            point.bomb = null;
            point.waitTimer = 0f;
        }
    }

    /// <summary>置いた物が、消えたか・場所から動かされたか（Respawn When Moved が OFF なら、消えたかだけ）。</summary>
    private bool HasLeft(Point point)
    {
        if (point.bomb == null || point.bomb.IsVanished)
        {
            return true;
        }

        if (!respawnWhenMoved)
        {
            return false;
        }

        Vector3 offset = point.bomb.transform.position - point.place.position;
        offset.y = 0f;
        return offset.sqrMagnitude > moveThreshold * moveThreshold;
    }

    /// <summary>子の場所から1つランダムに選ぶ。子が無ければ null。</summary>
    private Transform RandomChild()
    {
        return transform.childCount > 0 ? transform.GetChild(Random.Range(0, transform.childCount)) : null;
    }

    private void SpawnAt(Point point)
    {
        point.waitTimer = -1f;

        if (oneRandomPoint)
        {
            point.place = RandomChild();
        }

        if (point.place == null)
        {
            return;
        }

        GameObject spawned = Instantiate(spawnPrefab, point.place.position, point.place.rotation);
        point.bomb = spawned.GetComponent<HookableObject>();

        if (point.bomb == null)
        {
            Debug.LogWarning($"[JUNK] {spawnPrefab.name} に HookableObject が無いため、動かされたか分かりません。" +
                             "爆弾のプレハブを入れてください。");
        }

        // 通信中なら、全員の画面に出す
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening)
        {
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                networkObject.Spawn(true);
            }
            else
            {
                Debug.LogWarning($"[JUNK] {spawnPrefab.name} に NetworkObject が無いため、ホストの画面にしか出ません。" +
                                 "オンライン用のプレハブを入れてください。");
            }
        }
    }

    /// <summary>
    /// このPCが出す役目かどうか。
    /// 通信していなければ自分で出す。通信中なら**ホストだけ**が出す。
    /// ラウンドの結果が出たら出さない。
    /// </summary>
    private bool CanSpawnOnThisPC()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer)
        {
            return false;
        }

        return SpaceJunkRound.PlayAllowed;
    }

    private void OnDrawGizmos()
    {
        // 爆弾を置く場所を、シーン画面に赤い球で出す
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        foreach (Transform child in transform)
        {
            Gizmos.DrawWireSphere(child.position, 0.5f);
        }
    }
}
