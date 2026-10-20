using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerRig のプレハブと、その検証用シーンを自動で作り直すツール。
///
/// Unityのメニュー「Tools > StarSweepers > アーカイブ > 共通の部品 > プレイヤーの検証シーンを作り直す」から実行できる。
/// 手で組み立てると付け忘れが起きるので、作り直したいときはこれを使う。
///
/// ※ このスクリプトは Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class PlayerRigSetup
{
    private const string PrefabFolder = "Assets/Prefabs/Common";
    private const string SceneFolder = "Assets/Scenes/Test";

    // マテリアルの置き場。フォルダ名を変えたらここも直すこと
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string PrefabPath = PrefabFolder + "/PlayerRig.prefab";
    private const string ScenePath = SceneFolder + "/PlayerRigTest.unity";
    private const string BodyMaterialPath = MaterialFolder + "/PlayerRigBody.mat";
    private const string SkinMaterialPath = MaterialFolder + "/PlayerRigSkin.mat";
    private const string GroundMaterialPath = MaterialFolder + "/TestGround.mat";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [MenuItem("Tools/StarSweepers/アーカイブ/共通の部品/プレイヤーの検証シーンを作り直す")]
    public static void CreateAll()
    {
        EnsureFolder("Assets/Scripts");
        EnsureFolder("Assets/Scripts/Player");
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);
        EnsureFolder("Assets/Art");

        // フォルダ名が綴り違い（Matrials）で作られていたら、Materials に直す。
        // ここで先に直しておかないと、下の EnsureFolder が新しい空フォルダを作ってしまう
        MoveAssetIfExists("Assets/Art/Matrials", MaterialFolder);

        EnsureFolder(MaterialFolder);

        // 以前は Prefabs / Scenes の下にマテリアルを置いていた。Art/Materials へ移しておく
        MoveAssetIfExists("Assets/Prefabs/PlayerRigBody.mat", BodyMaterialPath);
        MoveAssetIfExists(SceneFolder + "/TestGround.mat", GroundMaterialPath);

        Material bodyMaterial = CreateMaterial(BodyMaterialPath, new Color(0.35f, 0.62f, 0.90f));
        Material skinMaterial = CreateMaterial(SkinMaterialPath, new Color(0.94f, 0.76f, 0.60f));
        Material groundMaterial = CreateMaterial(GroundMaterialPath, new Color(0.72f, 0.72f, 0.72f));

        GameObject prefab = CreatePlayerPrefab(bodyMaterial, skinMaterial);
        CreateTestScene(prefab, groundMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"PlayerRig を作り直しました。\nプレハブ: {PrefabPath}\nシーン: {ScenePath}");
    }

    /// <summary>PlayerRig のプレハブを組み立てて保存する。</summary>
    private static GameObject CreatePlayerPrefab(Material bodyMaterial, Material skinMaterial)
    {
        GameObject root = new GameObject("PlayerRig");

        // 当たり判定。足元が原点（y=0）で、身長2mのカプセルの中心は y=1m
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 1f, 0f);
        controller.slopeLimit = 50f;   // これより急な坂（度）は登れない
        controller.stepOffset = 0.3f;  // これより低い段差（メートル）は乗り越えられる

        // 見た目のカプセル。当たり判定は CharacterController が持つので、コライダーは外す
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;

        // 前がどちらか分かるようにする目印（体の前面、少し上に付けた小さな箱）
        GameObject frontMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontMark.name = "FrontMark";
        frontMark.transform.SetParent(body.transform, false);
        frontMark.transform.localPosition = new Vector3(0f, 0.25f, 0.5f);
        frontMark.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        Object.DestroyImmediate(frontMark.GetComponent<Collider>());
        frontMark.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

        // 上下の首振りをさせる場所。目の高さ（1.6m）に置く
        GameObject pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        GameObject cameraObject = new GameObject("PlayerCamera");
        cameraObject.transform.SetParent(pivot.transform, false);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        // 一人称の腕はカメラのすぐ手前（0.2m ほど）にあるので、描ける一番近い距離を短くしておく
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();

        // 壁に遮られたとき、カメラを体の近くへ寄せる（壁の裏が見えないように）
        cameraObject.AddComponent<CameraObstacleAvoid>();

        // 一人称のときに手前に見える腕。カメラの子にして、視点と一緒に動くようにする
        GameObject arms = new GameObject("FirstPersonArms");
        arms.transform.SetParent(cameraObject.transform, false);
        CreateArm(arms.transform, isRight: false, bodyMaterial, skinMaterial);
        CreateArm(arms.transform, isRight: true, bodyMaterial, skinMaterial);
        arms.SetActive(false); // 開始はTPSなので隠しておく

        Reticle reticle = ReticleFactory.Create(root.transform);

        PlayerController playerController = root.AddComponent<PlayerController>();
        PlayerViewSwitcher viewSwitcher = root.AddComponent<PlayerViewSwitcher>();

        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning($"{InputActionsPath} が見つかりませんでした。PlayerRig の入力設定は手で入れてください。");
        }

        // private な [SerializeField] にエディタから値を入れる。
        // 文字列はフィールド名そのもの。フィールドを改名したらここも直すこと
        SerializedObject controllerSerialized = new SerializedObject(playerController);
        controllerSerialized.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
        controllerSerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject switcherSerialized = new SerializedObject(viewSwitcher);
        switcherSerialized.FindProperty("cameraTransform").objectReferenceValue = cameraObject.transform;
        switcherSerialized.FindProperty("thirdPersonBody").objectReferenceValue = body;
        switcherSerialized.FindProperty("firstPersonArms").objectReferenceValue = arms;
        switcherSerialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>
    /// 一人称のときに画面の手前へ出す腕を1本作る。
    /// マインクラフトのように、細長い「腕（袖）」の先に「手」を付けた形にしている。
    /// 腕と手で色を変えているので、どこが手か分かる。
    /// </summary>
    private static void CreateArm(Transform parent, bool isRight, Material sleeveMaterial, Material skinMaterial)
    {
        float side = isRight ? 1f : -1f;

        GameObject arm = new GameObject(isRight ? "ArmRight" : "ArmLeft");
        arm.transform.SetParent(parent, false);

        // 画面の左右の端、少し下から、前方へ伸びるように置く
        arm.transform.localPosition = new Vector3(0.30f * side, -0.26f, 0.18f);

        // 少し下向き・少し内向きにして、自分の腕らしく見せる
        arm.transform.localRotation = Quaternion.Euler(12f, -8f * side, 0f);

        // 腕（袖）の部分。細長い四角
        GameObject sleeve = CreateArmPart(arm.transform, "Sleeve", sleeveMaterial);
        sleeve.transform.localPosition = new Vector3(0f, 0f, 0.24f);
        sleeve.transform.localScale = new Vector3(0.13f, 0.13f, 0.46f);

        // 手の部分。先端に付ける立方体
        GameObject hand = CreateArmPart(arm.transform, "Hand", skinMaterial);
        hand.transform.localPosition = new Vector3(0f, 0f, 0.54f);
        hand.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
    }

    /// <summary>腕を作る四角を1つ作る。当たり判定と影は持たせない。</summary>
    private static GameObject CreateArmPart(Transform parent, string name, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);

        // 腕は当たり判定を持たない。持つと自分の移動を邪魔する
        Object.DestroyImmediate(part.GetComponent<Collider>());

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        // カメラのすぐ手前にあるため、影を落とすと不自然になる
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return part;
    }

    /// <summary>PlayerRig を置いただけの、再生すれば動く検証用シーンを作る。</summary>
    private static void CreateTestScene(GameObject prefab, Material groundMaterial)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // 動いているか分かるように、目印の箱をいくつか置く
        CreateMarker(new Vector3(6f, 0.5f, 6f), groundMaterial);
        CreateMarker(new Vector3(-6f, 0.5f, 4f), groundMaterial);
        CreateMarker(new Vector3(3f, 0.5f, -7f), groundMaterial);

        // 上り下りを試せる坂
        GameObject slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slope.name = "Slope";
        slope.transform.position = new Vector3(-4f, 0.4f, -4f);
        slope.transform.rotation = Quaternion.Euler(-20f, 0f, 0f);
        slope.transform.localScale = new Vector3(4f, 0.2f, 6f);
        slope.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        if (prefab != null)
        {
            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(0f, 0.1f, 0f);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    /// <summary>検証シーンに置く目印の箱（1m 角）を1つ作る。</summary>
    private static void CreateMarker(Vector3 position, Material material)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Marker";
        marker.transform.position = position;
        marker.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>URPで正しく表示されるマテリアルを作る（無ければ）。</summary>
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
    /// 古い場所にファイルが残っていたら、新しい場所へ移す。
    /// Unityの機能で移すので、シーンやプレハブからの参照は外れない。
    /// （エクスプローラーで移動すると参照が外れる）
    /// </summary>
    private static void MoveAssetIfExists(string fromPath, string toPath)
    {
        if (fromPath == toPath)
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(fromPath) == null)
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(toPath) != null)
        {
            return;
        }

        string error = AssetDatabase.MoveAsset(fromPath, toPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"{fromPath} を {toPath} へ移せませんでした：{error}");
        }
    }

    /// <summary>フォルダが無ければ作る。途中のフォルダも一緒に作る。</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);

        // 親が無ければ先に作る（Unity は1段ずつしか作れない）
        EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }
}
