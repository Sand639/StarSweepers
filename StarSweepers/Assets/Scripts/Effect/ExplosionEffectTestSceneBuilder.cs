#if UNITY_EDITOR
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// fbxTest を開いたときに爆発FBXの再生設定を自動で用意する。
///
/// **爆発の FBX（`Explosion.fbx`）を、再生ボタンを押すだけで確認できるようにする**エディタ用の仕組み。
/// `[InitializeOnLoad]` なので、エディタの起動時とスクリプトの作り直しのたびに下の静的コンストラクタが動き、
/// 検証シーン `fbxTest` が開かれるのを待つ。開かれたら、FBX の読み込み設定・再生用のコントローラー・
/// プレビュー部品（<see cref="EffectPreviewController"/>）・カメラの位置を自動で整える。
/// メニュー `Tools > StarSweepers > Tests > Configure Explosion FBX Test Scene` から手動でも実行できる。
///
/// ※ Editor 用のコードだが Editor フォルダには無いので、`#if UNITY_EDITOR` でゲームのビルドから外している。
/// </summary>
[InitializeOnLoad]
public static class ExplosionEffectTestSceneBuilder
{
    /// <summary>自動設定の対象にする検証シーン。</summary>
    private const string ScenePath = "Assets/Scenes/Test/fbxTest.unity";

    /// <summary>シーンに FBX が置かれていないときに探しに行く、決まった置き場所。</summary>
    private const string FallbackModelPath = "Assets/Art/fbx/Explosion.fbx";

    /// <summary>プレビュー部品の、改名前のファイル名（残っていたら Unity の機能で改名する）。</summary>
    private const string OldPreviewScriptPath = "Assets/Scripts/Effect/ExplosionEffectPreview.cs";

    /// <summary>プレビュー部品の、いまのファイル名。</summary>
    private const string PreviewScriptPath = "Assets/Scripts/Effect/EffectPreviewController.cs";

    static ExplosionEffectTestSceneBuilder()
    {
        // delayCall で1回ぶん遅らせるのは、起動直後はアセットやシーンがまだ使えないことがあるため
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += RenamePreviewScriptWithUnity;
        EditorApplication.delayCall += ConfigureOpenTestScene;
    }

