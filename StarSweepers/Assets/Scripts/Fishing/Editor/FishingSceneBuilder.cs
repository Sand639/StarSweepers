using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// **釣りフックの検証シーンを組み立てる部品置き場。**
///
/// シーンを作るツールが2つ（<see cref="FishingHookTestSetup"/> と
/// <see cref="FishingArenaTestSetup"/>）あり、**どちらも同じ部品を使う**ので、
/// 床・プレイヤー・カメラ・フック・UIなどの作り方はここにまとめてある。
///
/// ツール側は「どの部品を、どう並べるか」だけを書く。
/// 部品の作り方を直したいときは、このファイルだけ直せば両方に効く。
///
/// その後に増えたツール（障害物 <see cref="FishingObstacleTestSetup"/>・アンカー <see cref="FishingAnchorTestSetup"/>・
/// オンライン <see cref="FishingOnlineSetup"/>・マップ <see cref="FishingMapSetup"/> など）も、ここの部品や置き場所の定数を使っている。
/// **ここを直すと、それらで作るシーン・プレハブもすべて変わる。**
///
/// ## 作る物の「名前」は消さない・変えない
///
/// `"HandPoint"`・`"Body"`・`"ReticleCanvas"` などの名前は、
/// このファイルの中で `transform.Find("名前")` で探して使っている。**名前を変えると、探しても見つからなくなる。**
/// それ以外の名前も、ヒエラルキーで見分ける目印や、説明・ドキュメントに出てくる名前なので変えない。
///
/// ## `SetRef` などで渡すフィールド名
///
/// `SetRef(hookController, "aim", aim)` のように、つなぎ先のスクリプトのフィールド名を**文字列で**書いている。
/// **つなぎ先のフィールドを改名すると、ここは静かに壊れる**（コンパイルエラーにならない）。
/// `SetRef` / `SetObjectArray` は Console に警告を出すが、`SetInt` / `SetFloat` / `SetString` は**何も出さずに飛ばす**ので特に気づきにくい。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
internal static class FishingSceneBuilder
{
    public const string MaterialFolder = "Assets/Art/Materials";
    public const string PrefabFolder = "Assets/Prefabs";

    // ---- プレハブの置き場所 ----
    // **プレハブを別のフォルダへ移したら、ここも直すこと。**
    // ここが合っていないと、ツールが「必要な物がありません」で止まるか、
    // 古い場所にプレハブを作り直して**二重になる**。
    /// <summary>どの遊びでも使う共通のプレハブ（PlayerRig など）。</summary>
    public const string CommonPrefabFolder = PrefabFolder + "/Common";

    /// <summary>釣りのプレハブ全体の置き場所。</summary>
    public const string FishPrefabFolder = PrefabFolder + "/Fish";

    /// <summary>釣りの1人用だけで使うプレハブ。</summary>
    public const string FishOfflinePrefabFolder = FishPrefabFolder + "/Offline";

    /// <summary>釣りのオンラインで使うプレハブ（プレイヤー・ゴール・仕組み一式）。</summary>
    public const string FishOnlinePrefabFolder = FishPrefabFolder + "/Online";

    /// <summary>オンラインで降ってくる物（物資・爆発物）。</summary>
    public const string FishOnlineGimmickFolder = FishOnlinePrefabFolder + "/Gimmick";

    // ---- シーンの置き場所 ----
    /// <summary>ロビー・会場・機能ごとの検証シーン。</summary>
    public const string SceneFolder = "Assets/Scenes/Test";

    /// <summary>**遊ぶためのマップ**（`FishingMap〜`）の置き場所。プロトタイプのステージはここ。</summary>
    public const string MapSceneFolder = "Assets/Scenes/Prototype/Fish";

    private const string PlayerRigPath = CommonPrefabFolder + "/PlayerRig.prefab";
    // ※ FishingSpawnerSetup.ExplosionPrefabPath も同じ場所を指している。動かすときは両方直す
    private const string ExplosionPrefabPath = FishPrefabFolder + "/FishingExplosion.prefab";
    private const string RopeMaterialPath = MaterialFolder + "/Rope.mat";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    /// <summary>
    /// チャージとスキルチェックのゲージの横幅（UIの px。1920×1080 の画面を基準にした大きさ）。
    /// `HookChargeUI` の `barWidth` にも同じ値を入れる（枠やマーカーの位置をこの幅で割り出すため）。
    /// </summary>
    public const float BarWidth = 420f;

