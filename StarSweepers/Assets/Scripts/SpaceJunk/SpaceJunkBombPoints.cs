using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった場所に爆弾を置き、無くなったら同じ場所にまた出すスポナー。**
///
/// 子オブジェクト1つが「爆弾を置く場所」1か所になる（4つ置けば4か所）。
/// 置いた爆弾が**その場所から動かされた**か**消えた**（爆発・場外）ら、
/// **Respawn Seconds（初期値3秒）後に、同じ場所へ新しい爆弾を出す。**
///
/// ・動かされた爆弾はそのまま残る（投げたり、爆発させたりできる）
/// ・**ラウンドの結果が出たら、もう出さない**
///
/// ## オンラインのとき
///
/// **出すのはホストだけ。** ホストが出して Spawn() すると、全員の画面に同じ物が現れる。
/// 爆弾のプレハブは `DefaultNetworkPrefabs` に登録してあるもの
/// （`FishingOnlineBomb` や `KnockBackBoom`）を使うこと。
/// </summary>
public class SpaceJunkBombPoints : MonoBehaviour
{
    [Header("出す爆弾")]
    [Tooltip("爆弾のプレハブ。オンラインで使うなら NetworkObject が付いたもの（FishingOnlineBomb / KnockBackBoom）")]
    [SerializeField] private GameObject bombPrefab;

    [Header("出し直し")]
    [Tooltip("爆弾が動かされた・消えてから、同じ場所に次を出すまでの秒数")]
    [SerializeField] private float respawnSeconds = 3f;

    [Tooltip("置いた場所から横にこれ以上離れたら「動かされた」とみなす（m）")]
    [SerializeField] private float moveThreshold = 1f;

    [Tooltip("始まってから最初の爆弾を出すまでの秒数（シーン切り替えが落ち着くのを待つ）")]
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
        points = new Point[transform.childCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Point { place = transform.GetChild(i) };
        }
    }

    private void Update()
    {
        if (!CanSpawnOnThisPC() || bombPrefab == null)
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

    /// <summary>置いた爆弾が、消えたか・場所から動かされたか。</summary>
    private bool HasLeft(Point point)
    {
        if (point.bomb == null || point.bomb.IsVanished)
        {
            return true;
        }

        Vector3 offset = point.bomb.transform.position - point.place.position;
        offset.y = 0f;
        return offset.sqrMagnitude > moveThreshold * moveThreshold;
    }

    private void SpawnAt(Point point)
    {
        point.waitTimer = -1f;

        GameObject spawned = Instantiate(bombPrefab, point.place.position, point.place.rotation);
        point.bomb = spawned.GetComponent<HookableObject>();

        if (point.bomb == null)
        {
            Debug.LogWarning($"[JUNK] {bombPrefab.name} に HookableObject が無いため、動かされたか分かりません。" +
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
                Debug.LogWarning($"[JUNK] {bombPrefab.name} に NetworkObject が無いため、ホストの画面にしか出ません。" +
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
