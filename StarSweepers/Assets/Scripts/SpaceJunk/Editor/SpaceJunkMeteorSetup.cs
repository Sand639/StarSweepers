using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>隕石攻撃のプレハブ生成と、選択した床への設置を行うEditorメニュー。</summary>
public static class SpaceJunkMeteorSetup
{
    public const string Stage03ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_03.unity";
    public const string PrefabFolder = "Assets/Prefabs/SpaceJunk/Gimmick/Meteor";
    public const string ManagerPrefabPath = PrefabFolder + "/MeteorManager.prefab";
    public const string AreaPrefabPath = PrefabFolder + "/MeteorArea.prefab";
    public const string StrikePrefabPath = PrefabFolder + "/MeteorStrikeEvent.prefab";
    public const string MeteorPrefabPath = PrefabFolder + "/Meteor.prefab";
    public const string WarningPrefabPath = PrefabFolder + "/MeteorWarning.prefab";
    public const string ImpactVfxPrefabPath = PrefabFolder + "/MeteorImpactVfx.prefab";

    private const string MeteorMaterialPath = PrefabFolder + "/Meteor.mat";
    private const string WarningMaterialPath = PrefabFolder + "/MeteorWarning.mat";
    private const string ImpactMaterialPath = PrefabFolder + "/MeteorImpact.mat";

    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Create Prefabs")]
    public static void CreateMeteorPrefabsCommand()
    {
        CreateMeteorPrefabs();
        Debug.Log($"[METEOR] 隕石攻撃のプレハブを作りました：{PrefabFolder}");
    }

    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Add To Current Scene")]
    public static void AddToCurrentScene()
    {
        CreateMeteorPrefabs();

        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        GameObject areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        MeteorManager manager = Object.FindFirstObjectByType<MeteorManager>();

        if (manager == null)
        {
            GameObject managerObject = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab);
            Undo.RegisterCreatedObjectUndo(managerObject, "Add Meteor Manager");
            manager = managerObject.GetComponent<MeteorManager>();
        }

        List<MeteorArea> areas = ReadAreas(manager);
        int added = 0;

        foreach (GameObject plate in Selection.gameObjects)
        {
            if (plate == null || !plate.scene.IsValid() || plate.GetComponent<MeteorManager>() != null)
            {
                continue;
            }

            MeteorArea area = FindDirectAreaChild(plate.transform);
            if (area == null)
            {
                GameObject areaObject = (GameObject)PrefabUtility.InstantiatePrefab(areaPrefab, plate.transform);
                Undo.RegisterCreatedObjectUndo(areaObject, "Add Meteor Area");
                areaObject.name = $"{plate.name}_MeteorArea";
                areaObject.transform.localPosition = Vector3.zero;
                areaObject.transform.localRotation = Quaternion.identity;
                areaObject.transform.localScale = Vector3.one;
                area = areaObject.GetComponent<MeteorArea>();
                CopyPlateBounds(plate, areaObject.GetComponent<BoxCollider>());
                added++;
            }

            if (!areas.Contains(area))
            {
                areas.Add(area);
            }
        }

        SetAreas(manager, areas);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        Selection.activeGameObject = manager.gameObject;

