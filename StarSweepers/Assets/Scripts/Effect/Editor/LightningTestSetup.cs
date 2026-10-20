using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// **電撃エフェクト（体のまわりのらせん・らせんをまとったビーム）**を試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 電撃エフェクトのプレハブを作る（無いものだけ）` | `Assets/Prefabs/Effect/` に体の電撃とビームのプレハブを作る。**あるものは作り直さない** |
/// | `Tools > StarSweepers > 電撃エフェクトの検証シーンを作り直す` | 試すための専用シーンを作る（プレハブが無ければ先に作る） |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class LightningTestSetup
{
    // 作る物の置き場所。AssetDatabase に渡す文字列なので、変えると既にある物を見つけられなくなる

    private const string MaterialFolder = "Assets/Art/Materials";
    private const string LightningMaterialPath = MaterialFolder + "/LightningAdditive.mat";
    private const string CharacterMaterialPath = MaterialFolder + "/LightningTestCharacter.mat";
    private const string GroundMaterialPath = MaterialFolder + "/LightningTestGround.mat";

    private const string PrefabFolder = "Assets/Prefabs/Effect";
    private const string LightningPrefabPath = PrefabFolder + "/HelixLightning.prefab";
    private const string BeamPrefabPath = PrefabFolder + "/HelixBeam.prefab";

    private const string VolumeProfilePath = "Assets/Settings/LightningTestVolume.asset";
    private const string ScenePath = "Assets/Scenes/Test/LightningTest.unity";

    /// <summary>
    /// 検証シーンを一から作って保存する（既にあれば上書き）。
    /// 必要な素材（マテリアル・プレハブ・後処理の設定）は、無いものだけ先に作る。
    /// </summary>
    [MenuItem("Tools/StarSweepers/電撃エフェクトの検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject lightningPrefab = GetOrCreateLightningPrefab();
        GameObject beamPrefab = GetOrCreateBeamPrefab();
        Material characterMaterial = GetOrCreateLitMaterial(CharacterMaterialPath, new Color(0.15f, 0.17f, 0.2f));
        Material ground = GetOrCreateLitMaterial(GroundMaterialPath, new Color(0.08f, 0.09f, 0.11f));
        VolumeProfile profile = GetOrCreateVolumeProfile();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ----- 暗めの環境（光が目立つように） -----
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.16f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.4f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(4f, 1f, 4f);
        SetMaterial(groundObject, ground);

        // ----- カメラ（トップダウン気味の斜め上から） -----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        // ビーム（最大15m）が画面の左から右へ横切るのが見えるよう、少し引いた位置から
        camera.fieldOfView = 45f;
        cameraObject.transform.position = new Vector3(1.5f, 10f, -11f);
        cameraObject.transform.LookAt(new Vector3(1.5f, 0.5f, 0f));

        // ブルーム（光のにじみ）を効かせるため、カメラの後処理をオンにする
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        GameObject volumeObject = new GameObject("PostProcessVolume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        // ----- キャラクター（仮のカプセル）と電撃 -----
        // 画面の左に立ち、右（+X）を向く。ビームは右へ飛ぶ
        GameObject character = new GameObject("Character");
        character.transform.position = new Vector3(-6f, 0f, 0f);
        character.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(character.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        SetMaterial(body, characterMaterial);

        // 体の電撃（プレハブ。足元が原点）
        GameObject lightningObject = (GameObject)PrefabUtility.InstantiatePrefab(lightningPrefab);
        lightningObject.transform.SetParent(character.transform, false);
        HelixLightning lightning = lightningObject.GetComponent<HelixLightning>();

        // ----- ビーム（プレハブ。胸の高さから前へ） -----
        GameObject beamObject = (GameObject)PrefabUtility.InstantiatePrefab(beamPrefab);
        beamObject.transform.SetParent(character.transform, false);
        beamObject.transform.localPosition = new Vector3(0f, 1.1f, 0.4f);
        HelixBeam beam = beamObject.GetComponent<HelixBeam>();

        // ----- 操作 -----
        GameObject driverObject = new GameObject("LightningTestDriver");
        LightningTestDriver driver = driverObject.AddComponent<LightningTestDriver>();

        // private の [SerializeField] に、インスペクターで入れるのと同じ仕組みで参照を入れる
        // （フィールド名の文字列で引くので、LightningTestDriver 側の名前を変えたらここも変えること）
        SerializedObject driverSerialized = new SerializedObject(driver);
        driverSerialized.FindProperty("lightning").objectReferenceValue = lightning;
        driverSerialized.FindProperty("beam").objectReferenceValue = beam;
        driverSerialized.FindProperty("character").objectReferenceValue = character.transform;
        driverSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"電撃エフェクトの検証シーンを作りました：{ScenePath}\n" +
                  "再生して、F の長押しでビーム（長く押すほど遠くへ）、Space で Burst、1 で出す／消す、2 でキャラクターを動かせます。");
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// 2つのプレハブを作る。**すでにあるものは作り直さない**（手で調整した色や太さが消えないように）。
    /// 作り直したいときは、そのプレハブを消してからもう一度押す。
    /// </summary>
    [MenuItem("Tools/StarSweepers/電撃エフェクトのプレハブを作る（無いものだけ）")]
    public static void CreatePrefabs()
    {
        bool hadLightning = AssetDatabase.LoadAssetAtPath<GameObject>(LightningPrefabPath) != null;
        bool hadBeam = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPrefabPath) != null;

        GetOrCreateLightningPrefab();
        GetOrCreateBeamPrefab();

        Debug.Log($"{LightningPrefabPath}：{(hadLightning ? "すでにあるので、そのまま" : "作りました")}\n" +
                  $"{BeamPrefabPath}：{(hadBeam ? "すでにあるので、そのまま" : "作りました")}");
    }

    /// <summary>体のまわりの電撃のプレハブ。キャラクターの子の、足元に置く。</summary>
    private static GameObject GetOrCreateLightningPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(LightningPrefabPath);

        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("HelixLightning");

        Light glowLight = CreatePointLight(root.transform, "LightningGlow", new Vector3(0f, 1f, 0f), 4f);

        HelixLightning lightning = root.AddComponent<HelixLightning>();

        SerializedObject serialized = new SerializedObject(lightning);
        serialized.FindProperty("lineMaterial").objectReferenceValue = GetOrCreateLightningMaterial();
        serialized.FindProperty("flickerLight").objectReferenceValue = glowLight;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(root, LightningPrefabPath);
    }

    /// <summary>ビームのプレハブ。キャラクターの子の、撃ち出す位置に置き、前（Z）を撃つ向きに向ける。</summary>
    private static GameObject GetOrCreateBeamPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPrefabPath);

        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("HelixBeam");

        Light headLight = CreatePointLight(root.transform, "BeamHeadLight", Vector3.zero, 5f);

        HelixBeam beam = root.AddComponent<HelixBeam>();

        SerializedObject serialized = new SerializedObject(beam);
        serialized.FindProperty("lineMaterial").objectReferenceValue = GetOrCreateLightningMaterial();
        serialized.FindProperty("headLight").objectReferenceValue = headLight;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(root, BeamPrefabPath);
    }

    /// <summary>電撃の色に合わせた緑がかった点光源を、<paramref name="parent"/> の子に作る（影は無し）。</summary>
    private static Light CreatePointLight(Transform parent, string name, Vector3 localPosition, float range)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = localPosition;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.4f, 1f, 0.55f);
        light.range = range;
        light.shadows = LightShadows.None;

        return light;
    }

    /// <summary>
    /// 組み立てたオブジェクトをプレハブとして保存し、シーンに残った元のオブジェクトは消す。
    /// 置き場のフォルダが無ければ作る（`Assets/Prefabs` はある前提）。
    /// </summary>
    private static GameObject SavePrefab(GameObject root, string path)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Effect");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ------------------------------------------------------------
    // 素材
    // ------------------------------------------------------------

    /// <summary>電撃用の加算合成のマテリアル。実行時に作るのと同じ物をアセットとして保存しておく。</summary>
    private static Material GetOrCreateLightningMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(LightningMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Material material = LightningLine.CreateAdditiveMaterial();
        AssetDatabase.CreateAsset(material, LightningMaterialPath);
        return material;
    }

    /// <summary>キャラクターや床に使う、ふつうの（光を受ける）単色のマテリアル。</summary>
    private static Material GetOrCreateLitMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        // 色のプロパティ名が URP（_BaseColor）と Standard（_Color）で違うので、両方に入れておく
        Material material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>ブルームだけを入れた後処理の設定を作る。</summary>
    private static VolumeProfile GetOrCreateVolumeProfile()
    {
        VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

        if (existing != null)
        {
            return existing;
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);

        // threshold 1 ＝ 明るさが 1 を超えた部分だけがにじむ。電撃の色を HDR で 1 より大きくしているのはこのため
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.5f);
        bloom.scatter.Override(0.7f);
        AssetDatabase.AddObjectToAsset(bloom, profile);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        AssetDatabase.AddObjectToAsset(tonemapping, profile);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    /// <summary>マテリアルを付ける（null なら付けず、Unity の既定のまま）。</summary>
    private static void SetMaterial(GameObject target, Material material)
    {
        if (material != null)
        {
            target.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
