using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **物資と爆発物を、マップ上に次々と出すスポナー。**
///
/// 決まった間隔ごとに、範囲の中のランダムな場所の少し上から1つ落とす。
/// 物資と爆発物のどちらを出すかは、**重み（出やすさ）の割合**で決める。
///
/// ・マップにある物（物資＋爆発物）が **Max Objects 以上なら出さない**
/// ・出す場所に他の物やプレイヤーがいたら、別の場所を探す
///
/// ## オンラインのとき
///
/// **出すのはホストだけ。** ホストが出して `Spawn()` すると、全員の画面に同じ物が現れる。
/// 参加者のPCでは何もしない（それぞれが出すと、人数分に増えてしまう）。
/// オンライン用のプレハブは `DefaultNetworkPrefabs` に登録しておく必要がある
/// （`Tools > StarSweepers > 釣りのスポナーを作る` が自動で登録する）。
/// </summary>
public class FishingObjectSpawner : MonoBehaviour
{
    [Header("出す物")]
    [Tooltip("点数になる物資のプレハブ")]
    [SerializeField] private GameObject supplyPrefab;

    [Tooltip("爆発物のプレハブ")]
    [SerializeField] private GameObject bombPrefab;

    [Header("割合（出やすさ）")]
    [Tooltip("物資の出やすさ。爆発物の値との比で決まる（初期値は 物資5：爆発物1）")]
    [SerializeField] private int supplyWeight = 5;

    [Tooltip("爆発物の出やすさ。物資の値との比で決まる（初期値は 物資5：爆発物1）")]
    [SerializeField] private int bombWeight = 1;

    [Header("数と間隔")]
    [Tooltip("マップにこの数以上あったら、もう出さない")]
    [SerializeField] private int maxObjects = 100;

    [Tooltip("始まった直後に、まとめて出す数")]
    [SerializeField] private int initialSpawnCount = 14;

    [Tooltip("何秒ごとに1つ出すか")]
    [SerializeField] private float spawnIntervalSeconds = 1f;

    [Tooltip("始まってから出し始めるまでの秒数（オンラインでシーン切り替えが落ち着くのを待つ）")]
    [SerializeField] private float startDelaySeconds = 0.5f;

    [Header("出す場所")]
    [Tooltip("このオブジェクトの位置を中心に、左右（X）・前後（Z）それぞれ何mの範囲に出すか")]
    [SerializeField] private Vector2 areaHalfSize = new Vector2(16f, 16f);

    [Tooltip("床から何mの高さに出して落とすか")]
    [SerializeField] private float dropHeight = 3f;

    [Tooltip("出す場所の周りにこの半径で何かあったら、別の場所を探す")]
    [SerializeField] private float clearRadius = 0.8f;

    [Tooltip("空いた場所を探す回数。見つからなければ、その回は出さない")]
    [SerializeField] private int placementTries = 8;

    /// <summary>前に出してからの経過秒数。最初は「始まってからの秒数」として使う。</summary>
    private float timer;

    /// <summary>始まった直後のまとめ出しを済ませたか。</summary>
    private bool initialDone;

    private void Update()
    {
        if (!CanSpawnOnThisPC())
        {
            return;
        }

        timer += Time.deltaTime;

        // 最初の1回：少し待ってから、まとめて出す
        if (!initialDone)
        {
            if (timer < startDelaySeconds)
            {
                return;
            }

            initialDone = true;
            timer = 0f;

            for (int i = 0; i < initialSpawnCount; i++)
            {
                TrySpawnOne();
            }
            return;
        }

        // そのあとは、決まった間隔で1つずつ出す
        if (timer < spawnIntervalSeconds)
        {
            return;
        }

        timer = 0f;
        TrySpawnOne();
    }

    /// <summary>
    /// このPCが出す役目かどうか。
    /// 通信していなければ自分で出す。通信中なら**ホストだけ**が出す。
    /// 試合が終わったら出さない。
    /// </summary>
    private bool CanSpawnOnThisPC()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer)
        {
            return false;
        }

        return FishingMatch.PlayAllowed;
    }

    /// <summary>1つ出してみる。数が上限に達している・空いた場所が無い、などのときは何も出さない。</summary>
    private void TrySpawnOne()
    {
        if (HookableObject.CountActive() >= maxObjects)
        {
            return;
        }

        GameObject prefab = ChoosePrefab();
        if (prefab == null)
        {
            return;
        }

        if (!TryFindPlace(out Vector3 position))
        {
            return;
        }

        // 向きは水平にランダム（見た目がそろいすぎないように）
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject spawned = Instantiate(prefab, position, rotation);

        // 通信中なら、全員の画面に出す
        SpawnForEveryoneIfOnline(spawned, prefab);
    }

    /// <summary>
    /// 通信中なら、出した物を全員の画面に出す（ホストだけが呼ばれる前提）。
    /// 通信していなければ何もしない（Instantiate しただけで、この画面に出ている）。
    /// </summary>
    private void SpawnForEveryoneIfOnline(GameObject spawned, GameObject prefab)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsListening)
        {
            return;
        }

        NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            // true ＝ ホストがシーンを切り替えたときに、この物も一緒に消す
            networkObject.Spawn(true);
        }
        else
        {
            Debug.LogWarning($"[FISH] {prefab.name} に NetworkObject が無いため、ホストの画面にしか出ません。" +
                             "オンライン用のプレハブを入れてください。");
        }
    }

    /// <summary>重みの割合で、物資か爆発物のプレハブを選ぶ。</summary>
    private GameObject ChoosePrefab()
    {
        int supply = supplyPrefab != null ? Mathf.Max(0, supplyWeight) : 0;
        int bomb = bombPrefab != null ? Mathf.Max(0, bombWeight) : 0;
        int total = supply + bomb;

        if (total <= 0)
        {
            return null;
        }

        return Random.Range(0, total) < supply ? supplyPrefab : bombPrefab;
    }

    /// <summary>範囲の中から、何も無い場所を探す。見つからなければ false（その回は出さない）。</summary>
    private bool TryFindPlace(out Vector3 position)
    {
        for (int i = 0; i < placementTries; i++)
        {
            Vector3 candidate = transform.position + new Vector3(
                Random.Range(-areaHalfSize.x, areaHalfSize.x),
                dropHeight,
                Random.Range(-areaHalfSize.y, areaHalfSize.y));

            // 落とす高さから床の少し上まで、縦に長く調べる（プレイヤーや障害物の真上を避ける）。
            // このオブジェクトの高さ＝床の高さとみなし、カプセルの下端が床に触れて「ふさがっている」と
            // 誤判定しないよう、半径ぶん＋0.1m だけ浮かせている
            Vector3 bottom = new Vector3(candidate.x, transform.position.y + clearRadius + 0.1f, candidate.z);
            bool blocked = Physics.CheckCapsule(bottom, candidate, clearRadius, ~0, QueryTriggerInteraction.Ignore);

            if (!blocked)
            {
                position = candidate;
                return true;
            }
        }

        position = Vector3.zero;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        // 出す範囲をシーン画面に緑の枠で出す
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(
            transform.position + Vector3.up * dropHeight,
            new Vector3(areaHalfSize.x * 2f, 0.1f, areaHalfSize.y * 2f));
    }
}
