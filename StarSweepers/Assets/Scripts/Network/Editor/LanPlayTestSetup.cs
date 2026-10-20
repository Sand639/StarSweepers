using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LAN通信の検証用シーンと、通信用プレイヤーのプレハブを作り直すツール。
///
/// Unityのメニュー「Tools > StarSweepers > アーカイブ > 通信の土台 > LAN通信の検証シーンを作り直す」から実行できる。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class LanPlayTestSetup
{
    // 作る場所。**すでに同じ名前のものがあれば上書きする**ので、手で直したプレハブやシーンは消える
    private const string PrefabFolder = "Assets/Prefabs/Common";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string PrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    private const string ScenePath = SceneFolder + "/LanPlayTest.unity";

    /// <summary>プレイヤーの操作に使う入力の設定。プレハブに差し込む。</summary>
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    // ------------------------------------------------------------
    // 遅れ（ラグ）に効く設定
    //
    // インターネット越しだと、次の3つが積み上がって「重い」と感じる。
    //   ① 通信そのものの往復（中継サーバー経由で 60〜80ms 前後）
    //   ② 送る回数（下の TickRate）
    //   ③ なめらかに見せるための待ち時間（下の InterpolationTime）
    //
    // ①は変えられないが、②と③は調整できる。
    // ------------------------------------------------------------

    /// <summary>1秒あたり何回、位置などを送るか。初期値は30。増やすと反応が良くなる</summary>
    private const uint NetworkTickRate = 60;

    /// <summary>プレイヤーをなめらかに見せるための待ち時間（秒）。初期値は0.1</summary>
    private const float PlayerInterpolationTime = 0.05f;

    /// <summary>箱をなめらかに見せるための待ち時間（秒）。物理なので少し長めにする</summary>
    private const float BoxInterpolationTime = 0.075f;

    /// <summary>
    /// メニューの入口。フォルダ → マテリアル → プレイヤーのプレハブ → シーンの順に作る
    /// （シーンがプレハブを参照するので、プレハブが先）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/通信の土台/LAN通信の検証シーンを作り直す")]
    public static void CreateAll()
    {
        // 親フォルダから順に作る（EnsureFolder は親があることを前提にしている）
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);

        Material bodyMaterial = CreateMaterial(MaterialFolder + "/NetworkPlayerBody.mat", Color.white);
        Material groundMaterial = CreateMaterial(MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        Material markerMaterial = CreateMaterial(MaterialFolder + "/NetworkMarker.mat", new Color(0.55f, 0.75f, 0.55f));

        GameObject prefab = CreatePlayerPrefab(bodyMaterial);
        CreateTestScene(prefab, groundMaterial, markerMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("LAN通信の検証シーンを作り直しました。\nシーン: " + ScenePath);
    }

    // ------------------------------------------------------------
    // プレイヤーのプレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// 通信用プレイヤーのプレハブを作って保存する。
    /// シーンに置かず、`NetworkManager` の「Player Prefab」に登録して、つながったときに自動で生まれる形で使う。
    /// </summary>
    private static GameObject CreatePlayerPrefab(Material bodyMaterial)
    {
        GameObject root = new GameObject("NetworkPlayer");

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.4f;

        // 通信で「同じ物」だと見分けるための印
        root.AddComponent<NetworkObject>();

        // 位置を他のPCへ送る部品。
        // Owner にすることで「動かしている本人が位置を送る」形になる
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        // ★遅れの体感に一番効く設定。
        //   初期値の 0.1 秒は「なめらかに見せるための待ち時間」で、
        //   そのぶん相手の動きが遅れて見える。半分にして反応を良くする
        networkTransform.PositionMaxInterpolationTime = PlayerInterpolationTime;
        networkTransform.RotationMaxInterpolationTime = PlayerInterpolationTime;

        // 位置は「届かなかったら次で埋まる」ものなので、
        // 届くまで待ち直さない送り方にすると引っかかりが減る
        networkTransform.UseUnreliableDeltas = true;

        // 見た目（当たり判定は CharacterController が持つので外す）
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        visual.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        MeshRenderer bodyRenderer = visual.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;

        // 前を向いているのが分かる印
        GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mark.name = "FrontMark";
        mark.transform.SetParent(root.transform, false);
        mark.transform.localPosition = new Vector3(0f, 1.3f, 0.42f);
        mark.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        Object.DestroyImmediate(mark.GetComponent<Collider>());
        mark.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

        NetworkPlayer player = root.AddComponent<NetworkPlayer>();

        // 得点。ホストだけが書き換えられる
        root.AddComponent<PlayerScore>();

        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning(InputActionsPath + " が見つかりません。入力の設定は手で入れてください。");
        }

        // NetworkPlayer の private な [SerializeField] に、Inspector で入れるのと同じ方法で差し込む。
        // **フィールド名の文字列で探している**ので、NetworkPlayer 側で名前を変えると見つからなくなる（null のまま保存される）
        SerializedObject serialized = new SerializedObject(player);
        serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        serialized.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // プレハブとして保存したら、シーン上の作業用の物は消す（残すと開いているシーンに置かれてしまう）
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    // ------------------------------------------------------------
    // 検証用シーン
    // ------------------------------------------------------------

    /// <summary>
    /// 検証用シーンを**まっさらから作り直して**保存する。
    /// 置いた順番が、そのままヒエラルキーの並び順になる。
    /// </summary>
    private static void CreateTestScene(GameObject prefab, Material groundMaterial, Material markerMaterial)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLight();
        CreateGroundAndMarkers(groundMaterial, markerMaterial);
        CreateCamera();
        CreateNetworkManager(prefab);
        CreateSyncCheckObjects();

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void CreateLight()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    /// <summary>床と、動いたことが分かるための目印（四方の箱と段差）。通信には関係しない。</summary>
    private static void CreateGroundAndMarkers(Material groundMaterial, Material markerMaterial)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // 動いたことが分かるように、目印を四方に置く
        CreateBox("Marker_North", new Vector3(0f, 0.5f, 10f), markerMaterial);
        CreateBox("Marker_South", new Vector3(0f, 0.5f, -10f), markerMaterial);
        CreateBox("Marker_East", new Vector3(10f, 0.5f, 0f), markerMaterial);
        CreateBox("Marker_West", new Vector3(-10f, 0.5f, 0f), markerMaterial);

        // 段差（登れることの確認用）
        GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
        step.name = "Step";
        step.transform.position = new Vector3(5f, 0.25f, 5f);
        step.transform.localScale = new Vector3(4f, 0.5f, 4f);
        step.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
    }

    /// <summary>カメラ。自分のプレイヤーが出てきたら、<see cref="LocalPlayerCamera"/> がそれを追いかける。</summary>
    private static void CreateCamera()
    {
        // カメラ
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 6f, -8f);
        cameraObject.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<LocalPlayerCamera>();
    }

    /// <summary>
    /// 通信のまとめ役（`NetworkManager`）と、同じ物に付ける確認用の部品
    /// （接続画面・インターネット接続・起動引数での自動接続・ログ・通信の様子の表示）。
    /// </summary>
    private static void CreateNetworkManager(GameObject prefab)
    {
        // 通信のまとめ役
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();

        // つなぎ先の初期値（同じPCで試す前提。他のPCへつなぐときは接続画面で打ち直す）。LanConnectionUi の初期値と同じ
        transport.SetConnectionData("127.0.0.1", 7777);

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = prefab;

        // 送る回数を増やして、反応を良くする（初期値は30）
        manager.NetworkConfig.TickRate = NetworkTickRate;

        managerObject.AddComponent<LanConnectionUi>();
        managerObject.AddComponent<InternetConnection>();
        managerObject.AddComponent<LanAutoStart>();
        managerObject.AddComponent<LanConnectionLogger>();
        managerObject.AddComponent<NetworkStatusHud>();

        EditorUtility.SetDirty(manager);
    }

    /// <summary>
    /// 中身の同期を確かめるもの（点が入るアイテムと、押せる箱）。
    /// 位置が合うだけでは「遊べるか」は分からないので、
    /// 得点と物理も同期されることを確かめられるようにしておく。
    /// </summary>
    private static void CreateSyncCheckObjects()
    {
        // ----- 中身の同期を確かめるもの -----
        Material itemMaterial = CreateMaterial(MaterialFolder + "/NetworkItem.mat", new Color(0.95f, 0.85f, 0.25f));
        Material boxMaterial = CreateMaterial(MaterialFolder + "/NetworkPushBox.mat", new Color(0.80f, 0.55f, 0.35f));

        CreatePickupItem("Item_North", new Vector3(0f, 1f, 6f), itemMaterial);
        CreatePickupItem("Item_South", new Vector3(0f, 1f, -6f), itemMaterial);
        CreatePickupItem("Item_East", new Vector3(6f, 1f, 0f), itemMaterial);
        CreatePickupItem("Item_West", new Vector3(-6f, 1f, 0f), itemMaterial);

        CreatePushableBox("PushBox_1", new Vector3(-2.5f, 0.5f, 2.5f), boxMaterial);
        CreatePushableBox("PushBox_2", new Vector3(2.5f, 0.5f, 2.5f), boxMaterial);
    }

    /// <summary>
    /// 触ると点が入るアイテムを1つ置く。
    /// **取った判定はホストだけが行う**ので、二重取りにならない。
    /// </summary>
    private static void CreatePickupItem(string name, Vector3 position, Material material)
    {
        GameObject item = new GameObject(name);
        item.transform.position = position;

        item.AddComponent<NetworkObject>();

        // 当たり判定は付けない。
        // 取った判定は「ホストが距離を測る」形にしてあるため
        // （自分以外のキャラクターは CharacterController を切ってあり、
        //   切ると当たり判定ごと無くなって、ホスト以外が取れなくなる）
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(item.transform, false);
        visual.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        visual.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<MeshRenderer>().sharedMaterial = material;

        PickupItem pickup = item.AddComponent<PickupItem>();

        SerializedObject serialized = new SerializedObject(pickup);
        serialized.FindProperty("visual").objectReferenceValue = visual;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// みんなで押せる箱を1つ置く。
    /// **物理はホストだけが回し、その結果の位置を全員へ配る。**
    /// </summary>
    private static void CreatePushableBox(string name, Vector3 position, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;

        Rigidbody body = box.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.linearDamping = 1.5f;
        body.angularDamping = 2f;

        box.AddComponent<NetworkObject>();

        // 権限は初期設定のまま（＝ホストが位置を送る）。
        // プレイヤーと違い、押される側なので本人権限にはしない
        NetworkTransform networkTransform = box.AddComponent<NetworkTransform>();
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        // 物理で動くものなので、プレイヤーより少しだけ長めに待つ。
        // 短くしすぎると、転がる動きがカクつく
        networkTransform.PositionMaxInterpolationTime = BoxInterpolationTime;
        networkTransform.RotationMaxInterpolationTime = BoxInterpolationTime;

        box.AddComponent<PushableBox>();
    }

    /// <summary>目印の箱を1つ置く。通信も物理も付けない、ただの見た目。</summary>
    private static void CreateBox(string name, Vector3 position, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = Vector3.one;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>
    /// 単色のマテリアルを用意する。
    /// すでにあれば色だけ変えて使い回す（作り直さないので、参照している側はそのまま）。
    /// </summary>
    private static Material CreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // このプロジェクトは URP。万一 URP が無い環境でもピンク（シェーダー無し）にならないよう、予備を用意しておく
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// フォルダが無ければ作る。
    /// **親フォルダはあるものとして作る**（1段しか作らない）ので、深い場所は親から順に呼ぶこと。
    /// </summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        // "Assets/Scenes/Test" → 親 "Assets/Scenes" と 名前 "Test" に分ける（AssetDatabase.CreateFolder はこの2つで受け取る）
        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