    // ---- ステージの大きさ（単位はすべてメートル。中心は原点）----
    public const float ArenaHalf = 20f;       // 床の半分の大きさ（40m四方）
    public const float WallLine = 19f;        // 壁を置く位置（中心から壁の厚みの真ん中までの距離）
    public const float WallHeight = 1.5f;     // 壁の高さ
    public const float WallThickness = 1f;    // 壁の厚み
    public const float PocketWidth = 5f;      // 壁を切り欠く幅
    public const float PocketDepth = 4f;      // ポケットの奥行き（壁より外側へ張り出す長さ）

    /// <summary>スキルチェックの枠の表示数。ThrowController の枠を増やすときはここも増やす。</summary>
    public const int ZoneVisualCount = 2;

    // ------------------------------------------------------------
    // 下準備
    // ------------------------------------------------------------

    /// <summary>
    /// ツールが物を保存する先のフォルダを、無ければ作る。
    /// 深いフォルダを作れば途中のフォルダもできるので、`Fish` や `Fish/Online` はここに並べていない。
    /// </summary>
    public static void EnsureFolders()
    {
        EnsureFolder(SceneFolder);
        EnsureFolder(MapSceneFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(CommonPrefabFolder);
        EnsureFolder(FishOfflinePrefabFolder);
        EnsureFolder(FishOnlineGimmickFolder);
    }

    /// <summary>
    /// プロジェクト共通の入力の設定（移動・クリックなど）を読み込む。
    /// 無いときは警告だけ出して null を返す（シーンは作れるが、操作は効かない）。
    /// </summary>
    public static InputActionAsset LoadInputActions()
    {
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (actions == null)
        {
            Debug.LogWarning(InputActionsPath + " が見つかりません。入力の設定は手で入れてください。");
        }
        return actions;
    }

    // ------------------------------------------------------------
    // 明かりと床
    // ------------------------------------------------------------

    /// <summary>
    /// 太陽の光を1つ置く。空のシーンから作るので、これが無いと真っ暗になる。
    /// 真上からではなく斜め上から照らす（影が横に伸びるので、物の高さが分かりやすい）。
    /// </summary>
    public static void CreateLight()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    /// <summary>40m四方の床を、中心を原点にして置く。</summary>
    public static void CreateGround()
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        // Unity の Plane は大きさ1倍で 10m 四方。「半分の大きさ × 2 ÷ 10」＝ ArenaHalf × 0.2 倍で、ちょうど 40m 四方になる
        ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ------------------------------------------------------------
    // 壁（切り欠きなし）
    // ------------------------------------------------------------

    /// <summary>四方に、まっすぐな壁を置く（ポケットの無いシーン用）。</summary>
    public static void CreatePlainWalls()
    {
        Material material = GetWallMaterial();

        // 床の端から端まで（40m）。四隅は南北の壁と東西の壁が重なってすき間が無くなる
        float length = ArenaHalf * 2f;

        // 箱は中心で位置を決めるので、高さの半分だけ持ち上げると足元が床（高さ0）に来る
        CreateBox("Wall_North", new Vector3(0f, WallHeight * 0.5f, WallLine),
            new Vector3(length, WallHeight, WallThickness), material);
        CreateBox("Wall_South", new Vector3(0f, WallHeight * 0.5f, -WallLine),
            new Vector3(length, WallHeight, WallThickness), material);
        CreateBox("Wall_East", new Vector3(WallLine, WallHeight * 0.5f, 0f),
            new Vector3(WallThickness, WallHeight, length), material);
        CreateBox("Wall_West", new Vector3(-WallLine, WallHeight * 0.5f, 0f),
            new Vector3(WallThickness, WallHeight, length), material);
    }

    // ------------------------------------------------------------
    // 壁（中央にポケットあり）
    // ------------------------------------------------------------

    /// <summary>四方に、中央を切り欠いてポケットを付けた壁を置く。</summary>
    public static void CreatePocketWalls(ScoreBoard scoreBoard)
    {
        Material wallMaterial = GetWallMaterial();
        Material padMaterial = GetOrCreateMaterial(
            MaterialFolder + "/FishingPocketPad.mat", new Color(0.25f, 0.4f, 0.6f));

        // 4辺とも点が入る先は プレイヤー0（1人用なので、どのポケットに入れても同じ人の点になる）
        CreateWallWithPocket("North", "北", 0f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("East", "東", 90f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("South", "南", 180f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("West", "西", 270f, 0, scoreBoard, wallMaterial, padMaterial);
    }

    /// <summary>
    /// 壁の素材。まっすぐな壁とポケット付きの壁で**同じ素材を使う**。
    /// （色は素材がまだ無いときに作る色。すでにあれば、その素材をそのまま使う）
    /// </summary>
    private static Material GetWallMaterial()
    {
        return GetOrCreateMaterial(MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
    }

    /// <summary>
    /// 1辺ぶんの壁と、その中央のポケットを作る。
    ///
    /// <paramref name="yaw"/> で向きを決め、ローカルの +Z を「外向き」として組む。
    /// こうすると、同じ計算で4辺すべてを作れる。
    ///
    /// ※ オンライン用の <see cref="FishingNetSceneBuilder"/> にも、**同じ形の壁を作る処理**がある
    /// （ゴールの部品だけが違う）。形を変えるときは両方そろえること。
    /// </summary>
    private static void CreateWallWithPocket(string name, string label, float yaw,
        int ownerPlayerIndex, ScoreBoard scoreBoard, Material wallMaterial, Material padMaterial)
    {
        GameObject root = new GameObject($"Wall_{name}");
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // 以下の位置はすべて root から見た位置（ローカル座標、メートル）。X が壁に沿った向き、Z が外向き
        float pocketHalf = PocketWidth * 0.5f;
        float segmentLength = ArenaHalf - pocketHalf;                 // 切り欠きの端から床の端までの長さ
        float segmentCenter = pocketHalf + segmentLength * 0.5f;      // その壁の真ん中の位置
        float wallY = WallHeight * 0.5f;                              // 足元を床（高さ0）に合わせる高さ

        // 壁を左右2枚に分けて、中央にポケットの入口を空ける
        AddBox(root.transform, "Segment_L",
            new Vector3(-segmentCenter, wallY, WallLine),
            new Vector3(segmentLength, WallHeight, WallThickness), wallMaterial);
        AddBox(root.transform, "Segment_R",
            new Vector3(segmentCenter, wallY, WallLine),
            new Vector3(segmentLength, WallHeight, WallThickness), wallMaterial);

        // ポケットの床（入ったときに光る部分）。厚み 0.1m の板を、半分の 0.05m だけ持ち上げて床の上に置く
        float padZ = WallLine + PocketDepth * 0.5f;
        GameObject pad = AddBox(root.transform, "Pad",
            new Vector3(0f, 0.05f, padZ),
            new Vector3(PocketWidth, 0.1f, PocketDepth), padMaterial);

        // ポケットの奥と左右。左右の壁は厚み 1m で、切り欠きのすぐ外側（端から 0.5m＝厚みの半分）に立てる。
        // 奥の壁は、その左右の壁の外側まで届くよう、切り欠きの幅 + 2m（左右 1m ずつ）にしてある
        AddBox(root.transform, "Pocket_Back",
            new Vector3(0f, wallY, WallLine + PocketDepth + WallThickness * 0.5f),
            new Vector3(PocketWidth + 2f, WallHeight, WallThickness), wallMaterial);
        AddBox(root.transform, "Pocket_Side_L",
            new Vector3(-(pocketHalf + 0.5f), wallY, padZ),
            new Vector3(1f, WallHeight, PocketDepth), wallMaterial);
        AddBox(root.transform, "Pocket_Side_R",
            new Vector3(pocketHalf + 0.5f, wallY, padZ),
            new Vector3(1f, WallHeight, PocketDepth), wallMaterial);

        // 得点になる当たり判定。ポケットの内側（PocketWidth × PocketDepth）より幅と奥行きを 0.4m 小さく、
        // 高さは壁より 0.4m 大きくしてある
        GameObject trigger = new GameObject($"Pocket_{name}");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, WallHeight * 0.5f, padZ);
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(PocketWidth - 0.4f, WallHeight + 0.4f, PocketDepth - 0.4f);

        // ⚠ フィールド名を文字列で指定している（ScorePocket 側を改名すると静かに壊れる）
        ScorePocket pocket = trigger.AddComponent<ScorePocket>();
        SetString(pocket, "pocketLabel", label);
        SetInt(pocket, "ownerPlayerIndex", ownerPlayerIndex);
        SetRef(pocket, "scoreBoard", scoreBoard);
        SetRef(pocket, "padRenderer", pad.GetComponent<MeshRenderer>());
    }

    // ------------------------------------------------------------
    // プレイヤー
    // ------------------------------------------------------------

    /// <summary>
    /// 既存の `PlayerRig.prefab` を土台にプレイヤーを作る。
    ///
    /// **一人称/三人称のマウス視点操作と内蔵カメラは取り除く。**
    /// 見下ろしと噛み合わないため。`enabled = false` では足りない
    /// （`Awake` が走ってマウスカーソルを画面中央に固定してしまう）。
    /// `PlayerRig.prefab` 本体は変更しない。
    /// </summary>
    public static GameObject CreatePlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRigPath);
        GameObject player;

        if (prefab != null)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // 検証シーンを PlayerRig.prefab の変更から切り離すため、完全に展開する
            PrefabUtility.UnpackPrefabInstance(
                player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        else
        {
            Debug.LogWarning(PlayerRigPath + " が見つかりません。仮のカプセルでプレイヤーを作ります。");
            player = new GameObject("PlayerRig");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
        }

        player.name = "PlayerRig";
        player.transform.position = Vector3.zero;
        player.transform.rotation = Quaternion.identity;

        RemoveComponent<PlayerViewSwitcher>(player);
        RemoveComponent<PlayerController>(player);

        foreach (Camera childCamera in player.GetComponentsInChildren<Camera>(true))
        {
            Object.DestroyImmediate(childCamera.gameObject);
        }

        // 一人称用の照準キャンバスは隠す
        Transform reticle = player.transform.Find("ReticleCanvas");
        if (reticle != null)
        {
            reticle.gameObject.SetActive(false);
        }

        // 手元（フックと糸の起点）。足元から高さ 1m、体の前へ 0.6m の所。
        // **名前で探して使う**（WirePlayer の `Find("HandPoint")`）ので、名前を変えない
        if (player.transform.Find("HandPoint") == null)
        {
            GameObject hand = new GameObject("HandPoint");
            hand.transform.SetParent(player.transform, false);
            hand.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);
        }

        // PlayerRig にすでに付いていればそれを使う。無いとき（仮のカプセル）だけ、
        // 背の高さ 2m・太さ（半径）0.4m の体を、足元が原点に来るように付ける
        if (player.GetComponent<CharacterController>() == null)
        {
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);
        }

        // 釣りの操作に必要なスクリプト。中身のつなぎ込みは WirePlayer で行う
        player.AddComponent<PlayerAimController>();
        player.AddComponent<PlayerStun>();
        player.AddComponent<FishingPlayerController>();
        player.AddComponent<HookController>();
        player.AddComponent<ThrowController>();
        player.AddComponent<HookAimAssist>();

        return player;
    }

    /// <summary>
    /// スタン中に点滅させる見た目と、頭の上に出す印をつなぐ。
    /// **つなぐだけでなく、頭の上の印（StunMarker）をここで作る**ので、1人のプレイヤーに1回だけ呼ぶ。
    /// </summary>
    public static void WireStun(GameObject player, PlayerStun stun)
    {
        // 点滅させるのは「Body」の下にある見た目だけ（PlayerRig の体の名前に合わせている）
        Transform body = player.transform.Find("Body");
        Renderer[] bodyRenderers = body != null
            ? body.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];

        SetObjectArray(stun, "bodyRenderers", bodyRenderers);

        Material markMaterial = GetOrCreateMaterial(
            MaterialFolder + "/FishingStunMark.mat", new Color(1f, 0.25f, 0.2f));

        // 頭の上（足元から 2.45m）に浮かぶ、横長の赤い棒（幅 0.5m）。ふだんは隠しておき、スタン中だけ出す
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "StunMarker";
        marker.transform.SetParent(player.transform, false);
        marker.transform.localPosition = new Vector3(0f, 2.45f, 0f);
        marker.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = markMaterial;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.SetActive(false);

        SetRef(stun, "stunMarker", marker);
    }

