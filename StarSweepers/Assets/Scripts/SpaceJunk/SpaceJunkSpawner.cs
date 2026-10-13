using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙船の素材（と爆弾）を、マップ上に次々と落とすスポナー。**
///
/// 決まった間隔ごとに、範囲の中のランダムな場所の少し上から1つ落とす。
/// 何を出すかは **Spawn List（プレハブと出やすさの一覧）** の割合で決める。素材も爆弾も同じ一覧に入れる（2026/10/6 から。
/// 前の「3種類＋追加の素材＋爆弾」の書き方のシーンは、自動で一覧へ移し替える）。
///
/// ・マップにある素材が **Max Objects 以上なら出さない**
/// ・出す場所に他の物やプレイヤーがいたら、別の場所を探す
/// ・**ラウンドの結果が出たら、もう出さない**
/// ・**Spawn Zones に範囲（四角）を入れたら、その範囲の中にだけ出す**（STAGE_02 の緑の範囲など）
/// ・**Spawn List に爆弾（ExplosiveObject 付き）を入れたら、素材と一緒に爆弾も出す**（マップの爆弾は Max Bombs 個まで。STAGE_06）
/// ・**Cycle Zones を ON にすると、Spawn Zones に1回ずつ出すのを1巡として回す**（1巡ごとに順番をシャッフル。STAGE_06_A）
/// ・**Kind Zones に種類と範囲を入れたら、その種類だけその範囲に出す**（STAGE_05 の「敵陣まで取りに行く」）
/// ・**Spawn Circle Radius を入れたら円の中に出す。Require Ground Below を ON にすると、真下に床がある所にだけ出す**（STAGE_03 のドーナツ）
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
/// （爆発物は入れない、が 2026/9/17 の決まりだったが、2026/10/6 から Bomb Prefab で出せるようにした。STAGE_06）。
/// </summary>
public class SpaceJunkSpawner : MonoBehaviour
{
    /// <summary>出す物1つ分（プレハブと出やすさ）。</summary>
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("出すプレハブ。素材（SpaceJunkMaterial 付き）でも、爆弾（ExplosiveObject 付き）でもよい")]
        public GameObject prefab;

        [Tooltip("出やすさ。ほかの物との割合で決まる（全部 1 なら同じくらい）。0 なら出さない")]
        [Min(0f)] public float weight = 1f;
    }

    [Header("出す物（＋／−で足したり消したりできる）")]
    [Tooltip("出すプレハブと出やすさの一覧。素材も爆弾もここに入れる。" +
             "例：素材5種類を 1 ずつ、スタン爆弾を 1.25 にすると、だいたい5回に1回が爆弾になる")]
    [SerializeField] private List<SpawnEntry> spawnList = new List<SpawnEntry>();

    [Tooltip("マップにある爆弾がこの数以上なら、爆弾は出さない。**素材の Max Objects とは別に数える**")]
    [SerializeField] private int maxBombs = 2;

    /// <summary>
    /// **いまのシーンのスポナー。** どの種類の素材が出るマップかを、ラウンドのイベントや画面が調べるのに使う
    /// （ホストでも参加者でも入っている）。
    /// </summary>
    public static SpaceJunkSpawner Current { get; private set; }

    // ------------------------------------------------------------
    // 古い書き方（2026/10/6 まで）。**Spawn List が空なら、ここから自動で移し替える。**
    // インスペクターには出さない。前に作ったマップのシーンを書き直さずに済むように残してある
    // ------------------------------------------------------------

    /// <summary>古い書き方の「追加の素材」1つ分。</summary>
    [System.Serializable]
    public class ExtraMaterial
    {
        public GameObject prefab;
        public int weight = 1;
    }

    [HideInInspector][SerializeField] private GameObject platePrefab;
    [HideInInspector][SerializeField] private GameObject circuitPrefab;
    [HideInInspector][SerializeField] private GameObject fuelPrefab;
    [HideInInspector][SerializeField] private int plateWeight = 1;
    [HideInInspector][SerializeField] private int circuitWeight = 1;
    [HideInInspector][SerializeField] private int fuelWeight = 1;
    [HideInInspector][SerializeField] private ExtraMaterial[] extraMaterials = new ExtraMaterial[0];
    [HideInInspector][SerializeField] private GameObject bombPrefab;
    [HideInInspector][SerializeField] private float bombChance = 0.2f;

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

    [Tooltip("ON：Spawn Zones が2つ以上あるとき、**全部のゾーンに1回ずつ出すのを1巡とする**（順番は1巡ごとにシャッフル。" +
             "テトリスの7種1巡と同じ）。OFF：毎回ランダムなゾーン（広いゾーンほど出やすい）")]
    [SerializeField] private bool cycleZones = false;

    [Tooltip("0より大きくすると、このオブジェクトの位置を中心に、この半径の円の中に出す（Spawn Zones が空のとき）。" +
             "STAGE_03 のドーナツなど。0なら Area Half Size の四角")]
    [SerializeField] private float spawnCircleRadius = 0f;

    [Tooltip("ON にすると、**真下に床があるときだけ**出す（穴の上には出さない）")]
    [SerializeField] private bool requireGroundBelow = false;

    [Tooltip("ON にすると、**土台など一段高い床の上にも**出す（真下の床から Drop Height の高さに出す。STAGE_01 の土台）。" +
             "高い床は、Drop Height より低くしておくこと。" +
             "OFF なら、このオブジェクトと同じ高さの床だけに出す（高い床の上は「何かある」とみなして出さない）")]
    [SerializeField] private bool spawnOnRaisedFloor = false;

    /// <summary>ある種類の素材だけを出す範囲。</summary>
    [System.Serializable]
    public class KindZone
    {
        [Tooltip("どの種類の素材か")]
        public SpaceJunkMaterialKind kind;

        [Tooltip("この種類だけを出す範囲（四角。Spawn Zones と同じ作り方）")]
        public Transform[] zones = new Transform[0];
    }

    [Header("種類ごとの出す範囲（STAGE_05）")]
    [Tooltip("ここに入れた種類は、その範囲にだけ出す（入れていない種類は Spawn Zones などのふつうの範囲）。" +
             "「敵陣まで取りに行く」ように、種類ごとに出る場所を分けたいとき")]
    [SerializeField] private KindZone[] kindZones = new KindZone[0];

    private float timer;
    private bool initialDone;

    /// <summary>1巡法で、この巡でまだ出していないゾーン（先頭から使う）。</summary>
    private readonly List<Transform> zoneBag = new List<Transform>();

    private void Awake()
    {
        MigrateLegacy();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 前の書き方のシーンを開いたら、インスペクターでも新しいリストで見えるようにする。
        // 移し替えたら古い項目は空にする（シーンを保存すると、新しい書き方で残る）
        if (MigrateLegacy())
        {
            platePrefab = null;
            circuitPrefab = null;
            fuelPrefab = null;
            extraMaterials = new ExtraMaterial[0];
            bombPrefab = null;
        }
    }
