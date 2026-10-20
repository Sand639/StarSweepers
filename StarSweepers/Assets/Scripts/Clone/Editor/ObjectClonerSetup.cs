using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 複製配置システムの検証用シーンと、複製元のプレハブを作り直すツール。
///
/// Unityのメニュー「Tools > StarSweepers > アーカイブ > 共通の部品 > 複製配置の検証シーンを作り直す」から実行できる。
/// 必要なタグ（Duplicable / PlacedClone）の登録も自動で行う。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class ObjectClonerSetup
{
    private const string PrefabFolder = "Assets/Prefabs/ObjectCloner";
    private const string CommonPrefabFolder = "Assets/Prefabs/Common";
    private const string SceneFolder = "Assets/Scenes/Prototype/ObjectCloner";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string ScenePath = SceneFolder + "/ObjectClonerTest.unity";
    private const string PlayerPrefabPath = CommonPrefabFolder + "/PlayerRig.prefab";

    private const string LightBoxPath = PrefabFolder + "/CloneSourceLightBox.prefab";
    private const string HeavyBoxPath = PrefabFolder + "/CloneSourceHeavyBox.prefab";
    private const string FragilePlankPath = PrefabFolder + "/CloneSourceFragilePlank.prefab";

    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    /// <summary>
    /// 順に、フォルダとタグの準備 → マテリアル → 複製元のプレハブ3種（軽い箱・重い箱・壊れる板）→ 検証シーン。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/共通の部品/複製配置の検証シーンを作り直す")]
    public static void CreateAll()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);

        EnsureTag(ObjectCloner.DuplicableTag);
        EnsureTag(ObjectCloner.PlacedCloneTag);

        Material lightMaterial = CreateMaterial(MaterialFolder + "/CloneLight.mat", new Color(0.95f, 0.80f, 0.35f));
        Material heavyMaterial = CreateMaterial(MaterialFolder + "/CloneHeavy.mat", new Color(0.40f, 0.42f, 0.48f));
        Material fragileMaterial = CreateMaterial(MaterialFolder + "/CloneFragile.mat", new Color(0.85f, 0.55f, 0.75f));
        Material groundMaterial = CreateMaterial(MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        Material goalMaterial = CreateMaterial(MaterialFolder + "/CloneGoal.mat", new Color(0.35f, 0.85f, 0.45f));

        GameObject lightBox = CreateSourcePrefab(
            LightBoxPath, "CloneSourceLightBox", new Vector3(0.8f, 0.8f, 0.8f), mass: 1f, lightMaterial, fragile: false);

        GameObject heavyBox = CreateSourcePrefab(
            HeavyBoxPath, "CloneSourceHeavyBox", new Vector3(1.1f, 1.1f, 1.1f), mass: 30f, heavyMaterial, fragile: false);

        GameObject fragilePlank = CreateSourcePrefab(
            FragilePlankPath, "CloneSourceFragilePlank", new Vector3(1.6f, 0.12f, 0.8f), mass: 2f, fragileMaterial, fragile: true);

        CreateTestScene(lightBox, heavyBox, fragilePlank, groundMaterial, goalMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"複製配置の検証シーンを作り直しました。\nシーン: {ScenePath}");
    }

    /// <summary>複製元になるオブジェクトのプレハブを作る。</summary>
    private static GameObject CreateSourcePrefab(
        string path, string objectName, Vector3 scale, float mass, Material material, bool fragile)
    {
        GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        source.name = objectName;
        source.transform.localScale = scale;
        source.tag = ObjectCloner.DuplicableTag;
        source.GetComponent<MeshRenderer>().sharedMaterial = material;

        Rigidbody body = source.AddComponent<Rigidbody>();
        body.mass = mass;

        if (fragile)
        {
            source.AddComponent<FragileObject>();
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(source, path);
        Object.DestroyImmediate(source);

        return saved;
    }

    /// <summary>再生すればすぐ試せる検証用シーンを作る。</summary>
    private static void CreateTestScene(
        GameObject lightBox, GameObject heavyBox, GameObject fragilePlank,
        Material groundMaterial, Material goalMaterial)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 地面。Plane は 10m 四方なので、4倍で 40m 四方
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // 複製元を並べる。プレイヤーの正面に置いて、すぐ狙えるようにする
        PlaceSource(lightBox, new Vector3(-2f, 0.4f, 4f));
        PlaceSource(heavyBox, new Vector3(0f, 0.55f, 4f));
        PlaceSource(fragilePlank, new Vector3(2f, 0.06f, 4f));

        CreateGoal(goalMaterial);

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogWarning($"{PlayerPrefabPath} が見つかりません。先に「プレイヤーの検証シーンを作り直す」を実行してください。");
        }
        else
        {
            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.position = new Vector3(0f, 0.1f, 0f);
            SetUpCloner(player);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    /// <summary>複製元をシーンに1つ置く。</summary>
    private static void PlaceSource(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
        {
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;

        // 複製元は動かないようにしておく。転がっていくと狙えなくなるため
        Rigidbody body = instance.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
        }
    }

    /// <summary>登って到達する目標。高いところに置いて「積んで登る」動機を作る。</summary>
    private static void CreateGoal(Material goalMaterial)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "GoalPlatform";
        platform.transform.position = new Vector3(0f, 2f, -6f);
        platform.transform.localScale = new Vector3(4f, 4f, 3f);
        platform.GetComponent<MeshRenderer>().sharedMaterial = goalMaterial;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "GoalMarker";
        marker.transform.position = new Vector3(0f, 4.6f, -6f);
        marker.transform.localScale = new Vector3(0.3f, 0.6f, 0.3f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = goalMaterial;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
    }

    /// <summary>プレイヤーに複製機能を付けて、参照をつなぐ。</summary>
    private static void SetUpCloner(GameObject player)
    {
        ObjectCloner cloner = player.GetComponent<ObjectCloner>();
        if (cloner == null)
        {
            cloner = player.AddComponent<ObjectCloner>();
        }

        Camera camera = player.GetComponentInChildren<Camera>(true);
        Reticle reticle = player.GetComponentInChildren<Reticle>(true);
        var inputActions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(InputActionsPath);

        if (reticle == null)
        {
            Debug.LogWarning("PlayerRig に照準（Reticle）がありません。先に「プレイヤーの検証シーンを作り直す」を実行してください。");
        }

        SerializedObject serialized = new SerializedObject(cloner);
        serialized.FindProperty("aimCamera").objectReferenceValue = camera;
        serialized.FindProperty("playerRoot").objectReferenceValue = player.transform;

        // 距離を測る基準は、カメラの親（CameraPivot＝頭の位置）にする。
        // TPSでカメラが後ろに下がっても、置く位置が自分の位置にならないようにするため
        if (camera != null && camera.transform.parent != null)
        {
            serialized.FindProperty("aimOrigin").objectReferenceValue = camera.transform.parent;
        }
        serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        serialized.FindProperty("reticle").objectReferenceValue = reticle;

        // 物を回している間、視点を止めるためにつなぐ
        serialized.FindProperty("playerController").objectReferenceValue =
            player.GetComponent<PlayerController>();

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>タグが未登録なら登録する。手でタグを作る手間をなくすため。</summary>
    private static void EnsureTag(string tagName)
    {
        // タグの一覧は ProjectSettings/TagManager.asset の中にある。
        // 直接いじる API が無いので、SerializedObject で開いて "tags" の配列に足す
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        SerializedProperty tags = tagManager.FindProperty("tags");

        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tagName)
            {
                return;
            }
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
        tagManager.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"タグ「{tagName}」を登録しました。");
    }

    /// <summary>
    /// 色だけのマテリアルを作る。既にあれば色を入れ直して使い回す（参照が切れないように）。
    /// URP のシェーダーが無い環境でも動くよう、Standard に逃がしている。
    /// </summary>
    private static Material CreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>フォルダが無ければ作る。親も無ければ、親から順に作る。</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);

        // 親が無ければ先に作る（Unity は1段ずつしか作れない）
        EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }
}
