using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のプレハブと検証シーンを作るツール。**（2026/10/27・小野田さんの仕様）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > プレイヤー（本実装）の検証シーンを作り直す` | `Assets/Scenes/Test/SweeperPlayerTest.unity` を作り直す。プレハブが無ければ先に作る |
/// | `Tools > StarSweepers > プレイヤー（本実装）のプレハブを作り直す` | `Assets/Prefabs/Common/SweeperPlayer.prefab` を最初の状態で作り直す。⚠️ **インスペクターで調整した値は消える** |
/// | `Tools > StarSweepers > プレイヤー（本実装）の体をキャラクターダミーに差し替える` | 2つの新しい体のプレハブ（これと `SpaceJunkSweeperPlayer.prefab`）を、**作り直さずに**カプセルからキャラクターダミーへ替える |
///
/// マテリアル（`Art/Materials/SweeperRing.mat`・`SweeperAimStrip.mat`）は無ければ作る。
/// 狙いの帯の画像（`Art/Sprites/AimLengthBody.png`・`AimLengthTip.png`）の読み込み設定もここで整える（繰り返し用に Wrap Mode を Repeat）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SweeperPlayerSetup
{
    private const string ScenePath = "Assets/Scenes/Test/SweeperPlayerTest.unity";
    private const string PrefabPath = "Assets/Prefabs/Common/SweeperPlayer.prefab";
    private const string RingMaterialPath = "Assets/Art/Materials/SweeperRing.mat";
    private const string StripMaterialPath = "Assets/Art/Materials/SweeperAimStrip.mat";
    private const string BodyMaterialPath = "Assets/Art/Materials/SweeperBody.mat";
    private const string BodyTexturePath = "Assets/Art/Sprites/AimLengthBody.png";
    private const string TipTexturePath = "Assets/Art/Sprites/AimLengthTip.png";
    private const string BeamPrefabPath = "Assets/Prefabs/Effect/LiquidBeam.prefab";
    private const string ModelPath = "Assets/Art/Models/SM_Character_Dummy.fbx";
    private const string ModelName = "Model";

    /// <summary>検証シーンに並べる、見本のプレイヤー（操作はしない）の形と色。</summary>
    private static readonly (SweeperRing.RingShape shape, int team, string name)[] Samples =
    {
        (SweeperRing.RingShape.Triangle, 1, "さんかく"),
        (SweeperRing.RingShape.Square, 2, "しかく"),
        (SweeperRing.RingShape.Star, 3, "ほし"),
        (SweeperRing.RingShape.Hexagon, 0, "ろっかく"),
    };

    [MenuItem("Tools/StarSweepers/プレイヤー（本実装）の検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            prefab = CreatePrefab();
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();
        FishingSceneBuilder.CreatePlainWalls();

        // ----- 操作するプレイヤー -----
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = "SweeperPlayer";
        player.transform.position = Vector3.zero;
        SweeperPlayer profile = player.GetComponent<SweeperPlayer>();
        FishingSceneBuilder.SetString(profile, "playerName", "プレイヤー1");
        FishingSceneBuilder.SetInt(profile, "teamIndex", 0);

        FishingSceneBuilder.CreateCamera(player);

        // ----- 見本のプレイヤー（リングの形とチームの色の見本。動かない） -----
        for (int i = 0; i < Samples.Length; i++)
        {
            CreateSample(Samples[i].shape, Samples[i].team, Samples[i].name, new Vector3(-6f + i * 4f, 0f, -7f));
        }

        // ----- 物資（場外に落ちたら6秒後に元の場所へ戻る） -----
        FishingSceneBuilder.CreateSupplyRing(8, 6f);
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            HookableObject far = FishingSceneBuilder.CreateSupply($"FarSupply_{i + 1}",
                new Vector3(Mathf.Sin(angle) * 11f, 0.4f, Mathf.Cos(angle) * 11f));
            FishingSceneBuilder.SetFloat(far, "respawnSeconds", 6f);
        }

        // ----- 画面の説明 -----
        GameObject hudObject = new GameObject("SweeperPlayerTestHud");
        SweeperPlayerTestHud hud = hudObject.AddComponent<SweeperPlayerTestHud>();
        FishingSceneBuilder.SetRef(hud, "shooter", player.GetComponent<SweeperShooter>());
        FishingSceneBuilder.SetRef(hud, "reel", player.GetComponent<SweeperReel>());

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SweeperPlayer] プレイヤー（本実装）の検証シーンを作りました：{ScenePath}\n" +
                  "再生して、WASD で歩き、左クリック長押し→離すで光線。巻き取り中に押すと後ろへ、押さなければ前へ投げる。");
    }

    [MenuItem("Tools/StarSweepers/プレイヤー（本実装）のプレハブを作り直す")]
    public static GameObject CreatePrefab()
    {
        Material bodyMaterial = BodyMaterial();

        // ----- 体（足元が原点。高さ 2m） -----
        GameObject root = new GameObject("SweeperPlayer");
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 1f, 0f);
        controller.height = 2f;
        controller.radius = 0.4f;

        AddSweeperParts(root, true);

        // キャラクターのモデルの代わり（カプセル）。当たり判定は CharacterController が持つので外す
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

        // 前が分かるように、顔の位置に小さな板（モデルが入ったら消してよい）
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "FrontMark";
        visor.transform.SetParent(root.transform, false);
        visor.transform.localPosition = new Vector3(0f, 1.5f, 0.42f);
        visor.transform.localScale = new Vector3(0.5f, 0.15f, 0.12f);
        Object.DestroyImmediate(visor.GetComponent<Collider>());

        // カプセルの見た目をキャラクターダミーに替える（モデルが無ければカプセルのまま）
        PutModel(root);

        FishingSceneBuilder.EnsureFolder("Assets/Prefabs/Common");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[SweeperPlayer] プレイヤー（本実装）のプレハブを作りました：{PrefabPath}");
        return prefab;
    }

    /// <summary>体（カプセル）のマテリアル（薄い灰色）。無ければ作る。</summary>
    internal static Material BodyMaterial()
    {
        return FishingSceneBuilder.GetOrCreateMaterial(BodyMaterialPath, new Color(0.88f, 0.9f, 0.93f));
    }

    /// <summary>
    /// **Sweeper の部品（移動・狙い・リング・帯・光線・巻き取り・アニメーション）を体に付けて、参照をつなぐ。**
    /// 体の見た目（カプセル）と当たり判定（CharacterController）は付けないので、呼ぶ側で用意する。
    /// <paramref name="withNameTag"/> が false なら、頭の上の名前（<see cref="SweeperNameTag"/>）は付けない
    /// （宇宙ごみのオンラインの体は、今の名前のタグを使うため。<see cref="SweeperOnlineSetup"/>）。
    /// </summary>
    internal static void AddSweeperParts(GameObject root, bool withNameTag)
    {
        PrepareTextures();
        Material ringMaterial = GetOrCreateShaderMaterial(RingMaterialPath, "StarSweepers/SweeperRing");
        Material stripMaterial = GetOrCreateShaderMaterial(StripMaterialPath, "StarSweepers/SweeperAimStrip");
        Texture2D bodyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BodyTexturePath);
        Texture2D tipTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TipTexturePath);
        LiquidBeam beamPrefab = AssetDatabase.LoadAssetAtPath<LiquidBeam>(BeamPrefabPath);
        var inputActions = FishingSceneBuilder.LoadInputActions();

        if (root.GetComponent<PlayerInputSource>() == null)
        {
            root.AddComponent<PlayerInputSource>();
        }
        root.AddComponent<SweeperPlayer>();
        SweeperMover mover = root.AddComponent<SweeperMover>();
        SweeperAim aim = root.AddComponent<SweeperAim>();
        SweeperRing ring = root.AddComponent<SweeperRing>();
        if (withNameTag)
        {
            root.AddComponent<SweeperNameTag>();
        }
        SweeperReel reel = root.AddComponent<SweeperReel>();
        SweeperAnimation animationPlayer = root.AddComponent<SweeperAnimation>();
        SweeperShooter shooter = root.AddComponent<SweeperShooter>();

        // ----- 狙いのカーソル（小さな円。SweeperAim が位置を動かす） -----
        GameObject cursor = new GameObject("AimCursor");
        cursor.transform.SetParent(root.transform, false);
        cursor.transform.localPosition = new Vector3(0f, 0f, 1f);
        SweeperRing cursorRing = cursor.AddComponent<SweeperRing>();
        FishingSceneBuilder.SetFloat(cursorRing, "size", 0.4f);
        FishingSceneBuilder.SetFloat(cursorRing, "thickness", 0.06f);
        FishingSceneBuilder.SetFloat(cursorRing, "fillAlpha", 0.35f);
        FishingSceneBuilder.SetFloat(cursorRing, "lift", 0f);
        SetBool(cursorRing, "rotateWithBody", false);
        FishingSceneBuilder.SetRef(cursorRing, "material", ringMaterial);

        // ----- 狙いの帯 -----
        GameObject areaObject = new GameObject("AimArea");
        areaObject.transform.SetParent(root.transform, false);
        SweeperAimArea area = areaObject.AddComponent<SweeperAimArea>();
        FishingSceneBuilder.SetRef(area, "bodyTexture", bodyTexture);
        FishingSceneBuilder.SetRef(area, "tipTexture", tipTexture);
        FishingSceneBuilder.SetRef(area, "material", stripMaterial);

        // ----- 参照をつなぐ -----
        FishingSceneBuilder.SetRef(ring, "material", ringMaterial);
        FishingSceneBuilder.SetRef(mover, "aim", aim);
        FishingSceneBuilder.SetRef(mover, "inputActions", inputActions);
        FishingSceneBuilder.SetRef(aim, "cursorVisual", cursor.transform);
        FishingSceneBuilder.SetRef(reel, "flashMaterial", ringMaterial);
        FishingSceneBuilder.SetRef(shooter, "aim", aim);
        FishingSceneBuilder.SetRef(shooter, "mover", mover);
        FishingSceneBuilder.SetRef(shooter, "reel", reel);
        FishingSceneBuilder.SetRef(shooter, "aimArea", area);
        FishingSceneBuilder.SetRef(shooter, "ring", ring);
        FishingSceneBuilder.SetRef(shooter, "animationPlayer", animationPlayer);
        FishingSceneBuilder.SetRef(shooter, "inputActions", inputActions);
        FishingSceneBuilder.SetRef(shooter, "beamPrefab", beamPrefab);
    }

    /// <summary>リングの形と色の見本。カプセルとリングと名前だけ（動かない）。</summary>
    private static void CreateSample(SweeperRing.RingShape shape, int team, string label, Vector3 position)
    {
        Material ringMaterial = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
        Material bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);

        GameObject root = new GameObject("Sample_" + shape);
        root.transform.position = position;

        SweeperPlayer profile = root.AddComponent<SweeperPlayer>();
        FishingSceneBuilder.SetString(profile, "playerName", label);
        FishingSceneBuilder.SetInt(profile, "teamIndex", team);

        SweeperRing ring = root.AddComponent<SweeperRing>();
        FishingSceneBuilder.SetInt(ring, "shape", (int)shape);
        FishingSceneBuilder.SetRef(ring, "material", ringMaterial);
        root.AddComponent<SweeperNameTag>();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
    }

    /// <summary>狙いの帯の画像を、繰り返して貼れる設定にする（繰り返す部分は Repeat、先は Clamp）。</summary>
    private static void PrepareTextures()
    {
        SetTextureWrap(BodyTexturePath, TextureWrapMode.Repeat);
        SetTextureWrap(TipTexturePath, TextureWrapMode.Clamp);
    }

    private static void SetTextureWrap(string path, TextureWrapMode wrapMode)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[SweeperPlayer] {path} が見つかりません。狙いの帯は画像なしで出ます。");
            return;
        }

        if (importer.textureType == TextureImporterType.Default && importer.wrapMode == wrapMode && importer.alphaIsTransparency)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = wrapMode;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    /// <summary>シェーダーの名前からマテリアルのファイルを作る（あればそのまま使う）。</summary>
    private static Material GetOrCreateShaderMaterial(string path, string shaderName)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[SweeperPlayer] シェーダー {shaderName} が見つかりません。");
            return null;
        }

        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SetBool(Object owner, string field, bool value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        serialized.FindProperty(field).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------
    // キャラクターのモデル（2026/10/29・小野田さんの依頼）
    // ------------------------------------------------------------

    [MenuItem("Tools/StarSweepers/プレイヤー（本実装）の体をキャラクターダミーに差し替える")]
    public static void ReplaceBodyWithModelInPrefabs()
    {
        foreach (string path in new[] { PrefabPath, SweeperOnlineSetup.PrefabPath })
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[SweeperPlayer] {path} が見つかりません。先にプレハブを作ってください。");
                continue;
            }

            // 作り直さずに開いて直す（インスペクターで調整した値を消さないため）
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (PutModel(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[SweeperPlayer] 体をキャラクターダミーに差し替えました：{path}");
            }
            else
            {
                Debug.Log($"[SweeperPlayer] 差し替え済みか、モデルが無いので何もしませんでした：{path}");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// **体の見た目を、カプセルからキャラクターダミー（Hansさん作）に替える。** 替えたら true。
    ///
    /// ・モデルは子の <c>Model</c> に置く（足元が原点。**大きさ・向きは変えない**。Hansさんのモデルは変更前に本人の了承が要るため）
    /// ・カプセルは見た目（MeshFilter・MeshRenderer）だけ外し、<c>Body</c> という入れ物は残す
    ///   （FrontMark の親で、ほかのツールが名前で探すため）
    /// ・スタンで点滅させる見た目（<see cref="PlayerStun"/> の bodyRenderers）のカプセルを、モデルの見た目に入れ替える
    /// ・アニメーションはまだ無い。<see cref="SweeperAnimation"/> はモデルの Animator を見つけるが、状態が無いので何もしない
    /// </summary>
    internal static bool PutModel(GameObject root)
    {
        if (root.transform.Find(ModelName) != null)
        {
            return false;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            Debug.LogWarning($"[SweeperPlayer] {ModelPath} が見つかりません。体はカプセルのままです。");
            return false;
        }

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
        model.name = ModelName;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>(true);

        Transform body = root.transform.Find("Body");
        MeshRenderer capsule = body != null ? body.GetComponent<MeshRenderer>() : null;

        PlayerStun stun = root.GetComponent<PlayerStun>();
        if (stun != null)
        {
            var renderers = new System.Collections.Generic.List<Object>(modelRenderers);
            SerializedProperty array = new SerializedObject(stun).FindProperty("bodyRenderers");
            for (int i = 0; i < array.arraySize; i++)
            {
                Object item = array.GetArrayElementAtIndex(i).objectReferenceValue;
                if (item != null && item != capsule)
                {
                    renderers.Add(item);
                }
            }
            FishingSceneBuilder.SetObjectArray(stun, "bodyRenderers", renderers.ToArray());
        }

        if (capsule != null)
        {
            Object.DestroyImmediate(capsule);
            Object.DestroyImmediate(body.GetComponent<MeshFilter>());
        }
        return true;
    }
}