    // ------------------------------------------------------------
    // カメラ
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーを追いかける見下ろしカメラを置く。
    ///
    /// 置く位置 (0, 16, -9) は、原点に立つプレイヤーから `TopDownCameraFollow` の初期の Offset だけ離れた所
    /// （高さ 16m・後ろへ 9m）。こうしておくと、再生した瞬間にカメラが飛ばない。
    /// 下向き 60度の傾きは、`TopDownCameraFollow` がそのまま使う（位置だけを追いかけるため）。
    /// </summary>
    public static Camera CreateCamera(GameObject player)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();

        TopDownCameraFollow follow = cameraObject.AddComponent<TopDownCameraFollow>();
        SetRef(follow, "target", player.transform);

        return camera;
    }

    // ------------------------------------------------------------
    // フックと糸
    // ------------------------------------------------------------

    /// <summary>
    /// フック（直径 0.4m の玉）を1つ置く。**1人用なのでシーンに1つだけ**置き、プレイヤーの子にはしない。
    /// （オンライン用は人ごとに要るので、プレイヤーのプレハブの中に入れている。<see cref="FishingNetSceneBuilder"/>）
    /// </summary>
    public static HookProjectile CreateHook()
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/FishingHook.mat", new Color(0.85f, 0.85f, 0.9f));

        GameObject hookObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hookObject.name = "Hook";
        hookObject.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hookObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        // 物にぶつかって止まるのではなく、「当たった」ことだけを知りたいので、すり抜ける当たり判定にする
        hookObject.GetComponent<SphereCollider>().isTrigger = true;

        // 当たったことを知るには Rigidbody が要る。動かすのはスクリプトなので、物理（重力など）では動かさない
        Rigidbody body = hookObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        return hookObject.AddComponent<HookProjectile>();
    }

    /// <summary>
    /// 手元とフックをつなぐ糸を、プレイヤーの子として作る（太さ 0.06m）。
    /// 糸の素材は `Rope.mat` があればそれを使い、無ければ `FishingLine.mat` を作って使う。
    /// </summary>
    public static HookLine CreateLine(GameObject player)
    {
        GameObject lineObject = new GameObject("HookLine");
        lineObject.transform.SetParent(player.transform, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.widthMultiplier = 0.06f;
        lineRenderer.numCapVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Material ropeMaterial = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath)
            ?? GetOrCreateMaterial(MaterialFolder + "/FishingLine.mat", new Color(0.9f, 0.9f, 0.8f));
        lineRenderer.sharedMaterial = ropeMaterial;

        return lineObject.AddComponent<HookLine>();
    }

    // ------------------------------------------------------------
    // 物資と爆発物
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーの周りに、円形に物資を並べる。
    /// <paramref name="count"/> 個を、中心から <paramref name="radius"/> m の円の上に等間隔で置く（北から時計回り）。
    /// </summary>
    public static void CreateSupplyRing(int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, radius);
            // 物資は 0.8m の箱なので、高さの半分（0.4m）持ち上げると床にちょうど乗る
            position.y = 0.4f;

            // 並べて置く物資は、消えても元の場所に戻ってくる（物資が尽きて遊べなくならないように）
            HookableObject hookable = CreateSupply($"Supply_{i + 1}", position);
            SetFloat(hookable, "respawnSeconds", 6f);
        }
    }

    /// <summary>
    /// 物資を1つ作る。**Respawn Seconds は 0（消えたら戻ってこない）。**
    /// スポナー用のプレハブ（<see cref="FishingSpawnerSetup"/>）もこれで作る。
    /// </summary>
    public static HookableObject CreateSupply(string name, Vector3 position)
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));

        GameObject supply = CreateBox(name, position, new Vector3(0.8f, 0.8f, 0.8f), material);

        // 重さ 1kg。Damping は、投げたあと床を滑り続けたり回り続けたりしないための「空気の抵抗」
        // （オンライン用の FishingNetSceneBuilder.AddSupplyParts も同じ値にしてある）
        Rigidbody body = supply.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = supply.AddComponent<HookableObject>();
        SetFloat(hookable, "respawnSeconds", 0f);
        return hookable;
    }

    /// <summary>爆発する物資を1つ置く。**点数は0**にしてポケットに入れても得点にならないようにする。</summary>
    public static GameObject CreateBomb(string name, Vector3 position, ExplosionEffect explosionPrefab)
    {
        Material bodyMaterial = GetOrCreateMaterial(
            MaterialFolder + "/FishingBomb.mat", new Color(0.35f, 0.32f, 0.36f));
        Material fuseMaterial = GetOrCreateMaterial(
            MaterialFolder + "/FishingBombFuse.mat", new Color(0.95f, 0.75f, 0.2f));

        GameObject bomb = CreateBox(name, position, new Vector3(0.9f, 0.9f, 0.9f), bodyMaterial);
        MeshRenderer bombRenderer = bomb.GetComponent<MeshRenderer>();

        // 爆発物だと見て分かるように、上に導火線を立てる。
        // 箱の子なので、位置と大きさは**箱の大きさ（0.9倍）がかけられた値**になる（0.6 → 実際は約 0.54m 上）
        GameObject fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fuse.name = "Fuse";
        fuse.transform.SetParent(bomb.transform, false);
        fuse.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        fuse.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
        fuse.GetComponent<MeshRenderer>().sharedMaterial = fuseMaterial;
        Object.DestroyImmediate(fuse.GetComponent<Collider>());

        Rigidbody body = bomb.AddComponent<Rigidbody>();
        body.mass = 1.2f;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = bomb.AddComponent<HookableObject>();
        SetInt(hookable, "scoreValue", 0);
        SetFloat(hookable, "respawnSeconds", 0f);

        ExplosiveObject explosive = bomb.AddComponent<ExplosiveObject>();
        SetObjectArray(explosive, "blinkRenderers", new Renderer[] { bombRenderer });
        SetRef(explosive, "explosionEffectPrefab", explosionPrefab);

        return bomb;
    }

    /// <summary>爆発の見た目のプレハブを作る（すでにあれば作り直す）。</summary>
    public static ExplosionEffect CreateExplosionPrefab()
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/FishingExplosion.mat", new Color(1f, 0.55f, 0.12f));

        GameObject root = new GameObject("FishingExplosion");
        ExplosionEffect effect = root.AddComponent<ExplosionEffect>();

        // 広がる火の玉。最初は大きさ0（見えない）にしておき、ExplosionEffect が広げていく。
        // 見た目だけなので当たり判定は消す
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.transform.SetParent(root.transform, false);
        ball.transform.localScale = Vector3.zero;
        ball.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(ball.GetComponent<Collider>());

        // 周りを一瞬照らす光（届く範囲 12m）。最初は明るさ0にしておき、ExplosionEffect が光らせる
        GameObject flashObject = new GameObject("Flash");
        flashObject.transform.SetParent(root.transform, false);
        Light flash = flashObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.6f, 0.25f);
        flash.intensity = 0f;
        flash.range = 12f;

        SetRef(effect, "ball", ball.transform);
        SetRef(effect, "flash", flash);

        // シーンに組んだ物をプレハブとして保存したら、シーンからは消す（作業用の仮置きのため）
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
        Object.DestroyImmediate(root);

        return saved != null ? saved.GetComponent<ExplosionEffect>() : null;
    }

    // ------------------------------------------------------------
    // UI
    // ------------------------------------------------------------

    /// <summary>
    /// チャージ量とスキルチェックのゲージを作る。
    /// 位置と大きさは 1920×1080 の画面を基準にした px で書いてあり、画面の大きさに合わせて自動で拡大・縮小される。
    /// どちらのゲージも最初は隠しておき、使うときに HookChargeUI が出す。
    ///
    /// ※ `HookChargeUI.BuildDefault`（ゲージが置かれていないマップ用）にも**同じ形・同じ色のゲージを作る処理**がある。
    /// 形や色を変えるときは、両方そろえること。
    /// </summary>
    public static HookChargeUI CreateUI()
    {
        GameObject canvasObject = new GameObject("HookUI",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // ---- チャージ表示（下から70px、中央）----
        GameObject chargeRoot = MakeContainer("ChargeRoot", canvasObject.transform, new Vector2(0f, 70f));
        MakeImage("ChargeBG", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);
        // 中身のバーは「左端を軸」にして横の倍率 0〜1 で伸ばす。だから左端（-BarWidth/2）に置き、最初は倍率0にしておく
        Image chargeFill = MakeImage("ChargeFill", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.3f, 0.8f, 1f, 1f), new Vector2(-BarWidth * 0.5f, 0f));
        chargeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        chargeFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);

        // ---- スキルチェック表示（下から120px、中央）----
        GameObject timingRoot = MakeContainer("TimingRoot", canvasObject.transform, new Vector2(0f, 120f));
        MakeImage("TimingBG", timingRoot.transform,
            new Vector2(BarWidth, 28f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);

        GameObject[] zoneRoots = new GameObject[ZoneVisualCount];
        Image[] hitRects = new Image[ZoneVisualCount];
        Image[] goodRects = new Image[ZoneVisualCount];
        Image[] perfectRects = new Image[ZoneVisualCount];

        // 前半＝後ろへ投げる枠は青、後半＝前へ投げる枠は橙、で見分けられるようにする
        Color[] zoneTints =
        {
            new Color(0.35f, 0.6f, 1f, 0.35f),
            new Color(1f, 0.6f, 0.25f, 0.35f)
        };

        // 枠の並び順＝ヒエラルキーの並び順（Zone1, Zone2, …）
        for (int i = 0; i < ZoneVisualCount; i++)
        {
            Color tint = zoneTints[Mathf.Min(i, zoneTints.Length - 1)];
            zoneRoots[i] = CreateZoneVisual(timingRoot.transform, i, tint,
                out hitRects[i], out goodRects[i], out perfectRects[i]);
        }

        // マーカーは一番上に出したいので、最後に作る
        Image marker = MakeImage("Marker", timingRoot.transform,
            new Vector2(6f, 44f), Color.white, Vector2.zero);

        chargeRoot.SetActive(false);
        timingRoot.SetActive(false);

        HookChargeUI ui = canvasObject.AddComponent<HookChargeUI>();
        SetRef(ui, "chargeRoot", chargeRoot);
        SetRef(ui, "chargeFill", chargeFill.rectTransform);
        SetRef(ui, "timingRoot", timingRoot);
        SetRef(ui, "timingMarker", marker.rectTransform);
        SetFloat(ui, "barWidth", BarWidth);

        WireZoneVisuals(ui, zoneRoots, hitRects, goodRects, perfectRects);

        return ui;
    }

    /// <summary>
    /// 枠の見た目（HookChargeUI の中の、入れ子のクラス `ZoneVisual` の配列）をつなぐ。
    ///
    /// 入れ子のクラスの中身は <see cref="SetRef"/> では届かないので、ここだけ直接
    /// `FindProperty` / `FindPropertyRelative` で書き込んでいる。
    /// ⚠ `"zoneVisuals"`・`"root"`・`"hitRect"` などは HookChargeUI 側のフィールド名。
    /// **向こうを改名すると、ここは NullReferenceException で止まる**（コンパイルエラーにはならない）。
    /// </summary>
    private static void WireZoneVisuals(HookChargeUI ui, GameObject[] zoneRoots,
        Image[] hitRects, Image[] goodRects, Image[] perfectRects)
    {
        SerializedObject serialized = new SerializedObject(ui);
        SerializedProperty zones = serialized.FindProperty("zoneVisuals");
        zones.arraySize = ZoneVisualCount;

        for (int i = 0; i < ZoneVisualCount; i++)
        {
            SerializedProperty element = zones.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("root").objectReferenceValue = zoneRoots[i];
            element.FindPropertyRelative("hitRect").objectReferenceValue = hitRects[i].rectTransform;
            element.FindPropertyRelative("goodRect").objectReferenceValue = goodRects[i].rectTransform;
            element.FindPropertyRelative("perfectRect").objectReferenceValue = perfectRects[i].rectTransform;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// スキルチェックの枠を1つ作る（`Zone1` のように、番号は1から）。
    ///
    /// 中に「通常（Hit）・良い（Good）・最適（Perfect）」の3つの四角を、**中央そろえで重ねる**。
    /// 位置と幅（90px / 50px / 18px）は仮の値で、再生中に `HookChargeUI.SetZone` が判定の位置と広さに合わせて置き直す。
    /// </summary>
    private static GameObject CreateZoneVisual(Transform timingRoot, int index, Color tint,
        out Image hitRect, out Image goodRect, out Image perfectRect)
    {
        GameObject zoneRoot = new GameObject($"Zone{index + 1}", typeof(RectTransform));
        zoneRoot.transform.SetParent(timingRoot, false);
        RectTransform zoneRect = zoneRoot.GetComponent<RectTransform>();
        zoneRect.anchorMin = new Vector2(0.5f, 0.5f);
        zoneRect.anchorMax = new Vector2(0.5f, 0.5f);
        zoneRect.pivot = new Vector2(0.5f, 0.5f);
        zoneRect.anchoredPosition = Vector2.zero;
        zoneRect.sizeDelta = Vector2.zero;

        // 「通常→良い→最適」の順に重ねる（あとに作ったほうが上に出る）
        hitRect = MakeImage("Hit", zoneRoot.transform,
            new Vector2(90f, 28f), tint, Vector2.zero);
        goodRect = MakeImage("Good", zoneRoot.transform,
            new Vector2(50f, 28f), new Color(1f, 0.85f, 0.2f, 0.55f), Vector2.zero);
        perfectRect = MakeImage("Perfect", zoneRoot.transform,
            new Vector2(18f, 28f), new Color(0.3f, 1f, 0.45f, 0.8f), Vector2.zero);

        return zoneRoot;
    }

    // ------------------------------------------------------------
    // 結線
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーに付いたスクリプトの参照をまとめてつなぐ。
    /// <paramref name="statusUI"/> と <paramref name="scoreBoard"/> は無くてもよい（null可）。
    ///
    /// **<see cref="CreatePlayer"/> で作ったプレイヤーに使う前提**（必要なスクリプトと「HandPoint」が付いていること）。
    /// 中で <see cref="WireStun"/> も呼ぶので、頭の上のスタンの印もここで作られる。
    /// </summary>
    public static void WirePlayer(GameObject player, Camera camera,
        HookProjectile hook, HookLine line, HookChargeUI ui,
        InputActionAsset inputActions, FishingStatusUI statusUI, ScoreBoard scoreBoard)
    {
        PlayerAimController aim = player.GetComponent<PlayerAimController>();
        PlayerStun stun = player.GetComponent<PlayerStun>();
        HookController hookController = player.GetComponent<HookController>();
        ThrowController throwController = player.GetComponent<ThrowController>();
        FishingPlayerController mover = player.GetComponent<FishingPlayerController>();
        Transform handPoint = player.transform.Find("HandPoint");

        // `~0` は「すべてのレイヤー」。マウスの指す床も、フックが引っ掛かる物も、レイヤーで絞らない
        SetRef(aim, "aimCamera", camera);
        SetInt(aim, "groundMask", ~0);

        WireStun(player, stun);

        SetRef(mover, "aim", aim);
        SetRef(mover, "stun", stun);
        SetRef(mover, "inputActions", inputActions);

        SetRef(hookController, "aim", aim);
        SetRef(hookController, "handPoint", handPoint);
        SetRef(hookController, "hook", hook);
        SetRef(hookController, "line", line);
        SetRef(hookController, "throwController", throwController);
        SetRef(hookController, "ui", ui);
        SetRef(hookController, "stun", stun);
        SetRef(hookController, "inputActions", inputActions);
        SetInt(hookController, "hookableMask", ~0);

        HookAimAssist aimAssist = player.GetComponent<HookAimAssist>();
        if (aimAssist != null)
        {
            SetRef(hookController, "aimAssist", aimAssist);
        }

        SetRef(throwController, "hook", hookController);
        SetRef(throwController, "inputActions", inputActions);

        if (statusUI != null)
        {
            SetRef(statusUI, "scoreBoard", scoreBoard);
            SetRef(statusUI, "playerStun", stun);
        }
    }

    // ------------------------------------------------------------
    // 小さな部品
    // ------------------------------------------------------------

    /// <summary>
    /// 箱を1つ置く。<paramref name="position"/> は**箱の中心**のワールド位置、<paramref name="size"/> は縦横高さ（メートル）。
    /// 床の上に乗せたいときは、高さの半分だけ持ち上げた位置を渡す。当たり判定（BoxCollider）付き。
    /// </summary>
    public static GameObject CreateBox(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    /// <summary>
    /// <see cref="CreateBox"/> と同じだが、<paramref name="parent"/> の子として置く。
    /// 位置は**親から見た位置**（ローカル座標）なので、親を回すと箱も一緒に回る（4辺の壁を同じ計算で作るのに使う）。
    /// </summary>
    public static GameObject AddBox(Transform parent, string name,
        Vector3 localPosition, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    /// <summary>
    /// UIの入れ物（見た目なし）を作る。**画面の下の中央**を基準にし、<paramref name="anchoredPosition"/> の Y は
    /// 画面の下端から上へ何 px か、を表す。
    /// </summary>
    public static GameObject MakeContainer(string name, Transform parent, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = Vector2.zero;
        return go;
    }

    /// <summary>
    /// 色を塗った四角（UIの画像）を作る。親の**中央**を基準に、<paramref name="anchoredPosition"/>（px）だけずらして置く。
    /// クリックを受け取らない設定（raycastTarget = false）にしてある。ゲージは見るだけで、押す物ではないため。
    /// </summary>
    public static Image MakeImage(string name, Transform parent,
        Vector2 size, Color color, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPosition;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// <paramref name="target"/> から、型 <typeparamref name="T"/> の部品を取り除く（無ければ何もしない）。
    /// `DestroyImmediate` の2つ目の true は「アセット（プレハブ本体など）でも消してよい」という意味。
    /// **プレハブ本体を渡すと本体から消えてしまう**ので、シーンに置いた物（展開済みの物）だけを渡すこと。
    /// </summary>
    public static void RemoveComponent<T>(GameObject target) where T : Component
    {
        // 依存（RequireComponent）で消せないことがあるので、消えるまで数回試す
        for (int i = 0; i < 4; i++)
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                return;
            }
            Object.DestroyImmediate(component, true);
        }
    }

    // ---- スクリプトの値を、インスペクターで入れるのと同じ形で書き込む ----
    // private の [SerializeField] にも書き込めるよう、SerializedObject を通している。
    // ⚠ 引数 field は、つなぎ先のフィールド名そのもの（文字列）。
    //   **つなぎ先のフィールドを改名すると、ここは静かに壊れる**（コンパイルエラーにならない）。
    //   見つからないとき、SetRef / SetObjectArray は Console に警告を出すが、
    //   SetInt / SetFloat / SetString は**何も言わずに飛ばす**。

    /// <summary>参照（ほかの部品・オブジェクト）を1つ入れる。</summary>
    public static void SetRef(Object owner, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>参照の配列を、<paramref name="values"/> の中身で丸ごと置き換える（長さも合わせる）。</summary>
    public static void SetObjectArray(Object owner, string field, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>整数を入れる。LayerMask（どのレイヤーを対象にするか）もこれで入れられる。</summary>
    public static void SetInt(Object owner, string field, int value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetFloat(Object owner, string field, float value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetString(Object owner, string field, string value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// <paramref name="path"/> の素材を読み込む。**無ければ <paramref name="color"/> の色で作って保存する。**
    ///
    /// ・すでにある素材は**そのまま使い、色は変えない**（Unity 上で色を変えていても、上書きしない）。
    /// ・URP のシェーダーが無ければ、標準のシェーダーで作る。
    /// ・素材のフォルダが無いときは保存せず、シーンの中だけの素材になる（ファイルとしては残らない）。
    /// </summary>
    public static Material GetOrCreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };

        if (AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    /// <summary>
    /// フォルダが無ければ作る。**途中のフォルダもまとめて作る**
    /// （`Assets/Scenes/Prototype/Fish` のように深い場所でも使える）。
    /// </summary>
    public static void EnsureFolder(string path)
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