        Debug.Log($"[METEOR] MeteorManagerを場面に用意し、選択した床へMeteorAreaを{added}個追加しました。" +
                  "床を選んでいなかった場合は、Area Prefabを手で置いてManagerのActive Areasへ登録してください。");
    }

    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Install Into STAGE_03")]
    public static void InstallIntoStage03()
    {
        if (!Application.isBatchMode &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        CreateMeteorPrefabs();

        Scene scene = EditorSceneManager.OpenScene(Stage03ScenePath, OpenSceneMode.Single);
        GameObject ground = FindSceneObject(scene, "Ground");
        if (ground == null)
        {
            throw new System.InvalidOperationException("[METEOR] STAGE_03にGroundが見つかりません。");
        }

        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        GameObject areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        MeteorManager manager = FindSceneComponent<MeteorManager>(scene);
        if (manager == null)
        {
            GameObject managerObject = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, scene);
            managerObject.name = "MeteorManager";
            manager = managerObject.GetComponent<MeteorManager>();
        }

        Transform areaRoot = ground.transform.Find("MeteorAreas");
        if (areaRoot == null)
        {
            GameObject rootObject = new GameObject("MeteorAreas");
            rootObject.transform.SetParent(ground.transform, false);
            areaRoot = rootObject.transform;
        }

        string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        List<MeteorArea> areas = new List<MeteorArea>();
        for (int i = 0; i < directions.Length; i++)
        {
            string areaName = $"MeteorArea_{i:00}_{directions[i]}";
            Transform existing = areaRoot.Find(areaName);
            GameObject areaObject;
            if (existing != null)
            {
                areaObject = existing.gameObject;
            }
            else
            {
                areaObject = (GameObject)PrefabUtility.InstantiatePrefab(areaPrefab, areaRoot);
                areaObject.name = areaName;
            }

            float angle = i * 45f;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
            areaObject.transform.localPosition = rotation * (Vector3.forward * 12f);
            areaObject.transform.localRotation = rotation;
            areaObject.transform.localScale = Vector3.one;

            BoxCollider box = areaObject.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.1f, 0f);
            box.size = new Vector3(6.5f, 0.2f, 4.5f);
            box.isTrigger = true;

            MeteorArea area = areaObject.GetComponent<MeteorArea>();
            areas.Add(area);
            EditorUtility.SetDirty(areaObject);
        }

        SerializedObject managerSerialized = new SerializedObject(manager);
        managerSerialized.FindProperty("intervalSeconds").floatValue = 4f;
        managerSerialized.FindProperty("meteorsPerRound").intValue = 3;
        managerSerialized.FindProperty("warningSeconds").floatValue = 4f;
        managerSerialized.FindProperty("spawnAtAreaCenter").boolValue = true;
        managerSerialized.FindProperty("impactRadius").floatValue = 5f;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();
        SetAreas(manager, areas);

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(areaRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, Stage03ScenePath))
        {
            throw new System.InvalidOperationException("[METEOR] STAGE_03を保存できませんでした。");
        }

        Debug.Log("[METEOR] STAGE_03へ半径5m・1ラウンド3個の隕石Areaを8か所設置しました。");
    }

    /// <summary>既存の調整済みPrefabは上書きせず、不足分だけ作る。</summary>
    public static void CreateMeteorPrefabs()
    {
        FishingSceneBuilder.EnsureFolder(PrefabFolder);

        GameObject meteor = CreateMeteorVisual();
        GameObject warning = CreateWarningVisual();
        GameObject impactVfx = CreateImpactVfx();
        GameObject strike = CreateStrikePrefab(meteor, warning, impactVfx);
        CreateAreaPrefab();
        CreateManagerPrefab(strike);

        SpaceJunkPrefabBuilder.RegisterNetworkPrefab(strike);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject CreateMeteorVisual()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MeteorPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            MeteorMaterialPath,
            new Color(0.16f, 0.12f, 0.1f, 1f),
            "Universal Render Pipeline/Lit");

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "Meteor";
        root.transform.localScale = Vector3.one * 2.5f;
        root.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(root.GetComponent<BoxCollider>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MeteorPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject CreateWarningVisual()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(WarningPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            WarningMaterialPath,
            new Color(1f, 0.08f, 0.02f, 1f),
            "Universal Render Pipeline/Unlit");

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        root.name = "MeteorWarning";
        root.transform.localScale = new Vector3(1f, 0.02f, 1f);
        root.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WarningPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject CreateImpactVfx()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactVfxPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            ImpactMaterialPath,
            new Color(1f, 0.32f, 0.04f, 1f),
            "Universal Render Pipeline/Particles/Unlit");

        GameObject root = new GameObject("MeteorImpactVfx");
        ParticleSystem particles = root.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.75f, 0.12f, 1f),
            new Color(1f, 0.08f, 0.02f, 1f));
        main.maxParticles = 100;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.5f;

        ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ImpactVfxPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject CreateStrikePrefab(
        GameObject meteor,
        GameObject warning,
        GameObject impactVfx)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StrikePrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorStrikeEvent");
        root.AddComponent<NetworkObject>();
        MeteorStrikeEvent strike = root.AddComponent<MeteorStrikeEvent>();
        FishingSceneBuilder.SetRef(strike, "meteorPrefab", meteor);
        FishingSceneBuilder.SetRef(strike, "warningPrefab", warning);
        FishingSceneBuilder.SetRef(strike, "impactVfxPrefab", impactVfx);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StrikePrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject CreateAreaPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorArea");
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(10f, 0.2f, 10f);
        box.isTrigger = true;
        root.AddComponent<MeteorArea>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, AreaPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject CreateManagerPrefab(GameObject strikePrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorManager");
        MeteorManager manager = root.AddComponent<MeteorManager>();
        FishingSceneBuilder.SetRef(manager, "strikePrefab", strikePrefab.GetComponent<MeteorStrikeEvent>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static Material GetOrCreateMaterial(string path, Color color, string shaderName)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(shaderName)
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static MeteorArea FindDirectAreaChild(Transform plate)
    {
        for (int i = 0; i < plate.childCount; i++)
        {
            MeteorArea area = plate.GetChild(i).GetComponent<MeteorArea>();
            if (area != null)
            {
                return area;
            }
        }
        return null;
    }

    private static void CopyPlateBounds(GameObject plate, BoxCollider target)
    {
        BoxCollider sourceBox = plate.GetComponent<BoxCollider>();
        if (sourceBox != null)
        {
            target.center = sourceBox.center;
            target.size = sourceBox.size;
            target.isTrigger = true;
            return;
        }

        MeshFilter mesh = plate.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null)
        {
            target.center = mesh.sharedMesh.bounds.center;
            target.size = mesh.sharedMesh.bounds.size;
            target.isTrigger = true;
        }
    }

    private static List<MeteorArea> ReadAreas(MeteorManager manager)
    {
        List<MeteorArea> result = new List<MeteorArea>();
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("activeAreas");
        for (int i = 0; i < property.arraySize; i++)
        {
            MeteorArea area = property.GetArrayElementAtIndex(i).objectReferenceValue as MeteorArea;
            if (area != null && !result.Contains(area))
            {
                result.Add(area);
            }
        }
        return result;
    }

    private static void SetAreas(MeteorManager manager, List<MeteorArea> areas)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("activeAreas");
        property.arraySize = areas.Count;
        for (int i = 0; i < areas.Count; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = areas[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindInChildren(root.transform, objectName);
            if (found != null)
            {
                return found.gameObject;
            }
        }
        return null;
    }

    private static Transform FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindInChildren(parent.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }
        return null;
    }
}
