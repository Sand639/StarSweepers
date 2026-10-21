using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **リアクションホイール（<see cref="ReactionWheel"/>）**を試すためのツール。（試作・2026/10/21）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > リアクションホイールの検証シーンを作り直す` | `Assets/Scenes/Test/ReactionWheelTest.unity` を作る（あれば上書き） |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class ReactionWheelTestSetup
{
    private const string ScenePath = "Assets/Scenes/Test/ReactionWheelTest.unity";

    /// <summary>
    /// 検証シーンを新しく作って保存する。床・光・仮のプレイヤー（カプセル）・後ろから見るカメラ・ホイールを置く。
    /// </summary>
    [MenuItem("Tools/StarSweepers/リアクションホイールの検証シーンを作り直す")]
    public static void CreateTestScene()
    {
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

        // ----- 仮のプレイヤー（足元が原点、高さ2mのカプセル）。頭の上にリアクションを出す -----
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

        // ----- ホイール（マウスのホイール長押しで開く） -----
        GameObject wheelObject = new GameObject("ReactionWheel");
        ReactionWheel wheel = wheelObject.AddComponent<ReactionWheel>();

        // bubble は private なので、名前を指定して書き込む（シーンに保存させるため）。
        // ※ ReactionWheel の bubble の名前を変えると、ここが null で落ちる
        SerializedObject wheelSerialized = new SerializedObject(wheel);
        wheelSerialized.FindProperty("bubble").objectReferenceValue = bubble;
        wheelSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"リアクションホイールの検証シーンを作りました：{ScenePath}\n" +
                  "再生して、マウスのホイール（中ボタン）を長押し → マウスを動かして選ぶ → 離すと頭の上に出る。");
    }
}
