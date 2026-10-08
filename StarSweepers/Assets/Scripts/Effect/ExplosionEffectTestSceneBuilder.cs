#if UNITY_EDITOR
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>fbxTest を開いたときに爆発FBXの再生設定を自動で用意する。</summary>
[InitializeOnLoad]
public static class ExplosionEffectTestSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Test/fbxTest.unity";
    private const string FallbackModelPath = "Assets/Art/fbx/Explosion.fbx";

    static ExplosionEffectTestSceneBuilder()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += ConfigureOpenTestScene;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath)
        {
            EditorApplication.delayCall += ConfigureOpenTestScene;
        }
    }

    [MenuItem("Tools/StarSweepers/Tests/Configure Explosion FBX Test Scene")]
    private static void ConfigureFromMenu()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ConfigureOpenTestScene();
    }

    private static void ConfigureOpenTestScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != ScenePath)
        {
            return;
        }

        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        ExplosionEffectPreview existing = roots
            .SelectMany(root => root.GetComponentsInChildren<ExplosionEffectPreview>(true))
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

        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.SaveAndReimport();
            return;
        }

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

        Animator animator = target.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(target);
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

        GameObject previewObject = existing != null ? existing.gameObject :
            new GameObject("ExplosionPreview");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(previewObject, "Create Explosion Preview");
            SceneManager.MoveGameObjectToScene(previewObject, SceneManager.GetActiveScene());
        }

        ExplosionEffectPreview preview = existing != null ? existing :
            Undo.AddComponent<ExplosionEffectPreview>(previewObject);
        preview.Configure(target, controller);
        FrameEffectWithCamera(target, roots);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("fbxTestに爆発エフェクトの自動再生と調整用プレビューを設定しました。");
    }

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

        float distance = Mathf.Max(2f, bounds.extents.magnitude * 1.8f /
            Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
        camera.transform.position = bounds.center + new Vector3(0f, 0f, -distance);
        camera.transform.LookAt(bounds.center);
        EditorUtility.SetDirty(camera);
    }
}
#endif
