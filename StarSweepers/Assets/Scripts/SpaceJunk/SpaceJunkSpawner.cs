using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙船の素材3種類を、マップ上に次々と落とすスポナー。**
///
/// 決まった間隔ごとに、範囲の中のランダムな場所の少し上から1つ落とす。
/// どの種類を出すかは**重み（出やすさ）の割合**で決める（初期値は3種類とも同じ）。
///
/// ・マップにある素材が **Max Objects 以上なら出さない**
/// ・出す場所に他の物やプレイヤーがいたら、別の場所を探す
/// ・**ラウンドの結果が出たら、もう出さない**
/// ・**Spawn Zones に範囲（四角）を入れたら、その範囲の中にだけ出す**（STAGE_02 の緑の範囲など）
///
/// ## オンラインのとき
///
/// **出すのはホストだけ。** ホストが出して Spawn() すると、全員の画面に同じ物が現れる。
/// 素材のプレハブは `DefaultNetworkPrefabs` に登録しておく必要がある
/// （`Tools > StarSweepers > 宇宙ごみ集めのシーンを作る（ロビー＋マップ）` が自動で登録する）。
///
/// ## 釣りのスポナーとの違い
///
/// 釣り（<c>FishingObjectSpawner</c>）は「物資」と「爆発物」の2種類を、爆発物6：物資1で出していた。
/// こちらは**素材3種類を均等に出す**のと、**爆発物を出さない**のが違う
/// （爆発物は今回入れない。2026/9/17・大槻さん）。
/// </summary>
public class SpaceJunkSpawner : MonoBehaviour
{
    [Header("出す素材（基本の3種類）")]
    [Tooltip("装甲板のプレハブ")]
    [SerializeField] private GameObject platePrefab;

    [Tooltip("回路基板のプレハブ")]
    [SerializeField] private GameObject circuitPrefab;

    [Tooltip("燃料タンクのプレハブ")]
    [SerializeField] private GameObject fuelPrefab;

    [Header("割合（出やすさ）")]
    [Tooltip("装甲板の出やすさ")]
    [SerializeField] private int plateWeight = 1;

    [Tooltip("回路基板の出やすさ")]
    [SerializeField] private int circuitWeight = 1;

    [Tooltip("燃料タンクの出やすさ")]
    [SerializeField] private int fuelWeight = 1;

    /// <summary>上の3種類のほかに出す素材1つ分。</summary>
    [System.Serializable]
    public class ExtraMaterial
    {
        [Tooltip("素材のプレハブ（SpaceJunkMaterial が付いたもの）")]
        public GameObject prefab;

        [Tooltip("出やすさ")]
        public int weight = 1;
    }

    [Header("追加の素材（4種類目から）")]
    [Tooltip("上の3種類のほかに出す素材。空なら3種類だけ（今までのマップ）")]
    [SerializeField] private ExtraMaterial[] extraMaterials = new ExtraMaterial[0];

    /// <summary>
    /// **いまのシーンのスポナー。** どの種類の素材が出るマップかを、ラウンドのイベントや画面が調べるのに使う
    /// （ホストでも参加者でも入っている）。
    /// </summary>
    public static SpaceJunkSpawner Current { get; private set; }

    [Header("数と間隔")]
    [Tooltip("マップにこの数以上あったら、もう出さない")]
    [SerializeField] private int maxObjects = 60;

    [Tooltip("ラウンドが始まった直後に、まとめて出す数")]
    [SerializeField] private int initialSpawnCount = 18;

    [Tooltip("何秒ごとに1つ出すか")]
    [SerializeField] private float spawnIntervalSeconds = 0.8f;

    [Tooltip("始まってから出し始めるまでの秒数（シーン切り替えが落ち着くのを待つ）")]
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

    [Tooltip("ここに入れた四角の中にだけ出す（空のオブジェクトを置き、位置と Scale の X・Z で四角を決める）。" +
             "空なら Area Half Size の範囲に出す。選ぶと、シーン画面に緑の枠で見える")]
    [SerializeField] private Transform[] spawnZones = new Transform[0];

    private float timer;
    private bool initialDone;

