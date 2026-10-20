using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **ロープ（上下に移動できる紐）をステージに置くためのツール。**
///
/// Unityのメニューから実行できる。
///
/// - `Tools > StarSweepers > アーカイブ > ロボットステージ > ロープを1本置く`
///   … いま開いているシーンにロープを1本作る。カメラが見ている場所の**床の上**に置かれる
/// - `Tools > StarSweepers > アーカイブ > ロボットステージ > ロボットにロープ機能を足す`
///   … `RobotRig` プレハブに、ロープにつかまる機能を足す（**一度やれば済む**）
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class RobotRopeSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string RopeMaterialPath = MaterialFolder + "/Rope.mat";
    private const string PrefabPath = "Assets/Prefabs/Robot/RobotRig.prefab";

    /// <summary>ロープの見た目の太さ（直径・メートル）。</summary>
    private const float RopeThickness = 0.1f;

    /// <summary>作ったときのロープの長さ（メートル）。あとから Inspector で変えられる。</summary>
    private const float DefaultHeight = 6f;

    /// <summary>横向きのロープを渡す高さ（メートル）。ぶら下がって足が着かない程度。</summary>
    private const float HorizontalRopeHeight = 2.8f;

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/ロープを1本置く")]
    public static void CreateRope()
    {
        GameObject rope = CreateRopeAt("Rope", FindGroundPosition(), DefaultHeight);

        Undo.RegisterCreatedObjectUndo(rope, "ロープを置く");
        Selection.activeGameObject = rope;
        EditorSceneManager.MarkSceneDirty(rope.scene);

        Debug.Log($"ロープを置きました（長さ {DefaultHeight}m）。" +
                  "長さは Inspector の Height で変えられます（見た目も一緒に伸びます）。", rope);
    }

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/横向きのロープを置く")]
    public static void CreateHorizontalRope()
    {
        // 床から `HorizontalRopeHeight` の高さに、奥（+Z）へ向けて渡す。
        // X軸まわりに90度倒すと、この物の上向きが「奥」を向く
        Vector3 position = FindGroundPosition() + Vector3.up * HorizontalRopeHeight;

        GameObject rope = CreateRopeAt(
            "RopeHorizontal", position, DefaultHeight, Quaternion.Euler(90f, 0f, 0f));

        Undo.RegisterCreatedObjectUndo(rope, "横向きのロープを置く");
        Selection.activeGameObject = rope;
        EditorSceneManager.MarkSceneDirty(rope.scene);

        Debug.Log($"横向きのロープを置きました（長さ {DefaultHeight}m・高さ {HorizontalRopeHeight}m）。\n" +
                  "**倒して置いてあるので、自動で「伝っていく」動きになります。** " +
                  "向きを変えたいときは、そのまま回転させてください。", rope);
    }

    /// <summary>
    /// ロープを1本作る。**検証シーンを作り直すツールからも呼んでいる。**
    /// `position` はロープの根元（縦なら床に置く位置）。
    /// </summary>
    public static GameObject CreateRopeAt(string name, Vector3 position, float height)
    {
        return CreateRopeAt(name, position, height, Quaternion.identity);
    }

    /// <summary>
    /// 傾きを指定してロープを1本作る。
    /// **倒して置くと、そのまま「横向きのロープ」になる**（`RobotRope` が自動で判断する）。
    /// </summary>
    public static GameObject CreateRopeAt(string name, Vector3 position, float height, Quaternion rotation)
    {
        GameObject rope = new GameObject(name);
        rope.transform.SetPositionAndRotation(position, rotation);

        RobotRope component = rope.AddComponent<RobotRope>();

        // 見た目の筒。**当たり判定は外す**。
        // ロープにぶつかって押し返されると、つかまる前に弾かれてしまうため
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Visual";
        visual.transform.SetParent(rope.transform, false);

        // 筒は「高さ2・中心が原点」なので、半分ずらして立てる
        visual.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        visual.transform.localScale = new Vector3(RopeThickness, height * 0.5f, RopeThickness);
        visual.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateRopeMaterial();

        Object.DestroyImmediate(visual.GetComponent<Collider>());

        SerializedObject serialized = new SerializedObject(component);
        serialized.FindProperty("height").floatValue = height;

        // 長さを変えたときに、筒も一緒に伸びるようにしておく
        serialized.FindProperty("visual").objectReferenceValue = visual.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return rope;
    }

    /// <summary>
    /// `RobotRig` プレハブに、ロープにつかまる機能（<see cref="RobotRopeClimber"/>）を足す。
    /// **すでに付いていれば何もしない**ので、何度実行してもよい。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/ロボットにロープ機能を足す")]
    public static void AddClimberToPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"{PrefabPath} が見つかりません。");
            return;
        }

        // プレハブは開いてから触る。直接いじると保存されない
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);

        // 途中で return しても、開いたプレハブは finally で必ず閉じる
        // （閉じないと、見えない所にプレハブの中身が読み込まれたまま残る）
        try
        {
            if (contents.GetComponent<RobotRopeClimber>() != null)
            {
                Debug.Log("ロープ機能はすでに付いています。");
                return;
            }

            RobotRopeClimber climber = contents.AddComponent<RobotRopeClimber>();

            // 画面中央のレティクルを繋ぐ（狙えているときに色が変わる）
            Reticle reticle = contents.GetComponentInChildren<Reticle>(true);

            if (reticle != null)
            {
                SerializedObject serialized = new SerializedObject(climber);
                serialized.FindProperty("reticle").objectReferenceValue = reticle;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);

            Debug.Log("RobotRig にロープ機能（RobotRopeClimber）を足しました。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// シーンビューが見ている場所の**床**を探す。
    /// 見つからなければ、見ている場所をそのまま使う。
    /// </summary>
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

    /// <summary>
    /// ロープの茶色いマテリアルを用意する。**すでにあれば、そのまま使う**（調整した色を消さないため）。
    /// 保存先のフォルダが無いときは、保存せずにその場限りのマテリアルを返す。
    /// </summary>
    private static Material GetOrCreateRopeMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = new Color(0.72f, 0.55f, 0.28f) };

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            Debug.LogWarning($"{MaterialFolder} が無いので、マテリアルは保存しませんでした。");
            return material;
        }

        AssetDatabase.CreateAsset(material, RopeMaterialPath);

        return material;
    }
}
