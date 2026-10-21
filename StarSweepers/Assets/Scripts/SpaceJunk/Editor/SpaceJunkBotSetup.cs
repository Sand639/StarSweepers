using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **宇宙ごみを集めるボット（<see cref="SpaceJunkBot"/>）**の検証シーンを作るツール。（試作・2026/10/21）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 宇宙ごみのボット > 検証シーンを作り直す` | `Assets/Scenes/Test/SpaceJunkBotTest.unity` を作る（あれば上書き） |
///
/// シーンには、**人が操作するプレイヤー1人と、ボット2体（青・赤）**を置く。
/// ボットの体は**人と同じ作りのプレイヤー**（釣りの検証シーンと同じ <see cref="FishingSceneBuilder.CreatePlayer"/>）に、
/// 見えないコントローラー（<see cref="VirtualPlayerInput"/>）とボットの頭（<see cref="SpaceJunkBot"/>）を足しただけ。
/// 投げ方は全員、宇宙ごみ式（<see cref="ThrowStyle.TwoButtons"/>）にする。
///
/// つながずに動く（オンラインの試合にボットを出す仕組みはまだ無い）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkBotSetup
{
    private const string ScenePath = "Assets/Scenes/Test/SpaceJunkBotTest.unity";

    /// <summary>ゴールの目印の位置（北と南の壁の手前）。床は 40m 四方（<see cref="FishingSceneBuilder.CreateGround"/>）。</summary>
    private const float GoalZ = 16f;

    /// <summary>
    /// 検証シーンを新しく作って保存する。
    /// 床・壁・光・全体を見下ろすカメラ・ゴールの目印2つ・人のプレイヤー・ボット2体・素材14個を置く。
    /// **ゴールの目印は点を数えない**（ボットが運んで投げ込むところまでを見る）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのボット/検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();
        if (inputActions == null)
        {
            Debug.LogError("入力の設定（InputSystem_Actions）が見つからないので、検証シーンを作れませんでした。");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();
        FishingSceneBuilder.CreatePlainWalls();

        // ----- カメラ（動かない。斜め上から床全体を見下ろす）-----
        // ボットは「このカメラに映っている画面のどこにカーソルを置くか」で狙うので、全体が映るカメラにしておく
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
        camera.fieldOfView = 55f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = new Vector3(0f, 34f, -26f);
        cameraObject.transform.LookAt(new Vector3(0f, 0f, -1f));

        // ----- ゴールの目印（北＝青、南＝赤）-----
        Transform blueGoal = CreateGoalMarker("Goal_Blue", new Vector3(0f, 0f, GoalZ), 0);
        Transform redGoal = CreateGoalMarker("Goal_Red", new Vector3(0f, 0f, -GoalZ), 1);

        // ----- 人のプレイヤー（いつもどおりマウス・コントローラーで動かせる。ゲージも出る）-----
        GameObject human = CreateBody("Player", new Vector3(0f, 0f, 0f), camera, inputActions,
            FishingSceneBuilder.CreateUI());

        // ----- ボット（同じ作りの体に、見えないコントローラーと頭を足す。ゲージの表示は無し）-----
        CreateBot("Bot_Blue", new Vector3(-8f, 0f, 6f), 0, blueGoal, camera, inputActions);
        CreateBot("Bot_Red", new Vector3(8f, 0f, -6f), 1, redGoal, camera, inputActions);

        // ----- 素材（毎回同じ並びになるよう、乱数の種を決めておく）-----
        Random.State oldState = Random.state;
        Random.InitState(1021);
        for (int i = 0; i < 14; i++)
        {
            Vector3 position = new Vector3(Random.Range(-15f, 15f), 0.5f, Random.Range(-10f, 10f));
            CreateTestMaterial($"Material_{i:00}", position, SpaceJunkMaterials.FromIndex(i % 3));
        }
        Random.state = oldState;

        Selection.activeGameObject = human;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"宇宙ごみのボットの検証シーンを作りました：{ScenePath}\n" +
                  "再生すると、青と赤のボットが**プレイヤーと同じ操作（スティック・カーソル・RT）だけで**、\n" +
                  "近くの素材を右で撃って持ち上げ、ゲージを見て自分の色のゴールの印へ投げる。\n" +
                  "あなたも Player を動かして一緒に遊べる（このシーンのゴールは目印だけなので、点は数えない）。");
    }

    // ------------------------------------------------------------
    // 体とボット
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーの体を1つ作る（フック・糸・操作の部品つき）。投げ方は宇宙ごみ式にする。
    /// <paramref name="ui"/> はゲージの表示。ボットには要らないので null でよい。
    /// </summary>
    private static GameObject CreateBody(string name, Vector3 position, Camera camera,
        InputActionAsset inputActions, HookChargeUI ui)
    {
        GameObject body = FishingSceneBuilder.CreatePlayer();
        body.name = name;
        body.transform.position = position;

        HookProjectile hook = FishingSceneBuilder.CreateHook();
        hook.name = $"{name}_Hook";
        HookLine line = FishingSceneBuilder.CreateLine(body);

        FishingSceneBuilder.WirePlayer(body, camera, hook, line, ui, inputActions, null, null);

        // ※ ThrowController の style の名前を変えると、ここが null で落ちる
        SerializedObject thrower = new SerializedObject(body.GetComponent<ThrowController>());
        thrower.FindProperty("style").enumValueIndex = (int)ThrowStyle.TwoButtons;
        thrower.ApplyModifiedPropertiesWithoutUndo();

        return body;
    }

    /// <summary>ボットを1体作る。チームの色に塗り、運ぶ先（ゴールの目印）を決める。</summary>
    private static void CreateBot(string name, Vector3 position, int team, Transform goal,
        Camera camera, InputActionAsset inputActions)
    {
        GameObject body = CreateBody(name, position, camera, inputActions, null);

        body.AddComponent<VirtualPlayerInput>();
        SpaceJunkBot bot = body.AddComponent<SpaceJunkBot>();

        // ※ SpaceJunkBot の team・goalOverride の名前を変えると、ここが null で落ちる
        SerializedObject serialized = new SerializedObject(bot);
        serialized.FindProperty("team").intValue = team;
        serialized.FindProperty("goalOverride").objectReferenceValue = goal;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 狙い方の表示（画面左下）は、人のプレイヤーのぶんだけ出す（ボットのぶんが重ならないように）
        HookAimAssist aimAssist = body.GetComponent<HookAimAssist>();
        if (aimAssist != null)
        {
            SerializedObject assist = new SerializedObject(aimAssist);
            assist.FindProperty("showPanel").boolValue = false;
            assist.ApplyModifiedPropertiesWithoutUndo();
        }

        // チームの色に塗る（Body の下の見た目だけ）。色はシーンの中に持たせる
        Transform look = body.transform.Find("Body");
        if (look != null)
        {
            foreach (Renderer renderer in look.GetComponentsInChildren<Renderer>(true))
            {
                Material material = new Material(renderer.sharedMaterial);
                material.color = SpaceJunkTeams.TeamColor(team);
                renderer.sharedMaterial = material;
            }
        }
    }

    // ------------------------------------------------------------
    // ゴールの目印と素材
    // ------------------------------------------------------------

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
