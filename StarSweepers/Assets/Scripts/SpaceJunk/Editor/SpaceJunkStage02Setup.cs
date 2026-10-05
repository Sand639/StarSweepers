using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **STAGE_02（真ん中を列車が通るステージ）の仕掛けを、開いているシーンに置くメニュー。**
///
/// 1. 爆弾の置き場所4つ（<see cref="SpaceJunkBombPoints"/>）を、左右の床の手前と奥に置く。動かされたら3秒後に出し直す
/// 2. 真ん中に**得点の高い特殊デブリ**の置き場所を置く。消えたら3秒後に真ん中へ出し直す
/// 3. 素材のスポナーを設定する
///    - **左右の床の緑の範囲（SpawnZone_West / East）の中にだけ**素材を出す
///    - **最大10個**
///    - **素材は5種類**（4・5種類目のアンテナ・エンジンを足す）
///
/// 足りないプレハブ（アンテナ・エンジン・特殊デブリ）は、装甲板のプレハブを複製して作る。
/// 何度実行してもよい（置き場所・範囲は作り直す。**手で動かした位置は初期位置に戻る**ので注意）。
/// </summary>
public static class SpaceJunkStage02Setup
{
    private const string BombPrefabPath = "Assets/Prefabs/Fish/Online/Gimmick/FishingOnlineBomb.prefab";
    private const string AntennaPrefabPath = SpaceJunkPrefabBuilder.OnlinePrefabFolder + "/SpaceJunkAntenna.prefab";
    private const string EnginePrefabPath = SpaceJunkPrefabBuilder.OnlinePrefabFolder + "/SpaceJunkEngine.prefab";
    private const string SpecialPrefabPath = SpaceJunkPrefabBuilder.OnlinePrefabFolder + "/SpaceJunkSpecial.prefab";

    private const string BombPointsName = "BombPoints";
    private const string SpecialPointName = "SpecialDebrisPoint";
    private const string SpawnZonesName = "SpawnZones";

    /// <summary>素材の最大数（企画メモ：ステージ中最大10個）。</summary>
    private const int MaxDebris = 10;

    /// <summary>特殊デブリの大きさ（装甲板の何倍か）。</summary>
    private const float SpecialScale = 1.8f;

    /// <summary>爆弾の置き場所（左右の床の手前と奥）。床の上面は高さ0。</summary>
    private static readonly Vector3[] BombPositions =
    {
        new Vector3(-11f, 0.6f, 10f),
        new Vector3(-11f, 0.6f, -10f),
        new Vector3(11f, 0.6f, 10f),
        new Vector3(11f, 0.6f, -10f),
    };

    /// <summary>
    /// 素材を出す範囲（緑の範囲）。左右の床（幅8m × 長さ30m）の、ふちを少し残した内側。
    /// 位置と、大きさ（X が横幅・Z が長さ）。
    /// </summary>
    private static readonly (string name, Vector3 position, Vector3 size)[] Zones =
    {
        ("SpawnZone_West", new Vector3(-11f, 0f, 0f), new Vector3(6f, 1f, 27f)),
        ("SpawnZone_East", new Vector3(11f, 0f, 0f), new Vector3(6f, 1f, 27f)),
    };

    [MenuItem("Tools/StarSweepers/開いているマップに STAGE_02 の爆弾と素材の出る地面を設定する")]
    private static void Setup()
    {
        GameObject antenna = EnsureMaterialPrefab(AntennaPrefabPath, SpaceJunkMaterialKind.Antenna);
        GameObject engine = EnsureMaterialPrefab(EnginePrefabPath, SpaceJunkMaterialKind.Engine);
        GameObject special = EnsureSpecialPrefab();
        AssetDatabase.SaveAssets();

        GameObject bomb = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        if (bomb == null)
        {
            Debug.LogError($"[JUNK] 爆弾のプレハブが見つかりません：{BombPrefabPath}。BombPoints の Spawn Prefab に手で入れてください。");
        }

        PlacePoints(BombPointsName, BombPositions, bomb, respawnWhenMoved: true);
        PlacePoints(SpecialPointName, new[] { new Vector3(0f, 0.6f, 0f) }, special, respawnWhenMoved: false);
        SetupSpawner(antenna, engine);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[JUNK] STAGE_02 の設定が終わりました（爆弾4つ・真ん中の特殊デブリ・緑の範囲・最大10個・素材5種類）。");
    }

