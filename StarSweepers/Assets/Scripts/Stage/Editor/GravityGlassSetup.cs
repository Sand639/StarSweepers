using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **重い重力で割れるガラス床**を置く・試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > アーカイブ > 月面運動会 > 重力で割れるガラスの床を置く` | **いま開いているシーン**に1枚置く |
/// | `Tools > StarSweepers > アーカイブ > 月面運動会 > 割れるガラス床の検証シーンを作り直す` | 試すための専用シーンを作る |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class GravityGlassSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string GlassMaterialPath = MaterialFolder + "/Glass.mat";
    private const string CrackMaterialPath = MaterialFolder + "/GlassCrack.mat";
    private const string GroundMaterialPath = MaterialFolder + "/TestGround.mat";

    private const string PrefabPath = "Assets/Prefabs/GravityRace/GravityGlassFloor.prefab";
    private const string RobotPrefabPath = "Assets/Prefabs/Robot/RobotRig.prefab";
    private const string ScenePath = "Assets/Scenes/Test/GravityGlassTest.unity";

    /// <summary>床1枚の大きさ。**人が乗れる広さと、ガラスと分かる厚み**にしてある。</summary>
    private static readonly Vector3 FloorSize = new Vector3(3f, 0.3f, 3f);

    /// <summary>
    /// 検証シーンの足場の上面の高さ（メートル）。スタート・ゴールの足場と、ガラスの橋の上面がこの高さにそろう。
    /// 落ちると下の地面（高さ0）まで落ちる。
    /// </summary>
    private const float DeckTop = 3f;

    // ------------------------------------------------------------
    // メニュー
    // ------------------------------------------------------------

    [MenuItem("Tools/StarSweepers/アーカイブ/月面運動会/重力で割れるガラスの床を置く")]
    public static void PlaceFloor()
    {
        GameObject prefab = GetOrCreatePrefab();

        if (prefab == null)
        {
            return;
        }

        // 床の位置は中心なので、厚みの半分だけ持ち上げて、下の面を床にぴったり付ける
        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        placed.transform.position = FindGroundPosition() + Vector3.up * (FloorSize.y * 0.5f);

        Undo.RegisterCreatedObjectUndo(placed, "割れるガラスの床を置く");
        Selection.activeGameObject = placed;
        EditorSceneManager.MarkSceneDirty(placed.scene);

        Debug.Log("割れるガラスの床を置きました。**重力が Break Gravity 以上のときに踏むと、ヒビが入って割れます。**\n" +
                  "重力を変えるには、シーンに GravityShifter が必要です。", placed);
    }

    /// <summary>
    /// 割れるガラス床を試すための専用シーン（<see cref="ScenePath"/>）を、**まっさらな状態から作り直す。**
    /// 前に同じ場所にあったシーンは上書きされる。
    ///
    /// 高さ3メートルのスタートの足場から、ガラスの橋を渡ってゴールの足場へ行く形。
    /// 重いときに踏むと割れて下の地面へ落ち、坂を登ってスタートへ戻れる。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/月面運動会/割れるガラス床の検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject floorPrefab = GetOrCreatePrefab();
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);

        if (floorPrefab == null)
        {
            return;
        }

        Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLight();

        // ----- 下の地面（落ちてもここで止まる） -----
        // Plane は1倍で10メートル四方なので、6倍で 60メートル四方
        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(6f, 1f, 6f);
        SetMaterial(groundObject, ground);

        // ----- 高さ3mの足場：スタート → ガラスの橋 → ゴール -----
        // （高さは DeckTop で決めている）
        CreateDecks(ground);

        // 落ちたあと登って戻れる坂（スタートの横）
        CreateRamp(ground);

        // ガラスの床を5枚、橋のように並べる（隙間は小さく、歩いて渡れる）
        CreateGlassBridge(floorPrefab);

        // ----- 重力を変える係（試しやすいよう、短い間隔で軽い⇔重いを行き来する） -----
        CreateGravityShifter();

        // ----- 画面表示（重力が出る） -----
        CreateHud();

        // ----- ロボット（カメラ付き） -----
        PlaceRobot(robotPrefab);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"割れるガラス床の検証シーンを作りました：{ScenePath}");
    }

    // ------------------------------------------------------------
    // 検証シーンの部品（CreateTestScene から、上から順に呼ぶ）
    // ------------------------------------------------------------

    /// <summary>明かり（斜め上から照らす太陽の光）を置く。影はやわらかく出す。</summary>
    private static void CreateLight()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    /// <summary>
    /// スタートとゴールの足場を置く。どちらも 8×8 メートル・厚み1メートルで、上面が <see cref="DeckTop"/> の高さ。
    /// スタートは原点、ゴールは奥（+Z）へ 24メートル。間にガラスの橋が渡る。
    /// </summary>
    private static void CreateDecks(Material ground)
    {
        CreateBlock("StartDeck", new Vector3(0f, DeckTop - 0.5f, 0f), new Vector3(8f, 1f, 8f), ground);
        CreateBlock("GoalDeck", new Vector3(0f, DeckTop - 0.5f, 24f), new Vector3(8f, 1f, 8f), ground);
    }

    /// <summary>
    /// 落ちたあと、下の地面からスタートの足場へ登って戻れる坂を置く。
    ///
    /// 長さ11メートルの板を、**ちょうど <see cref="DeckTop"/> だけ高くなる角度**（約16度）に傾け、
    /// 上の端がスタートの足場の左のふち（X = -4）に来るように置く。
    /// </summary>
    private static void CreateRamp(Material ground)
    {
        const float rampLength = 11f;

        // 斜辺 rampLength・高さ DeckTop の直角三角形の角度
        float rampAngle = Mathf.Asin(DeckTop / rampLength) * Mathf.Rad2Deg;

        // 板の中心の位置：X は足場のふち（-4）から、板の横の長さの半分だけ外側。
        // 高さは足場の半分から、板の厚み（0.4）の半分だけ下げる
        GameObject ramp = CreateBlock("Ramp",
            new Vector3(-4f - rampLength * 0.5f * Mathf.Cos(rampAngle * Mathf.Deg2Rad), DeckTop * 0.5f - 0.2f, 0f),
            new Vector3(rampLength, 0.4f, 3f), ground);

        // Z軸まわりに回して、足場に近い側（+X の端）を持ち上げる
        ramp.transform.rotation = Quaternion.Euler(0f, 0f, rampAngle);
    }

    /// <summary>
    /// ガラスの床を5枚、スタートからゴールへ橋のように並べる。
    /// 1枚 3メートルに、すき間 0.2メートルで 3.2メートルおき（Z = 5.6〜18.4）。
    /// 上面が足場と同じ高さ（<see cref="DeckTop"/>）になるよう、床の厚みの半分だけ下げて置く。
    /// </summary>
    private static void CreateGlassBridge(GameObject floorPrefab)
    {
        GameObject bridge = new GameObject("GlassBridge");

        for (int i = 0; i < 5; i++)
        {
            GameObject floor = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
            floor.name = $"GravityGlassFloor{i + 1}";
            floor.transform.SetParent(bridge.transform, false);
            floor.transform.position = new Vector3(0f, DeckTop - FloorSize.y * 0.5f, 5.6f + i * 3.2f);
        }
    }

    /// <summary>
    /// 重力を変える係を置く。**軽い（3.7）と重い（14）** の2つを、5〜7秒ごとに行き来させる
    /// （予告 1.5秒、変わりきるまで 1秒）。14 は床が割れる重さ（初期値 12）を超えるので、重いときに踏むと割れる。
    /// </summary>
    private static void CreateGravityShifter()
    {
        GameObject gravityObject = new GameObject("GravityShifter");
        GravityShifter shifter = gravityObject.AddComponent<GravityShifter>();

        SerializedObject gravity = new SerializedObject(shifter);
        SetFloatList(gravity.FindProperty("gravityChoices"), new List<float> { 3.7f, 14f });
        gravity.FindProperty("shortestInterval").floatValue = 5f;
        gravity.FindProperty("longestInterval").floatValue = 7f;
        gravity.FindProperty("warningSeconds").floatValue = 1.5f;
        gravity.FindProperty("changeSeconds").floatValue = 1f;
        gravity.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 画面表示（いまの重力と、遊び方のひとこと）を置く。
    /// ※ ひとことの「12」は、床の Break Gravity の初期値を手で書いたもの。床の値を変えたら、ここも合わせる。
    /// </summary>
    private static void CreateHud()
    {
        GameObject hudObject = new GameObject("RaceHud");
        RaceHud hud = hudObject.AddComponent<RaceHud>();

        SerializedObject hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("hintText").stringValue =
            "重力が 12 以上のときにガラスを踏むと、ヒビが入って割れる";
        hudSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 操作するロボットを、スタートの足場の上（少し手前、上面の 0.1メートル上）に置く。
    /// プレハブが見つからなければ、コンソールで知らせるだけにする（シーン作りは止めない）。
    /// </summary>
    private static void PlaceRobot(GameObject robotPrefab)
    {
        if (robotPrefab != null)
        {
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab);
            robot.transform.position = new Vector3(0f, DeckTop + 0.1f, -1.5f);
        }
        else
        {
            Debug.LogWarning($"{RobotPrefabPath} が見つかりません。操作するキャラクターを手で置いてください。");
        }
    }

    // ------------------------------------------------------------
    // プレハブ・マテリアル
    // ------------------------------------------------------------

    /// <summary>
    /// 床のプレハブを用意する。**すでにあれば、そのまま使う**（調整した値を消さないため）。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (existing != null)
        {
            return existing;
        }

        Material glass = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);

        if (glass == null)
        {
            Debug.LogError($"{GlassMaterialPath} が見つかりません。先に `ガラスの板を置く` でガラスのマテリアルを作ってください。");
            return null;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "GravityGlassFloor";
        root.transform.localScale = FloorSize;
        SetMaterial(root, glass);

        // ガラスなので、影は落とさない（透けているのに黒い影が出ると不自然）
        root.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GravityGlassFloor floor = root.AddComponent<GravityGlassFloor>();

        SerializedObject serialized = new SerializedObject(floor);
        serialized.FindProperty("crackMaterial").objectReferenceValue = GetOrCreateCrackMaterial();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>ヒビの線のマテリアル。**光の当たり方に左右されない白**にしてある（どこから見ても見える）。</summary>
    private static Material GetOrCreateCrackMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogWarning("URP の Unlit シェーダーが見つかりません。ヒビのマテリアルは作りませんでした。");
            return null;
        }

        Material material = new Material(shader);
        material.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f, 1f));

        AssetDatabase.CreateAsset(material, CrackMaterialPath);

        return material;
    }

    // ------------------------------------------------------------
    // 道具
    // ------------------------------------------------------------

    /// <summary>足場などの箱を1つ置く。<paramref name="position"/> は箱の中心、<paramref name="size"/> は大きさ（メートル）。</summary>
    private static GameObject CreateBlock(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.position = position;
        block.transform.localScale = size;
        SetMaterial(block, material);

        return block;
    }

    /// <summary>マテリアルを付ける。見つからなかった（null の）ときは、元の見た目のままにする。</summary>
    private static void SetMaterial(GameObject target, Material material)
    {
        if (material != null)
        {
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    /// <summary>Inspector の数字のリスト（List&lt;float&gt;）の中身を、まるごと入れ替える。</summary>
    private static void SetFloatList(SerializedProperty property, List<float> values)
    {
        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
        {
            property.GetArrayElementAtIndex(i).floatValue = values[i];
        }
    }

    /// <summary>シーンビューが見ている場所の床を探す。見つからなければ、見ている場所をそのまま使う。</summary>
    private static Vector3 FindGroundPosition()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot
            : Vector3.zero;

        // 見ている場所の 50メートル上から、真下へ 200メートルまで床を探す
        if (Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point;
        }

        return pivot;
    }
}
