using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **オンライン用の部品を組み立てる置き場。**
///
/// 1人用の部品は <see cref="FishingSceneBuilder"/> にあり、
/// **通信のための上乗せだけ**をこちらに分けてある。
/// こうしておくと、1人用のシーンを作る処理に手を入れずにオンラインを足せる。
///
/// 使っているツール：<see cref="FishingOnlineSetup"/>（ロビー＋会場）・<see cref="FishingMapSetup"/>（マップ）・
/// <see cref="FishingSpawnerSetup"/>（物資のプレハブ）。ほかの遊びのツール（`SpaceJunkSetup`）も `NetworkTickRate` を使っている。
///
/// ⚠ 1人用と**同じ形の物を、こちらでもう一度作っている**所が多い（壁とゴール・フック・糸・スタンの印・物資・爆発物）。
/// 形や色を変えるときは、`FishingSceneBuilder` の同じ部品もそろえること。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
internal static class FishingNetSceneBuilder
{
    public const string OnlinePlayerPrefabPath =
        FishingSceneBuilder.FishOnlinePrefabFolder + "/FishingOnlinePlayer.prefab";

    /// <summary>1秒あたり何回、位置などを送るか（`LanPlayTestSetup` と同じ値）。</summary>
    public const uint NetworkTickRate = 60;

    /// <summary>プレイヤーをなめらかに見せるための待ち時間（秒）。短いほど反応がよい。</summary>
    private const float PlayerInterpolationTime = 0.05f;

    /// <summary>物資をなめらかに見せるための待ち時間（秒）。物理なので少し長め。</summary>
    private const float SupplyInterpolationTime = 0.075f;

    // ------------------------------------------------------------
    // オンライン用プレイヤーのプレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// **オンラインで1人分になるプレハブを作る。**
    ///
    /// `NetworkManager` の Player Prefab に入れると、つないだ人数分だけ自動で生まれる。
    /// **フックと糸をプレハブの中に入れてある**のが1人用との違い
    /// （1人用はシーンに1つ置いているが、オンラインでは人ごとに必要なため）。
    ///
    /// 見た目は基本形状のカプセル。`PlayerRig.prefab` を使わない理由は、
    /// 中に一人称/三人称の操作とカメラが入っており、
    /// **プレハブとして持つと毎回それを取り除く処理が必要になる**ため。
    /// </summary>
    public static GameObject CreateOnlinePlayerPrefab(InputActionAsset inputActions)
    {
        Material bodyMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/PlayerRigBody.mat", Color.white);

        GameObject root = new GameObject("FishingOnlinePlayer");

        // ---- 通信の部品 ----
        root.AddComponent<NetworkObject>();

        // 自分のプレイヤーは自分のPCで動かし、その位置をほかの人へ送る
        NetworkTransform networkTransform = AddOwnerNetworkTransform(root, PlayerInterpolationTime);
        // 毎フレーム送る小さな変化は、届かなくても次ですぐ追いつくので、確実さより速さを選ぶ
        networkTransform.UseUnreliableDeltas = true;

        // ---- 体 ----
        // 背の高さ 2m・太さ（半径）0.4m で、足元が原点に来るようにする（1人用のプレイヤーと同じ大きさ）。
        // 50度までの坂は登れ、0.3m までの段差は乗り越えられる
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 1f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.3f;

        // 見た目のカプセル。ぶつかる判定は CharacterController が受け持つので、カプセルの当たり判定は消す
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;

        // 前を向いているのが分かる印（体の子にしておくと、色も一緒に変わる）
        GameObject frontMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontMark.name = "FrontMark";
        frontMark.transform.SetParent(body.transform, false);
        frontMark.transform.localPosition = new Vector3(0f, 0.25f, 0.5f);
        frontMark.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        Object.DestroyImmediate(frontMark.GetComponent<Collider>());
        MeshRenderer frontRenderer = frontMark.GetComponent<MeshRenderer>();
        frontRenderer.sharedMaterial = bodyMaterial;

        // ---- 手元・糸・フック ----
        // 手元は足元から高さ 1m、体の前へ 0.6m の所（1人用と同じ）
        GameObject hand = new GameObject("HandPoint");
        hand.transform.SetParent(root.transform, false);
        hand.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);

