using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **宇宙ごみを集めるボット（<see cref="SpaceJunkBot"/>）**のツール。（試作・2026/10/21）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 宇宙ごみのボット > プレハブを作る（無ければ）` | `Assets/Prefabs/SpaceJunk/Online/SpaceJunkBot.prefab` を作る。**あれば作り直さない** |
/// | `Tools > StarSweepers > 宇宙ごみのボット > 検証シーンを作り直す` | `Assets/Scenes/Test/SpaceJunkBotTest.unity` を作る（あれば上書き） |
/// | `Tools > StarSweepers > 宇宙ごみのボット > 開いているシーンに置く` | 開いているシーン（マップ）にボットを1体置く。**保存はしない**（置き場所とチームを決めてから自分で保存する） |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkBotSetup
{
    private const string PrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkBot.prefab";
    private const string ScenePath = "Assets/Scenes/Test/SpaceJunkBotTest.unity";

    /// <summary>プレハブを作る。**すでにあれば作り直さない**（手で調整した値や見た目が消えないように）。</summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのボット/プレハブを作る（無ければ）")]
    public static void CreatePrefabMenu()
    {
        GameObject prefab = GetOrCreatePrefab();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>
    /// 検証シーンを新しく作って保存する。
    /// 床・光・見下ろすカメラ・ゴールの目印2つ・ボット2体（青と赤）・素材12個を置く。
    /// つないでいなくても動く（ボットはそのPCで考える）。**ゴールの目印は点を数えない**（運んで投げ込むところまでを見る）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのボット/検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject prefab = GetOrCreatePrefab();
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Plane はもともと 10m 四方なので、3倍で 30m 四方の床になる
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3f, 1f, 3f);

        // ----- カメラ（斜め上から全体を見下ろす） -----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
        camera.fieldOfView = 55f;
        cameraObject.transform.position = new Vector3(0f, 22f, -18f);
        cameraObject.transform.LookAt(Vector3.zero);

        // ----- ゴールの目印（北＝青、南＝赤）とボット -----
        Transform blueGoal = CreateGoalMarker("Goal_Blue", new Vector3(0f, 0f, 11f), 0);
        Transform redGoal = CreateGoalMarker("Goal_Red", new Vector3(0f, 0f, -11f), 1);

        CreateBot(prefab, "Bot_Blue", new Vector3(-3f, 0f, 6f), 0, blueGoal);
        CreateBot(prefab, "Bot_Red", new Vector3(3f, 0f, -6f), 1, redGoal);

        // ----- 素材（毎回同じ並びになるよう、乱数の種を決めておく） -----
        Random.State oldState = Random.state;
        Random.InitState(1021);
        for (int i = 0; i < 12; i++)
        {
            Vector3 position = new Vector3(Random.Range(-12f, 12f), 0.5f, Random.Range(-7f, 7f));
            CreateTestMaterial($"Material_{i:00}", position, SpaceJunkMaterials.FromIndex(i % 3));
        }
        Random.state = oldState;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"宇宙ごみのボットの検証シーンを作りました：{ScenePath}\n" +
                  "再生すると、青と赤のボットが近くの素材を頭の上に持ち上げ、自分の色のゴールの目印へ放り込む。\n" +
                  "（このシーンのゴールは目印だけなので、点は数えない。点の入り方はタイトルから遊んで確かめる）");
    }

    /// <summary>
    /// **開いているシーンに、ボットを1体置く。** シーンビューで見ている場所に置き、選んだ状態にする。
    /// 通信の番号を付け直すので、宇宙ごみのマップに置いてもそのまま使える。**保存はしない。**
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのボット/開いているシーンに置く")]
    public static void PlaceInOpenScene()
    {
        GameObject prefab = GetOrCreatePrefab();
        var scene = EditorSceneManager.GetActiveScene();

        GameObject bot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        bot.name = "SpaceJunkBot";

        if (SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            bot.transform.position = new Vector3(pivot.x, 0f, pivot.z);
        }

        SpaceJunkFixedCameraSetup.RefreshNetworkIds();
        Undo.RegisterCreatedObjectUndo(bot, "ボットを置く");
        Selection.activeGameObject = bot;
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"{scene.name} にボットを置きました（まだ保存していません）。\n" +
                  "置き場所を動かし、インスペクターの Team（0=青 / 1=赤 / 2=緑 / 3=黄）を決めてから Ctrl+S で保存してください。\n" +
                  "※ 人と共有しているマップなら、触る前に Discord で一声かけること。");
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// プレハブを作る（あればそれを返す）。
    /// 根元に「歩く体（CharacterController）・通信の印・位置を配る部品・ボットの頭」、子に見た目（カプセルと顔の板）を付ける。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("SpaceJunkBot");

        // 足元が原点、高さ2mの体
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.45f;
        controller.center = new Vector3(0f, 1f, 0f);

        root.AddComponent<NetworkObject>();

        // ボットはホストが動かすので、**ホストが位置を配る**形にする。傾かないので縦以外の回転と大きさは送らない
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        networkTransform.SyncRotAngleX = false;
        networkTransform.SyncRotAngleZ = false;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        // ----- 見た目。当たり判定は根元の CharacterController だけにする -----
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        // どちらを向いているか分かるように、顔の位置に板を付ける
        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "Face";
        face.transform.SetParent(root.transform, false);
        face.transform.localPosition = new Vector3(0f, 1.5f, 0.38f);
        face.transform.localScale = new Vector3(0.6f, 0.25f, 0.15f);
        Object.DestroyImmediate(face.GetComponent<Collider>());

        SpaceJunkBot bot = root.AddComponent<SpaceJunkBot>();

        // bodyRenderer は private なので、名前を指定して書き込む。
        // ※ SpaceJunkBot の bodyRenderer の名前を変えると、ここが null で落ちる
        SerializedObject serialized = new SerializedObject(bot);
        serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"宇宙ごみのボットのプレハブを作りました：{PrefabPath}");
        return prefab;
    }

    // ------------------------------------------------------------
    // 検証シーンの部品
    // ------------------------------------------------------------

    /// <summary>ボットを置き、チームと運ぶ先（ゴールの目印）を決める。</summary>
    private static void CreateBot(GameObject prefab, string name, Vector3 position, int team, Transform goal)
    {
        GameObject bot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        bot.name = name;
        bot.transform.position = position;

        // ※ SpaceJunkBot の team・goalOverride の名前を変えると、ここが null で落ちる
        SerializedObject serialized = new SerializedObject(bot.GetComponent<SpaceJunkBot>());
        serialized.FindProperty("team").intValue = team;
        serialized.FindProperty("goalOverride").objectReferenceValue = goal;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// ゴールの目印（チームの色の丸い板）。**当たり判定も点を数える仕組みも無い**ただの印。
    /// 色はシーンに保存できるよう、マテリアルを作って持たせる。
    /// </summary>
    private static Transform CreateGoalMarker(string name, Vector3 position, int team)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = name;
        marker.transform.position = position + Vector3.up * 0.02f;
        marker.transform.localScale = new Vector3(4f, 0.02f, 4f);
        Object.DestroyImmediate(marker.GetComponent<Collider>());

        Renderer renderer = marker.GetComponent<Renderer>();
        Material material = new Material(renderer.sharedMaterial);
        material.color = SpaceJunkTeams.TeamColor(team);
        renderer.sharedMaterial = material;

        return marker.transform;
    }

    /// <summary>
    /// 検証用の素材（箱）。**通信の部品は付けない**（つないでいないシーンなので）。
    /// 引っ掛けられる印（HookableObject）と素材の種類（SpaceJunkMaterial）だけを持つ。
    /// </summary>
    private static void CreateTestMaterial(string name, Vector3 position, SpaceJunkMaterialKind kind)
    {
        GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name;
        item.transform.position = position;
        item.transform.localScale = Vector3.one * 0.8f;

        Rigidbody body = item.AddComponent<Rigidbody>();
        body.mass = 1f;
        item.AddComponent<HookableObject>();
        SpaceJunkMaterial material = item.AddComponent<SpaceJunkMaterial>();

        // ※ SpaceJunkMaterial の kind・tintRenderers の名前を変えると、ここが null で落ちる
        SerializedObject serialized = new SerializedObject(material);
        serialized.FindProperty("kind").enumValueIndex = (int)kind;
        SerializedProperty tint = serialized.FindProperty("tintRenderers");
        tint.arraySize = 1;
        tint.GetArrayElementAtIndex(0).objectReferenceValue = item.GetComponent<Renderer>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
