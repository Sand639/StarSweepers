using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **オンライン対戦の「ロビー」と「釣り会場」のシーンを作るツール。**
///
/// Unityのメニュー「Tools > StarSweepers > 釣りのオンライン用シーンを作る（ロビー＋会場）」から実行する。
///
/// できるもの：
///   ・`Assets/Scenes/Test/FishingLobby.unity` … つないで待ち合わせる部屋。
///     既存の接続画面（LAN／インターネット／1人）に、**ロビー表示とゲーム開始ボタン**を足したもの
///   ・`Assets/Scenes/Test/FishingOnline.unity` … みんなで釣る会場
///   ・`Assets/Prefabs/Fish/Online/FishingOnlinePlayer.prefab` … オンラインで1人分になる本体
///
/// **1人用のシーン（FishingHookTest / FishingArenaTest）は触らない。**
///
/// ⚠ **シーンを全員でまとめて切り替えるには、両方のシーンが Build Settings に
/// 入っている必要がある**（Netcode の決まり）。このツールが自動で登録する。
/// 検証用シーンをビルドに入れない決まりとぶつかるので、`質問リスト.md` に登録済み。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingOnlineSetup
{
    // ※ 同じ場所を FishingMapSetup・FishingSpawnerSetup・FishingOnlineBuilder も持っている。
    //   シーンの場所や名前を変えるときは、すべてそろえること
    private const string LobbyScenePath = FishingSceneBuilder.SceneFolder + "/FishingLobby.unity";
    private const string OnlineScenePath = FishingSceneBuilder.SceneFolder + "/FishingOnline.unity";

    // シーンを切り替えるときに使う名前（拡張子なし）。上のパスのファイル名と同じにしておく
    private const string OnlineSceneName = "FishingOnline";
    private const string LobbySceneName = "FishingLobby";

    /// <summary>
    /// ロビーと会場のシーン、オンライン用プレイヤーのプレハブをまとめて作り直す。
    /// **2つのシーンは毎回まるごと作り直す**ので、手で置いた物は消える
    /// （手で作り変えたいマップは `FishingMapSetup` の `釣りの新しいマップを作る` で作る）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/釣りのオンライン用シーンを作る（ロビー＋会場）")]
    public static void CreateAll()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        // 会場のシーンを先に作り、その中でプレハブも用意する
        // （プレハブ作りは作業用オブジェクトを一時的に置くため、
        //  いま開いているシーンを汚さないよう、新しいシーンにしてから行う）
        GameObject playerPrefab = CreateOnlineScene(inputActions);

        CreateLobbyScene(playerPrefab);

        RegisterScenesInBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【オンライン】ロビーと釣り会場のシーンを作りました。\n" +
            "ロビー: " + LobbyScenePath + "\n" +
            "会場　: " + OnlineScenePath + "\n" +
            "プレハブ: " + FishingNetSceneBuilder.OnlinePlayerPrefabPath + "\n" +
            "\n遊び方：\n" +
            "1. FishingLobby.unity を開いて再生\n" +
            "2. 「インターネットで遊ぶ」→「部屋を作る」（合言葉が出るまで10〜20秒）\n" +
            "3. 相手に合言葉を伝えて入ってもらう（最大8人）\n" +
            "4. 人数がそろったら、ホストが「ゲーム開始」を押す → 全員が会場へ移動\n" +
            "\n※ Build Settings に両方のシーンを自動で登録しました。" +
            "これが無いと、全員のシーンをまとめて切り替えられません。");
    }

    // ------------------------------------------------------------
    // ロビーのシーン
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーのシーンを作って保存する。<paramref name="playerPrefab"/> は、つないだ人ごとに生まれるプレイヤーのプレハブ。
    /// シーンの並び順は Directional Light → Ground → Main Camera → NetworkManager。
    /// </summary>
    private static void CreateLobbyScene(GameObject playerPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();

        // ロビーの床は会場より小さい 20m 四方（Plane は大きさ1倍で 10m 四方）。素材は会場の床と同じ
        Material groundMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // ロビーのカメラ。**追従は付けない**（ロビーでは全体が見えていればよい）
        // 高さ 12m・後ろへ 12m から、45度見下ろす
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 12f, -12f);
        cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<AudioListener>();

        // ---- 通信のまとめ役 ----
        CreateLobbyNetworkManager(playerPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LobbyScenePath);
    }

    /// <summary>
    /// ロビーの NetworkManager（通信のまとめ役）を作る。
    /// 既存の接続画面（LAN／インターネット／1人で練習）と、ロビー表示・ゲーム開始ボタンも、このオブジェクトに付ける。
    ///
    /// **NetworkManager はロビーにだけ置く。** 会場やマップへは、ロビーからそのまま連れていく
    /// （マップの点検 <c>FishingMapSetup.CheckOpenMap</c> でも、マップに置かれていたら問題として出す）。
    /// </summary>
    private static void CreateLobbyNetworkManager(GameObject playerPrefab)
    {
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        // つなぎ先の初期値は「このPC自身」（127.0.0.1）の 7777番。
        // 実際のつなぎ先は、接続画面（LanConnectionUi）や起動時の指定（LanAutoStart の -host / -client）で書き換わる
        transport.SetConnectionData("127.0.0.1", 7777);

        // ⚠ **AddComponent した直後の NetworkManager は、NetworkConfig が null。**
        //   Netcode 側のコードにも「コンポーネントを足したときに起こりうる」と書かれている。
        //   インスペクターから足したときは Reset() が呼ばれて作られるが、
        //   スクリプトの AddComponent では呼ばれないため、ここで自分で用意する。
        //   （用意しないと、次の行で NullReferenceException になる）
        if (manager.NetworkConfig == null)
        {
            manager.NetworkConfig = new NetworkConfig();
        }

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.TickRate = FishingNetSceneBuilder.NetworkTickRate;

        // **これがONでないと、全員のシーンをまとめて切り替えられない。**
        manager.NetworkConfig.EnableSceneManagement = true;

        // 既存の接続画面をそのまま使う（LAN／インターネット／1人で練習）
        managerObject.AddComponent<LanConnectionUi>();
        InternetConnection internet = managerObject.AddComponent<InternetConnection>();
        managerObject.AddComponent<LanAutoStart>();
        managerObject.AddComponent<LanConnectionLogger>();
        managerObject.AddComponent<NetworkStatusHud>();

        // **8人まで入れるようにする**（初期値は4のため）
        FishingSceneBuilder.SetInt(internet, "maxPlayers", FishingTeams.MaxPlayers);

        // ロビー表示とゲーム開始ボタン
        FishingLobbyUI lobbyUI = managerObject.AddComponent<FishingLobbyUI>();
        FishingSceneBuilder.SetString(lobbyUI, "gameSceneName", OnlineSceneName);

        // NetworkConfig を直接書き換えたので、「変更あり」の印を付けて保存に含める
        EditorUtility.SetDirty(manager);
    }

    // ------------------------------------------------------------
    // 釣り会場のシーン
    // ------------------------------------------------------------

    /// <summary>
    /// 釣り会場のシーンを作る。あわせてオンライン用プレイヤーのプレハブも用意して返す。
    /// 中身は `FishingMapSetup` で作るマップとほぼ同じ（遊ぶための仕組みは同じ。床の上の物が無い）。
    /// </summary>
    private static GameObject CreateOnlineScene(InputActionAsset inputActions)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // プレハブ類は、この新しいシーンの中で組んで保存する（作業用オブジェクトはすぐ消える）
        ExplosionEffect explosionPrefab = FishingSceneBuilder.CreateExplosionPrefab();
        GameObject playerPrefab = FishingNetSceneBuilder.CreateOnlinePlayerPrefab(inputActions);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();

        // ---- 試合のまとめ役（チーム数・点数・ゴールの割り当て）----
        GameObject matchObject = new GameObject("FishingMatch");
        matchObject.AddComponent<NetworkObject>();
        matchObject.AddComponent<FishingMatch>();

        // ---- 画面表示（点数・爆発カウントダウン・スタン）----
        // 点数はオンラインの試合から読むので ScoreBoard は入れない
        GameObject hudObject = new GameObject("FishingHUD");
        hudObject.AddComponent<FishingStatusUI>();

        // 残り時間と、決着したときの結果表示
        FishingMatchUI matchUI = hudObject.AddComponent<FishingMatchUI>();
        FishingSceneBuilder.SetString(matchUI, "lobbySceneName", LobbySceneName);

        // ---- 四方の壁とゴール ----
        FishingNetSceneBuilder.CreateNetworkPocketWalls();

        // ---- 見下ろしカメラ（追う相手は、自分のプレイヤーが生まれた時点で決まる）----
        // 位置と角度は FishingSceneBuilder.CreateCamera と同じ（理由はそちらを参照）
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<TopDownCameraFollow>();

        // ---- 物資と爆発物 ----
        // 決まった場所には置かず、ホストのスポナーが次々に出す（FishingObjectSpawner）
        FishingSpawnerSetup.CreatePrefabsAndSpawner(true, explosionPrefab);

        // ---- チャージとスキルチェックのゲージ ----
        // 自分のプレイヤーが生まれた時点で、そのプレイヤーに結びつけられる
        FishingSceneBuilder.CreateUI();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, OnlineScenePath);

        return playerPrefab;
    }

    // ------------------------------------------------------------
    // Build Settings への登録
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーと会場のシーンを Build Settings に登録する。
    ///
    /// **Netcode でシーンをまとめて切り替えるには、そのシーンがビルドの一覧に
    /// 入っている必要がある。** 入っていないと「読み込めません」で止まる。
    /// すでに入っていれば何もしない（何度実行しても増えない）。
    /// </summary>
    private static void RegisterScenesInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        bool changed = false;
        changed |= AddSceneIfMissing(scenes, LobbyScenePath);
        changed |= AddSceneIfMissing(scenes, OnlineScenePath);

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[FISH] Build Settings にロビーと会場のシーンを登録しました。");
        }
    }

    /// <summary>
    /// <paramref name="path"/> のシーンが一覧に無ければ後ろに足し、無効になっていれば有効に戻す。
    /// 一覧を変えたら true を返す（実際に Build Settings へ書き戻すのは呼び出し側）。
    /// ※ `FishingMapSetup.RegisterMapsInBuildSettings` も同じ考え方で、マップまでまとめて登録している。
    /// </summary>
    private static bool AddSceneIfMissing(List<EditorBuildSettingsScene> scenes, string path)
    {
        foreach (EditorBuildSettingsScene entry in scenes)
        {
            if (entry.path == path)
            {
                // 入っているが無効になっている場合は有効に戻す
                if (!entry.enabled)
                {
                    entry.enabled = true;
                    return true;
                }
                return false;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        return true;
    }
}
