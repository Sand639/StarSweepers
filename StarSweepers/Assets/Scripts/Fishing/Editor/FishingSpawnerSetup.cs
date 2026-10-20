using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **物資と爆発物のスポナーを、プレハブとシーンに組み込むツール。**
///
/// Unityのメニュー「Tools > StarSweepers > 釣りのスポナーを作る（ステージ検証＋オンライン会場）」から実行する。
///
/// やること：
///   1. スポナーが出す物のプレハブを作る（すでにあれば作り直す）
///      ・`Assets/Prefabs/Fish/FishingSupply.prefab` / `Fish/Offline/FishingBomb.prefab` … 1人用
///      ・`Assets/Prefabs/Fish/Online/Gimmick/FishingOnlineSupply.prefab` / `FishingOnlineBomb.prefab` … オンライン用
///   2. オンライン用の2つを `Assets/DefaultNetworkPrefabs.asset` に登録する（しないと全員の画面に出せない）
///   3. `FishingArenaTest.unity` と `FishingOnline.unity` から、**最初から置いてあった物資と爆発物を取り除き**、
///      代わりにスポナーを1つ置く
///
/// **シーンを作り直すのではなく、今あるシーンに手を入れる**ので、
/// あとから手で足した設定（明かりなど）は消えない。何度実行してもスポナーは1つのまま。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingSpawnerSetup
{
    public const string SupplyPrefabPath = FishingSceneBuilder.FishPrefabFolder + "/FishingSupply.prefab";
    public const string BombPrefabPath = FishingSceneBuilder.FishOfflinePrefabFolder + "/FishingBomb.prefab";
    public const string OnlineSupplyPrefabPath = FishingSceneBuilder.FishOnlineGimmickFolder + "/FishingOnlineSupply.prefab";
    public const string OnlineBombPrefabPath = FishingSceneBuilder.FishOnlineGimmickFolder + "/FishingOnlineBomb.prefab";

    // ※ FishingSceneBuilder が爆発のプレハブを作る場所と同じ（あちらは private なので、ここにも書いてある）
    private const string ExplosionPrefabPath = FishingSceneBuilder.FishPrefabFolder + "/FishingExplosion.prefab";

    /// <summary>オンラインで出せるプレハブの一覧。ロビーの NetworkManager がこれを使う。</summary>
    private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

    // ※ 手を入れる2つのシーン。FishingArenaTestSetup・FishingOnlineSetup が作る場所と同じ
    private const string ArenaScenePath = FishingSceneBuilder.SceneFolder + "/FishingArenaTest.unity";
    private const string OnlineScenePath = FishingSceneBuilder.SceneFolder + "/FishingOnline.unity";

    private const string SpawnerName = "FishingObjectSpawner";

    [MenuItem("Tools/StarSweepers/釣りのスポナーを作る（ステージ検証＋オンライン会場）")]
    public static void SetupAll()
    {
        // 開いているシーンに保存していない変更があれば、保存するか尋ねる（「キャンセル」なら何もしない）
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        FishingSceneBuilder.EnsureFolders();

        // プレハブ作りは作業用オブジェクトを一時的に置くので、空のシーンで行う
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ExplosionEffect explosion = LoadOrCreateExplosionPrefab();

        GameObject supply = CreatePrefabs(false, explosion, out GameObject bomb);
        GameObject onlineSupply = CreatePrefabs(true, explosion, out GameObject onlineBomb);

        RegisterNetworkPrefab(onlineSupply);
        RegisterNetworkPrefab(onlineBomb);

        ApplyToScene(ArenaScenePath, supply, bomb);
        ApplyToScene(OnlineScenePath, onlineSupply, onlineBomb);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【スポナー】物資と爆発物のスポナーを組み込みました。\n" +
            "プレハブ: " + SupplyPrefabPath + " / " + BombPrefabPath + "\n" +
            "　　　　　" + OnlineSupplyPrefabPath + " / " + OnlineBombPrefabPath + "\n" +
            "シーン　: " + ArenaScenePath + " / " + OnlineScenePath + "\n" +
            "・最初から置いてあった物資と爆発物は取り除き、スポナーが出す形にしました\n" +
            "・割合や上限は、シーンの FishingObjectSpawner を選んで Inspector で変えられます");
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>爆発の見た目のプレハブを読み込む。**まだ無いときだけ作る**（あるものは作り直さない）。</summary>
    private static ExplosionEffect LoadOrCreateExplosionPrefab()
    {
        ExplosionEffect existing = AssetDatabase.LoadAssetAtPath<ExplosionEffect>(ExplosionPrefabPath);
        return existing != null ? existing : FishingSceneBuilder.CreateExplosionPrefab();
    }

    /// <summary>
    /// プレハブを作り（オンライン用なら登録もして）、今開いているシーンにスポナーを置く。
    /// **シーンを作るツール（ステージ検証・オンライン会場）から呼ばれる。**
    /// </summary>
    public static FishingObjectSpawner CreatePrefabsAndSpawner(bool online, ExplosionEffect explosion)
    {
        GameObject supply = CreatePrefabs(online, explosion, out GameObject bomb);

        if (online)
        {
            RegisterNetworkPrefab(supply);
            RegisterNetworkPrefab(bomb);
        }

        return CreateSpawner(supply, bomb);
    }

    /// <summary>物資と爆発物のプレハブを作って保存する。戻り値は物資、<paramref name="bombPrefab"/> に爆発物。</summary>
    private static GameObject CreatePrefabs(bool online, ExplosionEffect explosion, out GameObject bombPrefab)
    {
        // 今開いているシーンに作業用の物を原点に一時的に置き、プレハブとして保存したらすぐ消す
        GameObject supplyObject;
        GameObject bombObject;

        if (online)
        {
            supplyObject = FishingNetSceneBuilder.CreateNetworkSupply("FishingOnlineSupply", Vector3.zero);
            bombObject = FishingNetSceneBuilder.CreateNetworkBomb("FishingOnlineBomb", Vector3.zero, explosion);
        }
        else
        {
            supplyObject = FishingSceneBuilder.CreateSupply("FishingSupply", Vector3.zero).gameObject;
            bombObject = FishingSceneBuilder.CreateBomb("FishingBomb", Vector3.zero, explosion);
        }

        GameObject supplyPrefab = PrefabUtility.SaveAsPrefabAsset(
            supplyObject, online ? OnlineSupplyPrefabPath : SupplyPrefabPath);
        bombPrefab = PrefabUtility.SaveAsPrefabAsset(
            bombObject, online ? OnlineBombPrefabPath : BombPrefabPath);

        Object.DestroyImmediate(supplyObject);
        Object.DestroyImmediate(bombObject);

        return supplyPrefab;
    }

    /// <summary>
    /// オンライン用のプレハブを、ロビーの NetworkManager が使っている一覧に登録する。
    /// **登録していないプレハブは、ホストが出しても参加者の画面に出せない。**
    /// </summary>
    private static void RegisterNetworkPrefab(GameObject prefab)
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
        if (list == null)
        {
            Debug.LogWarning($"[FISH] {NetworkPrefabsListPath} が見つかりません。" +
                             $"{prefab.name} を NetworkManager の Network Prefabs に手で登録してください。");
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }
    }

    // ------------------------------------------------------------
    // シーン
    // ------------------------------------------------------------

    /// <summary>シーンを開き、置いてあった物資を取り除いてスポナーを置き、保存する。</summary>
    private static void ApplyToScene(string scenePath, GameObject supplyPrefab, GameObject bombPrefab)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            Debug.LogWarning($"[FISH] {scenePath} がありません。先にシーンを作るツールを実行してください。");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        RemovePlacedObjects(scene);
        CreateSpawner(supplyPrefab, bombPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// 最初から置いてあった物資・爆発物と、前に置いたスポナーを消す。
    ///
    /// 見分け方は「**シーンの一番上にあり**、HookableObject（引っ掛けられる物）か FishingObjectSpawner が付いている物」。
    /// 何かの子にした物は消さない。逆に、物資でなくても HookableObject が付いた物（アンカーなど）を一番上に置いていると、それも消える。
    /// </summary>
    private static void RemovePlacedObjects(Scene scene)
    {
        List<GameObject> toRemove = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<HookableObject>() != null || root.GetComponent<FishingObjectSpawner>() != null)
            {
                toRemove.Add(root);
            }
        }

        foreach (GameObject target in toRemove)
        {
            Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// 今開いているシーンにスポナーを1つ置く。
    /// シーンを作るツール（ステージ検証・オンライン会場）からも呼ばれる。
    /// </summary>
    public static FishingObjectSpawner CreateSpawner(GameObject supplyPrefab, GameObject bombPrefab)
    {
        // スポナーの位置が、物資が降る範囲の中心になる（範囲の広さは Area Half Size。初期値のまま）
        GameObject spawnerObject = new GameObject(SpawnerName);
        spawnerObject.transform.position = Vector3.zero;

        // ⚠ "supplyPrefab"・"bombPrefab" は FishingObjectSpawner のフィールド名（向こうを改名すると静かに壊れる）
        FishingObjectSpawner spawner = spawnerObject.AddComponent<FishingObjectSpawner>();
        FishingSceneBuilder.SetRef(spawner, "supplyPrefab", supplyPrefab);
        FishingSceneBuilder.SetRef(spawner, "bombPrefab", bombPrefab);

        return spawner;
    }
}
