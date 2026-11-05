using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビーの練習場専用の、特定位置・無限おかわりスポナー。**
/// 範囲指定は一切せず、このコンポーネントが付いている空オブジェクトの位置（Position/Rotation）に
/// 設定した秒数ごとに、コンベアへ流すためのオブジェクトを直接生成し続けます。
/// 場に存在するオブジェクトの最大数を制限する機能付き。
/// </summary>
public class SpaceJunkLobbyFixedSpawner : MonoBehaviour
{
    [Header("出す物")]
    [Tooltip("ロビーで無限に出したいプレハブ（SpaceJunkSpecialなど、NetworkObject付きのもの）")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("間隔と制限")]
    [Tooltip("何秒ごとに1つ落とすか（秒）")]
    [SerializeField] private float spawnIntervalSeconds = 3f;

    [Tooltip("同時に場に存在できる最大数（0なら無制限）")]
    [SerializeField] private int maxCount = 5;

    // 生成したオブジェクトを管理するリスト
    private readonly List<GameObject> spawnedInstances = new List<GameObject>();
    private float timer;

    private void Update()
    {
        // ホスト（サーバー）のPC以外、または本戦が始まってしまったらロビー用なので止める
        if (!CanSpawnLobby()) return;

        // すでに消滅（Destroy）したオブジェクトをリストから除外する
        spawnedInstances.RemoveAll(item => item == null);

        // 現在の場にある数が最大数以上なら、タイマーを進めずに待機する
        if (maxCount > 0 && spawnedInstances.Count >= maxCount)
        {
            return;
        }

        timer += Time.deltaTime;
        if (timer >= spawnIntervalSeconds)
        {
            timer = 0f;
            SpawnOne();
        }
    }

    private bool CanSpawnLobby()
    {
        // 通信中ならホストだけが湧かせる責任を持つ
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer) return false;

        // 本戦のラウンド（SpaceJunkRound）が動いていない「ロビーの時だけ」動くようにする
        return SpaceJunkRound.Current == null;
    }

    private void SpawnOne()
    {
        if (spawnPrefab == null) return;

        // このオブジェクトのまさにその位置、その向きで生成する
        Vector3 spawnPosition = transform.position;
        Quaternion spawnRotation = transform.rotation;

        GameObject spawned = Instantiate(spawnPrefab, spawnPosition, spawnRotation);

        // 生成したオブジェクトをリストに登録して数を数えられるようにする
        spawnedInstances.Add(spawned);

        // オンライン同期：全員の画面に出現させる
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening)
        {
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                networkObject.Spawn(true);
            }
        }
    }

    private void OnDrawGizmos()
    {
        // どこに湧くかエディタの画面に水色の球で出す
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}