    /// <summary>
    /// 古い名前のスクリプトが残っていたら、Unity の機能（AssetDatabase）で改名する。
    /// OS のファイル操作で改名すると .meta との対応が壊れてシーンの参照が外れるので、こうしている。
    /// 新しい名前の方が既にあれば何もしない。
    /// </summary>
    private static void RenamePreviewScriptWithUnity()
    {
        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(PreviewScriptPath)) ||
            string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(OldPreviewScriptPath)))
        {
            return;
        }

        string error = AssetDatabase.MoveAsset(OldPreviewScriptPath, PreviewScriptPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"プレビュースクリプト名を変更できませんでした: {error}");
        }
    }

    /// <summary>対象のシーンが開かれたときだけ、自動設定を予約する。</summary>
    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath)
        {
            EditorApplication.delayCall += ConfigureOpenTestScene;
        }
    }

    /// <summary>メニューから手動で実行する。対象のシーンが開いていなければ先に開く。</summary>
    [MenuItem("Tools/StarSweepers/Tests/Configure Explosion FBX Test Scene")]
    private static void ConfigureFromMenu()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ConfigureOpenTestScene();
    }

    /// <summary>
    /// 開いている fbxTest を整える。流れ：
    /// 1. すでにプレビュー部品が設定済みなら何もしない
    /// 2. FBX を探し、読み込み設定を「アニメーション付き（Generic）」にする（変えたときは読み込み直しが走るので、いったん抜ける）
    /// 3. FBX のクリップを再生する Animator Controller（`ExplosionPreview.controller`）を FBX と同じフォルダに作る
    /// 4. シーンに置かれた FBX（無ければ置く）に Animator を付け、プレビュー部品に登録する
    /// 5. カメラを爆発が収まる位置に動かし、シーンを保存する
    /// </summary>
    private static void ConfigureOpenTestScene()
    {
        // 再生中（または再生に入る直前）は触らない。対象以外のシーンも触らない
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != ScenePath)
        {
            return;
        }

        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        EffectPreviewController existing = roots
            .SelectMany(root => root.GetComponentsInChildren<EffectPreviewController>(true))
            .FirstOrDefault();
        if (existing != null && existing.TargetEffect != null && existing.AnimatorController != null)
        {
            return;
        }

        string modelPath = FindModelPath(roots);
        if (string.IsNullOrEmpty(modelPath))
        {
            Debug.LogError("fbxTest内のExplosion FBXを見つけられませんでした。");
            return;
        }
        string controllerPath = Path.Combine(Path.GetDirectoryName(modelPath) ?? "Assets/Art/fbx",
            "ExplosionPreview.controller").Replace('\\', '/');
        ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"爆発FBXが見つかりません: {modelPath}");
            return;
        }

        // 読み込み設定を変えたときは読み込み直しが走るので、ここでは続けない
        // （次に fbxTest を開いたとき、またはメニューから実行したときに続きが行われる）
        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.SaveAndReimport();
            return;
        }

        // FBX の中には名前が「__」で始まる Unity 内部用のクリップ（プレビュー用）も入るので、それ以外の最初のクリップを使う
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__", System.StringComparison.Ordinal));
        if (clip == null)
        {
            Debug.LogError("FBXに再生可能なアニメーションクリップがありません。");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState state = stateMachine.states.Select(child => child.state)
            .FirstOrDefault(candidate => candidate.name == "Explosion");
        if (state == null)
        {
            state = stateMachine.AddState("Explosion");
        }
        state.motion = clip;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);

        // シーンに置かれている、この FBX から作られたオブジェクトを探す。無ければ新しく置く
        GameObject target = roots
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject =>
            {
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                return source != null && AssetDatabase.GetAssetPath(source) == modelPath;
            });
        if (target == null)
        {
            Object model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError("爆発FBXをPrefabとして読み込めませんでした。");
                return;
            }

            target = (GameObject)PrefabUtility.InstantiatePrefab(model, SceneManager.GetActiveScene());
            target.name = "ExplosionEffect";
        }

        // Undo 付きで付ける（Ctrl+Z で戻せる）。FBX のインスタンスへの変更は記録しないと保存されないので、最後に記録する
        Animator animator = target.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(target);
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

        // プレビュー部品を置くオブジェクト。既にある部品 → 名前が ExplosionPreview の物 → 新しく作る、の順で使う
        GameObject previewObject = existing != null ? existing.gameObject : roots
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject => gameObject.name == "ExplosionPreview");
        if (previewObject == null)
        {
            previewObject = new GameObject("ExplosionPreview");
            Undo.RegisterCreatedObjectUndo(previewObject, "Create Explosion Preview");
            SceneManager.MoveGameObjectToScene(previewObject, SceneManager.GetActiveScene());
        }

        EffectPreviewController preview = existing != null ? existing :
            Undo.AddComponent<EffectPreviewController>(previewObject);
        preview.Configure(target, controller);
        FrameEffectWithCamera(target, roots);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("fbxTestに爆発エフェクトの自動再生と調整用プレビューを設定しました。");
    }

    /// <summary>
    /// 爆発の FBX の場所を探す。順：シーンに置かれた `Explosion.fbx` → 決まった置き場所 → プロジェクト全体から名前で検索。
    /// 見つからなければ null。
    /// </summary>
    private static string FindModelPath(GameObject[] roots)
    {
        string placedModelPath = roots
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject))
            .Where(source => source != null)
            .Select(AssetDatabase.GetAssetPath)
            .FirstOrDefault(path => path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
                                    Path.GetFileNameWithoutExtension(path).Equals("Explosion", System.StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(placedModelPath))
        {
            return placedModelPath;
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(FallbackModelPath) != null
            ? FallbackModelPath
            : AssetDatabase.FindAssets("Explosion t:Model")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// メインカメラを、エフェクト全体が画面に収まる位置（真正面・-Z 側）へ動かす。
    /// エフェクトの描画範囲をまとめた箱を作り、その大きさから必要な距離を決める。
    /// </summary>
    private static void FrameEffectWithCamera(GameObject target, GameObject[] roots)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("爆発FBX内にRendererがありません。表示できるメッシュがあるか確認してください。");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
        {
            bounds.Encapsulate(renderer.bounds);
        }

        Camera camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault(candidate => candidate.CompareTag("MainCamera"));
        if (camera == null)
        {
            return;
        }

        // 箱の中心から角までの長さ（extents.magnitude）が視野に収まる距離 ＝ 長さ ÷ tan(視野角の半分)。
        // 1.8 倍で余白を取り、小さすぎるエフェクトでも最低 2m は離す
        float distance = Mathf.Max(2f, bounds.extents.magnitude * 1.8f /
            Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
        camera.transform.position = bounds.center + new Vector3(0f, 0f, -distance);
        camera.transform.LookAt(bounds.center);
        EditorUtility.SetDirty(camera);
    }
}

/// <summary>
/// プログラマー以外でもエフェクト確認用シーンを作れるウィンドウ。
///
/// `Tools > StarSweepers > Effect Preview > Create Test Scene` で開く。
/// プレハブか FBX を選んでボタンを押すと、`Assets/Scenes/Test/EffectTest_名前.unity` を新しく作って開く
/// （カメラ・ライト・<see cref="EffectPreviewController"/> 入り）。上の自動設定が `fbxTest` 専用なのに対し、こちらはどのエフェクトにも使える。
/// </summary>
public sealed class EffectPreviewSetupWindow : EditorWindow
{
    // ウィンドウで入力する値。作るシーンのプレビュー部品にそのまま渡す

    private GameObject effectAsset;
    private RuntimeAnimatorController animatorController;
    private float startDelay;
    private float playbackSpeed = 1f;
    private float visibleDuration;

    [MenuItem("Tools/StarSweepers/Effect Preview/Create Test Scene")]
    private static void Open()
    {
        GetWindow<EffectPreviewSetupWindow>("Effect Preview");
    }

    /// <summary>ウィンドウの中身。エフェクトが選ばれるまで「作成」ボタンは押せない。</summary>
    private void OnGUI()
    {
        EditorGUILayout.LabelField("エフェクト確認シーンを作成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("PrefabまたはFBXを選んでボタンを押すと、再生確認用のシーンを自動で作ります。",
            MessageType.Info);

        effectAsset = (GameObject)EditorGUILayout.ObjectField("エフェクト", effectAsset,
            typeof(GameObject), false);
        animatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
            "Animator Controller（任意）", animatorController, typeof(RuntimeAnimatorController), false);

        EditorGUILayout.Space();
        startDelay = Mathf.Max(0f, EditorGUILayout.FloatField("再生までの待ち時間", startDelay));
        playbackSpeed = Mathf.Max(0.01f, EditorGUILayout.FloatField("再生速度", playbackSpeed));
        visibleDuration = Mathf.Max(0f,
            EditorGUILayout.FloatField("表示時間（0なら自動）", visibleDuration));

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(effectAsset == null))
        {
            if (GUILayout.Button("テストシーンを作成して開く", GUILayout.Height(34f)))
            {
                CreateTestScene();
            }
        }
        EditorGUILayout.HelpBox("Particle SystemだけのエフェクトはAnimator Controller不要です。",
            MessageType.None);
    }

    /// <summary>
    /// 選んだエフェクトの確認用シーンを作って開く。流れ：
    /// 1. 選んだ物が Prefab か FBX かを確かめる
    /// 2. FBX なら読み込み設定を「アニメーション付き」にする（変えたときは読み込み直しが要るので、もう一度押してもらう）
    /// 3. コントローラーが未指定なら、FBX の最初のクリップを再生するものを作る
    /// 4. 新しいシーンにエフェクト・プレビュー部品・カメラ・ライトを置いて保存する
    /// </summary>
    private void CreateTestScene()
    {
        string assetPath = AssetDatabase.GetAssetPath(effectAsset);
        string extension = Path.GetExtension(assetPath);
        if (string.IsNullOrEmpty(assetPath) ||
            (!extension.Equals(".prefab", System.StringComparison.OrdinalIgnoreCase) &&
             !extension.Equals(".fbx", System.StringComparison.OrdinalIgnoreCase)))
        {
            EditorUtility.DisplayDialog("エフェクトを選んでください",
                "ProjectウィンドウからPrefabまたはFBXを選択してください。", "OK");
            return;
        }

        if (extension.Equals(".fbx", System.StringComparison.OrdinalIgnoreCase))
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer != null &&
                (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation))
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = true;
                importer.SaveAndReimport();
                effectAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                EditorUtility.DisplayDialog("FBXを読み込み直しました",
                    "読み込みが終わったら、もう一度「テストシーンを作成して開く」を押してください。", "OK");
                return;
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("読み込めません", "選択したPrefabまたはFBXを読み込めませんでした。", "OK");
            return;
        }

        RuntimeAnimatorController controller = animatorController;
        if (controller == null)
        {
            controller = CreateControllerForFirstClip(assetPath);
        }

        string scenePath = CreateUniqueScenePath(prefab.name);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject effect = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        if (effect == null)
        {
            EditorUtility.DisplayDialog("作成できません", "エフェクトPrefabをシーンに配置できませんでした。", "OK");
            return;
        }
        effect.name = "Effect";

        Animator animator = effect.GetComponentInChildren<Animator>(true);
        if (controller != null && animator == null)
        {
            animator = Undo.AddComponent<Animator>(effect);
        }
        if (animator != null && controller != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }

        GameObject previewObject = new GameObject("EffectPreviewController");
        SceneManager.MoveGameObjectToScene(previewObject, scene);
        EffectPreviewController preview = previewObject.AddComponent<EffectPreviewController>();
        preview.Configure(effect, controller);

        // private の [SerializeField] に、インスペクターで入れるのと同じ仕組みで値を入れる
        // （フィールド名の文字列で引くので、EffectPreviewController 側の名前を変えたらここも変えること）
        SerializedObject previewSettings = new SerializedObject(preview);
        previewSettings.FindProperty("startDelay").floatValue = startDelay;
        previewSettings.FindProperty("playbackSpeed").floatValue = playbackSpeed;
        previewSettings.FindProperty("visibleDuration").floatValue = visibleDuration;
        previewSettings.ApplyModifiedPropertiesWithoutUndo();

        SetInitialCamera(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
        Selection.activeGameObject = previewObject;
        EditorUtility.DisplayDialog("テストシーンを作成しました",
            $"{scenePath}\nPlayボタンでエフェクトを確認できます。", "OK");
    }

    /// <summary>
    /// アセットの中の最初のクリップ（「__」で始まる内部用は除く）を再生するだけの Animator Controller を、
    /// アセットと同じフォルダに `名前Preview.controller` として作る。クリップが無ければ null。
    /// </summary>
    private static RuntimeAnimatorController CreateControllerForFirstClip(string assetPath)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__", System.StringComparison.Ordinal));
        if (clip == null)
        {
            return null;
        }

        string directory = Path.GetDirectoryName(assetPath) ?? "Assets";
        string controllerName = SanitizeAssetName(Path.GetFileNameWithoutExtension(assetPath));
        string controllerPath = Path.Combine(directory, $"{controllerName}Preview.controller").Replace('\\', '/');
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState state = stateMachine.states.Select(child => child.state)
            .FirstOrDefault(candidate => candidate.name == "Effect");
        if (state == null)
        {
            state = stateMachine.AddState("Effect");
        }
        state.motion = clip;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>`EffectTest_名前.unity` が既にあれば `_02`、`_03` … を付けて、ぶつからない名前にする。</summary>
    private static string CreateUniqueScenePath(string assetName)
    {
        string safeName = SanitizeAssetName(assetName);
        string basePath = $"Assets/Scenes/Test/EffectTest_{safeName}";
        string candidate = basePath + ".unity";
        int suffix = 2;
        while (AssetDatabase.LoadAssetAtPath<SceneAsset>(candidate) != null)
        {
            candidate = $"{basePath}_{suffix++:00}.unity";
        }
        return candidate;
    }

    /// <summary>半角の英数字だけを残す（`Assets/` の中のファイル名は半角英数字にする決まりのため）。何も残らなければ "NewEffect"。</summary>
    private static string SanitizeAssetName(string assetName)
    {
        string safeName = new string(assetName.Where(character =>
            (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z') ||
            (character >= '0' && character <= '9')).ToArray());
        return string.IsNullOrEmpty(safeName) ? "NewEffect" : safeName;
    }

    /// <summary>
    /// カメラとライトを置く。カメラの位置・角度（少し上から原点を見下ろす）は手で調整した値をそのまま使っている。
    /// </summary>
    private static void SetInitialCamera(Scene scene)
    {
        GameObject cameraObject = new GameObject("PreviewCamera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 45f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        camera.transform.position = new Vector3(0f, 2.32f, -5.35f);
        camera.transform.rotation = Quaternion.Euler(17.418f, 0f, 0f);
        camera.transform.localScale = Vector3.one;

        GameObject lightObject = new GameObject("PreviewLight", typeof(Light));
        SceneManager.MoveGameObjectToScene(lightObject, scene);
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }
}
#endif