        HookLine hookLine = CreatePlayerLine(root.transform);

        HookProjectile hookProjectile = CreatePlayerHook(root.transform);

        // ---- スタンの印 ----
        GameObject stunMarker = CreateStunMarker(root.transform);

        // ---- 操作のスクリプト ----
        PlayerAimController aim = root.AddComponent<PlayerAimController>();
        PlayerStun stun = root.AddComponent<PlayerStun>();
        FishingPlayerController mover = root.AddComponent<FishingPlayerController>();
        HookController hookController = root.AddComponent<HookController>();
        ThrowController throwController = root.AddComponent<ThrowController>();
        // 狙いの切り替え。**他の人のぶんでも目印を出すので、下の ownerOnlyScripts には入れない**
        HookAimAssist aimAssist = root.AddComponent<HookAimAssist>();
        FishingNetPlayer netPlayer = root.AddComponent<FishingNetPlayer>();

        // ---- 結線 ----
        // ⚠ "groundMask" などは、つなぎ先のスクリプトのフィールド名（向こうを改名すると静かに壊れる）
        FishingSceneBuilder.SetInt(aim, "groundMask", ~0);

        FishingSceneBuilder.SetObjectArray(stun, "bodyRenderers",
            new Renderer[] { bodyRenderer, frontRenderer });
        FishingSceneBuilder.SetRef(stun, "stunMarker", stunMarker);

        FishingSceneBuilder.SetRef(mover, "aim", aim);
        FishingSceneBuilder.SetRef(mover, "stun", stun);
        FishingSceneBuilder.SetRef(mover, "inputActions", inputActions);

        FishingSceneBuilder.SetRef(hookController, "aim", aim);
        FishingSceneBuilder.SetRef(hookController, "handPoint", hand.transform);
        FishingSceneBuilder.SetRef(hookController, "hook", hookProjectile);
        FishingSceneBuilder.SetRef(hookController, "line", hookLine);
        FishingSceneBuilder.SetRef(hookController, "throwController", throwController);
        FishingSceneBuilder.SetRef(hookController, "stun", stun);
        FishingSceneBuilder.SetRef(hookController, "netPlayer", netPlayer);
        FishingSceneBuilder.SetRef(hookController, "aimAssist", aimAssist);
        FishingSceneBuilder.SetRef(hookController, "inputActions", inputActions);
        FishingSceneBuilder.SetInt(hookController, "hookableMask", ~0);

        FishingSceneBuilder.SetRef(throwController, "hook", hookController);
        FishingSceneBuilder.SetRef(throwController, "inputActions", inputActions);

        // 自分のぶんだけ有効にするスクリプト（他の人のぶんは止める）
        FishingSceneBuilder.SetObjectArray(netPlayer, "ownerOnlyScripts",
            new MonoBehaviour[] { aim, mover, hookController, throwController });
        FishingSceneBuilder.SetRef(netPlayer, "characterController", controller);
        FishingSceneBuilder.SetObjectArray(netPlayer, "teamRenderers",
            new Renderer[] { bodyRenderer, frontRenderer });
        FishingSceneBuilder.SetRef(netPlayer, "hookVisual", hookProjectile.transform);
        FishingSceneBuilder.SetRef(netPlayer, "line", hookLine);
        FishingSceneBuilder.SetRef(netPlayer, "handPoint", hand.transform);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, OnlinePlayerPrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>
    /// 手元とフックをつなぐ糸を、プレイヤーの子として作る（太さ 0.06m）。
    ///
    /// ※ 1人用の <c>FishingSceneBuilder.CreateLine</c> とは素材の選び方が少し違う。
    /// こちらは `Rope.mat` が無ければ **`Rope.mat` という名前で**作る（1人用は `FishingLine.mat` を作る）。
    /// </summary>
    private static HookLine CreatePlayerLine(Transform parent)
    {
        GameObject lineObject = new GameObject("HookLine");
        lineObject.transform.SetParent(parent, false);
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.widthMultiplier = 0.06f;
        lineRenderer.numCapVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.sharedMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/Rope.mat", new Color(0.9f, 0.9f, 0.8f));
        return lineObject.AddComponent<HookLine>();
    }

