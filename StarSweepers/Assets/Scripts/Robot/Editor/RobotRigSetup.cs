using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボット（上半身・下半身の分離）のプレハブと検証用シーンを作り直すツール。
///
/// Unityのメニュー「Tools > StarSweepers > アーカイブ > ロボットステージ > ロボット分離の検証シーンを作り直す」から実行できる。
///
/// **合体した姿の体・上半身・下半身の3つを作り、出し入れで切り替える。**
/// 物理で繋ぐ方式はガタつくためやめた。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class RobotRigSetup
{
    private const string PrefabFolder = "Assets/Prefabs/Robot";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string PrefabPath = PrefabFolder + "/RobotRig.prefab";
    private const string ScenePath = SceneFolder + "/RobotSplitTest.unity";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    // 体の大きさ。合体した姿と、分けたときの姿で合うようにしてある
    private static readonly Vector3 UpperSize = new Vector3(0.9f, 0.9f, 0.7f);
    private static readonly Vector3 LowerSize = new Vector3(0.8f, 1.1f, 0.6f);

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/ロボット分離の検証シーンを作り直す")]
    public static void CreateAll()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);

        // 上半身は青、下半身はオレンジ、地面は灰色、段差・壁は緑
        Material upperMaterial = CreateMaterial(MaterialFolder + "/RobotUpper.mat", new Color(0.35f, 0.62f, 0.90f));
        Material lowerMaterial = CreateMaterial(MaterialFolder + "/RobotLower.mat", new Color(0.95f, 0.62f, 0.30f));
        Material groundMaterial = CreateMaterial(MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        Material stepMaterial = CreateMaterial(MaterialFolder + "/RobotStep.mat", new Color(0.55f, 0.75f, 0.55f));

        GameObject prefab = CreateRobotPrefab(upperMaterial, lowerMaterial);
        CreateTestScene(prefab, groundMaterial, stepMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"ロボット分離の検証シーンを作り直しました。\nシーン: {ScenePath}");
    }

    /// <summary>
    /// ロボットのプレハブ（`RobotRig.prefab`）を作って保存する。
    ///
    /// 出来上がる形（上から作る順）：
    ///
    ///     RobotRig … まとめ役の部品が付く（RobotController・RobotGrabber・RobotRopeClimber・RobotKeyUser・RobotViewSwitcher）
    ///     ├ CameraRig（RobotCameraLook）… 左右に回る
    ///     │  └ CameraPivot … 上下に首を振る
    ///     │     └ RobotCamera（Camera・AudioListener・CameraObstacleAvoid）
    ///     ├ CombinedBody … 合体した姿。最初はこれだけが出ている
    ///     ├ UpperBody … 上半身（最初は隠しておく）
    ///     ├ LowerBody … 下半身（最初は隠しておく。手が無いので物を持てない）
    ///     └ レティクル（ReticleFactory が作る）
    ///
    /// 部品どうしのつなぎ込み（Inspector の欄に入れる物）も、ここで全部済ませる。
    /// </summary>
    private static GameObject CreateRobotPrefab(Material upperMaterial, Material lowerMaterial)
    {
        GameObject root = new GameObject("RobotRig");

        // ----- カメラ（体の子にしない。追いかける形にする） -----
        GameObject cameraRig = new GameObject("CameraRig");
        cameraRig.transform.SetParent(root.transform, false);

        GameObject pitchPivot = new GameObject("CameraPivot");
        pitchPivot.transform.SetParent(cameraRig.transform, false);

        GameObject cameraObject = new GameObject("RobotCamera");
        cameraObject.transform.SetParent(pitchPivot.transform, false);

        // 三人称のときの位置（0.8m 上・5.5m 後ろ）。RobotViewSwitcher の「三人称のとき」の初期値と同じにしてある
        cameraObject.transform.localPosition = new Vector3(0f, 0.8f, -5.5f);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();

        // 5cm 先から映す。一人称のとき、目の前の物が欠けて見えないように近くまで映している
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();

        // 壁に遮られたとき、カメラを体の近くへ寄せる（壁の裏が見えないように）
        cameraObject.AddComponent<CameraObstacleAvoid>();

        RobotCameraLook cameraLook = cameraRig.AddComponent<RobotCameraLook>();

        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning($"{InputActionsPath} が見つかりません。入力の設定は手で入れてください。");
        }

        SerializedObject lookSerialized = new SerializedObject(cameraLook);
        lookSerialized.FindProperty("pitchPivot").objectReferenceValue = pitchPivot.transform;
        lookSerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        lookSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- 合体した姿（上下がくっついた1体） -----
        // 足元を原点にして、上に下半身、その上に上半身を積んだ形
        // （radius は当たり判定の太さ＝半径。メートル）
        GameObject combined = CreateBody(root.transform, "CombinedBody",
            height: LowerSize.y + UpperSize.y, radius: 0.4f);
        AddVisual(combined.transform, "LowerVisual", new Vector3(0f, LowerSize.y * 0.5f, 0f), LowerSize, lowerMaterial);
        AddVisual(combined.transform, "UpperVisual", new Vector3(0f, LowerSize.y + UpperSize.y * 0.5f, 0f), UpperSize, upperMaterial);
        AddFrontMark(combined.transform, new Vector3(0f, LowerSize.y + UpperSize.y * 0.5f, UpperSize.z * 0.6f), upperMaterial);

        // 持った物を置く場所。**手のある体にだけ付ける**
        // （上半身の真ん中の高さで、体の中心から 0.9m 前）
        AddHoldPoint(combined, new Vector3(0f, LowerSize.y + UpperSize.y * 0.5f, 0.9f));

        // 一人称のときのカメラの高さ。体ごとに違う
        // （どの体も「いちばん上の箱の、下から8割の高さ」を目の位置にしている）
        SetEyeHeight(combined, LowerSize.y + UpperSize.y * 0.8f);

        // ----- 上半身（分けたとき） -----
        GameObject upper = CreateBody(root.transform, "UpperBody", height: UpperSize.y, radius: 0.4f);
        AddVisual(upper.transform, "Visual", new Vector3(0f, UpperSize.y * 0.5f, 0f), UpperSize, upperMaterial);
        AddFrontMark(upper.transform, new Vector3(0f, UpperSize.y * 0.5f, UpperSize.z * 0.6f), upperMaterial);

        AddHoldPoint(upper, new Vector3(0f, UpperSize.y * 0.5f, 0.9f));
        SetEyeHeight(upper, UpperSize.y * 0.8f);

        // ----- 下半身（分けたとき） -----
        // **下半身には手を付けない。** これだけで「下半身は物を持てない」が決まる
        GameObject lower = CreateBody(root.transform, "LowerBody", height: LowerSize.y, radius: 0.38f);
        AddVisual(lower.transform, "Visual", new Vector3(0f, LowerSize.y * 0.5f, 0f), LowerSize, lowerMaterial);
        AddFrontMark(lower.transform, new Vector3(0f, LowerSize.y * 0.5f, LowerSize.z * 0.6f), lowerMaterial);
        SetEyeHeight(lower, LowerSize.y * 0.8f);

        // ----- 画面中央のレティクル -----
        // プレイヤーのプロトタイプと同じものを使っている（ReticleFactory）
        Reticle reticle = ReticleFactory.Create(root.transform);

        // ----- まとめ役 -----
        RobotController controller = root.AddComponent<RobotController>();

        // 物を持つ機能。手のある体を操作しているときだけ働く
        RobotGrabber grabber = root.AddComponent<RobotGrabber>();
        SetObjectReference(grabber, "reticle", reticle);

        // ロープにつかまる機能。こちらも手のある体だけ
        RobotRopeClimber climber = root.AddComponent<RobotRopeClimber>();
        SetObjectReference(climber, "reticle", reticle);

        // 持っている鍵を扉に使う機能
        RobotKeyUser keyUser = root.AddComponent<RobotKeyUser>();
        SetObjectReference(keyUser, "reticle", reticle);

        // ----- 一人称・三人称の切り替え -----
        RobotViewSwitcher viewSwitcher = root.AddComponent<RobotViewSwitcher>();

        SerializedObject viewSerialized = new SerializedObject(viewSwitcher);
        viewSerialized.FindProperty("cameraLook").objectReferenceValue = cameraLook;
        viewSerialized.FindProperty("cameraTransform").objectReferenceValue = cameraObject.transform;
        viewSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- まとめ役に、3つの体・カメラ・入力の設定をつなぐ -----
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("combinedBody").objectReferenceValue = combined.GetComponent<RobotBody>();
        serialized.FindProperty("upperBody").objectReferenceValue = upper.GetComponent<RobotBody>();
        serialized.FindProperty("lowerBody").objectReferenceValue = lower.GetComponent<RobotBody>();
        serialized.FindProperty("cameraLook").objectReferenceValue = cameraLook;
        serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 最初は合体している状態にしておく
        upper.SetActive(false);
        lower.SetActive(false);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>
    /// 持った物を置く場所を作り、その体に登録する。
    ///
    /// **ここを付けた体だけが、物を持てるようになる。**
    /// 状態を見て分岐するのではなく、体そのものに持たせている。
    /// </summary>
    private static void AddHoldPoint(GameObject body, Vector3 localPosition)
    {
        GameObject hold = new GameObject("HoldPoint");
        hold.transform.SetParent(body.transform, false);
        hold.transform.localPosition = localPosition;

        SetObjectReference(body.GetComponent<RobotBody>(), "holdPoint", hold.transform);
    }

    /// <summary>
    /// 部品の設定項目（Inspector の欄）に、物を1つ入れる。
    ///
    /// 設定項目は外から触れない（private）ので、**欄の名前を文字で指定して**書き込んでいる。
    /// この名前は部品側の**フィールド名そのもの**。部品側で名前を変えると、ここで入らなくなる。
    /// </summary>
    private static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 一人称のときのカメラの高さを決める。
    /// **体の大きさが違うので、体ごとに設定する**（上半身だけのときは低くなる）。
    /// </summary>
    private static void SetEyeHeight(GameObject body, float height)
    {
        RobotBody robotBody = body.GetComponent<RobotBody>();

        SerializedObject serialized = new SerializedObject(robotBody);
        serialized.FindProperty("eyeHeight").floatValue = height;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>体を1つ作る。足元が原点になるように当たり判定を置く。</summary>
    private static GameObject CreateBody(Transform parent, string name, float height, float radius)
    {
        GameObject body = new GameObject(name);
        body.transform.SetParent(parent, false);

        CharacterController controller = body.AddComponent<CharacterController>();
        controller.height = height;
        controller.radius = radius;

        // 当たり判定の中心を背丈の半分だけ上げて、**足元が原点**になるようにする
        controller.center = new Vector3(0f, height * 0.5f, 0f);

        // 登れる坂の角度（度）
        controller.slopeLimit = 50f;

        // そのまま乗り越えられる段差の高さ（メートル）。背丈の4割まで、ただし最大40cm
        // （上半身だけの体は 0.9m × 0.4 ＝ 36cm、それより背の高い体は 40cm になる）
        controller.stepOffset = Mathf.Min(0.4f, height * 0.4f);

        body.AddComponent<RobotBody>();

        return body;
    }

    /// <summary>見た目の箱を足す。当たり判定は CharacterController が持つので外す。</summary>
    private static void AddVisual(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = position;
        visual.transform.localScale = size;
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>
    /// **体の正面を示す目印**（22cm角の小さな箱）を足す。
    /// 箱だけの体だとどちらが前か分からないので、前面から少し出っ張らせて付けている。
    /// 作り方は <see cref="AddVisual"/> と同じ（名前と大きさが決まっているだけ）。
    /// </summary>
    private static void AddFrontMark(Transform parent, Vector3 position, Material material)
    {
        AddVisual(parent, "FrontMark", position, new Vector3(0.22f, 0.22f, 0.22f), material);
    }

    /// <summary>再生すればすぐ試せる検証用シーンを作る。</summary>
    private static void CreateTestScene(GameObject prefab, Material groundMaterial, Material stepMaterial)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Unity の板（Plane）は10m四方なので、4倍して40m四方の地面にする
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // 合体したままでは通れず、上半身だけなら通れる隙間
        // （3つの箱の間に、幅2.4m・高さ2.0m の隙間ができる。合体した体の背丈はちょうど2.0m）
        CreateBox("LowGap_Left", new Vector3(-3.2f, 1.6f, 6f), new Vector3(4f, 3.2f, 1f), stepMaterial);
        CreateBox("LowGap_Right", new Vector3(3.2f, 1.6f, 6f), new Vector3(4f, 3.2f, 1f), stepMaterial);
        CreateBox("LowGap_Top", new Vector3(0f, 2.6f, 6f), new Vector3(2.4f, 1.2f, 1f), stepMaterial);

        // 高さの違いを試せる段差
        CreateBox("LowStep", new Vector3(-6f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f), stepMaterial);
        CreateBox("HighStep", new Vector3(6f, 0.9f, 0f), new Vector3(4f, 1.8f, 4f), stepMaterial);

        CreateBox("Marker", new Vector3(0f, 0.5f, 12f), Vector3.one, stepMaterial);

        // ----- 持てる箱 -----
        // 大きさを変えて3つ置く。持ったまま隙間を通れるかも試せる
        Material grabMaterial = CreateMaterial(MaterialFolder + "/RobotGrabBox.mat", new Color(0.85f, 0.60f, 0.85f));

        CreateGrabbableBox("GrabBox_Small", new Vector3(-2f, 0.25f, 2.5f), 0.5f, 1f, grabMaterial);
        CreateGrabbableBox("GrabBox_Medium", new Vector3(0f, 0.35f, 3f), 0.7f, 2f, grabMaterial);
        CreateGrabbableBox("GrabBox_Large", new Vector3(2f, 0.5f, 2.5f), 1f, 5f, grabMaterial);

        // ----- ロープ（上下に移動できる紐） -----
        // 上の足場は高すぎてジャンプでは届かない。**ロープでしか上がれない**
        // （足場の上面は高さ6.2m。ロープは長さ6.6mで、足場の端から0.6m離れた所に立てる）
        CreateBox("RopeTop", new Vector3(-8f, 6f, 6f), new Vector3(4f, 0.4f, 4f), stepMaterial);
        RobotRopeSetup.CreateRopeAt("Rope", new Vector3(-5.4f, 0f, 6f), 6.6f);

        // ----- 踏むボタンと、上下する壁 -----
        // ボタンを踏むと、その先にある壁が下がって通れるようになる
        PressButton button = StageGimmickSetup.CreateButton("PressButton", new Vector3(0f, 0f, -6f));
        MoveObject wall = StageGimmickSetup.CreateWall("MovingWall", new Vector3(0f, 1.5f, -9f));
        StageGimmickSetup.Connect(button, wall);

        // ----- 横向きのロープ（うんてい） -----
        // 下は通れないので、ぶら下がって渡る
        // （X軸まわりに90度倒すと、ロープは高さ2.8mのところを Z の向きへ6m伸びる）
        RobotRopeSetup.CreateRopeAt("RopeHorizontal", new Vector3(8f, 2.8f, -8f), 6f,
            Quaternion.Euler(90f, 0f, 0f));

        // ----- ガラスの板 -----
        // 透けるが通れない。ぶつかって初めて気づかないよう、ふちが光る作りになっている
        StageGimmickSetup.CreateGlass("Glass", new Vector3(-3f, 1.25f, -6f));

        // ----- 鍵と、鍵付きの扉 -----
        // 鍵を持って扉まで運び、F で開ける
        // （最後の "" は鍵の種類の名前。空にしておくと、どの鍵でもこの扉に合う）
        StageGimmickSetup.CreateKey("DoorKey", new Vector3(-6f, 0.3f, -6f), "");
        StageGimmickSetup.CreateLockedDoor("LockedDoor", new Vector3(-9f, 1.5f, -9f), "");

        if (prefab != null)
        {
            // プレハブとのつながりを残したまま置く（あとでプレハブを直すと、このシーンにも反映される）。
            // 地面に埋まらないよう、10cm 浮かせておく
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            robot.transform.position = new Vector3(0f, 0.1f, -3f);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    /// <summary>
    /// ロボットが持てる箱を1つ置く。
    /// `size` は1辺の長さ（メートル）、`mass` は重さ（kg）。
    /// </summary>
    private static void CreateGrabbableBox(string name, Vector3 position, float size, float mass, Material material)
    {
        GameObject box = CreateBox(name, position, new Vector3(size, size, size), material);

        // 動き（linear）と回転（angular）に少しブレーキをかける。離したあと、いつまでも滑ったり転がったりしにくくなる
        Rigidbody body = box.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = 0.5f;
        body.angularDamping = 1f;

        box.AddComponent<Grabbable>();
    }

    /// <summary>
    /// 当たり判定つきの箱を1つ置く（壁・段差・足場などに使う）。
    /// `position` は箱の中心、`scale` は幅・高さ・奥行き（メートル）。
    /// </summary>
    private static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;

        return box;
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>
    /// 色だけのマテリアルを用意する。
    ///
    /// **すでにあれば作り直さず、色だけ塗り直す。**
    /// 同じファイルを使い続けるので、他のシーンやプレハブからの参照もそのまま残る。
    ///
    /// URP のシェーダーが見つからない（URP を使っていない）ときは、標準のシェーダーで作る。
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
    /// **1段ずつしか作れない**（親フォルダが先に無いと失敗する）ので、呼ぶ側で親から順に呼んでいる。
    /// </summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