    private void OnEnable()
    {
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>この種類の素材を出すか（プレハブが入っていて、出やすさが0より大きいか）。</summary>
    public bool UsesKind(SpaceJunkMaterialKind kind)
    {
        if (IsKind(platePrefab, plateWeight, kind) || IsKind(circuitPrefab, circuitWeight, kind) ||
            IsKind(fuelPrefab, fuelWeight, kind))
        {
            return true;
        }

        foreach (ExtraMaterial extra in extraMaterials)
        {
            if (extra != null && IsKind(extra.prefab, extra.weight, kind))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsKind(GameObject prefab, int weight, SpaceJunkMaterialKind kind)
    {
        return prefab != null && weight > 0 &&
               prefab.TryGetComponent(out SpaceJunkMaterial material) && material.Kind == kind;
    }

    private void Update()
    {
        if (!CanSpawnOnThisPC())
        {
            return;
        }

        timer += Time.deltaTime;

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

    /// <summary>
    /// **イベント用に、数の上限を気にせず1つ出す**（期間限定高価値デブリで使う）。**ホストだけが呼ぶ。**
    /// 置き場所が見つからなければ null。
    /// </summary>
    public GameObject ServerSpawnExtra()
    {
        return SpawnOne(ignoreLimit: true);
    }

    private void TrySpawnOne()
    {
        SpawnOne(ignoreLimit: false);
    }

    private GameObject SpawnOne(bool ignoreLimit)
    {
        if (!ignoreLimit && CountMaterials() >= maxObjects)
        {
            return null;
        }

        GameObject prefab = ChoosePrefab();
        if (prefab == null)
        {
            return null;
        }

        if (!TryFindPlace(out Vector3 position))
        {
            return null;
        }

        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject spawned = Instantiate(prefab, position, rotation);

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
                Debug.LogWarning($"[JUNK] {prefab.name} に NetworkObject が無いため、ホストの画面にしか出ません。" +
                                 "オンライン用のプレハブを入れてください。");
            }
        }

        return spawned;
    }

    /// <summary>
    /// マップにある素材の数。**爆弾と、真ん中の特殊デブリは数えない**
    /// （<see cref="SpaceJunkBombPoints"/> で置いた物で、出せる素材の数が減らないようにする）。
    /// </summary>
    private static int CountMaterials()
    {
        int count = 0;
        foreach (HookableObject item in HookableObject.All)
        {
            if (item != null && !item.IsVanished && item.GetComponent<ExplosiveObject>() == null &&
                item.GetComponent<SpaceJunkBonusDebris>() == null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>重みの割合で、3種類（＋追加の素材）のうち1つのプレハブを選ぶ。</summary>
    private GameObject ChoosePrefab()
    {
        int plate = platePrefab != null ? Mathf.Max(0, plateWeight) : 0;
        int circuit = circuitPrefab != null ? Mathf.Max(0, circuitWeight) : 0;
        int fuel = fuelPrefab != null ? Mathf.Max(0, fuelWeight) : 0;
        int total = plate + circuit + fuel;
        foreach (ExtraMaterial extra in extraMaterials)
        {
            total += ExtraWeight(extra);
        }

        if (total <= 0)
        {
            return null;
        }

        int roll = Random.Range(0, total);

        if (roll < plate)
        {
            return platePrefab;
        }

        if (roll < plate + circuit)
        {
            return circuitPrefab;
        }

        if (roll < plate + circuit + fuel)
        {
            return fuelPrefab;
        }

        roll -= plate + circuit + fuel;
        foreach (ExtraMaterial extra in extraMaterials)
        {
            int weight = ExtraWeight(extra);
            if (roll < weight)
            {
                return extra.prefab;
            }
            roll -= weight;
        }

        return fuelPrefab;
    }

    private static int ExtraWeight(ExtraMaterial extra)
    {
        return extra != null && extra.prefab != null ? Mathf.Max(0, extra.weight) : 0;
    }

    /// <summary>範囲の中から、何も無い場所を探す。</summary>
    private bool TryFindPlace(out Vector3 position)
    {
        for (int i = 0; i < placementTries; i++)
        {
            Vector3 candidate = RandomPointInArea();

            // 落とす高さから床の少し上まで、縦に長く調べる（プレイヤーや障害物の真上を避ける）
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

    /// <summary>
    /// 出す候補の場所を1つ選ぶ（高さは「床＋Drop Height」）。
    /// Spawn Zones があれば、**その四角のどれか**（広いものほど選ばれやすい）の中。無ければ Area Half Size の範囲の中。
    /// </summary>
    private Vector3 RandomPointInArea()
    {
        float totalArea = 0f;
        foreach (Transform zone in spawnZones)
        {
            totalArea += ZoneArea(zone);
        }

        if (totalArea <= 0f)
        {
            return transform.position + new Vector3(
                Random.Range(-areaHalfSize.x, areaHalfSize.x),
                dropHeight,
                Random.Range(-areaHalfSize.y, areaHalfSize.y));
        }

        float roll = Random.Range(0f, totalArea);
        foreach (Transform zone in spawnZones)
        {
            float area = ZoneArea(zone);
            if (area <= 0f)
            {
                continue;
            }

            if (roll <= area)
            {
                // 四角の中の1点（-0.5〜0.5 を、四角の大きさと向きに合わせて広げる）
                Vector3 local = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
                Vector3 point = zone.TransformPoint(local);
                point.y = transform.position.y + dropHeight;
                return point;
            }
            roll -= area;
        }

        return transform.position + Vector3.up * dropHeight;
    }

    /// <summary>範囲の四角の広さ（大きさ Scale の X × Z）。</summary>
    private static float ZoneArea(Transform zone)
    {
        if (zone == null)
        {
            return 0f;
        }

        Vector3 size = zone.lossyScale;
        return Mathf.Abs(size.x * size.z);
    }

    private void OnDrawGizmosSelected()
    {
        // 出す範囲をシーン画面に緑の枠で出す
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);

        bool hasZone = false;
        foreach (Transform zone in spawnZones)
        {
            if (zone == null)
            {
                continue;
            }

            hasZone = true;
            Gizmos.matrix = zone.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
        }
        Gizmos.matrix = Matrix4x4.identity;

        if (!hasZone)
        {
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * dropHeight,
                new Vector3(areaHalfSize.x * 2f, 0.1f, areaHalfSize.y * 2f));
        }
    }
}
