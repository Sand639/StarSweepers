using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 隕石攻撃のプレハブ生成と、選択した床への設置を行うEditorメニュー。
///
/// | メニュー（`Tools > StarSweepers > Space Junk > Meteor >`） | すること |
/// | --- | --- |
/// | `Create Prefabs` | `Assets/Prefabs/SpaceJunk/Gimmick/Meteor/` に、隕石攻撃の6つのプレハブと3つのマテリアルを作る。**すでにある物は上書きしない** |
/// | `Add To Current Scene` | いま開いているシーンに管理役（MeteorManager）を置き、**選んでいる床**それぞれに落下範囲（MeteorArea）を子として足す |
/// | `Install Into STAGE_03` | STAGE_03 を開き、ドーナツ床の上に8方向の落下範囲を置いて、管理役の数値を決めた値にして保存する |
///
/// STAGE_03 の数値（4秒ごと・1回3個・予告4秒・半径5m・範囲の中央に落とす）は `隕石攻撃.md` に書いてあり、
/// `Stage03MeteorSetupTests` がその値になっているかを確かめている。ここを変えたらテストも合わせること。
/// </summary>
public static class SpaceJunkMeteorSetup
{
    public const string Stage03ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_03.unity";
    public const string PrefabFolder = "Assets/Prefabs/SpaceJunk/Gimmick/Meteor";
    public const string ManagerPrefabPath = PrefabFolder + "/MeteorManager.prefab";
    public const string AreaPrefabPath = PrefabFolder + "/MeteorArea.prefab";
    public const string StrikePrefabPath = PrefabFolder + "/MeteorStrikeEvent.prefab";
    public const string MeteorPrefabPath = PrefabFolder + "/Meteor.prefab";
    public const string WarningPrefabPath = PrefabFolder + "/MeteorWarning.prefab";
    public const string ImpactVfxPrefabPath = PrefabFolder + "/MeteorImpactVfx.prefab";

    private const string MeteorMaterialPath = PrefabFolder + "/Meteor.mat";
    private const string WarningMaterialPath = PrefabFolder + "/MeteorWarning.mat";
    private const string ImpactMaterialPath = PrefabFolder + "/MeteorImpact.mat";

    /// <summary>メニューから呼ぶ入口。テストからは <see cref="CreateMeteorPrefabs"/> を直接呼ぶ。</summary>
    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Create Prefabs")]
    public static void CreateMeteorPrefabsCommand()
    {
        CreateMeteorPrefabs();
        Debug.Log($"[METEOR] 隕石攻撃のプレハブを作りました：{PrefabFolder}");
    }

    /// <summary>
    /// いま開いているシーンに隕石攻撃を足す。管理役がまだ無ければ置き、
    /// ヒエラルキーで選んでいる床それぞれに、床と同じ大きさの落下範囲を子として足して、管理役の一覧に入れる。
    /// すでに落下範囲が付いている床には足さない（一覧に入っていなければ入れるだけ）。
    /// 置いた物は Ctrl+Z で消せるが、管理役の一覧の書き換えは Ctrl+Z では戻らない。
    /// </summary>
    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Add To Current Scene")]
    public static void AddToCurrentScene()
    {
        // 置くプレハブが無いと始まらないので、足りない物を先に作っておく
        CreateMeteorPrefabs();

        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        GameObject areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        MeteorManager manager = Object.FindFirstObjectByType<MeteorManager>();

        if (manager == null)
        {
            GameObject managerObject = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab);
            Undo.RegisterCreatedObjectUndo(managerObject, "Add Meteor Manager");
            manager = managerObject.GetComponent<MeteorManager>();
        }

        List<MeteorArea> areas = ReadAreas(manager);
        int added = 0;

        foreach (GameObject plate in Selection.gameObjects)
        {
            // シーンの物だけを相手にする（プロジェクト画面で選んだプレハブなどは scene が無効）。
            // 管理役そのものを選んでいても床とはみなさない
            if (plate == null || !plate.scene.IsValid() || plate.GetComponent<MeteorManager>() != null)
            {
                continue;
            }

            MeteorArea area = FindDirectAreaChild(plate.transform);
            if (area == null)
            {
                GameObject areaObject = (GameObject)PrefabUtility.InstantiatePrefab(areaPrefab, plate.transform);
                Undo.RegisterCreatedObjectUndo(areaObject, "Add Meteor Area");
                areaObject.name = $"{plate.name}_MeteorArea";
                // 床とぴったり重ねる。大きさは子の拡大ではなく、当たり判定の大きさで床に合わせる
                areaObject.transform.localPosition = Vector3.zero;
                areaObject.transform.localRotation = Quaternion.identity;
                areaObject.transform.localScale = Vector3.one;
                area = areaObject.GetComponent<MeteorArea>();
                CopyPlateBounds(plate, areaObject.GetComponent<BoxCollider>());
                added++;
            }

            if (!areas.Contains(area))
            {
                areas.Add(area);
            }
        }

