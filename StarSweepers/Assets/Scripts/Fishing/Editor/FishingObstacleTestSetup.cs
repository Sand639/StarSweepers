using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **動く障害物の検証シーンを作るツール。**（新しく足したツール）
///
/// Unityのメニュー「Tools > StarSweepers > 釣りの障害物の検証シーンを作る」から実行できる。
/// 作られるシーン：`Assets/Scenes/Test/FishingObstacleTest.unity`
///
/// 中身は「ステージ」検証シーン（ポケット・物資）に、**動く障害物を3つ**足したもの。
///   ・北側を東西に行き来する障害物（行って戻る）
///   ・南側を四角くぐるぐる回る障害物（角で止まる）
///   ・西側を南北に行き来する障害物（ゆっくり止まって、ゆっくり動き出す）
/// 爆発物は、障害物だけを確かめやすいように置いていない。
///
/// **他の検証シーン（FishingHookTest / FishingArenaTest / オンライン用）は上書きしない。**
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingObstacleTestSetup
{
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingObstacleTest.unity";

    /// <summary>動く障害物の素材（紫）。マップを作るツール（FishingMapSetup）も同じ素材を使う。</summary>
    public const string ObstacleMaterialPath = FishingSceneBuilder.MaterialFolder + "/FishingObstacle.mat";

    [MenuItem("Tools/StarSweepers/釣りの障害物の検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();

        // ---- 点数と画面表示 ----
        GameObject hudObject = new GameObject("FishingHUD");
        ScoreBoard scoreBoard = hudObject.AddComponent<ScoreBoard>();
        FishingStatusUI statusUI = hudObject.AddComponent<FishingStatusUI>();

        FishingSceneBuilder.CreatePocketWalls(scoreBoard);

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);

        // 物資8個を、中心から 8m の円の上に並べる（消えても6秒で戻ってくる）
        FishingSceneBuilder.CreateSupplyRing(8, 8f);

        // ---- 動く障害物 ----
        CreateSampleObstacles();

        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions, statusUI, scoreBoard);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【障害物】釣りの障害物の検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・紫の長い箱が障害物。決まった道すじを動き続ける\n" +
            "・プレイヤーは通り抜けられず、動いてきた障害物に押し出される\n" +
            "・物資も押しのけられる\n" +
            "・道すじは障害物を選ぶとシーン画面に橙の線で出る。Inspector の Points で変えられる");
    }

    /// <summary>
    /// 検証用の動く障害物を3つ置く（北 → 南 → 西 の順に作る＝シーンの並び順）。
    /// 数字の読み方は <see cref="CreateObstacle"/> の説明を参照（速さは m/秒、止まる時間は秒）。
    /// </summary>
    private static void CreateSampleObstacles()
    {
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            ObstacleMaterialPath, new Color(0.55f, 0.3f, 0.75f));

        // 北側を東西に行き来する（横長）
        CreateObstacle("Obstacle_NorthSlide", new Vector3(0f, 0f, 12f), 0f,
            new Vector3(6f, 1.2f, 1f), material,
            new[] { new Vector3(-11f, 0f, 0f), new Vector3(11f, 0f, 0f) },
            MovingObstacle.PathMode.PingPong, 4f, 0.4f, false);

        // 南側を四角くぐるぐる回る（横長。角で少し止まる）
        CreateObstacle("Obstacle_SouthLoop", new Vector3(0f, 0f, -12f), 0f,
            new Vector3(5f, 1.2f, 1f), material,
            new[]
            {
                new Vector3(-10f, 0f, 2f),
                new Vector3(10f, 0f, 2f),
                new Vector3(10f, 0f, -3f),
                new Vector3(-10f, 0f, -3f)
            },
            MovingObstacle.PathMode.Loop, 5f, 0.6f, false);

        // 西側を南北に行き来する（縦長。ゆっくり止まってゆっくり動き出す）
        CreateObstacle("Obstacle_WestSlide", new Vector3(-14f, 0f, 0f), 90f,
            new Vector3(6f, 1.2f, 1f), material,
            new[] { new Vector3(0f, 0f, -7f), new Vector3(0f, 0f, 7f) },
            MovingObstacle.PathMode.PingPong, 3f, 1f, true);
    }

    /// <summary>
    /// 動く障害物を1つ置く。
    /// <paramref name="points"/> は、置いた位置 <paramref name="position"/> からのずれ。
    /// **マップの雛形を作るツール（FishingMapSetup）からも使う。**
    /// </summary>
    /// <param name="position">足元の位置（高さ 0 なら床の上に立つ）。</param>
    /// <param name="yaw">上から見た向き（度）。0 なら長い辺（X）が東西、90 なら南北に向く。</param>
    /// <param name="size">箱の大きさ（幅・高さ・奥行き、メートル）。</param>
    /// <param name="mode">PingPong＝最後まで行ったら来た道を戻る、Loop＝最後まで行ったら最初の点へ直行して回り続ける。</param>
    /// <param name="speed">動く速さ（m/秒）。</param>
    /// <param name="waitSeconds">各点で止まる時間（秒）。</param>
    /// <param name="easeInOut">true なら、止まる前にゆっくりになり、ゆっくり動き出す。</param>
    public static GameObject CreateObstacle(string name, Vector3 position, float yaw, Vector3 size,
        Material material, Vector3[] points, MovingObstacle.PathMode mode,
        float speed, float waitSeconds, bool easeInOut)
    {
        // 足元が床（高さ0）に来るように、高さの半分だけ持ち上げる
        Vector3 center = position + new Vector3(0f, size.y * 0.5f, 0f);

        GameObject obstacle = FishingSceneBuilder.CreateBox(name, center, size, material);
        obstacle.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // スクリプト（MovingObstacle）が MovePosition で動かすので、物理（重力・ぶつかった反動）では動かさない。
        // Interpolate は、物理の更新の合間も見た目をなめらかにつなぐ設定
        Rigidbody body = obstacle.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        MovingObstacle mover = obstacle.AddComponent<MovingObstacle>();

        // ⚠ "points"・"mode"・"moveSpeed"・"waitSeconds"・"easeInOut" は MovingObstacle のフィールド名。
        //   **向こうを改名すると、ここは NullReferenceException で止まる**（コンパイルエラーにはならない）。
        //   mode は enum の「上から何番目か」で入れている（PathMode に番号を書き足して並びとずれると、違うモードになる）
        SerializedObject serialized = new SerializedObject(mover);

        SerializedProperty pointsProperty = serialized.FindProperty("points");
        pointsProperty.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            pointsProperty.GetArrayElementAtIndex(i).vector3Value = points[i];
        }

        serialized.FindProperty("mode").enumValueIndex = (int)mode;
        serialized.FindProperty("moveSpeed").floatValue = speed;
        serialized.FindProperty("waitSeconds").floatValue = waitSeconds;
        serialized.FindProperty("easeInOut").boolValue = easeInOut;

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return obstacle;
    }
}
