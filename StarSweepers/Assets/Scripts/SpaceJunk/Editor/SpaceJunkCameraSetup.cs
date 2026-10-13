using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// **宇宙ごみのカメラの種類（全体・チーム・自分）を、ステージのカメラと検証シーンに用意するツール。**（2026/10/7・小野田さん）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 宇宙ごみのステージのカメラにチームカメラを足す` | `SpaceJunkStageCamera.prefab` に、全体カメラ・チームカメラの部品を足し、切り替えの順を「全体 → チーム → 自分」にする。**すでに付いていれば足さない**（調整した値は消えない） |
/// | `Tools > StarSweepers > 宇宙ごみのチームカメラの検証シーンを作り直す` | `Assets/Scenes/Test/SpaceJunkTeamCameraTest.unity` を作る。通信なしで A・B を別々に動かして、カメラの動きを試せる |
///
/// 検証シーンのカメラは**ステージと同じプレハブ**を置いている。検証シーンで値を調整したら、
/// インスペクターの「Overrides ＞ Apply All」でプレハブに書き戻すと、STAGE_01〜05 にもそのまま入る。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkCameraSetup
{
    private const string CameraPrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkStageCamera.prefab";
    private const string TestScenePath = "Assets/Scenes/Test/SpaceJunkTeamCameraTest.unity";

    [MenuItem("Tools/StarSweepers/宇宙ごみのステージのカメラにチームカメラを足す")]
    public static void AddModesToStageCamera()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CameraPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[JUNK] カメラのプレハブが見つかりません：{CameraPrefabPath}");
            return;
        }

        try
        {
            SpaceJunkCameraModeSwitch modeSwitch = root.GetComponent<SpaceJunkCameraModeSwitch>();
            if (modeSwitch == null)
            {
                modeSwitch = root.AddComponent<SpaceJunkCameraModeSwitch>();
            }

            SpaceJunkWholeStageCamera whole = GetOrAdd<SpaceJunkWholeStageCamera>(root);
            SpaceJunkTeamGroupCamera team = GetOrAdd<SpaceJunkTeamGroupCamera>(root);
            SpaceJunkTeamFollowCamera self = GetOrAdd<SpaceJunkTeamFollowCamera>(root);

            // 切り替えの順＝付いている順。「全体 → チーム → 自分」にする
            // （下から順に決める。先に全体を上げると、あとでチームを上げたときに全体を追い越すため）
            MoveAbove(team, self);
            MoveAbove(whole, team);

            // 始まりは、前と同じ全体カメラ（2026/10/6・大槻さんの決め）
            SerializedObject so = new SerializedObject(modeSwitch);
            SerializedProperty first = so.FindProperty("firstMode");
            if (first.objectReferenceValue == null)
            {
                first.objectReferenceValue = whole;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, CameraPrefabPath);
            Debug.Log($"[JUNK] {CameraPrefabPath} に全体カメラ・チームカメラを用意しました（切り替えの順：全体 → チーム → 自分）");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/StarSweepers/宇宙ごみのチームカメラの検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        GameObject cameraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraPrefabPath);
        if (cameraPrefab == null)
        {
            Debug.LogError($"[JUNK] カメラのプレハブが見つかりません：{CameraPrefabPath}");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ----- 床（80m 四方）と、動きが分かるための目印 -----
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(8f, 1f, 8f);
        Paint(ground, new Color(0.22f, 0.24f, 0.28f));

        Transform marks = new GameObject("Landmarks").transform;
        for (int x = -3; x <= 3; x++)
        {
            for (int z = -3; z <= 3; z++)
            {
                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mark.name = $"Mark_{x}_{z}";
                mark.transform.SetParent(marks, false);
                mark.transform.localPosition = new Vector3(x * 10f, 0.25f, z * 10f);
                mark.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
                Object.DestroyImmediate(mark.GetComponent<Collider>());
                Paint(mark, (x + z) % 2 == 0 ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.38f, 0.40f, 0.44f));
            }
        }

        // 自陣（南）の目印：自陣が手前に来ているかを見るため
        GameObject home = GameObject.CreatePrimitive(PrimitiveType.Cube);
        home.name = "OwnGoalMark_South";
        home.transform.position = new Vector3(0f, 0.05f, -36f);
        home.transform.localScale = new Vector3(16f, 0.1f, 4f);
        Object.DestroyImmediate(home.GetComponent<Collider>());
        Paint(home, new Color(0.30f, 0.60f, 0.95f));

        // ----- 人形（A：WASD、B：矢印キー） -----
        Transform a = CreateDummy("A_WASD", new Vector3(-3f, 0f, -6f), SpaceJunkCameraTestDummy.Control.Wasd, false);
        Transform b = CreateDummy("B_Arrows", new Vector3(4f, 0f, -2f), SpaceJunkCameraTestDummy.Control.Arrows, true);

        GameObject targetsObject = new GameObject("CameraTestTargets");
        SpaceJunkCameraTestTargets targets = targetsObject.AddComponent<SpaceJunkCameraTestTargets>();
        SerializedObject targetsSo = new SerializedObject(targets);
        SerializedProperty players = targetsSo.FindProperty("players");
        players.arraySize = 2;
        players.GetArrayElementAtIndex(0).objectReferenceValue = a;
        players.GetArrayElementAtIndex(1).objectReferenceValue = b;
        targetsSo.ApplyModifiedPropertiesWithoutUndo();

        // ----- カメラ（ステージと同じプレハブ。始まりはチームカメラ） -----
        GameObject cameraObject = (GameObject)PrefabUtility.InstantiatePrefab(cameraPrefab);
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 20f, -20f), Quaternion.Euler(55f, 0f, 0f));

        SpaceJunkCameraModeSwitch modeSwitch = cameraObject.GetComponent<SpaceJunkCameraModeSwitch>();
        SpaceJunkTeamGroupCamera team = cameraObject.GetComponent<SpaceJunkTeamGroupCamera>();
        if (modeSwitch != null && team != null)
        {
            SerializedObject so = new SerializedObject(modeSwitch);
            so.FindProperty("firstMode").objectReferenceValue = team;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[JUNK] カメラのプレハブにチームカメラがありません。先に「宇宙ごみのステージのカメラにチームカメラを足す」を実行してください");
        }

        EditorSceneManager.SaveScene(scene, TestScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[JUNK] チームカメラの検証シーンを作りました：{TestScenePath}\n" +
                  "再生して、WASD で A、矢印キーで B を動かせます。C でカメラ切り替え、Tab で自分を入れ替え、Q／E で自陣の向きを回します。");
    }

    private static Transform CreateDummy(string name, Vector3 position, SpaceJunkCameraTestDummy.Control control, bool wander)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        Paint(body, control == SpaceJunkCameraTestDummy.Control.Wasd ? new Color(0.30f, 0.60f, 0.95f) : new Color(0.55f, 0.80f, 1f));

        // 向きが分かるように、前に鼻を付ける
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, 1.4f, 0.5f);
        nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.4f);
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        Paint(nose, Color.white);

        SpaceJunkCameraTestDummy dummy = root.AddComponent<SpaceJunkCameraTestDummy>();
        SerializedObject so = new SerializedObject(dummy);
        so.FindProperty("control").enumValueIndex = (int)control;
        so.FindProperty("wander").boolValue = wander;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root.transform;
    }

    /// <summary>色を塗る。マテリアルはシーンの中に入れる（素材のファイルは作らない）。</summary>
    private static void Paint(GameObject target, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            return;
        }

        Material material = new Material(shader) { name = target.name };
        material.SetColor("_BaseColor", color);
        target.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static T GetOrAdd<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    /// <summary><paramref name="upper"/> が <paramref name="lower"/> より上（先）に来るまで上へ動かす。</summary>
    private static void MoveAbove(Component upper, Component lower)
    {
        for (int guard = 0; guard < 20 && IndexOf(upper) > IndexOf(lower); guard++)
        {
            if (!ComponentUtility.MoveComponentUp(upper))
            {
                break;
            }
        }
    }

    private static int IndexOf(Component component)
    {
        Component[] all = component.GetComponents<Component>();
        return System.Array.IndexOf(all, component);
    }
}
