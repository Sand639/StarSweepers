using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **リアクション（<see cref="ReactionWheel"/>・<see cref="ReactionFeed"/>・<see cref="ReactionRelay"/>）**のツール。（試作・2026/10/21）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > リアクション > プレハブを作る（無ければ）` | `Assets/Prefabs/Common/ReactionSystem.prefab` を作る。**あれば作り直さない** |
/// | `Tools > StarSweepers > リアクション > 検証シーンを作り直す` | `Assets/Scenes/Test/ReactionWheelTest.unity` を作る（あれば上書き） |
/// | `Tools > StarSweepers > リアクション > ロビーに置く（ゲームで試す）` | ロビーのシーンに `ReactionSystem` を1つ置いて保存する（もうあれば何もしない） |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class ReactionWheelTestSetup
{
    private const string PrefabPath = "Assets/Prefabs/Common/ReactionSystem.prefab";
    private const string ScenePath = "Assets/Scenes/Test/ReactionWheelTest.unity";
    private const string LobbySceneName = "SpaceJunkLobby";

    /// <summary>プレハブを作る。**すでにあれば作り直さない**（手で調整した値や画像が消えないように）。</summary>
    [MenuItem("Tools/StarSweepers/リアクション/プレハブを作る（無ければ）")]
    public static void CreatePrefabMenu()
    {
        GameObject prefab = GetOrCreatePrefab();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>
    /// 検証シーンを新しく作って保存する。床・光・仮のプレイヤー（カプセル）・後ろから見るカメラ・ReactionSystem を置く。
    /// つないでいなくても動くよう、ReactionSystem の Allow Offline を ON にする（このシーンの中だけ）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/リアクション/検証シーンを作り直す")]
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

        // ----- 仮のプレイヤー（足元が原点、高さ2mのカプセル）。頭の上にもリアクションを出す -----
        GameObject player = new GameObject("Player");
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        ReactionBubble bubble = player.AddComponent<ReactionBubble>();

        // ----- カメラ（プレイヤーの斜め後ろから） -----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.35f, 0.5f, 0.7f);
        camera.fieldOfView = 50f;
        cameraObject.transform.position = new Vector3(0f, 3.2f, -5.5f);
        cameraObject.transform.LookAt(new Vector3(0f, 1.8f, 0f));

        // ----- ReactionSystem（プレハブ）。このシーンだけ、つないでいなくても使えて、シーンを移っても残さない -----
        GameObject system = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        SetProperty(system.GetComponent<ReactionRelay>(), "allowOffline", true);
        SetProperty(system.GetComponent<ReactionRelay>(), "keepAcrossScenes", false);

        // bubble は private なので、名前を指定して書き込む（シーンに保存させるため）。
        // ※ ReactionWheel の bubble の名前を変えると、ここが null で落ちる
        SerializedObject wheelSerialized = new SerializedObject(system.GetComponent<ReactionWheel>());
        wheelSerialized.FindProperty("bubble").objectReferenceValue = bubble;
        wheelSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"リアクションの検証シーンを作りました：{ScenePath}\n" +
                  "再生して、マウスのホイール（中ボタン）を長押し → マウスを動かして選ぶ → 離すと、画面の右端と頭の上に出る。");
    }

    /// <summary>
    /// **ロビーのシーンに ReactionSystem を置く。** ロビーに置けば、マップを移っても残るので試合中も使える。
    /// もう置いてあれば何もしない。**使わないときは、置いた ReactionSystem のチェックを外すだけでよい。**
    /// </summary>
    [MenuItem("Tools/StarSweepers/リアクション/ロビーに置く（ゲームで試す）")]
    public static void PlaceInLobby()
    {
        GameObject prefab = GetOrCreatePrefab();

        string lobbyPath = FindScenePath(LobbySceneName);
        if (lobbyPath == null)
        {
            Debug.LogError($"ロビーのシーン（{LobbySceneName}.unity）が見つかりません。");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scene = EditorSceneManager.OpenScene(lobbyPath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<ReactionRelay>(true) != null)
            {
                Selection.activeGameObject = root;
                Debug.Log($"ロビー（{lobbyPath}）には、もう ReactionSystem が置いてあります。使わないときは、そのチェックを外してください。");
                return;
            }
        }

        GameObject system = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        system.name = "ReactionSystem";
        Selection.activeGameObject = system;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"ロビー（{lobbyPath}）に ReactionSystem を置きました。\n" +
                  "タイトルから遊び始めると、ロビーと試合中にマウスのホイール長押しでリアクションを出せます。" +
                  "使わないときは、ロビーの ReactionSystem のチェックを外してください。");
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// プレハブを作る（あればそれを返す）。1つのオブジェクトに、選ぶ円・画面の端の表示・通信の係を付ける。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("ReactionSystem");
        ReactionWheel wheel = root.AddComponent<ReactionWheel>();
        ReactionFeed feed = root.AddComponent<ReactionFeed>();
        ReactionRelay relay = root.AddComponent<ReactionRelay>();

        // 項目は private なので、名前を指定して書き込む。
        // ※ ReactionRelay の wheel・feed の名前を変えると、ここが null で落ちる
        SerializedObject relaySerialized = new SerializedObject(relay);
        relaySerialized.FindProperty("wheel").objectReferenceValue = wheel;
        relaySerialized.FindProperty("feed").objectReferenceValue = feed;
        relaySerialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"リアクションのプレハブを作りました：{PrefabPath}");
        return prefab;
    }

    private static void SetProperty(Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>名前が一致するシーンのパス。無ければ null。</summary>
    private static string FindScenePath(string sceneName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{sceneName} t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return path;
            }
        }

        return null;
    }
}
