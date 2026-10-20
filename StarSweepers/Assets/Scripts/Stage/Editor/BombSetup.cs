using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **爆弾**を置く・試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > アーカイブ > 爆弾 > 爆弾を置く` | **いま開いているシーン**に、宙に浮いた爆弾を1つ置く |
/// | `Tools > StarSweepers > アーカイブ > 爆弾 > 爆弾の検証シーンを作り直す` | 試すための専用シーンを作る |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class BombSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string BodyMaterialPath = MaterialFolder + "/BombBody.mat";
    private const string FuseMaterialPath = MaterialFolder + "/BombFuse.mat";
    private const string BlastMaterialPath = MaterialFolder + "/BombBlast.mat";
    private const string GroundMaterialPath = MaterialFolder + "/TestGround.mat";
    private const string LineMaterialPath = MaterialFolder + "/RaceStartLine.mat";

    private const string PrefabPath = "Assets/Prefabs/GravityRace/Bomb.prefab";
    private const string RobotPrefabPath = "Assets/Prefabs/Robot/RobotRig.prefab";
    private const string ScenePath = "Assets/Scenes/Test/BombTest.unity";

    /// <summary>爆弾の見た目の直径（メートル）。</summary>
    private const float BodyDiameter = 0.8f;

    // ------------------------------------------------------------
    // メニュー
    // ------------------------------------------------------------

    [MenuItem("Tools/StarSweepers/アーカイブ/爆弾/爆弾を置く")]
    public static void PlaceBomb()
    {
        GameObject prefab = GetOrCreatePrefab();

        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        // 宙に浮いていると分かるよう、床から少し上に置く（1.2メートル。だいたい腰の高さ）
        placed.transform.position = FindGroundPosition() + Vector3.up * 1.2f;

        Undo.RegisterCreatedObjectUndo(placed, "爆弾を置く");
        Selection.activeGameObject = placed;
        EditorSceneManager.MarkSceneDirty(placed.scene);

        Debug.Log("爆弾を置きました。**空中に動かしても落ちません。** 触れると爆発して、触れた人を吹っ飛ばします。\n" +
                  "重力が軽いほど遠くへ飛びます（Gravity Influence で効き方を変えられます）。", placed);
    }

    /// <summary>
    /// 爆弾を試すための専用シーン（<see cref="ScenePath"/>）を、**まっさらな状態から作り直す。**
    /// 前に同じ場所にあったシーンは上書きされる。
    ///
    /// ロボットは原点に立ち、奥（+Z）に爆弾が並ぶ。爆弾に触れると手前（-Z）へ飛ばされるので、
    /// 手前側の白線で、どれだけ飛んだかを読む。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/爆弾/爆弾の検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject bombPrefab = GetOrCreatePrefab();
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        Material line = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLight();

        // 地面。Plane は1倍で10メートル四方なので、10倍で 100メートル四方
        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(10f, 1f, 10f);
        SetMaterial(groundObject, ground);

        // ----- 爆弾：地面すれすれ・腰の高さ・跳ばないと届かない高さ の3段 -----
        CreateBombRows(bombPrefab);

        // ----- 飛んだ距離が分かる目盛り（5mごとの白線。スタートの後ろ側） -----
        CreateDistanceMarks(line);

        // ----- 重力を変える係（月・地球・重い を行き来） -----
        CreateGravityShifter();

        // ----- 画面表示（重力が出る） -----
        CreateHud();

        // ----- ロボット -----
        PlaceRobot(robotPrefab);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"爆弾の検証シーンを作りました：{ScenePath}");
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
    /// 爆弾を **3段 × 4列** に並べる。
    /// 段は高さ 0.6・1.3・2.2 メートルで、奥（+Z）へ 3メートルおき（6・9・12 メートル）。
    /// 列は横に 3メートルおき（X が -4.5・-1.5・1.5・4.5）。
    /// </summary>
    private static void CreateBombRows(GameObject bombPrefab)
    {
        GameObject bombs = new GameObject("Bombs");
        float[] heights = { 0.6f, 1.3f, 2.2f };

        for (int row = 0; row < heights.Length; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                GameObject bomb = (GameObject)PrefabUtility.InstantiatePrefab(bombPrefab);
                bomb.name = $"Bomb_{heights[row]:0.0}m_{column + 1}";
                bomb.transform.SetParent(bombs.transform, false);
                bomb.transform.position = new Vector3(-4.5f + column * 3f, heights[row], 6f + row * 3f);

                // 試しやすいよう、このシーンだけ3秒で戻る
                SerializedObject serialized = new SerializedObject(bomb.GetComponent<Bomb>());
                serialized.FindProperty("respawnSeconds").floatValue = 3f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    /// <summary>
    /// 飛んだ距離が分かる白線を、**5メートルごとに7本**（0〜30メートル）並べる。
    /// 一番手前の爆弾の列（Z = 6）を 0メートルとして、そこから手前（-Z）へ向かって引く。
    /// 線は幅14メートル・太さ0.15メートルで、地面すれすれ（1センチ上）に置く。踏んでも引っかからないよう当たり判定は外す。
    /// </summary>
    private static void CreateDistanceMarks(Material line)
    {
        GameObject marks = new GameObject("DistanceMarks");

        for (int i = 0; i <= 6; i++)
        {
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = $"Mark_{i * 5}m";
            mark.transform.SetParent(marks.transform, false);
            mark.transform.position = new Vector3(0f, 0.01f, 6f - i * 5f);
            mark.transform.localScale = new Vector3(14f, 0.02f, 0.15f);
            SetMaterial(mark, line);

            Collider collider = mark.GetComponent<Collider>();
            Object.DestroyImmediate(collider);
        }
    }

    /// <summary>
    /// 重力を変える係を置く。**月（1.6）・地球（9.81）・重い（14）** の3つを、7〜9秒ごとに行き来させる
    /// （予告 1.5秒、変わりきるまで 1秒）。飛ぶ距離の違いを短い時間で試せるようにするため。
    /// </summary>
    private static void CreateGravityShifter()
    {
        GameObject gravityObject = new GameObject("GravityShifter");
        GravityShifter shifter = gravityObject.AddComponent<GravityShifter>();

        SerializedObject gravity = new SerializedObject(shifter);
        SetFloatList(gravity.FindProperty("gravityChoices"), new List<float> { 1.6f, 9.81f, 14f });
        gravity.FindProperty("shortestInterval").floatValue = 7f;
        gravity.FindProperty("longestInterval").floatValue = 9f;
        gravity.FindProperty("warningSeconds").floatValue = 1.5f;
        gravity.FindProperty("changeSeconds").floatValue = 1f;
        gravity.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>画面表示（いまの重力と、遊び方のひとこと）を置く。</summary>
    private static void CreateHud()
    {
        GameObject hudObject = new GameObject("RaceHud");
        RaceHud hud = hudObject.AddComponent<RaceHud>();

        SerializedObject hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("hintText").stringValue =
            "爆弾に触ると吹っ飛ぶ。重力が軽いほど遠くへ飛ぶ（白線は5mごと）";
        hudSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 操作するロボットを、原点のすぐ上（0.1メートル）に置く。床にめり込んで始まらないよう、少し浮かせている。
    /// プレハブが見つからなければ、コンソールで知らせるだけにする（シーン作りは止めない）。
    /// </summary>
    private static void PlaceRobot(GameObject robotPrefab)
    {
        if (robotPrefab != null)
        {
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab);
            robot.transform.position = new Vector3(0f, 0.1f, 0f);
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
    /// 爆弾のプレハブを用意する。**すでにあれば、そのまま使う**（調整した値を消さないため）。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (existing != null)
        {
            return existing;
        }

        // 本体は黒に近い灰色（光で陰影が付く Lit）。
        // 火花と爆発はオレンジ（光に左右されない Unlit。暗い所でも光って見える）
        Material body = GetOrCreateMaterial(BodyMaterialPath, "Universal Render Pipeline/Lit",
            new Color(0.12f, 0.12f, 0.14f));
        Material fuse = GetOrCreateMaterial(FuseMaterialPath, "Universal Render Pipeline/Unlit",
            new Color(1f, 0.35f, 0.15f));
        Material blast = GetOrCreateMaterial(BlastMaterialPath, "Universal Render Pipeline/Unlit",
            new Color(1f, 0.62f, 0.2f));

        // 入れ物は空にして、見た目は子に持たせる（揺れる動きを入れ物で行うため）
        GameObject root = new GameObject("Bomb");

        CreatePart(root.transform, PrimitiveType.Sphere, "Body",
            Vector3.zero, Vector3.one * BodyDiameter, Quaternion.identity, body);

        // 導火線と、その先の火花
        // （本体のてっぺんから少し出る位置。導火線は 20度傾け、火花はその先端あたりに置く）
        CreatePart(root.transform, PrimitiveType.Cylinder, "Fuse",
            new Vector3(0f, BodyDiameter * 0.55f, 0f), new Vector3(0.08f, 0.12f, 0.08f),
            Quaternion.Euler(0f, 0f, 20f), body);

        CreatePart(root.transform, PrimitiveType.Sphere, "Spark",
            new Vector3(-0.05f, BodyDiameter * 0.7f, 0f), Vector3.one * 0.14f, Quaternion.identity, fuse);

        Bomb bomb = root.AddComponent<Bomb>();

        SerializedObject serialized = new SerializedObject(bomb);
        serialized.FindProperty("blastMaterial").objectReferenceValue = blast;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>爆弾の見た目の部品を1つ、<paramref name="parent"/> の子として作る。位置・向き・大きさは親から見た値。</summary>
    private static void CreatePart(Transform parent, PrimitiveType type, string name,
        Vector3 position, Vector3 scale, Quaternion rotation, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        SetMaterial(part, material);

        // **爆弾には当たり判定を付けない。** 付けると、人が押し戻されて触れる前に止まってしまう
        Object.DestroyImmediate(part.GetComponent<Collider>());
    }

    /// <summary>
    /// マテリアルを用意する。**すでにあれば、そのまま使う**（調整した色を消さないため）。
    /// 指定のシェーダーが無いとき（URP を入れていないなど）は、Unity 標準の Standard で作る。
    /// </summary>
    private static Material GetOrCreateMaterial(string path, string shaderName, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(shaderName);

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        // 色の欄の名前がシェーダーで違う（URP は _BaseColor、Standard は _Color ＝ material.color）ので、両方に入れる
        Material material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    // ------------------------------------------------------------
    // 道具
    // ------------------------------------------------------------

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
