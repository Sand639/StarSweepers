using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// **液体のビーム（<see cref="LiquidBeam"/>）**を試すためのツール。電撃のビーム（<see cref="LightningTestSetup"/>）の液体版。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 液体ビームのプレハブを作る（無ければ）` | `Assets/Prefabs/Effect/LiquidBeam.prefab` を作る。**あれば作り直さない** |
/// | `Tools > StarSweepers > 液体ビームの検証シーンを作り直す` | 試すための専用シーンを作る（プレハブが無ければ先に作る） |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class LiquidBeamTestSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string LiquidMaterialPath = MaterialFolder + "/LiquidAlpha.mat";
    private const string CharacterMaterialPath = MaterialFolder + "/LiquidBeamTestCharacter.mat";
    private const string GroundMaterialPath = MaterialFolder + "/LiquidBeamTestGround.mat";

    private const string PrefabFolder = "Assets/Prefabs/Effect";
    private const string BeamPrefabPath = PrefabFolder + "/LiquidBeam.prefab";

    private const string VolumeProfilePath = "Assets/Settings/LiquidBeamTestVolume.asset";
    private const string ScenePath = "Assets/Scenes/Test/LiquidBeamTest.unity";

    [MenuItem("Tools/StarSweepers/液体ビームの検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject beamPrefab = GetOrCreateBeamPrefab();
        Material characterMaterial = GetOrCreateLitMaterial(CharacterMaterialPath, new Color(0.15f, 0.17f, 0.2f));
        Material ground = GetOrCreateLitMaterial(GroundMaterialPath, new Color(0.08f, 0.09f, 0.11f));
        VolumeProfile profile = GetOrCreateVolumeProfile();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ----- 暗めの環境（電撃の検証シーンと同じ） -----
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.16f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.5f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(4f, 1f, 4f);
        SetMaterial(groundObject, ground);

        // ----- カメラ（斜め上から。流れが画面の左から右へ横切る） -----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        camera.fieldOfView = 45f;
        cameraObject.transform.position = new Vector3(1.5f, 10f, -11f);
        cameraObject.transform.LookAt(new Vector3(1.5f, 0.5f, 0f));
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        GameObject volumeObject = new GameObject("PostProcessVolume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        // ----- キャラクター（仮のカプセル）。画面の左に立ち、右（+X）を向く -----
        GameObject character = new GameObject("Character");
        character.transform.position = new Vector3(-6f, 0f, 0f);
        character.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(character.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        SetMaterial(body, characterMaterial);

        // ----- 液体のビーム（プレハブ。胸の高さから前へ） -----
        GameObject beamObject = (GameObject)PrefabUtility.InstantiatePrefab(beamPrefab);
        beamObject.transform.SetParent(character.transform, false);
        beamObject.transform.localPosition = new Vector3(0f, 1.1f, 0.4f);
        LiquidBeam beam = beamObject.GetComponent<LiquidBeam>();

        // ----- 操作 -----
        GameObject driverObject = new GameObject("LiquidBeamTestDriver");
        LiquidBeamTestDriver driver = driverObject.AddComponent<LiquidBeamTestDriver>();

        SerializedObject driverSerialized = new SerializedObject(driver);
        driverSerialized.FindProperty("beam").objectReferenceValue = beam;
        driverSerialized.FindProperty("character").objectReferenceValue = character.transform;
        driverSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"液体ビームの検証シーンを作りました：{ScenePath}\n" +
                  "再生して、F の長押しで撃つ（長く押すほど遠くへ）、C で先端から色が変わる、R で色を戻す、F でとめる。");
    }

    // ------------------------------------------------------------
    // プレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// プレハブを作る。**すでにあれば作り直さない**（手で調整した色や太さが消えないように）。
    /// 作り直したいときは、プレハブを消してからもう一度押す。
    /// </summary>
    [MenuItem("Tools/StarSweepers/液体ビームのプレハブを作る（無ければ）")]
    public static void CreatePrefab()
    {
        bool had = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPrefabPath) != null;

        GetOrCreateBeamPrefab();

        Debug.Log($"{BeamPrefabPath}：{(had ? "すでにあるので、そのまま" : "作りました")}");
    }

    /// <summary>液体のビームのプレハブ。キャラクターの子の、撃ち出す位置に置き、前（Z）を撃つ向きに向ける。</summary>
    private static GameObject GetOrCreateBeamPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPrefabPath);

        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("LiquidBeam");

        GameObject lightObject = new GameObject("LiquidHeadLight");
        lightObject.transform.SetParent(root.transform, false);

        Light headLight = lightObject.AddComponent<Light>();
        headLight.type = LightType.Point;
        headLight.color = new Color(0.4f, 0.7f, 1f);
        headLight.range = 4f;
        headLight.shadows = LightShadows.None;

        LiquidBeam beam = root.AddComponent<LiquidBeam>();

        SerializedObject serialized = new SerializedObject(beam);
        serialized.FindProperty("lineMaterial").objectReferenceValue = GetOrCreateLiquidMaterial();
        serialized.FindProperty("headLight").objectReferenceValue = headLight;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Effect");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BeamPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ------------------------------------------------------------
    // 素材
    // ------------------------------------------------------------

    private static Material GetOrCreateLiquidMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(LiquidMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Material material = LiquidBeam.CreateAlphaMaterial();
        AssetDatabase.CreateAsset(material, LiquidMaterialPath);
        return material;
    }

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

        Material material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>ブルームだけを入れた後処理の設定を作る（電撃の検証シーンと同じ値）。</summary>
    private static VolumeProfile GetOrCreateVolumeProfile()
    {
        VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

        if (existing != null)
        {
            return existing;
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.2f);
        bloom.scatter.Override(0.7f);
        AssetDatabase.AddObjectToAsset(bloom, profile);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        AssetDatabase.AddObjectToAsset(tonemapping, profile);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        if (material != null)
        {
            target.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