    // ------------------------------------------------------------
    // 列車と壁
    // ------------------------------------------------------------

    private const string TrainName = "Train";

    /// <summary>外す壁（ゴールの囲い Pocket_* / Pad_* / GoalArea_* は残す）。「親/子」で指定。</summary>
    private static readonly string[] WallsToRemove =
    {
        "Stage/Wall_North",
        "Stage/Wall_South",
        "Stage/Wall_East/Segment_L",
        "Stage/Wall_East/Segment_R",
        "Stage/Wall_West/Segment_L",
        "Stage/Wall_West/Segment_R",
    };

    /// <summary>
    /// **列車を置き、ゴールの囲い以外の壁を外す**（落ちるステージにする）。
    /// 壁は消さずに**非表示（無効）**にするだけなので、Hierarchy でチェックを入れれば戻せる。
    /// 何度実行してもよい（列車は作り直す）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/開いているマップに STAGE_02 の列車を置き、ゴール以外の壁を外す")]
    private static void SetupTrainAndWalls()
    {
        PlaceTrain();
        RemoveWalls();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[JUNK] 列車を置き、ゴール以外の壁を外しました。");
    }

    private static void PlaceTrain()
    {
        GameObject old = GameObject.Find(TrainName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
        }

        GameObject train = new GameObject(TrainName);
        Undo.RegisterCreatedObjectUndo(train, "列車を置く");
        train.transform.position = new Vector3(0f, 1.25f, 45f);

        // 見た目だけの箱。当たり判定は SpaceJunkTrain が自分で調べるので、箱の当たり判定は外す
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(train.transform, false);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            $"{FishingSceneBuilder.MaterialFolder}/SpaceJunkTrain.mat", new Color(0.12f, 0.12f, 0.14f));
        body.GetComponent<MeshRenderer>().sharedMaterial = material;

