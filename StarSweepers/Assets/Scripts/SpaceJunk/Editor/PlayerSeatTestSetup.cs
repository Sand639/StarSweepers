using ProjectEL4S.InputControl;
using ProjectEL4S.MultiKeyboard;
using ProjectEL4S.MultiMouse;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **1台のPCで2人がプレイヤーを動かす検証シーンを作るツール。**
///
/// Unityのメニュー「Tools > StarSweepers > 1台のPCで複数人の検証シーンを作る」から実行できる。
/// 作られるシーン：`Assets/Scenes/Test/PlayerSeatTest.unity`
///
/// 宇宙ごみのプレイヤー（`SpaceJunkPlayer.prefab`）を2人置き、<see cref="PlayerInputSource"/> の席を
/// P1・P2 にしてある。通信はつながないので、再生するだけで2人を別々のキーボード＋マウス／コントローラで動かせる。
///
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class PlayerSeatTestSetup
{
    private const string ScenePath = "Assets/Scenes/Test/PlayerSeatTest.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlayer.prefab";
    private const string SupplyPrefabPath = "Assets/Prefabs/Fish/FishingSupply.prefab";

    private const float FloorSize = 24f;

    [MenuItem("Tools/StarSweepers/1台のPCで複数人の検証シーンを作る")]
    public static void CreateScene()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError("プレイヤーのプレハブが見つかりません: " + PlayerPrefabPath);
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 明かり
        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 見下ろしの固定カメラ（北向き。W で画面の上へ進む）
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 22f, -12f), Quaternion.Euler(60f, 0f, 0f));

        // 床と、落ちないための壁
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(FloorSize, 1f, FloorSize);

        float half = FloorSize * 0.5f;
        CreateWall("Wall_N", new Vector3(0f, 1f, half), new Vector3(FloorSize, 2f, 0.5f));
        CreateWall("Wall_S", new Vector3(0f, 1f, -half), new Vector3(FloorSize, 2f, 0.5f));
        CreateWall("Wall_E", new Vector3(half, 1f, 0f), new Vector3(0.5f, 2f, FloorSize));
        CreateWall("Wall_W", new Vector3(-half, 1f, 0f), new Vector3(0.5f, 2f, FloorSize));

        // 席の管理役（キーボード・マウス・席）と、確認用の表示（F5〜F8 の登録もここ）
        var seats = new GameObject("InputSeats");
        seats.AddComponent<MultiKeyboardManager>();
        seats.AddComponent<MultiMouseManager>();
        seats.AddComponent<InputSeatManager>();
        seats.AddComponent<InputSeatTestHud>();

        // プレイヤー2人。席だけ P1・P2 に変える
        CreatePlayer(playerPrefab, 0, new Vector3(-4f, 1f, 0f));
        CreatePlayer(playerPrefab, 1, new Vector3(4f, 1f, 0f));

        // フックで引っ掛ける物（無くても動きは確かめられる）
        GameObject supplyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SupplyPrefabPath);
        if (supplyPrefab != null)
        {
            Vector3[] spots =
            {
                new Vector3(-6f, 1f, 6f), new Vector3(0f, 1f, 7f), new Vector3(6f, 1f, 6f),
                new Vector3(-6f, 1f, -6f), new Vector3(6f, 1f, -6f),
            };

            foreach (Vector3 spot in spots)
            {
                var supply = (GameObject)PrefabUtility.InstantiatePrefab(supplyPrefab);
                supply.transform.position = spot;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "1台のPCで複数人の検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・キーボードのキーを押す／マウスを動かす／コントローラのボタンを押すと、P1・P2 の順に入る\n" +
            "・WASD／左スティックで移動、マウス／右スティックで狙い、左クリック（LT）・右クリック（RT）でフック\n" +
            "・F6 で固定登録（キーボード P1→P2、マウス P1→P2 の順に押す）");
    }

    private static void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
    }

    private static void CreatePlayer(GameObject prefab, int seatIndex, Vector3 position)
    {
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = $"Player_P{seatIndex + 1}";
        player.transform.position = position;

        // 単体操作（-1）ではなく、席の番号で読ませる
        PlayerInputSource source = player.GetComponent<PlayerInputSource>();
        if (source == null)
        {
            source = player.AddComponent<PlayerInputSource>();
        }

        var serialized = new SerializedObject(source);
        serialized.FindProperty("seatIndex").intValue = seatIndex;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
