using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **踏むボタンと、動く壁をステージに置くためのツール。**
///
/// Unityのメニュー `Tools > StarSweepers > アーカイブ > ロボットステージ > 踏むボタンと動く壁を置く` から実行できる。
/// **ボタンと壁が繋がった状態**で置かれるので、そのまま再生すれば動く。
///
/// ほかに、同じ `ロボットステージ` の下に次のメニューもある。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `鍵と鍵付きの扉を置く` | 鍵（<see cref="DoorKey"/>）と、それで開く扉（<see cref="LockedDoor"/>）を1組置く |
/// | `ガラスの板を置く` | 透けるが通れないガラスの板を1枚置く |
///
/// 置く場所は、どれも**シーンビューが見ている場所の床の上**。
/// 部品を作る public のメソッド（CreateButton など）は、**検証シーンを作り直すツール（`RobotRigSetup`）からも呼んでいる。**
/// 名前や引数を変えるときは、そちらも合わせて直すこと。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class StageGimmickSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";

    // 土台とボタンの大きさ
    private static readonly Vector3 BaseSize = new Vector3(1.2f, 0.2f, 1.2f);
    private const float ButtonRadius = 0.35f;
    private const float ButtonHeight = 0.1f;

    private static readonly Vector3 WallSize = new Vector3(4f, 3f, 0.4f);

    // ガラスの板。**厚みを付けておくと、角のふちが見えて存在が分かりやすい**
    private static readonly Vector3 GlassSize = new Vector3(3f, 2.5f, 0.08f);

    private const string GlassShaderName = "StarSweepers/Glass";
    private const string GlassMaterialPath = MaterialFolder + "/Glass.mat";

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/踏むボタンと動く壁を置く")]
    public static void CreateButtonAndWall()
    {
        Vector3 position = FindGroundPosition();

        // 壁はボタンの奥（+Z）へ 4メートルの所に置く。壁の位置は中心なので、高さの半分だけ持ち上げて床に立たせる
        PressButton button = CreateButton("PressButton", position);
        MoveObject wall = CreateWall("MovingWall", position + new Vector3(0f, WallSize.y * 0.5f, 4f));

        Connect(button, wall);

        Undo.RegisterCreatedObjectUndo(button.gameObject, "ボタンを置く");
        Undo.RegisterCreatedObjectUndo(wall.gameObject, "動く壁を置く");

        Selection.activeGameObject = button.gameObject;
        EditorSceneManager.MarkSceneDirty(button.gameObject.scene);

        Debug.Log("踏むボタンと動く壁を置きました。**ボタンを踏むと壁が上下します。**\n" +
                  "別の物を動かしたいときは、ボタンの「Pressed」に相手を入れ替えてください。", button);
    }

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/鍵と鍵付きの扉を置く")]
    public static void CreateKeyAndDoor()
    {
        Vector3 position = FindGroundPosition();

        // 鍵は横（-X）へ 2メートルずらし、床から 0.2メートル浮かせて置く（再生すると落ちて床に乗る）。
        // 扉は奥（+Z）へ 4メートル。keyId は空 ＝ どの鍵でもどの扉でも開く
        GameObject key = CreateKey("DoorKey", position + new Vector3(-2f, 0.2f, 0f), "");
        LockedDoor door = CreateLockedDoor(
            "LockedDoor", position + new Vector3(0f, WallSize.y * 0.5f, 4f), "");

        Undo.RegisterCreatedObjectUndo(key, "鍵を置く");
        Undo.RegisterCreatedObjectUndo(door.gameObject, "鍵付きの扉を置く");

        Selection.activeGameObject = door.gameObject;
        EditorSceneManager.MarkSceneDirty(door.gameObject.scene);

        Debug.Log("鍵と鍵付きの扉を置きました。**鍵を F で持って、扉に近づいて F を押すと開きます。**\n" +
                  "鍵の種類を分けたいときは、鍵と扉の Key Id に同じ言葉を入れてください。", door);
    }

    [MenuItem("Tools/StarSweepers/アーカイブ/ロボットステージ/ガラスの板を置く")]
    public static void CreateGlassPane()
    {
        Vector3 position = FindGroundPosition() + Vector3.up * (GlassSize.y * 0.5f);

        GameObject glass = CreateGlass("Glass", position);

        Undo.RegisterCreatedObjectUndo(glass, "ガラスを置く");
        Selection.activeGameObject = glass;
        EditorSceneManager.MarkSceneDirty(glass.scene);

        Debug.Log("ガラスの板を置きました。**透けますが、当たり判定はあります。**\n" +
                  "濃さは Inspector の Base Color の A、ふちの見え方は Edge Strength で変えられます。", glass);
    }

    /// <summary>
    /// ガラスの板を1つ作る。**透けるが、当たり判定はそのまま残す。**
    /// `position` は板の中心。
    /// </summary>
    public static GameObject CreateGlass(string name, Vector3 position)
    {
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
        glass.name = name;
        glass.transform.position = position;
        glass.transform.localScale = GlassSize;
        glass.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateGlassMaterial();

        // **当たり判定はそのまま。** Cube に最初から付いている BoxCollider を消さない

        // 透けて見えるので、三人称カメラは壁として扱わない（付けないとガラス越しでカメラが寄る）
        glass.AddComponent<SeeThrough>();

        return glass;
    }

    /// <summary>ガラスのマテリアルを用意する。すでにあれば、そのまま使う（調整を消さない）。</summary>
    private static Material GetOrCreateGlassMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(GlassShaderName);

        if (shader == null)
        {
            Debug.LogError($"シェーダー「{GlassShaderName}」が見つかりません。" +
                           "Assets/Art/Shaders/Glass.shader があるか確認してください。");
            return null;
        }

        Material material = new Material(shader);

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            Debug.LogWarning($"{MaterialFolder} が無いので、マテリアルは保存しませんでした。");
            return material;
        }

        AssetDatabase.CreateAsset(material, GlassMaterialPath);

        return material;
    }

    /// <summary>
    /// 鍵を1つ作る。**持ち運べる物**として作るので、そのまま F で持てる。
    /// `keyId` を空にすると、どの扉にも使える。
    /// </summary>
    public static GameObject CreateKey(string name, Vector3 position, string keyId)
    {
        GameObject key = new GameObject(name);
        key.transform.position = position;

        Material material = GetOrCreateKeyMaterial();

        // 持ち手（輪）と、差し込む棒。鍵らしい形にしておくと拾う物だと分かりやすい
        AddPart(key.transform, "Ring", new Vector3(0f, 0.1f, 0f),
            new Vector3(0.22f, 0.22f, 0.06f), material);
        AddPart(key.transform, "Shaft", new Vector3(0f, -0.08f, 0f),
            new Vector3(0.06f, 0.26f, 0.06f), material);
        AddPart(key.transform, "Tooth", new Vector3(0.07f, -0.16f, 0f),
            new Vector3(0.09f, 0.06f, 0.06f), material);

        // 当たり判定は、部品ごとではなく**まとめて1つ**にする。
        // 細かい形のまま物理に任せると、床で跳ねたり引っかかったりしやすい
        // 大きさ（メートル）は、輪から歯の先までがすっぽり収まる、少し大きめの箱
        BoxCollider collider = key.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.24f, 0.5f, 0.1f);

        // 重さ 0.5kg。空気の抵抗（Damping）を少し入れて、落としたときに転がり続けないようにする
        Rigidbody body = key.AddComponent<Rigidbody>();
        body.mass = 0.5f;
        body.linearDamping = 0.5f;
        body.angularDamping = 1f;

        key.AddComponent<Grabbable>();

        DoorKey doorKey = key.AddComponent<DoorKey>();

        SerializedObject serialized = new SerializedObject(doorKey);
        serialized.FindProperty("keyId").stringValue = keyId;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return key;
    }

    /// <summary>
    /// 鍵付きの扉を1つ作る。`position` は扉の中心。
    /// **開くと上へ持ち上がる**（床に沈める形にしたいときは Move Offset を変える）。
    /// </summary>
    public static LockedDoor CreateLockedDoor(string name, Vector3 position, string keyId)
    {
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = name;
        door.transform.position = position;
        door.transform.localScale = WallSize;
        door.GetComponent<MeshRenderer>().sharedMaterial =
            GetOrCreateMaterial(MaterialFolder + "/LockedDoor.mat", new Color(0.55f, 0.42f, 0.32f));

        MoveObject moving = door.AddComponent<MoveObject>();

        SerializedObject wallSerialized = new SerializedObject(moving);

        // **上へ持ち上がる。** 鍵で開いた扉は「上がる」ほうが見て分かりやすい
        // （扉の高さより 0.2メートル多く上げて、下をくぐれるようにする）
        wallSerialized.FindProperty("positionOffset").vector3Value = new Vector3(0f, WallSize.y + 0.2f, 0f);
        wallSerialized.ApplyModifiedPropertiesWithoutUndo();

        // 鍵穴の印。開いたら消える。**扉の子にしているので、扉と一緒に動く**
        GameObject lockMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lockMark.name = "LockMark";
        lockMark.transform.SetParent(door.transform, false);

        // 扉が大きく引き伸ばされているので、印の大きさは割り算で戻す
        // （実際の大きさは 横0.5 × 縦0.5 × 厚み0.15 メートル）。
        // 位置の -0.55 は、扉の手前の面（-0.5）より少しだけ手前。印が扉から飛び出して見える
        lockMark.transform.localPosition = new Vector3(0f, 0f, -0.55f);
        lockMark.transform.localScale = new Vector3(
            0.5f / WallSize.x, 0.5f / WallSize.y, 0.15f / WallSize.z);
        lockMark.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateKeyMaterial();

        Object.DestroyImmediate(lockMark.GetComponent<Collider>());

        LockedDoor locked = door.AddComponent<LockedDoor>();

        SerializedObject serialized = new SerializedObject(locked);
        serialized.FindProperty("keyId").stringValue = keyId;
        serialized.FindProperty("door").objectReferenceValue = moving;
        serialized.FindProperty("lockIndicator").objectReferenceValue = lockMark;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return locked;
    }

    /// <summary>
    /// 鍵の金色のマテリアル。**鍵そのものと、扉の鍵穴の印の両方に使う**
    /// （同じ色にしておくと、鍵を使う場所だと見て分かる）。
    /// </summary>
    private static Material GetOrCreateKeyMaterial()
    {
        return GetOrCreateMaterial(MaterialFolder + "/DoorKey.mat", new Color(0.95f, 0.78f, 0.25f));
    }

    /// <summary>鍵の部品を1つ足す。当たり判定は持たせない（まとめて1つにするため）。</summary>
    private static void AddPart(Transform parent, string name, Vector3 localPosition,
        Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = size;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;

        Object.DestroyImmediate(part.GetComponent<Collider>());
    }

    /// <summary>
    /// 踏むボタンを1つ作る。**検証シーンを作り直すツールからも呼んでいる。**
    /// `position` は土台を置く床の位置。
    /// </summary>
    public static PressButton CreateButton(string name, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        // ----- 土台（この上に乗る。当たり判定はこちらが持つ） -----
        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObject.name = "Base";
        baseObject.transform.SetParent(root.transform, false);
        baseObject.transform.localPosition = new Vector3(0f, BaseSize.y * 0.5f, 0f);
        baseObject.transform.localScale = BaseSize;
        baseObject.GetComponent<MeshRenderer>().sharedMaterial =
            GetOrCreateMaterial(MaterialFolder + "/ButtonBase.mat", new Color(0.35f, 0.35f, 0.38f));

        // ----- 赤いボタン（沈む部分。**当たり判定は付けない**） -----
        // 付けると、沈むたびに乗っている物を押し上げてしまう
        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        top.name = "Button";
        top.transform.SetParent(root.transform, false);
        top.transform.localPosition = new Vector3(0f, BaseSize.y + ButtonHeight * 0.5f, 0f);

        // 筒は高さ2なので、大きさは半分にする
        top.transform.localScale = new Vector3(ButtonRadius * 2f, ButtonHeight * 0.5f, ButtonRadius * 2f);
        top.GetComponent<MeshRenderer>().sharedMaterial =
            GetOrCreateMaterial(MaterialFolder + "/ButtonTop.mat", new Color(0.85f, 0.15f, 0.15f));

        Object.DestroyImmediate(top.GetComponent<Collider>());

        PressButton button = root.AddComponent<PressButton>();

        SerializedObject serialized = new SerializedObject(button);
        serialized.FindProperty("buttonVisual").objectReferenceValue = top.transform;

        // 反応する箱は、土台の上を覆う位置に置く
        // （土台と同じ広さで、土台の上面から 0.5メートルの高さまで。箱の中心は上面の 0.25メートル上）
        serialized.FindProperty("detectSize").vector3Value =
            new Vector3(BaseSize.x, 0.5f, BaseSize.z);
        serialized.FindProperty("detectOffset").vector3Value =
            new Vector3(0f, BaseSize.y + 0.25f, 0f);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    /// <summary>動く壁を1つ作る。`position` は壁の中心。</summary>
    public static MoveObject CreateWall(string name, Vector3 position)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = WallSize;
        wall.GetComponent<MeshRenderer>().sharedMaterial =
            GetOrCreateMaterial(MaterialFolder + "/MovingWall.mat", new Color(0.45f, 0.55f, 0.75f));

        MoveObject moving = wall.AddComponent<MoveObject>();

        // 床に沈む形にしておく。壁の高さより少し多めに下げると、上端まで隠れる
        SerializedObject serialized = new SerializedObject(moving);
        serialized.FindProperty("positionOffset").vector3Value = new Vector3(0f, -(WallSize.y + 0.2f), 0f);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return moving;
    }

    /// <summary>
    /// ボタンと壁を繋ぐ。
    ///
    /// **インスペクターで手作業でやることを、コードでやっているだけ。**
    /// `UnityEventTools` を使うと、保存される形（Persistent Listener）で繋げる。
    /// `AddListener` で繋ぐと再生を止めたときに消えてしまうので、そちらは使わない。
    /// </summary>
    public static void Connect(PressButton button, MoveObject wall)
    {
        if (button == null || wall == null)
        {
            return;
        }

        UnityEventTools.AddVoidPersistentListener(button.Pressed, wall.Toggle);
        EditorUtility.SetDirty(button);
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
        // （見ている場所が床より下にあっても、上にあっても見つかるように）
        if (Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point;
        }

        return pivot;
    }

    /// <summary>
    /// マテリアルを用意する。
    /// **すでにある場合は色を上書きしない**（調整された色を消さないため）。
    /// </summary>
    private static Material GetOrCreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            Debug.LogWarning($"{MaterialFolder} が無いので、マテリアルは保存しませんでした。");
            return material;
        }

        AssetDatabase.CreateAsset(material, path);

        return material;
    }
}