    /// <summary>
    /// フック（直径 0.4m の玉）を、プレイヤーの子として作る。最初は手元（HandPoint）と同じ位置に置く。
    /// 1人用の <c>FishingSceneBuilder.CreateHook</c> と同じ形・同じ色・同じ当たり判定
    /// （すり抜ける当たり判定＋物理では動かない Rigidbody。理由はそちらを参照）。
    /// </summary>
    private static HookProjectile CreatePlayerHook(Transform parent)
    {
        GameObject hookObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hookObject.name = "Hook";
        hookObject.transform.SetParent(parent, false);
        hookObject.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);
        hookObject.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hookObject.GetComponent<MeshRenderer>().sharedMaterial =
            FishingSceneBuilder.GetOrCreateMaterial(
                FishingSceneBuilder.MaterialFolder + "/FishingHook.mat",
                new Color(0.85f, 0.85f, 0.9f));
        hookObject.GetComponent<SphereCollider>().isTrigger = true;
        Rigidbody hookBody = hookObject.AddComponent<Rigidbody>();
        hookBody.isKinematic = true;
        hookBody.useGravity = false;
        return hookObject.AddComponent<HookProjectile>();
    }

    /// <summary>
    /// 頭の上（足元から 2.45m）に浮かぶ、横長の赤い棒（幅 0.5m）を作る。ふだんは隠しておき、スタン中だけ出す。
    /// 1人用の <c>FishingSceneBuilder.WireStun</c> で作る印と同じ形・同じ色。
    /// </summary>
    private static GameObject CreateStunMarker(Transform parent)
    {
        GameObject stunMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stunMarker.name = "StunMarker";
        stunMarker.transform.SetParent(parent, false);
        stunMarker.transform.localPosition = new Vector3(0f, 2.45f, 0f);
        stunMarker.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
        stunMarker.GetComponent<MeshRenderer>().sharedMaterial =
            FishingSceneBuilder.GetOrCreateMaterial(
                FishingSceneBuilder.MaterialFolder + "/FishingStunMark.mat",
                new Color(1f, 0.25f, 0.2f));
        Object.DestroyImmediate(stunMarker.GetComponent<Collider>());
        stunMarker.SetActive(false);
        return stunMarker;
    }

    // ------------------------------------------------------------
    // オンライン用のゴール（ポケット）
    // ------------------------------------------------------------

    /// <summary>
    /// 四方に、中央を切り欠いてゴールを付けた壁を置く（オンライン用）。
    /// 1人用との違いは、ゴールが <see cref="FishingNetPocket"/> になり、
    /// **持ち主のチームが人数に応じて変わる**こと。
    /// </summary>
    public static void CreateNetworkPocketWalls()
    {
        Material wallMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
        Material padMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingPocketPad.mat", new Color(0.25f, 0.4f, 0.6f));

        // 並び順は FishingTeams.PocketPlaceName と合わせる（0=北 / 1=東 / 2=南 / 3=西）
        CreateNetworkWallWithPocket("North", 0, 0f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("East", 1, 90f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("South", 2, 180f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("West", 3, 270f, wallMaterial, padMaterial);
    }

    /// <summary>
    /// 1辺ぶんの壁と、その中央のゴールを作る。<paramref name="yaw"/> で向きを決め、ローカルの +Z を「外向き」として組む。
    ///
    /// **壁の形（位置・大きさ・名前）は、1人用の <c>FishingSceneBuilder.CreateWallWithPocket</c> と同じ**にしてある。
    /// 違うのはゴールの部品（<see cref="FishingNetPocket"/>＋NetworkObject）だけ。
    /// 数値の意味はそちらのコメントを参照。形を変えるときは両方そろえること。
    /// </summary>
    private static void CreateNetworkWallWithPocket(string name, int pocketIndex, float yaw,
        Material wallMaterial, Material padMaterial)
    {
        GameObject root = new GameObject($"Wall_{name}");
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float pocketHalf = FishingSceneBuilder.PocketWidth * 0.5f;
        float segmentLength = FishingSceneBuilder.ArenaHalf - pocketHalf;
        float segmentCenter = pocketHalf + segmentLength * 0.5f;
        float wallY = FishingSceneBuilder.WallHeight * 0.5f;
        float wallLine = FishingSceneBuilder.WallLine;
        float depth = FishingSceneBuilder.PocketDepth;
        float thickness = FishingSceneBuilder.WallThickness;
        float height = FishingSceneBuilder.WallHeight;

        FishingSceneBuilder.AddBox(root.transform, "Segment_L",
            new Vector3(-segmentCenter, wallY, wallLine),
            new Vector3(segmentLength, height, thickness), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Segment_R",
            new Vector3(segmentCenter, wallY, wallLine),
            new Vector3(segmentLength, height, thickness), wallMaterial);

        float padZ = wallLine + depth * 0.5f;
        GameObject pad = FishingSceneBuilder.AddBox(root.transform, "Pad",
            new Vector3(0f, 0.05f, padZ),
            new Vector3(FishingSceneBuilder.PocketWidth, 0.1f, depth), padMaterial);

        FishingSceneBuilder.AddBox(root.transform, "Pocket_Back",
            new Vector3(0f, wallY, wallLine + depth + thickness * 0.5f),
            new Vector3(FishingSceneBuilder.PocketWidth + 2f, height, thickness), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Pocket_Side_L",
            new Vector3(-(pocketHalf + 0.5f), wallY, padZ),
            new Vector3(1f, height, depth), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Pocket_Side_R",
            new Vector3(pocketHalf + 0.5f, wallY, padZ),
            new Vector3(1f, height, depth), wallMaterial);

        // 得点の判定。**通信で合図を送るので NetworkObject が必要**
        GameObject trigger = new GameObject($"Pocket_{name}");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, wallY, padZ);

        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(
            FishingSceneBuilder.PocketWidth - 0.4f, height + 0.4f, depth - 0.4f);

        trigger.AddComponent<NetworkObject>();

        // ⚠ "pocketIndex"・"padRenderer" は FishingNetPocket のフィールド名（向こうを改名すると静かに壊れる）
        FishingNetPocket pocket = trigger.AddComponent<FishingNetPocket>();
        FishingSceneBuilder.SetInt(pocket, "pocketIndex", pocketIndex);
        FishingSceneBuilder.SetRef(pocket, "padRenderer", pad.GetComponent<MeshRenderer>());
    }

    // ------------------------------------------------------------
    // オンライン用の物資と爆発物
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーの周りに、円形に物資を並べる（オンライン用）。並べ方は 1人用の <c>FishingSceneBuilder.CreateSupplyRing</c> と同じ。
    /// ※ 今はどのツールからも呼ばれていない（オンライン会場の物資は、スポナーが出す形に変わったため）。
    /// </summary>
    public static void CreateNetworkSupplyRing(int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, radius);
            position.y = 0.4f;

            CreateNetworkSupply($"Supply_{i + 1}", position);
        }
    }

    /// <summary>物資を1つ作る（オンライン用）。スポナー用のプレハブもこれで作る。</summary>
    public static GameObject CreateNetworkSupply(string name, Vector3 position)
    {
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));

        GameObject supply = FishingSceneBuilder.CreateBox(
            name, position, new Vector3(0.8f, 0.8f, 0.8f), material);

        HookableObject hookable = AddSupplyParts(supply, 1f);
        FishingSceneBuilder.SetFloat(hookable, "respawnSeconds", 0f);
        return supply;
    }

    /// <summary>
    /// 爆発する物資を1つ置く（オンライン用）。スポナー用のプレハブもこれで作る。
    /// 見た目と点数0は 1人用の <c>FishingSceneBuilder.CreateBomb</c> と同じで、通信の部品が足してある。
    /// </summary>
    public static GameObject CreateNetworkBomb(string name, Vector3 position, ExplosionEffect explosionPrefab)
    {
        Material bodyMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingBomb.mat", new Color(0.35f, 0.32f, 0.36f));
        Material fuseMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingBombFuse.mat", new Color(0.95f, 0.75f, 0.2f));

        GameObject bomb = FishingSceneBuilder.CreateBox(
            name, position, new Vector3(0.9f, 0.9f, 0.9f), bodyMaterial);
        MeshRenderer bombRenderer = bomb.GetComponent<MeshRenderer>();

        GameObject fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fuse.name = "Fuse";
        fuse.transform.SetParent(bomb.transform, false);
        fuse.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        fuse.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
        fuse.GetComponent<MeshRenderer>().sharedMaterial = fuseMaterial;
        Object.DestroyImmediate(fuse.GetComponent<Collider>());

        HookableObject hookable = AddSupplyParts(bomb, 1.2f);
        FishingSceneBuilder.SetInt(hookable, "scoreValue", 0);
        FishingSceneBuilder.SetFloat(hookable, "respawnSeconds", 0f);

        ExplosiveObject explosive = bomb.AddComponent<ExplosiveObject>();
        FishingSceneBuilder.SetObjectArray(explosive, "blinkRenderers",
            new Renderer[] { bombRenderer });
        FishingSceneBuilder.SetRef(explosive, "explosionEffectPrefab", explosionPrefab);

        // 導火線を全員でそろえる部品（オンライン用）
        bomb.AddComponent<FishingNetBomb>();

        return bomb;
    }

    /// <summary>
    /// 物資に、物理と通信の部品をまとめて付ける。<paramref name="mass"/> は重さ（kg）。
    /// 空気の抵抗（Damping）は 1人用の物資と同じ値。
    /// </summary>
    private static HookableObject AddSupplyParts(GameObject supply, float mass)
    {
        Rigidbody body = supply.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = supply.AddComponent<HookableObject>();

        supply.AddComponent<NetworkObject>();

        // **持ち主が動かす形。** 引き寄せ中は引っ掛けた人が持ち主になり、
        // 投げたあとはホストが持ち主に戻る（FishingNetSupply が入れ替える）
        AddOwnerNetworkTransform(supply, SupplyInterpolationTime);

        supply.AddComponent<FishingNetSupply>();

        return hookable;
    }

    /// <summary>
    /// 位置と向きを全員の画面にそろえる部品（NetworkTransform）を付ける。
    ///
    /// ・**持ち主（Owner）が動かし、その結果をほかの人へ送る**形にする（ホストが全部動かす形ではない）。
    /// ・大きさは変わらないので送らない（通信量を減らすため）。
    /// ・<paramref name="interpolationTime"/>（秒）は、届いた位置をなめらかにつなぐための待ち時間。
    ///   短いほど反応がよく、長いほど動きがなめらかになる。
    /// </summary>
    private static NetworkTransform AddOwnerNetworkTransform(GameObject target, float interpolationTime)
    {
        NetworkTransform networkTransform = target.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
        networkTransform.PositionMaxInterpolationTime = interpolationTime;
        networkTransform.RotationMaxInterpolationTime = interpolationTime;
        return networkTransform;
    }
}