        SpaceJunkTrain component = train.AddComponent<SpaceJunkTrain>();
        SerializedObject serialized = new SerializedObject(component);
        serialized.FindProperty("body").objectReferenceValue = body.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        body.transform.localScale = serialized.FindProperty("trainSize").vector3Value;
    }

    private static void RemoveWalls()
    {
        foreach (string path in WallsToRemove)
        {
            Transform wall = FindByPath(path);
            if (wall == null)
            {
                Debug.LogWarning($"[JUNK] 壁が見つかりません（もう外したか、名前が違う）：{path}");
                continue;
            }

            Undo.RecordObject(wall.gameObject, "壁を外す");
            wall.gameObject.SetActive(false);
        }
    }

    /// <summary>「親/子/孫」の形で、無効になっている物も含めて探す。</summary>
    private static Transform FindByPath(string path)
    {
        string[] names = path.Split('/');
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != names[0])
            {
                continue;
            }

            Transform current = root.transform;
            for (int i = 1; i < names.Length && current != null; i++)
            {
                current = current.Find(names[i]);
            }

            if (current != null)
            {
                return current;
            }
        }
        return null;
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// 素材のプレハブが無ければ、**装甲板のプレハブを複製して、種類と色だけ変えて**作る。
    /// Unity の中で複製するので、通信で使う番号は新しく付く。
    /// </summary>
    private static GameObject EnsureMaterialPrefab(string path, SpaceJunkMaterialKind kind)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            SpaceJunkPrefabBuilder.RegisterNetworkPrefab(existing);
            return existing;
        }

        if (!AssetDatabase.CopyAsset(SpaceJunkPrefabBuilder.PlatePrefabPath, path))
        {
            Debug.LogError($"[JUNK] {SpaceJunkPrefabBuilder.PlatePrefabPath} を複製できませんでした。");
            return null;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        contents.name = $"SpaceJunk{kind}";

        SpaceJunkMaterial marker = contents.GetComponent<SpaceJunkMaterial>();
        if (marker != null)
        {
            FishingSceneBuilder.SetInt(marker, "kind", (int)kind);
        }

        // エディタで見ても色が分かるように、種類の色のマテリアルを貼る（再生中は SpaceJunkMaterial も色を塗る）
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            $"{FishingSceneBuilder.MaterialFolder}/SpaceJunk{kind}.mat",
            SpaceJunkMaterials.Color(kind));
        foreach (MeshRenderer renderer in contents.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial = material;
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);

        SpaceJunkPrefabBuilder.RegisterNetworkPrefab(saved);
        Debug.Log($"[JUNK] {SpaceJunkMaterials.Name(kind)}（{SpaceJunkMaterials.ColorName(kind)}）のプレハブを作りました：{path}");
        return saved;
    }

    /// <summary>
    /// 特殊デブリのプレハブが無ければ、**装甲板のプレハブを複製して、大きくし、<see cref="SpaceJunkBonusDebris"/> を付けて**作る。
    /// </summary>
    private static GameObject EnsureSpecialPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SpecialPrefabPath);
        if (existing != null)
        {
            SpaceJunkPrefabBuilder.RegisterNetworkPrefab(existing);
            return existing;
        }

        if (!AssetDatabase.CopyAsset(SpaceJunkPrefabBuilder.PlatePrefabPath, SpecialPrefabPath))
        {
            Debug.LogError($"[JUNK] {SpaceJunkPrefabBuilder.PlatePrefabPath} を複製できませんでした。");
            return null;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(SpecialPrefabPath);
        contents.name = "SpaceJunkSpecial";
        contents.transform.localScale *= SpecialScale;

        if (contents.GetComponent<SpaceJunkBonusDebris>() == null)
        {
            contents.AddComponent<SpaceJunkBonusDebris>();
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, SpecialPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);

        SpaceJunkPrefabBuilder.RegisterNetworkPrefab(saved);
        Debug.Log($"[JUNK] 特殊デブリのプレハブを作りました：{SpecialPrefabPath}");
        return saved;
    }

    // ------------------------------------------------------------
    // シーン
    // ------------------------------------------------------------

    /// <summary>置き場所（<see cref="SpaceJunkBombPoints"/>）を作り直す。子1つが1か所。</summary>
    private static void PlacePoints(string rootName, Vector3[] positions, GameObject prefab, bool respawnWhenMoved)
    {
        GameObject old = GameObject.Find(rootName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
        }

        GameObject root = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(root, "置き場所を置く");

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject point = new GameObject($"{rootName}_{i + 1}");
            point.transform.SetParent(root.transform, false);
            point.transform.position = positions[i];
        }

        SpaceJunkBombPoints points = root.AddComponent<SpaceJunkBombPoints>();
        SerializedObject serialized = new SerializedObject(points);
        serialized.FindProperty("spawnPrefab").objectReferenceValue = prefab;
        serialized.FindProperty("respawnWhenMoved").boolValue = respawnWhenMoved;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>素材のスポナーに、緑の範囲・最大数・追加の素材を設定する。</summary>
    private static void SetupSpawner(GameObject antenna, GameObject engine)
    {
        SpaceJunkSpawner spawner = Object.FindFirstObjectByType<SpaceJunkSpawner>();
        if (spawner == null)
        {
            Debug.LogError("[JUNK] SpaceJunkSpawner が見つかりません。");
            return;
        }

        // 緑の範囲を作り直す
        GameObject old = GameObject.Find(SpawnZonesName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
        }

        GameObject root = new GameObject(SpawnZonesName);
        Undo.RegisterCreatedObjectUndo(root, "素材を出す範囲を置く");

        Transform[] zones = new Transform[Zones.Length];
        for (int i = 0; i < Zones.Length; i++)
        {
            GameObject zone = new GameObject(Zones[i].name);
            zone.transform.SetParent(root.transform, false);
            zone.transform.position = Zones[i].position;
            zone.transform.localScale = Zones[i].size;
            zones[i] = zone.transform;
        }

        SerializedObject serialized = new SerializedObject(spawner);

        SerializedProperty zoneList = serialized.FindProperty("spawnZones");
        zoneList.arraySize = zones.Length;
        for (int i = 0; i < zones.Length; i++)
        {
            zoneList.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
        }

        serialized.FindProperty("maxObjects").intValue = MaxDebris;

        GameObject[] extras = { antenna, engine };
        SerializedProperty extraList = serialized.FindProperty("extraMaterials");
        extraList.arraySize = extras.Length;
        for (int i = 0; i < extras.Length; i++)
        {
            SerializedProperty entry = extraList.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("prefab").objectReferenceValue = extras[i];
            entry.FindPropertyRelative("weight").intValue = 1;
        }

        serialized.ApplyModifiedProperties();
    }
}