        SetAreas(manager, areas);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        Selection.activeGameObject = manager.gameObject;

        Debug.Log($"[METEOR] MeteorManagerを場面に用意し、選択した床へMeteorAreaを{added}個追加しました。" +
                  "床を選んでいなかった場合は、Area Prefabを手で置いてManagerのActive Areasへ登録してください。");
    }

    /// <summary>
    /// STAGE_03 に隕石攻撃を設置して保存する。何度実行しても、同じ名前の物は作り直さずに位置と数値だけ合わせ直す。
    /// </summary>
    [MenuItem("Tools/StarSweepers/Space Junk/Meteor/Install Into STAGE_03")]
    public static void InstallIntoStage03()
    {
        // STAGE_03 を Single で開き直すので、作業中のシーンに保存していない変更があれば先に聞く
        // （コマンドラインからの実行では聞けないので飛ばす）
        if (!Application.isBatchMode &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        CreateMeteorPrefabs();

        Scene scene = EditorSceneManager.OpenScene(Stage03ScenePath, OpenSceneMode.Single);
        GameObject ground = FindSceneObject(scene, "Ground");
        if (ground == null)
        {
            throw new System.InvalidOperationException("[METEOR] STAGE_03にGroundが見つかりません。");
        }

        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        GameObject areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        MeteorManager manager = FindSceneComponent<MeteorManager>(scene);
        if (manager == null)
        {
            GameObject managerObject = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, scene);
            managerObject.name = "MeteorManager";
            manager = managerObject.GetComponent<MeteorManager>();
        }

        // 落下範囲は Ground の子の「MeteorAreas」の下にまとめる（床ごと動かしても付いてくるように）
        Transform areaRoot = ground.transform.Find("MeteorAreas");
        if (areaRoot == null)
        {
            GameObject rootObject = new GameObject("MeteorAreas");
            rootObject.transform.SetParent(ground.transform, false);
            areaRoot = rootObject.transform;
        }

        // 北（奥 +Z）から時計回りに 45度ずつ、8方向。名前は「番号_方角」（例：MeteorArea_02_E）
        string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        List<MeteorArea> areas = new List<MeteorArea>();
        for (int i = 0; i < directions.Length; i++)
        {
            string areaName = $"MeteorArea_{i:00}_{directions[i]}";
            Transform existing = areaRoot.Find(areaName);
            GameObject areaObject;
            if (existing != null)
            {
                areaObject = existing.gameObject;
            }
            else
            {
                areaObject = (GameObject)PrefabUtility.InstantiatePrefab(areaPrefab, areaRoot);
                areaObject.name = areaName;
            }

            // ドーナツ床（内周 9m・外周 15m）の幅の真ん中、中心から 12m の所に置き、外向きに回す。
            // 回しているので、範囲の X は円周に沿う向き、Z は中心から外へ向かう向きになる
            float angle = i * 45f;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
            areaObject.transform.localPosition = rotation * (Vector3.forward * 12f);
            areaObject.transform.localRotation = rotation;
            areaObject.transform.localScale = Vector3.one;

            // 円周に沿って 6.5m・外へ 4.5m（12 ± 2.25m）。四隅まで含めて床の上に収まる大きさ。
            // 厚さ 0.2m で中心を 0.1m 下げ、範囲の上面（＝落下点の高さ）を Ground の基準の高さ（ローカルの 0）に合わせる
            BoxCollider box = areaObject.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.1f, 0f);
            box.size = new Vector3(6.5f, 0.2f, 4.5f);
            box.isTrigger = true;

            MeteorArea area = areaObject.GetComponent<MeteorArea>();
            areas.Add(area);
            EditorUtility.SetDirty(areaObject);
        }

        // 数値は `隕石攻撃.md`「STAGE_03への設置」で決めたもの。private の項目なので名前を指定して書き込む。
        // ※ MeteorManager の項目名を変えると、ここが null で落ちる（Stage03MeteorSetupTests も同じ名前を使っている）
        SerializedObject managerSerialized = new SerializedObject(manager);
        managerSerialized.FindProperty("intervalSeconds").floatValue = 4f;
        managerSerialized.FindProperty("meteorsPerRound").intValue = 3;
        managerSerialized.FindProperty("warningSeconds").floatValue = 4f;
        managerSerialized.FindProperty("spawnAtAreaCenter").boolValue = true;
        managerSerialized.FindProperty("impactRadius").floatValue = 5f;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();
        SetAreas(manager, areas);

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(areaRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, Stage03ScenePath))
        {
            throw new System.InvalidOperationException("[METEOR] STAGE_03を保存できませんでした。");
        }

        Debug.Log("[METEOR] STAGE_03へ半径5m・1ラウンド3個の隕石Areaを8か所設置しました。");
    }

    /// <summary>
    /// 既存の調整済みPrefabは上書きせず、不足分だけ作る。
    /// 攻撃本体は隕石・予告円・エフェクトを、管理役は攻撃本体を参照するので、参照される側から順に作る。
    /// 攻撃本体は通信で全員のPCに出すため、通信で出す物の一覧（Network Prefabs）にも登録する。
    /// </summary>
    public static void CreateMeteorPrefabs()
    {
        FishingSceneBuilder.EnsureFolder(PrefabFolder);

        GameObject meteor = CreateMeteorVisual();
        GameObject warning = CreateWarningVisual();
        GameObject impactVfx = CreateImpactVfx();
        GameObject strike = CreateStrikePrefab(meteor, warning, impactVfx);
        CreateAreaPrefab();
        CreateManagerPrefab(strike);

        SpaceJunkPrefabBuilder.RegisterNetworkPrefab(strike);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 落ちてくる隕石の見た目（2.5m の黒っぽい箱）を作る。当たり判定は攻撃本体が別に調べるので、見た目からは外す。
    /// </summary>
    private static GameObject CreateMeteorVisual()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MeteorPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            MeteorMaterialPath,
            new Color(0.16f, 0.12f, 0.1f, 1f),
            "Universal Render Pipeline/Lit");

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "Meteor";
        root.transform.localScale = Vector3.one * 2.5f;
        root.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(root.GetComponent<BoxCollider>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MeteorPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 落ちる場所を知らせる、地面の赤い円（ごく薄い円柱）を作る。
    /// 光の当たり方で色が変わらないよう Unlit にし、乗っても邪魔にならないよう当たり判定は外す。
    /// </summary>
    private static GameObject CreateWarningVisual()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(WarningPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            WarningMaterialPath,
            new Color(1f, 0.08f, 0.02f, 1f),
            "Universal Render Pipeline/Unlit");

        // 円柱はもともと直径 1m・高さ 2m なので、高さだけ 0.02 倍（4cm）にして円盤にする
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        root.name = "MeteorWarning";
        root.transform.localScale = new Vector3(1f, 0.02f, 1f);
        root.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WarningPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 着弾したときの火花（パーティクル）を作る。出たときに1回だけ 60 粒をまとめて飛ばし、繰り返さない。
    /// </summary>
    private static GameObject CreateImpactVfx()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactVfxPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        Material material = GetOrCreateMaterial(
            ImpactMaterialPath,
            new Color(1f, 0.32f, 0.04f, 1f),
            "Universal Render Pipeline/Particles/Unlit");

        GameObject root = new GameObject("MeteorImpactVfx");
        ParticleSystem particles = root.AddComponent<ParticleSystem>();

        // 寿命 0.45～0.9 秒、速さ 6～14 m/秒、大きさ 0.5～1.8m。色は黄色～赤のあいだでばらつかせる
        ParticleSystem.MainModule main = particles.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.75f, 0.12f, 1f),
            new Color(1f, 0.08f, 0.02f, 1f));
        main.maxParticles = 100;

        // 少しずつ出し続けるのではなく、出た瞬間（0 秒）に 60 粒をまとめて出す
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

        // 半径 1.5m の球の中から、あらゆる向きへ飛び散らせる
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.5f;

        ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ImpactVfxPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 隕石攻撃1回ぶんの本体を作る。ホストが通信で全員のPCに出すので NetworkObject を付け、
    /// 見た目の3つ（隕石・予告円・エフェクト）を参照させる。
    /// ※ meteorPrefab などは MeteorStrikeEvent の private の項目名。変えると参照が入らなくなる（警告が出る）
    /// </summary>
    private static GameObject CreateStrikePrefab(
        GameObject meteor,
        GameObject warning,
        GameObject impactVfx)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StrikePrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorStrikeEvent");
        root.AddComponent<NetworkObject>();
        MeteorStrikeEvent strike = root.AddComponent<MeteorStrikeEvent>();
        FishingSceneBuilder.SetRef(strike, "meteorPrefab", meteor);
        FishingSceneBuilder.SetRef(strike, "warningPrefab", warning);
        FishingSceneBuilder.SetRef(strike, "impactVfxPrefab", impactVfx);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StrikePrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 落下範囲のひな形（10m 四方・厚さ 0.2m のトリガー）を作る。
    /// 置いたあとで、床に合わせて BoxCollider の大きさを変えて使う。
    /// </summary>
    private static GameObject CreateAreaPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(AreaPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorArea");
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(10f, 0.2f, 10f);
        box.isTrigger = true;
        root.AddComponent<MeteorArea>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, AreaPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 管理役（いつ・どこへ落とすかを決める役）を作り、出す攻撃本体のプレハブを参照させる。
    /// 落下範囲の一覧はシーンごとに違うので、ここでは入れない。
    /// </summary>
    private static GameObject CreateManagerPrefab(GameObject strikePrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject("MeteorManager");
        MeteorManager manager = root.AddComponent<MeteorManager>();
        FishingSceneBuilder.SetRef(manager, "strikePrefab", strikePrefab.GetComponent<MeteorStrikeEvent>());

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// マテリアルを読み込む。無ければ指定の色で作ってファイルに保存する（すでにあれば色も変えない）。
    /// </summary>
    private static Material GetOrCreateMaterial(string path, Color color, string shaderName)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        // 指定のシェーダーが無い環境でも作れるよう、URP の Lit → 標準の Standard の順に代わりを探す
        Shader shader = Shader.Find(shaderName)
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>床のすぐ下の子（孫は見ない）に、落下範囲がすでに付いているかを探す。無ければ null。</summary>
    private static MeteorArea FindDirectAreaChild(Transform plate)
    {
        for (int i = 0; i < plate.childCount; i++)
        {
            MeteorArea area = plate.GetChild(i).GetComponent<MeteorArea>();
            if (area != null)
            {
                return area;
            }
        }
        return null;
    }

    /// <summary>
    /// 落下範囲の大きさを床に合わせる。床に BoxCollider があればその形を、無ければメッシュの外形をそのまま写す。
    /// どちらも無ければ、ひな形の大きさ（10m 四方）のまま残る。
    /// </summary>
    private static void CopyPlateBounds(GameObject plate, BoxCollider target)
    {
        BoxCollider sourceBox = plate.GetComponent<BoxCollider>();
        if (sourceBox != null)
        {
            target.center = sourceBox.center;
            target.size = sourceBox.size;
            target.isTrigger = true;
            return;
        }

        MeshFilter mesh = plate.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null)
        {
            target.center = mesh.sharedMesh.bounds.center;
            target.size = mesh.sharedMesh.bounds.size;
            target.isTrigger = true;
        }
    }

    /// <summary>
    /// 管理役の落下範囲の一覧を読む（空き・重複は除く）。
    /// ※ activeAreas は MeteorManager の private の項目名。変えるとここと <see cref="SetAreas"/> が落ちる
    /// </summary>
    private static List<MeteorArea> ReadAreas(MeteorManager manager)
    {
        List<MeteorArea> result = new List<MeteorArea>();
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("activeAreas");
        for (int i = 0; i < property.arraySize; i++)
        {
            MeteorArea area = property.GetArrayElementAtIndex(i).objectReferenceValue as MeteorArea;
            if (area != null && !result.Contains(area))
            {
                result.Add(area);
            }
        }
        return result;
    }

    /// <summary>管理役の落下範囲の一覧を、渡したものに丸ごと入れ替える。</summary>
    private static void SetAreas(MeteorManager manager, List<MeteorArea> areas)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("activeAreas");
        property.arraySize = areas.Count;
        for (int i = 0; i < areas.Count; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = areas[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 指定したシーンの中から、名前が一致する物を探す（ルートから順に、子・孫までたどる）。最初に見つかった物を返す。
    /// </summary>
    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindInChildren(root.transform, objectName);
            if (found != null)
            {
                return found.gameObject;
            }
        }
        return null;
    }

    /// <summary>自分 → 子 → 孫…の順に、名前が一致する物を探す。無ければ null。</summary>
    private static Transform FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindInChildren(parent.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// 指定したシーンの中から、部品を1つ探す（非表示の物も含める）。
    /// 他に開いているシーンの物を拾わないよう、FindFirstObjectByType ではなくシーンのルートからたどる。
    /// </summary>
    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }
        return null;
    }
}
