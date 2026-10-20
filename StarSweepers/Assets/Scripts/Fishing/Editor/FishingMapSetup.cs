using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **釣りのオンライン対戦で遊ぶ「マップ」を増やすためのツール。**
///
/// Unityのメニュー「Tools > StarSweepers」から使う。
///
/// | メニュー | 何をするか |
/// | --- | --- |
/// | `釣りの新しいマップを作る（オンライン）` | 遊ぶのに必要な部品が入った**マップの雛形**を `Assets/Scenes/Prototype/Fish/FishingMap03.unity` のように作る。**同じ名前があれば番号をずらし、上書きしない** |
/// | `釣りの対戦用マップを作る（大きな遮り）` | **オンライン対戦用に形を作り込んだマップ**を `Assets/Scenes/Prototype/Fish/FishingMapBattle01.unity` のように作る。四隅の斜めの大きな壁と、ゴールの前を動く長い遮りが入っている |
/// | `釣りのマップをビルドの一覧に登録し直す` | ロビー・最初の会場・すべてのマップを Build Profiles のシーン一覧に入れ、通信の部品の番号が抜けていれば付ける。**マップの名前を変えたあとや、複製したあとに実行する** |
/// | `開いている釣りマップを点検する` | 今開いているマップに、遊ぶのに必要な物がそろっているかを調べて Console に出す |
///
/// ## `釣りのオンライン用シーンを作る` との違い
///
/// あちらは `FishingOnline.unity` を**毎回まるごと作り直す**ので、手で置いた物が消える。
/// このツールで作ったマップは**一度作ったら二度と上書きしない**ので、Unity 上で自由に作り変えてよい。
///
/// ## ロビーとのつながり
///
/// 名前が `FishingMap` で始まり、ビルドの一覧に入っているシーンは、
/// **ロビーのマップ選びに自動で並ぶ**（`FishingLobbyUI`）。ロビーのシーンを触る必要はない。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingMapSetup
{
    /// <summary>マップのシーン名の頭。**ロビーはこの名前で始まるシーンを候補に並べる。**</summary>
    public const string MapPrefix = "FishingMap";

    /// <summary>対戦用マップのシーン名の頭。**`MapPrefix` で始めてあるので、ロビーの候補にもそのまま並ぶ。**</summary>
    public const string BattleMapPrefix = MapPrefix + "Battle";

    /// <summary>マップを探す範囲。この下ならどのフォルダにあっても見つける。</summary>
    private const string SceneRootFolder = "Assets/Scenes";

    public const string LobbyScenePath = FishingSceneBuilder.SceneFolder + "/FishingLobby.unity";
    public const string OnlineScenePath = FishingSceneBuilder.SceneFolder + "/FishingOnline.unity";
    private const string LobbySceneName = "FishingLobby";

    /// <summary>プレイヤーが出てくる円の半径が読めなかったときに使う値（`FishingNetPlayer` の初期値）。</summary>
    private const float DefaultSpawnRadius = 6f;

    // ------------------------------------------------------------
    // 新しいマップを作る
    // ------------------------------------------------------------

    [MenuItem("Tools/StarSweepers/釣りの新しいマップを作る（オンライン）")]
    public static void CreateNewMap()
    {
        CreateMap(
            NextFreeMapPath(MapPrefix),
            BuildTemplateStage,
            "・「Stage」の中の床・壁と、「Obstacles」の中の障害物は、自由に作り変えてよい");
    }

    [MenuItem("Tools/StarSweepers/釣りの対戦用マップを作る（大きな遮り）")]
    public static void CreateBattleMap()
    {
        CreateMap(
            NextFreeMapPath(BattleMapPrefix),
            BuildBattleStage,
            "・四隅の斜めの大きな壁（Block_）は動かない。中央と外側を行き来する道を、北・東・南・西の4つの門にしぼっている\n" +
            "・ゴールの前を横切る長い遮り（Shutter_）は、端まで行くと1秒止まる。そのあいだだけゴールの正面が開く\n" +
            "・中央（半径6mの円）は、プレイヤーが出てくる場所なので何も置いていない\n" +
            "・4つの門と4つの遮りは同じ形・同じ動きなので、どのチームのゴールでも条件は同じ");
    }

    /// <summary>
    /// マップのシーンを1枚作る。**遊ぶための仕組みはどのマップでも同じ**なので、
    /// 違うのは <paramref name="buildStage"/> が置く「床の上の物」だけ。
    /// </summary>
    /// <param name="buildStage">
    /// 床・壁・ゴールを置いたあとに呼ばれる。1つ目は `Stage`（動かない物を入れる）、
    /// 2つ目は `Obstacles`（動く障害物を入れる）。
    /// </param>
    /// <param name="description">Console に出す、このマップの説明。</param>
    private static void CreateMap(string scenePath, System.Action<Transform, Transform> buildStage, string description)
    {
        // 開いているシーンに保存していない変更があれば、保存するか尋ねる（「キャンセル」なら何もしない）。
        // Unity を開かずに動かすとき（バッチ実行）は尋ねられないので飛ばす
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!CheckPrerequisites(out GameObject supplyPrefab, out GameObject bombPrefab))
        {
            return;
        }

        // マップの置き場所（`Scenes/Prototype/Fish`）が無ければ作る
        FishingSceneBuilder.EnsureFolders();

        string sceneName = Path.GetFileNameWithoutExtension(scenePath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();

        // ---- ステージ（ここを自由に作り変える）----
        // 床・四方の壁・ゴールは「Stage」の中に、動く障害物は「Obstacles」の中にまとめる
        GameObject stage = new GameObject("Stage");
        GameObject obstacles = new GameObject("Obstacles");

        // 床と壁を作る処理は、シーンの一番上に物を置く。置く前の一覧を控えておき、増えた物だけを Stage の中へ移す
        List<GameObject> before = RootObjects(scene);
        FishingSceneBuilder.CreateGround();
        FishingNetSceneBuilder.CreateNetworkPocketWalls();
        MoveNewRootsUnder(scene, before, stage.transform);

        // ---- マップごとの中身 ----
        buildStage(stage.transform, obstacles.transform);

        // ---- ここから下は遊ぶための仕組み（消さない）----
        CreatePlaySystems(supplyPrefab, bombPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);

        // 保存したあとで、通信の部品に番号を付けて保存し直す（EnsureNetworkIds の説明を参照）
        EnsureNetworkIdsInScene(scenePath);

        RegisterMapsInBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"【マップ】新しいマップ「{sceneName}」を作りました。\n" +
            "場所: " + scenePath + "\n" +
            description + "\n" +
            "・FishingMatch / FishingHUD / Main Camera / FishingObjectSpawner / HookUI は遊ぶための仕組みなので消さない\n" +
            "・ビルドの一覧に登録したので、ロビーのマップ選びに出てくる\n" +
            "・作り変えたら `Tools > StarSweepers > 開いている釣りマップを点検する` で確かめる");

        CheckOpenMap();
    }

    /// <summary>
    /// **遊ぶための仕組み**（どのマップでも同じ物）を置く。シーンの並び順は
    /// FishingMatch → FishingHUD → Main Camera → FishingObjectSpawner → HookUI（この順番で作る）。
    /// ここで置く物は、`CheckOpenMap` の点検で「そろっているか」を確かめている物でもある。
    /// </summary>
    private static void CreatePlaySystems(GameObject supplyPrefab, GameObject bombPrefab)
    {
        // 試合のまとめ役。全員のPCで同じ物として扱うので NetworkObject を付ける
        GameObject matchObject = new GameObject("FishingMatch");
        matchObject.AddComponent<NetworkObject>();
        matchObject.AddComponent<FishingMatch>();

        // 点数・残り時間・結果の表示。結果の画面の「ロビーへ戻る」で、lobbySceneName のシーンへ全員を戻す
        // ⚠ "lobbySceneName" は FishingMatchUI のフィールド名（向こうを改名すると静かに壊れる）
        GameObject hudObject = new GameObject("FishingHUD");
        hudObject.AddComponent<FishingStatusUI>();
        FishingMatchUI matchUI = hudObject.AddComponent<FishingMatchUI>();
        FishingSceneBuilder.SetString(matchUI, "lobbySceneName", LobbySceneName);

        // 見下ろしカメラ。位置と角度は FishingSceneBuilder.CreateCamera と同じ（理由はそちらを参照）。
        // 追う相手は、自分のプレイヤーが生まれた時点で決まるので、ここでは空のまま
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<TopDownCameraFollow>();

        // 物資と爆発物は、今あるオンライン用のプレハブを使う（作り直さない）
        FishingSpawnerSetup.CreateSpawner(supplyPrefab, bombPrefab);

        FishingSceneBuilder.CreateUI();
    }

    // ------------------------------------------------------------
    // 雛形のステージ（まっさらなマップ）
    // ------------------------------------------------------------

    /// <summary>床・壁・ゴールだけの雛形に、動く障害物の見本を1つ置く。</summary>
    private static void BuildTemplateStage(Transform stage, Transform obstacles)
    {
        Material obstacleMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingObstacleTestSetup.ObstacleMaterialPath, new Color(0.55f, 0.3f, 0.75f));

        // 北寄り（中心から 12m）に、幅 6m の板を置く。左右 9m ずつを、秒速 3m で行き来し、端で 0.5秒止まる。
        // stage は使わない（床・壁・ゴールは CreateMap で入れてある）
        GameObject sample = FishingObstacleTestSetup.CreateObstacle(
            "Obstacle_Sample", new Vector3(0f, 0f, 12f), 0f,
            new Vector3(6f, 1.2f, 1f), obstacleMaterial,
            new[] { new Vector3(-9f, 0f, 0f), new Vector3(9f, 0f, 0f) },
            MovingObstacle.PathMode.PingPong, 3f, 0.5f, false);

        sample.transform.SetParent(obstacles, true);
    }

    // ------------------------------------------------------------
    // 対戦用のステージ（大きな遮り）
    // ------------------------------------------------------------

    /// <summary>四隅の斜めの大きな壁：中心からどれだけ離した所に置くか（X・Zとも／メートル）。</summary>
    private const float BlockOffset = 8f;

    /// <summary>四隅の斜めの大きな壁：長さと厚み（メートル）。</summary>
    private const float BlockLength = 15f;
    private const float BlockThickness = 1.2f;

    /// <summary>ゴールの前を横切る遮り：中心からの距離（メートル）。</summary>
    private const float ShutterLine = 15.5f;

    /// <summary>ゴールの前を横切る遮り：長さ・厚み・中央からどこまで動くか（メートル）。</summary>
    private const float ShutterLength = 11f;
    private const float ShutterThickness = 1.2f;
    private const float ShutterTravel = 8f;

    /// <summary>ゴールの前を横切る遮り：動く速さ（m/秒）と、端で止まっている秒数。</summary>
    private const float ShutterSpeed = 4f;
    private const float ShutterWaitSeconds = 1f;

    /// <summary>
    /// **対戦用ステージの中身。広い面でプレイヤーをまとめて足止めする置き方にしてある。**
    ///
    /// 40m × 40m の床を、真上から見るとこうなる。
    ///
    /// <code>
    ///               ゴール（北）
    ///       ┌────────■■■────────┐
    ///       │  ←───── 遮り ─────→ │
    ///       │    ＼           ／   │
    ///  ゴ ■ │     ＼  中央   ／    │ ■ ゴ
    ///  ｜   │      （空ける）      │   ｜
    ///  ル   │     ／         ＼    │   ル
    ///       │    ／           ＼   │
    ///       │  ←───── 遮り ─────→ │
    ///       └────────■■■────────┘
    ///               ゴール（南）
    /// </code>
    ///
    /// ・**＼／ … 斜めの大きな壁**（動かない。長さ15m）
    /// ・**遮り … 長さ11mの板**（左右に動く。端で1秒止まる）
    /// ・**中央 … 半径6mの円**（プレイヤーが出てくる場所なので何も置かない）
    ///
    /// ねらいは次の3つ。
    ///
    /// 1. **斜めの大きな壁**が対角線をふさぐので、中央と外側の行き来は北・東・南・西の**4つの門**だけになる。
    ///    まっすぐ突っ切れないぶん、どの道を通るかの読み合いが生まれる。
    /// 2. **ゴールの前を横切る長い遮り**が、ゴールの正面を広く覆う。端まで行って止まっているあいだだけ正面が開く。
    /// 3. **中央と四隅**は広く空けてあるので、逃げ場が無くならない。
    ///
    /// 4つの門も4つの遮りも**同じ形・同じ動き**なので、どのチームのゴールでも条件は同じになる。
    /// </summary>
    private static void BuildBattleStage(Transform stage, Transform obstacles)
    {
        // 四方の壁と同じ素材（FishingSceneBuilder の壁と同じパス・同じ色）
        Material wallMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
        Material obstacleMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingObstacleTestSetup.ObstacleMaterialPath, new Color(0.55f, 0.3f, 0.75f));

        // ---- 四隅の斜めの大きな壁（動かない）----
        CreateBattleBlock("Block_NE", new Vector3(BlockOffset, 0f, BlockOffset), 45f, wallMaterial, stage);
        CreateBattleBlock("Block_SE", new Vector3(BlockOffset, 0f, -BlockOffset), -45f, wallMaterial, stage);
        CreateBattleBlock("Block_SW", new Vector3(-BlockOffset, 0f, -BlockOffset), 45f, wallMaterial, stage);
        CreateBattleBlock("Block_NW", new Vector3(-BlockOffset, 0f, BlockOffset), -45f, wallMaterial, stage);

        // ---- ゴールの前を横切る、長い遮り（動く）----
        // 4つとも同じ動かし方なので、開くときは4か所いっせいに開く
        CreateBattleShutter("Shutter_North", new Vector3(0f, 0f, ShutterLine), 0f, obstacleMaterial, obstacles);
        CreateBattleShutter("Shutter_East", new Vector3(ShutterLine, 0f, 0f), 90f, obstacleMaterial, obstacles);
        CreateBattleShutter("Shutter_South", new Vector3(0f, 0f, -ShutterLine), 0f, obstacleMaterial, obstacles);
        CreateBattleShutter("Shutter_West", new Vector3(-ShutterLine, 0f, 0f), 90f, obstacleMaterial, obstacles);
    }

    /// <summary>斜めの大きな壁を1枚置く（動かない）。足元が床（高さ0）に来るように持ち上げる。</summary>
    private static void CreateBattleBlock(string name, Vector3 position, float yaw, Material material, Transform parent)
    {
        float height = FishingSceneBuilder.WallHeight;

        GameObject block = FishingSceneBuilder.CreateBox(name,
            position + new Vector3(0f, height * 0.5f, 0f),
            new Vector3(BlockLength, height, BlockThickness), material);

        block.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        block.transform.SetParent(parent, true);
    }

    /// <summary>
    /// ゴールの前を横切る遮りを1つ置く（動く）。
    /// **長い辺と同じ向きに動く**ので、置いた向き（<paramref name="yaw"/>）から動く向きを決めている。
    /// </summary>
    private static void CreateBattleShutter(string name, Vector3 position, float yaw, Material material, Transform parent)
    {
        Vector3 axis = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        GameObject shutter = FishingObstacleTestSetup.CreateObstacle(
            name, position, yaw,
            new Vector3(ShutterLength, FishingSceneBuilder.WallHeight, ShutterThickness), material,
            new[] { -axis * ShutterTravel, axis * ShutterTravel },
            MovingObstacle.PathMode.PingPong, ShutterSpeed, ShutterWaitSeconds, true);

        shutter.transform.SetParent(parent, true);
    }

    /// <summary>雛形に使うオンライン用のプレハブと、ロビーがあるかを確かめる。</summary>
    private static bool CheckPrerequisites(out GameObject supplyPrefab, out GameObject bombPrefab)
    {
        supplyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpawnerSetup.OnlineSupplyPrefabPath);
        bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpawnerSetup.OnlineBombPrefabPath);

        bool lobbyExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyScenePath) != null;

        if (supplyPrefab != null && bombPrefab != null && lobbyExists)
        {
            return true;
        }

        Debug.LogError(
            "【マップ】マップを作る前に必要な物がありません。\n" +
            (lobbyExists ? "" : "・ロビーのシーン " + LobbyScenePath + "\n") +
            (supplyPrefab != null ? "" : "・" + FishingSpawnerSetup.OnlineSupplyPrefabPath + "\n") +
            (bombPrefab != null ? "" : "・" + FishingSpawnerSetup.OnlineBombPrefabPath + "\n") +
            "先に `Tools > StarSweepers > 釣りのオンライン用シーンを作る（ロビー＋会場）` を実行してください。");
        return false;
    }

    /// <summary>
    /// `FishingMap01` のように、<paramref name="prefix"/> のあとに番号を付けて、まだ無い名前を探す。**既存のマップは上書きしない。**
    ///
    /// 置き場所が違っても**名前が同じマップは作らない**。
    /// （`Scenes/Test` に `FishingMap01` が残っている間も、番号がぶつからないようにするため。
    ///  同じ名前のシーンが2つあると、ビルドの一覧とロビーのマップ選びで取り違える）
    /// </summary>
    private static string NextFreeMapPath(string prefix)
    {
        HashSet<string> usedNames = new HashSet<string>();
        foreach (string path in FindMapScenePaths())
        {
            usedNames.Add(Path.GetFileNameWithoutExtension(path));
        }

        // 空いている番号が見つかるまで 01, 02, … と順に試す（マップの数は有限なので、必ずどこかで見つかる）
        for (int number = 1; ; number++)
        {
            string name = $"{prefix}{number:00}";
            string path = $"{FishingSceneBuilder.MapSceneFolder}/{name}.unity";

            if (!usedNames.Contains(name) &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null && !File.Exists(path))
            {
                return path;
            }
        }
    }

    /// <summary>シーンの一番上（親の無い物）の一覧を、今の時点で写し取る。</summary>
    private static List<GameObject> RootObjects(Scene scene)
    {
        return new List<GameObject>(scene.GetRootGameObjects());
    }

    /// <summary><paramref name="before"/> のあとに増えた一番上の物を、<paramref name="parent"/> の中へ移す。</summary>
    private static void MoveNewRootsUnder(Scene scene, List<GameObject> before, Transform parent)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!before.Contains(root) && root.transform != parent)
            {
                root.transform.SetParent(parent, true);
            }
        }
    }

    // ------------------------------------------------------------
    // 通信の部品の番号
    // ------------------------------------------------------------

    /// <summary>
    /// シーンを開き直し、**番号（GlobalObjectIdHash）が 0 のままの通信の部品に番号を付けて**保存する。
    ///
    /// Netcode は、シーンに置いた NetworkObject（試合のまとめ役・ゴール）を**この番号で全員のPCで対応づける。**
    /// 番号は Netcode が自動で付けるが、**付けられるのはシーンが保存されたあと**。
    /// ツールで「部品を置く → 保存」とすると、置いた時点ではまだ保存されていないので **0 のまま残る**
    /// （0 のままだと、参加者の画面でゴールや試合が同期されない）。
    /// そこで保存したあとにもう一度開き直して番号を付けさせ、保存し直している。
    ///
    /// ⚠ **開き直した時点で、番号はメモリ上では付いている**（読み込み時に Netcode が付ける）が、
    /// 「変更あり」の印が付かないので、**そのままでは保存されず、ファイルは 0 のまま**になる。
    /// だから番号の有無にかかわらず、必ず「変更あり」にして保存する（番号は同じ値になるので、何度やっても変わらない）。
    /// </summary>
    private static void EnsureNetworkIdsInScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int remaining = EnsureNetworkIds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (remaining > 0)
        {
            Debug.LogWarning($"【マップ】{scene.name} に、番号が付けられなかった通信の部品が {remaining} 個あります。" +
                             "Unity でシーンを開いて Ctrl+S で保存し直してください。");
        }
    }

    /// <summary>今開いているシーンの NetworkObject すべてに番号を付けさせる。戻り値は、それでも 0 のままの数。</summary>
    private static int EnsureNetworkIds()
    {
        // NetworkObject の OnValidate は、ふつうのコードからは呼べないので、名前（文字列）で探して呼ぶ。
        // ⚠ Netcode の更新でこの名前が変わると、validate が null になり、**エラーも出さずに何もしなくなる**
        System.Reflection.MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        int remaining = 0;

        foreach (NetworkObject networkObject in FindAllInScene<NetworkObject>())
        {
            // Netcode が番号を付けるときに使う処理を、そのまま呼ぶ
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);

            if (ReadNetworkId(networkObject) == 0)
            {
                remaining++;
            }
        }

        return remaining;
    }

    /// <summary>
    /// NetworkObject の番号を読む（外から直接読めない項目なので、保存される値として読む）。
    /// 項目が見つからないときは -1 を返す。
    /// ⚠ `"GlobalObjectIdHash"` は Netcode 側の項目名。Netcode の更新で名前が変わると -1 が返り、
    /// **「0 ではない＝番号あり」と見なされて、点検をすり抜ける**。
    /// </summary>
    private static long ReadNetworkId(NetworkObject networkObject)
    {
        SerializedProperty property = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash");
        return property != null ? property.longValue : -1;
    }

    // ------------------------------------------------------------
    // ビルドの一覧への登録
    // ------------------------------------------------------------

    [MenuItem("Tools/StarSweepers/釣りのマップをビルドの一覧に登録し直す")]
    public static void RegisterMapsFromMenu()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // 番号付けのためにマップを1枚ずつ開くので、終わったら元のシーンを開き直せるよう控えておく
        string openScenePath = SceneManager.GetActiveScene().path;

        // 複製したマップなどで、通信の部品の番号が 0 のまま残っていないかも直しておく
        foreach (string path in FindMapScenePaths())
        {
            EnsureNetworkIdsInScene(path);
        }

        int added = RegisterMapsInBuildSettings();
        AssetDatabase.SaveAssets();

        if (!string.IsNullOrEmpty(openScenePath))
        {
            EditorSceneManager.OpenScene(openScenePath, OpenSceneMode.Single);
        }

        Debug.Log($"【マップ】ビルドの一覧を確かめました（新しく入れた数：{added}）。\n" +
                  "入っているマップ：" + string.Join(" / ", FindMapScenePaths()));
    }

    /// <summary>
    /// ロビー・最初の会場・すべてのマップを、ビルドのシーン一覧に入れる。
    /// **Netcode でシーンをまとめて切り替えるには、一覧に入っている必要がある。**
    /// ロビーは起動時に開くよう先頭へ置く。戻り値は新しく入れた（または有効に戻した）数。
    ///
    /// ※ 実際の動きは「**一覧に無い物を、ロビー → 最初の会場 → マップの順に後ろへ足す**」だけで、並べ替えはしない。
    /// ロビーが先頭になるのは、一覧が空だった（または先頭にすでにロビーがある）場合だけ。
    /// すでに一覧に入っている物の順番は変えない（何度実行しても増えない）。
    /// </summary>
    public static int RegisterMapsInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int changed = 0;

        List<string> wanted = new List<string> { LobbyScenePath, OnlineScenePath };
        wanted.AddRange(FindMapScenePaths());

        foreach (string path in wanted)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                continue;
            }

            // EditorBuildSettingsScene はクラスなので、見つけた物の enabled を書き換えると一覧の中身も変わる
            EditorBuildSettingsScene existing = scenes.Find(entry => entry.path == path);

            if (existing == null)
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                changed++;
            }
            else if (!existing.enabled)
            {
                existing.enabled = true;
                changed++;
            }
        }

        if (changed > 0)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        return changed;
    }

    /// <summary>
    /// 名前が `FishingMap` で始まるシーンを、**`Assets/Scenes` の中からすべて**探す。
    ///
    /// 置き場所は問わない。新しいマップは `Scenes/Prototype/Fish` に作られるが、
    /// 昔のマップが `Scenes/Test` に残っていても拾えるようにしてある。
    /// </summary>
    public static List<string> FindMapScenePaths()
    {
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneRootFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).StartsWith(MapPrefix))
            {
                paths.Add(path);
            }
        }

        paths.Sort();
        return paths;
    }

    // ------------------------------------------------------------
    // 点検
    // ------------------------------------------------------------

    /// <summary>
    /// 今開いているマップを点検し、結果を Console に出す（バッチ実行でなければ、ダイアログでも出す）。
    ///
    /// 「直したほうがよい所」（problems）と、「知っておくとよいこと」（notes：物資が降る範囲など）を分けて集める。
    /// **シーンは書き換えない**（調べるだけ）。マップを作った直後にも自動で呼ばれる。
    /// </summary>
    [MenuItem("Tools/StarSweepers/開いている釣りマップを点検する")]
    public static void CheckOpenMap()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<string> problems = new List<string>();
        List<string> notes = new List<string>();

        CheckSceneName(scene, problems);
        CheckSystems(problems);
        CheckPockets(problems);
        CheckSpawner(problems, notes);
        CheckSpawnRing(problems, notes);

        string header = $"【マップの点検】{scene.name}\n";

        if (problems.Count == 0)
        {
            Debug.Log(header + "✔ 遊ぶのに必要な物はそろっています。\n" + string.Join("\n", notes));
        }
        else
        {
            Debug.LogWarning(header + $"✖ 直したほうがよい所が {problems.Count} 件あります。\n・" +
                             string.Join("\n・", problems) +
                             (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : ""));
        }

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("釣りマップの点検",
                problems.Count == 0
                    ? "遊ぶのに必要な物はそろっています。"
                    : $"直したほうがよい所が {problems.Count} 件あります。\n\n・" + string.Join("\n・", problems),
                "OK");
        }
    }

    /// <summary>
    /// ロビーのマップ選びに出る条件（名前が `FishingMap` で始まる・ビルドの一覧に入っている）を満たしているか。
    /// 最初の会場（`FishingOnline.unity`）だけは、名前が違ってもよい。
    /// </summary>
    private static void CheckSceneName(Scene scene, List<string> problems)
    {
        if (string.IsNullOrEmpty(scene.path))
        {
            problems.Add("シーンがまだ保存されていません。");
            return;
        }

        string name = Path.GetFileNameWithoutExtension(scene.path);
        if (!name.StartsWith(MapPrefix) && scene.path != OnlineScenePath)
        {
            problems.Add($"シーン名が「{MapPrefix}」で始まっていないので、ロビーのマップ選びに出ません（今：{name}）。");
        }

        bool inBuildList = System.Array.Exists(EditorBuildSettings.scenes,
            entry => entry.enabled && entry.path == scene.path);
        if (!inBuildList)
        {
            problems.Add("ビルドの一覧に入っていません。`釣りのマップをビルドの一覧に登録し直す` を実行してください。");
        }
    }

    /// <summary>
    /// 遊ぶための仕組み（<see cref="CreatePlaySystems"/> で置く物）がそろっているか。
    /// あわせて、**マップに置いてはいけない物**（NetworkManager・プレイヤー）が無いかも確かめる。
    /// </summary>
    private static void CheckSystems(List<string> problems)
    {
        FishingMatch[] matches = FindAllInScene<FishingMatch>();
        if (matches.Length != 1)
        {
            problems.Add($"FishingMatch（試合のまとめ役）はちょうど1つ必要です（今：{matches.Length}）。");
        }
        else if (matches[0].GetComponent<NetworkObject>() == null)
        {
            problems.Add("FishingMatch に NetworkObject が付いていません。");
        }

        // **カメラと耳が二重になっていないか。**
        // 空のシーンから作ると、最初からある Main Camera を消し忘れたまま
        // 撮影用のプレハブを置いてしまいやすい。音が二重になり、どちらが写るかも決まらない
        Camera[] cameras = FindAllInScene<Camera>();
        if (cameras.Length > 1)
        {
            problems.Add($"カメラが {cameras.Length} 個あります。**追いかけるカメラ1つだけ**にしてください" +
                         "（空のシーンに元からある Main Camera を消し忘れていませんか）。");
        }

        AudioListener[] listeners = FindAllInScene<AudioListener>();
        if (listeners.Length > 1)
        {
            problems.Add($"音を聞く部品（AudioListener）が {listeners.Length} 個あります。1つにしてください。");
        }

        RequireOne<FishingMatchUI>("FishingMatchUI（残り時間と結果の表示）", problems);
        RequireOne<FishingStatusUI>("FishingStatusUI（点数の表示）", problems);
        RequireOne<HookChargeUI>("HookChargeUI（チャージのゲージ）", problems);
        RequireOne<TopDownCameraFollow>("TopDownCameraFollow の付いたカメラ", problems);

        // プレイヤーはロビーから連れてくるので、マップの中に置いてはいけない物
        if (FindAllInScene<NetworkManager>().Length > 0)
        {
            problems.Add("NetworkManager が置かれています。**マップには置かない**（ロビーから連れてくる）。");
        }

        int zeroIds = 0;
        foreach (NetworkObject networkObject in FindAllInScene<NetworkObject>())
        {
            if (ReadNetworkId(networkObject) == 0)
            {
                zeroIds++;
            }
        }
        if (zeroIds > 0)
        {
            problems.Add($"通信の部品のうち {zeroIds} 個に番号（GlobalObjectIdHash）が付いていません。" +
                         "シーンを保存（Ctrl+S）するか、`釣りのマップをビルドの一覧に登録し直す` を実行してください。");
        }

        if (FindAllInScene<FishingPlayerController>().Length > 0)
        {
            problems.Add("プレイヤーが置かれています。**マップには置かない**（ロビーで生まれた本体がそのまま来る）。");
        }
    }

    /// <summary>
    /// 型 <typeparamref name="T"/> の部品が**1つ以上**あるか（0個のときだけ問題にする。2つ以上あっても問題にはしない）。
    /// </summary>
    private static void RequireOne<T>(string label, List<string> problems) where T : Object
    {
        int count = FindAllInScene<T>().Length;
        if (count == 0)
        {
            problems.Add($"{label} がありません。");
        }
    }

    /// <summary>
    /// 今開いているシーンから、型 <typeparamref name="T"/> の部品をすべて探す。
    /// **隠れている（非アクティブの）物も含める**（番号付けや点検で見落とさないため）。並び順は決まっていない。
    /// </summary>
    private static T[] FindAllInScene<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>ゴールが4つあり、Pocket Index が 0〜3 で重なっていないか。</summary>
    private static void CheckPockets(List<string> problems)
    {
        FishingNetPocket[] pockets = FindAllInScene<FishingNetPocket>();

        if (pockets.Length != FishingTeams.PocketCount)
        {
            problems.Add($"ゴール（FishingNetPocket）はちょうど {FishingTeams.PocketCount} つ必要です（今：{pockets.Length}）。");
        }

        HashSet<int> used = new HashSet<int>();

        foreach (FishingNetPocket pocket in pockets)
        {
            int index = pocket.PocketIndex;

            if (index < 0 || index >= FishingTeams.PocketCount)
            {
                problems.Add($"ゴール「{pocket.name}」の Pocket Index が 0〜{FishingTeams.PocketCount - 1} の外です（今：{index}）。");
            }
            else if (!used.Add(index))
            {
                problems.Add($"Pocket Index {index} のゴールが2つ以上あります（「{pocket.name}」）。0〜3 を1つずつ使ってください。");
            }

            if (pocket.GetComponent<NetworkObject>() == null)
            {
                problems.Add($"ゴール「{pocket.name}」に NetworkObject が付いていません。");
            }

            Collider trigger = pocket.GetComponent<Collider>();
            if (trigger == null || !trigger.isTrigger)
            {
                problems.Add($"ゴール「{pocket.name}」に、Is Trigger が ON の当たり判定が付いていません。");
            }

            // 色を塗る板が無いと、**どのゴールが自分のチームのものか分からない**
            if (pocket.GetComponentInChildren<Renderer>() == null)
            {
                problems.Add($"ゴール「{pocket.name}」に、チームの色に塗る板（見た目）がありません。" +
                             "子どもに板（Plane や Cube）を置いてください。持ち主の色が出ません。");
            }
        }
    }

    /// <summary>
    /// 物資を降らせる係がちょうど1つあり、出す物にオンライン用のプレハブが入っているか。
    /// あわせて、物資が降る範囲を notes に書き出す。
    ///
    /// ⚠ `"supplyPrefab"`・`"bombPrefab"`・`"areaHalfSize"` は FishingObjectSpawner のフィールド名。
    /// **向こうを改名すると、ここは NullReferenceException で止まる**（コンパイルエラーにはならない）。
    /// </summary>
    private static void CheckSpawner(List<string> problems, List<string> notes)
    {
        FishingObjectSpawner[] spawners = FindAllInScene<FishingObjectSpawner>();

        if (spawners.Length != 1)
        {
            problems.Add($"FishingObjectSpawner（物資を降らせる係）はちょうど1つ必要です（今：{spawners.Length}）。");
            return;
        }

        SerializedObject serialized = new SerializedObject(spawners[0]);
        CheckSpawnPrefab(serialized, "supplyPrefab", "Supply Prefab", problems);
        CheckSpawnPrefab(serialized, "bombPrefab", "Bomb Prefab", problems);

        Vector2 half = serialized.FindProperty("areaHalfSize").vector2Value;
        Vector3 center = spawners[0].transform.position;
        notes.Add($"物資が降る範囲：中心 ({center.x:0.#}, {center.z:0.#}) から X ±{half.x:0.#}m ／ Z ±{half.y:0.#}m" +
                  "（スポナーを選ぶとシーン画面に枠が出る。**ステージの広さを変えたら Area Half Size も合わせる**）");
    }

    /// <summary>
    /// スポナーの <paramref name="field"/> に、オンライン用（NetworkObject 付き）のプレハブが入っているか。
    /// <paramref name="label"/> は Inspector に出る項目名で、問題の文に使う。
    /// </summary>
    private static void CheckSpawnPrefab(SerializedObject spawner, string field, string label, List<string> problems)
    {
        GameObject prefab = spawner.FindProperty(field).objectReferenceValue as GameObject;

        if (prefab == null)
        {
            problems.Add($"スポナーの {label} が空です。");
        }
        else if (prefab.GetComponent<NetworkObject>() == null)
        {
            problems.Add($"スポナーの {label} が1人用のプレハブです（{prefab.name}）。オンライン用（FishingOnline〜）を入れてください。");
        }
    }

    /// <summary>
    /// プレイヤーが出てくる場所（中心から半径 6m の円の上に8か所）に、床があってふさがっていないか。
    /// プレイヤーの出てくる場所は `FishingNetPlayer` が決めていて、**マップ側では変えられない。**
    /// </summary>
    private static void CheckSpawnRing(List<string> problems, List<string> notes)
    {
        float radius = ReadSpawnRadius();
        // 再生していないエディタでは、置いた物を動かしても当たり判定の位置がすぐには追いつかない。
        // 下の Raycast が今の位置で当たるよう、先にそろえておく
        Physics.SyncTransforms();

        for (int i = 0; i < FishingTeams.MaxPlayers; i++)
        {
            float angle = i * (360f / FishingTeams.MaxPlayers);
            Vector3 point = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * radius);

            // 高さ 30m から真下へ 60m（高さ -30m まで）光線を飛ばし、最初に当たった物を見る。
            // すり抜ける当たり判定（ゴールの判定など）は無視する
            if (!Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) に床がありません。");
                continue;
            }

            // 当たった高さが床（高さ0）から ±0.3m を超えていたら、何かが上に乗っている／床が低すぎる
            if (hit.point.y > 0.3f)
            {
                string what = hit.collider.GetComponentInParent<MovingObstacle>() != null ? "動く障害物" : "物";
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) が{what}「{hit.collider.name}」でふさがっています。");
            }
            else if (hit.point.y < -0.3f)
            {
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) の床が低すぎます（高さ {hit.point.y:0.#}m）。床は高さ0に置いてください。");
            }
        }

        notes.Add($"プレイヤーが出てくる場所：中心 (0, 0) から半径 {radius:0.#}m の円の上（床は高さ0。ここはふさがない）");
    }

    /// <summary>
    /// プレイヤーが出てくる円の半径（m）を、オンライン用プレイヤーのプレハブから読む。
    /// 読めなければ <see cref="DefaultSpawnRadius"/> を使う。
    /// ⚠ `"spawnRadius"` は FishingNetPlayer のフィールド名（向こうを改名すると、いつも初期値の 6m で点検してしまう）。
    /// </summary>
    private static float ReadSpawnRadius()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingNetSceneBuilder.OnlinePlayerPrefabPath);
        FishingNetPlayer player = prefab != null ? prefab.GetComponent<FishingNetPlayer>() : null;

        if (player == null)
        {
            return DefaultSpawnRadius;
        }

        SerializedProperty property = new SerializedObject(player).FindProperty("spawnRadius");
        return property != null ? property.floatValue : DefaultSpawnRadius;
    }
}