#endif

    /// <summary>
    /// **古い書き方（3種類＋追加の素材＋爆弾）を Spawn List へ移し替える。** Spawn List が空のときだけ。
    /// 爆弾の「確率」は、素材の出やすさの合計から、同じ割合になる出やすさに直す。移し替えたら true。
    /// </summary>
    private bool MigrateLegacy()
    {
        if (spawnList == null)
        {
            spawnList = new List<SpawnEntry>();
        }

        if (spawnList.Count > 0)
        {
            return false;
        }

        float materialTotal = 0f;
        materialTotal += AddLegacy(platePrefab, plateWeight);
        materialTotal += AddLegacy(circuitPrefab, circuitWeight);
        materialTotal += AddLegacy(fuelPrefab, fuelWeight);
        if (extraMaterials != null)
        {
            foreach (ExtraMaterial extra in extraMaterials)
            {
                if (extra != null)
                {
                    materialTotal += AddLegacy(extra.prefab, extra.weight);
                }
            }
        }

        if (bombPrefab != null && bombChance > 0f)
        {
            float chance = Mathf.Clamp(bombChance, 0.01f, 0.95f);
            float weight = Mathf.Round(materialTotal * chance / (1f - chance) * 100f) / 100f;
            spawnList.Add(new SpawnEntry { prefab = bombPrefab, weight = weight });
        }

        return spawnList.Count > 0;
    }

    private float AddLegacy(GameObject prefab, int weight)
    {
        if (prefab == null)
        {
            return 0f;
        }

        spawnList.Add(new SpawnEntry { prefab = prefab, weight = Mathf.Max(0, weight) });
        return Mathf.Max(0, weight);
    }

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
        MigrateLegacy();

        foreach (SpawnEntry entry in spawnList)
        {
            if (entry != null && entry.prefab != null && entry.weight > 0f &&
                entry.prefab.TryGetComponent(out SpaceJunkMaterial material) && material.Kind == kind)
            {
                return true;
            }
        }
        return false;
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

    /// <summary>
    /// このマップで出る素材のプレハブを、出やすさに合わせて1つ選ぶ（爆弾は選ばない）。
    /// 決まった場所に重いデブリを置く <see cref="SpaceJunkBombPoints"/> が使う。
    /// </summary>
    public GameObject ChooseMaterialPrefab()
    {
        return ChoosePrefab(bombsAllowed: false);
    }

    private void TrySpawnOne()
    {
        SpawnOne(ignoreLimit: false);
    }

    private GameObject SpawnOne(bool ignoreLimit)
    {
        // 爆弾は、素材の数とは別に数えて上限までだけ出す（イベント用の追加では出さない）
        bool bombsAllowed = !ignoreLimit && CanSpawnBomb();
        GameObject prefab = ChoosePrefab(bombsAllowed);

        if (prefab == null)
        {
            return null;
        }

        // 素材を選んだが、マップの素材がもう上限なら、この回は出さない
        // （上限のときに爆弾ばかりにならないよう、選び直さない）
        if (!IsBomb(prefab) && !ignoreLimit && CountMaterials() >= maxObjects)
        {
            return null;
        }

        if (!TryFindPlace(ZonesFor(prefab), out Vector3 position))
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
    /// マップにある素材の数。**素材の目印（SpaceJunkMaterial）が付いた物だけ数え、特殊デブリは数えない**
    /// （爆弾・アンカー・<see cref="SpaceJunkBombPoints"/> で置いた特殊デブリで、出せる素材の数が減らないようにする）。
    /// </summary>
    private static int CountMaterials()
    {
        int count = 0;
        foreach (HookableObject item in HookableObject.All)
        {
            if (item != null && !item.IsVanished && item.GetComponent<SpaceJunkMaterial>() != null &&
                item.GetComponent<SpaceJunkBonusDebris>() == null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>爆弾を出してよいか（プレハブが入っていて、マップの爆弾が上限より少ない）。</summary>
    private bool CanSpawnBomb()
    {
        if (maxBombs <= 0)
        {
            return false;
        }

        int count = 0;
        foreach (HookableObject item in HookableObject.All)
        {
            if (item != null && !item.IsVanished && item.GetComponent<ExplosiveObject>() != null)
            {
                count++;
            }
        }
        return count < maxBombs;
    }

    /// <summary>
    /// **出やすさの割合で、Spawn List から1つ選ぶ。**
    /// <paramref name="bombsAllowed"/> が false なら、爆弾は選ばない（マップの爆弾が上限・イベント用の追加）。
    /// </summary>
    private GameObject ChoosePrefab(bool bombsAllowed)
    {
        MigrateLegacy();

        float total = 0f;
        foreach (SpawnEntry entry in spawnList)
        {
            total += Weight(entry, bombsAllowed);
        }

        if (total <= 0f)
        {
            return null;
        }

        float roll = Random.value * total;
        GameObject last = null;

        foreach (SpawnEntry entry in spawnList)
        {
            float weight = Weight(entry, bombsAllowed);
            if (weight <= 0f)
            {
                continue;
            }

            last = entry.prefab;
            roll -= weight;
            if (roll < 0f)
            {
                return entry.prefab;
            }
        }

        return last;
    }

    /// <summary>その1つ分の出やすさ。プレハブが無い・爆弾を選ばない回の爆弾なら 0。</summary>
    private static float Weight(SpawnEntry entry, bool bombsAllowed)
    {
        if (entry == null || entry.prefab == null || entry.weight <= 0f)
        {
            return 0f;
        }

        if (!bombsAllowed && IsBomb(entry.prefab))
        {
            return 0f;
        }

        return entry.weight;
    }

    /// <summary>爆弾（ExplosiveObject 付き）か。</summary>
    private static bool IsBomb(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ExplosiveObject>() != null;
    }

    /// <summary>範囲の中から、何も無い場所を探す。</summary>
    private bool TryFindPlace(Transform[] zones, out Vector3 position)
    {
        // 1巡法：この回に出すゾーンを、1巡の残りから1つ取る
        Transform bagZone = null;
        if (cycleZones && zones == spawnZones && UsableZoneCount(zones) >= 2)
        {
            bagZone = NextZoneFromBag(zones);
        }

        for (int i = 0; i < placementTries; i++)
        {
            Vector3 candidate = bagZone != null ? PointInZone(bagZone) : RandomPointInArea(zones);

            // 一段高い床の上なら、その床の高さを基準にする（Spawn On Raised Floor が ON のときだけ）
            float floorY = transform.position.y;
            if (spawnOnRaisedFloor && TryFindRaisedFloor(candidate, out float raisedY) && raisedY > floorY)
            {
                floorY = raisedY;
                candidate.y = Mathf.Max(candidate.y, floorY + dropHeight);
            }

            // 落とす高さから床の少し上まで、縦に長く調べる（プレイヤーや障害物の真上を避ける）
            Vector3 bottom = new Vector3(candidate.x, floorY + clearRadius + 0.1f, candidate.z);
            bool blocked = Physics.CheckCapsule(bottom, candidate, clearRadius, ~0, QueryTriggerInteraction.Ignore);

            if (!blocked && HasGroundBelow(candidate))
            {
                position = candidate;
                return true;
            }
        }

        // 空いた場所が無かった。1巡法なら、このゾーンを次の回に回す（1巡の中で抜けが出ないように）
        if (bagZone != null)
        {
            zoneBag.Insert(0, bagZone);
        }

        position = Vector3.zero;
        return false;
    }

    // ------------------------------------------------------------
    // 1巡法（Cycle Zones）
    // ------------------------------------------------------------

    /// <summary>使えるゾーン（入っていて、広さがある）の数。</summary>
    private static int UsableZoneCount(Transform[] zones)
    {
        int count = 0;
        foreach (Transform zone in zones)
        {
            if (ZoneArea(zone) > 0f)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// **1巡の残りから、次のゾーンを1つ取る。** 残りが無くなったら、全ゾーンを入れ直してシャッフルする
    /// （テトリスの7種1巡と同じ。1巡の中では、どのゾーンにも1回ずつ出る）。
    /// </summary>
    private Transform NextZoneFromBag(Transform[] zones)
    {
        // ゾーンが消された・入れ替えられたときに、古いものを使わない
        zoneBag.RemoveAll(zone => ZoneArea(zone) <= 0f || System.Array.IndexOf(zones, zone) < 0);

        if (zoneBag.Count == 0)
        {
            foreach (Transform zone in zones)
            {
                if (ZoneArea(zone) > 0f)
                {
                    zoneBag.Add(zone);
                }
            }

            // 並びをシャッフル（フィッシャー–イェーツ）
            for (int i = zoneBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (zoneBag[i], zoneBag[j]) = (zoneBag[j], zoneBag[i]);
            }
        }

        Transform next = zoneBag[0];
        zoneBag.RemoveAt(0);
        return next;
    }

    /// <summary>ゾーン（四角）の中のランダムな1点（高さは「床＋Drop Height」）。</summary>
    private Vector3 PointInZone(Transform zone)
    {
        Vector3 local = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
        Vector3 point = zone.TransformPoint(local);
        point.y = transform.position.y + dropHeight;
        return point;
    }

    /// <summary>
    /// 真下にある床（地形）の上面の高さ。プレイヤーや、物理で動く物（物資・爆弾）は床とみなさず、通り抜けて下を見る。
    /// いくつも当たったときは、一番高いもの。
    /// </summary>
    private static bool TryFindRaisedFloor(Vector3 candidate, out float floorY)
    {
        floorY = float.MinValue;
        bool found = false;

        foreach (RaycastHit hit in Physics.RaycastAll(candidate, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider is CharacterController ||
                (hit.rigidbody != null && !hit.rigidbody.isKinematic))
            {
                continue;
            }

            if (hit.point.y > floorY)
            {
                floorY = hit.point.y;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 真下に床があるか（Require Ground Below が OFF なら常に true）。
    /// 出す場所の周りは空いていると確かめてあるので、真下に最初に当たる物が床になる。
    /// </summary>
    private bool HasGroundBelow(Vector3 candidate)
    {
        if (!requireGroundBelow)
        {
            return true;
        }

        return Physics.Raycast(candidate, Vector3.down, dropHeight + 2f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// このプレハブを出す範囲。Kind Zones にその種類があればその範囲、無ければ Spawn Zones。
    /// </summary>
    private Transform[] ZonesFor(GameObject prefab)
    {
        if (prefab != null && prefab.TryGetComponent(out SpaceJunkMaterial material))
        {
            foreach (KindZone kindZone in kindZones)
            {
                if (kindZone != null && kindZone.kind == material.Kind && kindZone.zones != null &&
                    kindZone.zones.Length > 0)
                {
                    return kindZone.zones;
                }
            }
        }

        return spawnZones;
    }

    /// <summary>
    /// 出す候補の場所を1つ選ぶ（高さは「床＋Drop Height」）。
    /// 範囲（<paramref name="zones"/>）があれば、**その四角のどれか**（広いものほど選ばれやすい）の中。
    /// 無ければ、Spawn Circle Radius の円か、Area Half Size の四角の中。
    /// </summary>
    private Vector3 RandomPointInArea(Transform[] zones)
    {
        float totalArea = 0f;
        foreach (Transform zone in zones)
        {
            totalArea += ZoneArea(zone);
        }

        if (totalArea <= 0f && spawnCircleRadius > 0f)
        {
            // 円の中で、どこも同じくらいの出やすさになるように選ぶ
            Vector2 inCircle = Random.insideUnitCircle * spawnCircleRadius;
            return transform.position + new Vector3(inCircle.x, dropHeight, inCircle.y);
        }

        if (totalArea <= 0f)
        {
            return transform.position + new Vector3(
                Random.Range(-areaHalfSize.x, areaHalfSize.x),
                dropHeight,
                Random.Range(-areaHalfSize.y, areaHalfSize.y));
        }

        float roll = Random.Range(0f, totalArea);
        foreach (Transform zone in zones)
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

        // 種類ごとの範囲は、その素材の色の枠で出す
        foreach (KindZone kindZone in kindZones)
        {
            if (kindZone == null || kindZone.zones == null)
            {
                continue;
            }

            Gizmos.color = SpaceJunkMaterials.Color(kindZone.kind);
            foreach (Transform zone in kindZone.zones)
            {
                if (zone == null)
                {
                    continue;
                }

                hasZone = true;
                Gizmos.matrix = zone.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);

        if (!hasZone && spawnCircleRadius > 0f)
        {
            // 円を線でつないで描く
            Vector3 center = transform.position + Vector3.up * dropHeight;
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(
                    center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * spawnCircleRadius,
                    center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * spawnCircleRadius);
            }
        }
        else if (!hasZone)
        {
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * dropHeight,
                new Vector3(areaHalfSize.x * 2f, 0.1f, areaHalfSize.y * 2f));
        }
    }
}
