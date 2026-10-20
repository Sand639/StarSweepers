using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
// Key（押すキーの種類）は UnityEngine.InputSystem にある

/// <summary>
/// **宇宙ごみ集めの「ロビー」と「マップ」を作るツール。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみ集めのシーンを作る（ロビー＋マップ）」から実行する。
///
/// ## できるもの
///
///   ・`SpaceJunkLobby.unity`（2026/10/1 から `Assets/Scenes/Prototype/SpaceJunk/`。ツールは名前で探す） … つないで待ち合わせる部屋。
///     **設定端末**が置いてあり、ホストだけが近づいて `E` で詳細設定を開ける
///   ・`Assets/Scenes/Prototype/SpaceJunk/SpaceJunkMap01〜03.unity` …
///     **釣りのマップをコピーして、中の部品を宇宙ごみ用に差し替えたもの**
///   ・`Assets/Prefabs/SpaceJunk/Online/` … 試合の係・プレイヤー・素材3種類
///
/// ## 釣りを壊さないために
///
///   ・**釣りのシーンとプレハブには一切書き込まない。** マップはコピーしてから差し替える
///   ・**すでにある SpaceJunkMap は作り直さない。** 手で直した中身が消えないようにするため
///     （作り直したいときは、そのシーンを消してからもう一度実行する）
///
/// ## マップの差し替えでやっていること
///
/// | 釣りの部品 | 宇宙ごみの部品 |
/// | --- | --- |
/// | `FishingMatch`（試合のまとめ役） | `SpaceJunkRound`（1ラウンドの進行役） |
/// | `FishingMatchUI` / `FishingStatusUI` | `SpaceJunkMatchUI` |
/// | `FishingNetPocket`（ゴール） | `SpaceJunkGoal`（自陣ゴール） |
/// | `FishingObjectSpawner` | `SpaceJunkSpawner`（素材3種類） |
/// | 最初から置いてある物資・爆発物 | **取り除く**（スポナーが出すため） |
///
/// 地形・壁・障害物・カメラ・ゲージはそのまま残る。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkSetup
{
    public const string LobbySceneName = "SpaceJunkLobby";
    public const string MapSceneFolder = "Assets/Scenes/Prototype/SpaceJunk";

    /// <summary>ロビーがどこにも無いときに、新しく作る場所。</summary>
    private const string DefaultLobbyScenePath = MapSceneFolder + "/" + LobbySceneName + ".unity";

    /// <summary>
    /// **ロビーのシーンの置き場所。名前（SpaceJunkLobby）で探す**ので、Unity の中で移動しても追いかける。
    /// （2026/10/1：ロビーを `Scenes/Test/` から `Scenes/Prototype/SpaceJunk/` へ移したら、
    ///  置き場所を決め打ちしていたツールが「ロビーが見つかりません」になったため。`AIの申し送り.md` 2026/9/16 と同じ失敗）
    /// 見つからなければ <see cref="DefaultLobbyScenePath"/>。
    /// </summary>
    public static string LobbyScenePath
    {
        get
        {
            return FindScenePathByName(LobbySceneName) ?? DefaultLobbyScenePath;
        }
    }

    /// <summary>タイトルのシーンの名前（2026/10/6。ゲームはここから始まる）。</summary>
    public const string TitleSceneName = "TitleScene";

    /// <summary>
    /// **タイトルのシーンの置き場所。名前（TitleScene）で探す**（ロビーと同じく、移動しても追いかける）。見つからなければ null。
    /// </summary>
    public static string TitleScenePath
    {
        get
        {
            return FindScenePathByName(TitleSceneName);
        }
    }

    /// <summary>
    /// **名前がぴったり同じシーンを、プロジェクト全体から探す。** 見つからなければ null。
    /// `FindAssets` は名前の一部が合うものも返すことがある（`SpaceJunkLobby` で `SpaceJunkLobbyOld` も拾う、など）ので、
    /// ファイル名を見比べて、ぴったり同じものだけを返す。同じ名前が2つあれば、先に見つかった方。
    /// </summary>
    private static string FindScenePathByName(string sceneName)
    {
        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// コピー元の釣りマップと、コピー先の宇宙ごみマップ。
    /// 1行が1マップで、`[i, 0]` がコピー元、`[i, 1]` がコピー先。上から順に作る。
    /// </summary>
    private static readonly string[,] MapSources =
    {
        { "Assets/Scenes/Test/FishingMap01.unity", MapSceneFolder + "/SpaceJunkMap01.unity" },
        { "Assets/Scenes/Test/FishingMap02.unity", MapSceneFolder + "/SpaceJunkMap02.unity" },
        { "Assets/Scenes/Prototype/Fish/FishingMap03.unity", MapSceneFolder + "/SpaceJunkMap03.unity" },
        { "Assets/Scenes/Test/FishingOnline.unity", MapSceneFolder + "/SpaceJunkMap04.unity" }
    };

    /// <summary>
    /// **プレハブ・ロビー・マップをまとめて作る。** 何をするかはクラスの説明を参照。
    /// 作る順番に意味がある：プレハブ → ロビー（プレイヤーと試合の係のプレハブを使う）→ マップ（素材のプレハブを使う）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみ集めのシーンを作る（ロビー＋マップ）")]
    public static void CreateAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        FishingSceneBuilder.EnsureFolders();
        FishingSceneBuilder.EnsureFolder(MapSceneFolder);
        SpaceJunkPrefabBuilder.EnsureFolders();

        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        // プレハブ作りは作業用オブジェクトを一時的に置くので、空のシーンで行う
        // （いま開いているシーンを汚さないため）
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject sessionPrefab = SpaceJunkPrefabBuilder.CreateSessionPrefab(LobbySceneName);
        GameObject playerPrefab = SpaceJunkPrefabBuilder.CreatePlayerPrefab(inputActions);
        GameObject[] materialPrefabs = SpaceJunkPrefabBuilder.CreateMaterialPrefabs();

        CreateLobbyScene(playerPrefab, sessionPrefab);

        List<string> madeMaps = new List<string>();
        List<string> skippedMaps = new List<string>();
        ConvertMaps(materialPrefabs, madeMaps, skippedMaps);

        RegisterScenesInBuildSettings(madeMaps);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 2行目からの頭の全角の空白は、ログの「マップ　: 」の後ろに文字の位置を揃えるため
        string mapList = madeMaps.Count > 0 ? string.Join("\n　　　　　", madeMaps) : "（1つも作れませんでした）";
        string skipped = skippedMaps.Count > 0
            ? "\n\n※ すでにあったので作り直さなかったマップ：\n　　　　　" + string.Join("\n　　　　　", skippedMaps) +
              "\n　作り直したいときは、そのシーンを消してからもう一度実行してください。"
            : string.Empty;

        Debug.Log(
            "【宇宙ごみ】ロビーとマップを作りました。\n" +
            "ロビー　: " + LobbyScenePath + "\n" +
            "マップ　: " + mapList + "\n" +
            "プレハブ: " + SpaceJunkPrefabBuilder.OnlinePrefabFolder + "\n" +
            "\n遊び方：\n" +
            "1. " + LobbySceneName + ".unity を開いて再生\n" +
            "2. 「インターネットで遊ぶ」→「部屋を作る」（合言葉が出るまで10〜20秒）\n" +
            "3. 相手に合言葉を伝えて入ってもらう（最大8人）\n" +
            "4. **ホストが設定端末に近づいて E キー** → チーム分け・何本先取・最大時間・使うマップを決める\n" +
            "5. 「ゲーム開始」を押す → 全員が1ラウンド目のマップへ\n" +
            "\n※ Build Settings にロビーとマップを自動で登録しました。" +
            "これが無いと、全員のシーンをまとめて切り替えられません。" +
            skipped);
    }

    /// <summary>
    /// **すでにあるマップを、作り直さずに点検するだけのメニュー。**
    ///
    /// 「ゴールが足りない」「進行役がいない」といった、遊ばないと気づきにくい抜けを探す。
    /// 手でマップを直したあとの確認にも使える。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのマップを点検する（作り直さない）")]
    public static void VerifyAllMaps()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        int checkedCount = 0;

        // **マップの一覧に入っているもの**と、ツールで作るマップの両方を点検する
        // （一覧には、どんな名前のマップでも入れられるため）
        List<string> paths = SpaceJunkMapListSetup.ScenePaths();

        // ツールで作るマップ（MapSources のコピー先）のうち、一覧に無いものを後ろに足す
        for (int i = 0; i < MapSources.GetLength(0); i++)
        {
            if (!paths.Contains(MapSources[i, 1]))
            {
                paths.Add(MapSources[i, 1]);
            }
        }

        foreach (string path in paths)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                continue;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            VerifyMap(path);
            checkedCount++;
        }

        if (checkedCount == 0)
        {
            Debug.LogWarning("[JUNK] 点検できる宇宙ごみのマップがありません。" +
                             "先に『宇宙ごみ集めのシーンを作る』を実行してください。");
            return;
        }

        // どの方角にゴールがあるかを一覧に控える（ロビーでチーム数に合わないマップを外すため）
        SpaceJunkMapListSetup.RefreshGoalInfo();

        Debug.Log($"[JUNK] 宇宙ごみのマップ {checkedCount} 個を点検しました。" +
                  "**赤いエラーが出ていなければ、全部そろっています。**");
    }

    // ------------------------------------------------------------
    // ロビーのシーン
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーのシーンを作る。**毎回作り直す**（釣りのロビーと同じ扱い）。
    /// 歩き回れる地面と、ホスト専用の設定端末を置く。
    /// </summary>
    private static void CreateLobbyScene(GameObject playerPrefab, GameObject sessionPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 作る順番がそのままシーンの中の並び順になるので、
        // 「光 → 地面 → カメラ → 設定端末 → 通信のまとめ役」の順は変えない
        FishingSceneBuilder.CreateLight();
        CreateLobbyGround();
        CreateLobbyCamera();
        CreateSettingsTerminal();
        CreateLobbyNetworkManager(playerPrefab, sessionPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LobbyScenePath);
    }

    /// <summary>歩き回れる地面。Unity の板（Plane）は 10m 四方なので、2倍にして 20m 四方にしている。</summary>
    private static void CreateLobbyGround()
    {
        Material groundMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
    }

    /// <summary>
    /// ロビーのカメラ。**追従は付けない**（ロビーでは全体が見えていればよい）。
    /// 手前（南）14m・高さ 14m から、45 度で見下ろす。
    /// </summary>
    private static void CreateLobbyCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 14f, -14f);
        cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<AudioListener>();
    }

    /// <summary>
    /// **通信のまとめ役（NetworkManager）と、ロビーで使う部品をまとめて1つのオブジェクトに付ける。**
    ///
    /// ⚠ 部品の中身を `FishingSceneBuilder.SetInt` / `SetRef` で**フィールドの名前（文字）を指定して**書き込んでいる。
    /// 書き込む先のフィールド名を変えると、エラーも出ずに値が入らなくなる（`SetInt` は何も言わない）ので、
    /// 名前を変えるときはここも合わせて直すこと。
    /// </summary>
    private static void CreateLobbyNetworkManager(GameObject playerPrefab, GameObject sessionPrefab)
    {
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777); // 初期値は「同じPCの中・7777番」。実際のつなぎ先は接続画面で変わる

        // ⚠ **AddComponent した直後の NetworkManager は NetworkConfig が null。**
        //   インスペクターから足したときは Reset() が呼ばれて作られるが、
        //   スクリプトの AddComponent では呼ばれないため、ここで自分で用意する
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
        FishingSceneBuilder.SetInt(internet, "maxPlayers", SpaceJunkTeams.MaxPlayers);

        // ロビーの画面（参加者一覧と、端末を開いたときの詳細設定）
        SpaceJunkLobbyUI lobbyUI = managerObject.AddComponent<SpaceJunkLobbyUI>();

        // **マップの一覧を結びつける。** ロビーの候補は、この一覧に入っているマップだけになる
        // （いまロビーを作っている最中なので、一覧の側からロビーを書き換えさせない）
        FishingSceneBuilder.SetRef(lobbyUI, "mapList", SpaceJunkMapListSetup.EnsureList(wireLobby: false));

        // 試合の係を、ホストが1つだけ出す役
        SpaceJunkSessionSpawner spawner = managerObject.AddComponent<SpaceJunkSessionSpawner>();
        FishingSceneBuilder.SetRef(spawner, "sessionPrefab", sessionPrefab);

        EditorUtility.SetDirty(manager);
    }

    /// <summary>
    /// ホストだけが使える設定端末を置く。ロビーの奥（北）5m に置く、高さ 1.5m の箱
    /// （箱の真ん中を高さ 0.75m に置いて、底を床に付けている）。
    /// 中身はフィールドの名前（文字）で書き込んでいるので、<see cref="SpaceJunkLobbyTerminal"/> の
    /// フィールド名を変えると静かに値が入らなくなる。
    /// </summary>
    private static void CreateSettingsTerminal()
    {
        Material terminalMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/SpaceJunkTerminal.mat", new Color(0.3f, 0.35f, 0.45f));

        GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        terminal.name = "SettingsTerminal";
        terminal.transform.position = new Vector3(0f, 0.75f, 5f);
        terminal.transform.localScale = new Vector3(1.2f, 1.5f, 0.6f);

        MeshRenderer renderer = terminal.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = terminalMaterial;

        SpaceJunkLobbyTerminal script = terminal.AddComponent<SpaceJunkLobbyTerminal>();
        FishingSceneBuilder.SetRef(script, "highlightRenderer", renderer);

        // **押すキーは明示的に入れておく。**
        // 以前 KeyCode（古い入力）で作っていたころのシーンが残っていると、
        // 数字だけがそのまま読まれて別のキーになってしまうため
        FishingSceneBuilder.SetInt(script, "interactKey", (int)Key.E);
    }

    // ------------------------------------------------------------
    // マップ（釣りのマップをコピーして差し替える）
    // ------------------------------------------------------------

    /// <summary>
    /// <see cref="MapSources"/> の釣りマップを順にコピーして、宇宙ごみのマップにする。
    /// </summary>
    /// <param name="made">
    /// 使えるマップの置き場所を書き足す。**作り直さずにとばしたマップも入る**
    /// （ビルドの一覧とマップの一覧には、とばしたマップも入れたいため）。
    /// </param>
    /// <param name="skipped">すでにあったので作り直さなかったマップの置き場所を書き足す（最後の知らせ用）。</param>
    private static void ConvertMaps(GameObject[] materialPrefabs, List<string> made, List<string> skipped)
    {
        for (int i = 0; i < MapSources.GetLength(0); i++)
        {
            string source = MapSources[i, 0];
            string destination = MapSources[i, 1];

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(source) == null)
            {
                Debug.LogWarning($"[JUNK] コピー元のシーンが見つかりません：{source}（とばします）");
                continue;
            }

            // **すでにあるマップは作り直さない。** 手で直した中身が消えないようにするため
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(destination) != null)
            {
                skipped.Add(destination);
                made.Add(destination);
                continue;
            }

            if (!AssetDatabase.CopyAsset(source, destination))
            {
                Debug.LogError($"[JUNK] シーンをコピーできませんでした：{source} → {destination}");
                continue;
            }

            ConvertOneMap(destination, materialPrefabs);
            made.Add(destination);
        }

        // **作ったマップを、マップの一覧にも入れる**（入っていないとロビーの候補に出ない）。
        // すでに一覧にあるものは、チェックや表示名を上書きしない
        foreach (string path in made)
        {
            SpaceJunkMapListSetup.AddMap(path);
        }
    }

    /// <summary>コピーしたマップを開いて、釣りの部品を宇宙ごみの部品に差し替える。</summary>
    private static void ConvertOneMap(string scenePath, GameObject[] materialPrefabs)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        ReplaceMatch();
        ReplaceHud();
        ReplaceGoals();
        ReplaceSpawner(materialPrefabs);
        RemovePlacedItems();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);

        VerifyMap(scenePath);
    }

    /// <summary>
    /// **できあがったマップが、ちゃんと遊べる形になっているかを点検する。**
    ///
    /// コピー元の釣りマップは、作られた時期によって中身がそろっていない。
    /// 足りないまま気づかずに遊ぶと、**あるチームだけ点が入らない**といった
    /// 分かりにくい形で出るので、作った直後にここで知らせる。
    ///
    /// （2026/9/20：`FishingMap03` にゴールが1つしか無く、コピー先の
    ///  `SpaceJunkMap03` も1つのままだったのを、遊んだあとに気づいた）
    /// </summary>
    internal static void VerifyMap(string scenePath)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        // 見つけた問題を、この順に並べて最後にまとめて出す（進行役 → ゴール → スポナー → 画面）
        List<string> problems = new List<string>();

        int rounds = FindAllInScene<SpaceJunkRound>().Length;

        if (rounds != 1)
        {
            problems.Add($"ラウンドの進行役（SpaceJunkRound）が {rounds} 個です。**1個必要**" +
                         "（コピー元に FishingMatch が無かった可能性があります）");
        }

        SpaceJunkGoal[] goals = FindAllInScene<SpaceJunkGoal>();

        // **ゴールは4つでなくてもよい**（1対1のマップは2つ。2026/9/30）。
        // 何チームまで遊べるかは、どの方角にゴールがあるかで決まり、ロビーはそれに合わせて候補を絞る
        if (goals.Length == 0)
        {
            problems.Add("ゴールが1つもありません。**どのチームも点を入れられません**");
        }

        // ゴールの番号（北=0/東=1/南=2/西=3）が重複していないか
        int goalMask = CheckGoalIndices(goals, problems);

        int spawners = FindAllInScene<SpaceJunkSpawner>().Length;

        if (spawners != 1)
        {
            problems.Add($"素材のスポナー（SpaceJunkSpawner）が {spawners} 個です。**1個必要**");
        }

        int uis = FindAllInScene<SpaceJunkMatchUI>().Length;

        if (uis != 1)
        {
            problems.Add($"プレイ中の画面（SpaceJunkMatchUI）が {uis} 個です。**1個必要**");
        }

        if (goals.Length > 0 && SpaceJunkTeams.MaxPlayableTeams(goalMask) < 2)
        {
            // エラーにはしない（1人で試すだけのマップもありうる）。ただ、対戦には使えないので知らせる
            Debug.LogWarning($"[JUNK] {name} … {SpaceJunkMapListSetup.DescribeGoals(goalMask)}。" +
                             "**2チームの対戦には使えません**（ゴールが2つ以上要る）。");
        }

        if (problems.Count == 0)
        {
            Debug.Log($"[JUNK] {name} … 点検OK（{SpaceJunkMapListSetup.DescribeGoals(goalMask)}。進行役・スポナー・画面がそろっています）");
            return;
        }

        Debug.LogError(
            $"[JUNK] **{name} は、このままでは正しく遊べません。**\n・" +
            string.Join("\n・", problems) +
            "\n\nUnity でこのシーンを開いて直してください。" +
            "ゴールを足すときは、**今あるマップのゴール（Goal_〜）をコピーして**貼り付け、" +
            "その `SpaceJunkGoal` の Goal Index を空いている番号にします" +
            "（4つなら 0＝北・1＝東・2＝南・3＝西。4つ未満なら、番号の小さい順に 青・赤・緑… のゴールになる）。\n" +
            "**釣りの `GoalArea.prefab` をそのまま置いても、宇宙ごみのゴールにはなりません。** 置いてしまったときは、" +
            "`Tools > StarSweepers > 開いているマップの釣りのゴールを宇宙ごみのゴールに直す` を実行してください。");
    }

    /// <summary>
    /// ゴールの番号（北=0/東=1/南=2/西=3）が、範囲の外になっていないか・重複していないかを調べ、
    /// 見つけた問題を problems に書き足す。
    /// 戻り値は、使われている番号をまとめた数（北=1 / 東=2 / 南=4 / 西=8 を足したもの。範囲の外の番号は入れない）。
    /// </summary>
    private static int CheckGoalIndices(SpaceJunkGoal[] goals, List<string> problems)
    {
        bool[] used = new bool[SpaceJunkTeams.GoalCount];

        foreach (SpaceJunkGoal goal in goals)
        {
            int index = goal.GoalIndex;

            if (index < 0 || index >= used.Length)
            {
                problems.Add($"ゴール「{goal.name}」の Goal Index が {index} です（0〜{used.Length - 1} のはず）");
                continue;
            }

            if (used[index])
            {
                problems.Add($"ゴールの Goal Index {index}（{SpaceJunkTeams.GoalPlaceName(index)}）が重複しています");
            }

            used[index] = true;
        }

        int goalMask = 0;
        for (int i = 0; i < used.Length; i++)
        {
            if (used[i])
            {
                goalMask |= 1 << i;
            }
        }

        return goalMask;
    }

    /// <summary>
    /// 開いているシーンから、その部品を**隠れている物も含めて**全部探す（並び順は決まっていない）。
    /// 隠れている物も含めるのは、差し替え・点検に漏れを出さないため。
    /// </summary>
    private static T[] FindAllInScene<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>試合のまとめ役 → 1ラウンドの進行役。**NetworkObject はそのまま使い回す。**</summary>
    private static void ReplaceMatch()
    {
        FishingMatch[] matches = FindAllInScene<FishingMatch>();

        foreach (FishingMatch match in matches)
        {
            GameObject host = match.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkRound";

            Object.DestroyImmediate(match);

            if (host.GetComponent<NetworkObject>() == null)
            {
                host.AddComponent<NetworkObject>();
            }

            if (host.GetComponent<SpaceJunkRound>() == null)
            {
                host.AddComponent<SpaceJunkRound>();
            }
        }
    }

    /// <summary>釣りの画面表示 → 宇宙ごみの画面表示。</summary>
    private static void ReplaceHud()
    {
        FishingMatchUI[] matchUis = FindAllInScene<FishingMatchUI>();

        foreach (FishingMatchUI matchUi in matchUis)
        {
            GameObject host = matchUi.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkHUD";

            Object.DestroyImmediate(matchUi);

            // 釣りの点数表示（爆発カウントダウンとスタン）は使わないので外す
            FishingStatusUI status = host.GetComponent<FishingStatusUI>();
            if (status != null)
            {
                Object.DestroyImmediate(status);
            }

            if (host.GetComponent<SpaceJunkMatchUI>() == null)
            {
                host.AddComponent<SpaceJunkMatchUI>();
            }
        }
    }

    /// <summary>
    /// 釣りのゴール → 自陣ゴール。**並び順の番号と床の見た目は引き継ぐ。**
    /// 読む側（`FishingNetPocket`）も書く側（`SpaceJunkGoal`）も**フィールドの名前（文字）で指定している**ので、
    /// どちらかのフィールド名を変えると、エラーも出ずに番号が 0 になる・床の見た目が外れる。
    /// </summary>
    private static void ReplaceGoals()
    {
        FishingNetPocket[] pockets = FindAllInScene<FishingNetPocket>();

        foreach (FishingNetPocket pocket in pockets)
        {
            GameObject host = pocket.gameObject;
            UnpackIfPrefabInstance(host);

            // 部品を外す前に、引き継ぐ値を控えておく
            int index = GetInt(pocket, "pocketIndex");
            Object padRenderer = GetRef(pocket, "padRenderer");

            host.name = host.name.Replace("Pocket_", "Goal_");

            Object.DestroyImmediate(pocket);

            SpaceJunkGoal goal = host.GetComponent<SpaceJunkGoal>();
            if (goal == null)
            {
                goal = host.AddComponent<SpaceJunkGoal>();
            }

            FishingSceneBuilder.SetInt(goal, "goalIndex", index);
            FishingSceneBuilder.SetRef(goal, "padRenderer", padRenderer);
        }
    }

    /// <summary>
    /// 釣りのスポナー → 素材3種類のスポナー。**出す範囲と高さは引き継ぐ。**
    /// 値はフィールドの名前（文字）で読み書きしているので、名前を変えるとエラーも出ずに 0 や空になる。
    /// </summary>
    /// <param name="materialPrefabs">
    /// 素材のプレハブ。<see cref="SpaceJunkPrefabBuilder.CreateMaterialPrefabs"/> の戻り値で、
    /// 装甲板・回路基板・燃料タンク の順に並んでいる前提。
    /// </param>
    private static void ReplaceSpawner(GameObject[] materialPrefabs)
    {
        FishingObjectSpawner[] spawners = FindAllInScene<FishingObjectSpawner>();

        foreach (FishingObjectSpawner spawner in spawners)
        {
            GameObject host = spawner.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkSpawner";

            // 部品を外す前に、引き継ぐ値を控えておく
            Vector2 area = GetVector2(spawner, "areaHalfSize");
            float dropHeight = GetFloat(spawner, "dropHeight");

            Object.DestroyImmediate(spawner);

            SpaceJunkSpawner replacement = host.GetComponent<SpaceJunkSpawner>();
            if (replacement == null)
            {
                replacement = host.AddComponent<SpaceJunkSpawner>();
            }

            SetVector2(replacement, "areaHalfSize", area);
            FishingSceneBuilder.SetFloat(replacement, "dropHeight", dropHeight);

            if (materialPrefabs.Length >= 3)
            {
                FishingSceneBuilder.SetRef(replacement, "platePrefab", materialPrefabs[0]);
                FishingSceneBuilder.SetRef(replacement, "circuitPrefab", materialPrefabs[1]);
                FishingSceneBuilder.SetRef(replacement, "fuelPrefab", materialPrefabs[2]);
            }
        }
    }

    /// <summary>
    /// **最初から置いてある物資・爆発物を取り除く。**
    /// 宇宙ごみではスポナーが素材を出すので、釣りの物が残っていると混ざってしまう。
    /// </summary>
    private static void RemovePlacedItems()
    {
        HookableObject[] items = FindAllInScene<HookableObject>();

        foreach (HookableObject item in items)
        {
            // 親ごと先に消えた物は null になっているので、とばす
            if (item != null)
            {
                Object.DestroyImmediate(item.gameObject);
            }
        }
    }

    // ------------------------------------------------------------
    // Build Settings への登録
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーとマップを Build Settings に登録する。
    ///
    /// **Netcode でシーンをまとめて切り替えるには、そのシーンがビルドの一覧に
    /// 入っている必要がある。** 入っていないと「読み込めません」で止まる。
    /// すでに入っていれば何もしない（何度実行しても増えない）。
    /// </summary>
    internal static void RegisterScenesInBuildSettings(List<string> mapPaths)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        bool changed = AddSceneIfMissing(scenes, LobbyScenePath);

        foreach (string path in mapPaths)
        {
            changed |= AddSceneIfMissing(scenes, path);
        }

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[JUNK] Build Settings に、ロビーと宇宙ごみのマップを登録しました。");
        }
    }

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

    // ------------------------------------------------------------
    // プレハブからの切り離し
    // ------------------------------------------------------------

    /// <summary>
    /// **差し替える相手がプレハブのインスタンスなら、先に切り離す。**
    ///
    /// 釣りのマップは、試合のまとめ役・ゴール・スポナーなどを**プレハブとして**置いている。
    /// そのまま部品を消したり足したりすると、**「プレハブに対する上書き」として記録される**ため、
    /// できあがった宇宙ごみのマップが**釣りのプレハブに繋がったまま**になる。
    ///
    /// その状態だと、**釣りのプレハブを直したときに宇宙ごみのマップも一緒に変わる**
    /// （消したはずの部品が復活する、など）。
    /// 宇宙ごみのマップは釣りとは別物にしたいので、ここで縁を切っておく。
    ///
    /// プレハブでなければ何もしない。
    /// （2026/9/20 に追加。それ以前に作られた `SpaceJunkMap` は繋がったままなので、
    ///  作り直すときに切り離される。`リスクリスト.md` に登録済み）
    /// </summary>
    internal static void UnpackIfPrefabInstance(GameObject target)
    {
        if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target))
        {
            return;
        }

        GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(target);

        if (instanceRoot == null)
        {
            return;
        }

        PrefabUtility.UnpackPrefabInstance(
            instanceRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Debug.Log($"[JUNK] 「{instanceRoot.name}」を釣りのプレハブから切り離しました。");
    }

    // ------------------------------------------------------------
    // 値を読む道具（FishingSceneBuilder には書き込む側しか無いため）
    // ------------------------------------------------------------
    //
    // ⚠ どれも**フィールドの名前を文字で指定して**読み書きする（private のフィールドにも届くため）。
    //   その名前のフィールドが無いときは、エラーを出さずに 0・null を返す／何もしない。
    //   つまり**読む先・書く先のフィールドを改名すると、静かに値が引き継がれなくなる。**
    //   改名するときは、このファイルの中の "フィールド名" の文字も合わせて直すこと。

    private static int GetInt(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.intValue : 0;
    }

    private static float GetFloat(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.floatValue : 0f;
    }

    private static Vector2 GetVector2(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.vector2Value : Vector2.zero;
    }

    private static Object GetRef(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.objectReferenceValue : null;
    }

    private static void SetVector2(Object owner, string field, Vector2 value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);

        if (property == null)
        {
            return;
        }

        property.vector2Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
