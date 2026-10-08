#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>既存のfbxTestシーンへ爆発FBXの再生確認機能を接続するEditorツール。</summary>
public static class ExplosionEffectTestSceneBuilder
{
    private const string ModelPath = "Assets/Art/Models/Explosion.fbx";
    private const string ControllerPath = "Assets/Art/Models/ExplosionPreview.controller";
    private const string ScenePath = "Assets/Scenes/Test/fbxTest.unity";

    [MenuItem("Tools/StarSweepers/Tests/Configure Explosion FBX Test Scene")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (model == null || importer == null)
        {
            throw new System.InvalidOperationException($"FBXを読み込めません: {ModelPath}");
        }

        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips.Length > 0)
        {
            clips[0].loopTime = false;
            clips[0].wrapMode = WrapMode.ClampForever;
            importer.clipAnimations = clips;
        }

        importer.SaveAndReimport();
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__", System.StringComparison.Ordinal));

        RuntimeAnimatorController controller = null;
        if (clip != null)
        {
            AnimatorController animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (animatorController == null)
            {
                animatorController = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            AnimatorStateMachine stateMachine = animatorController.layers[0].stateMachine;
            AnimatorState state = stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(existingState => existingState.name == "Explosion");
            if (state == null)
            {
                state = stateMachine.AddState("Explosion");
            }

            state.motion = clip;
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(animatorController);
            controller = animatorController;
            Debug.Log($"Explosion FBX animation imported: {clip.name}, {clip.length:0.000}s");
        }
        else
        {
            Debug.LogWarning("FBXにアニメーションクリップがありません。FBX自体の表示確認はできます。");
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject target = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .FirstOrDefault(candidate =>
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
                return source != null && AssetDatabase.GetAssetPath(source) == ModelPath;
            });
        if (target == null)
        {
            throw new System.InvalidOperationException("fbxTestシーン内にExplosion.fbxの配置が見つかりません。");
        }

        Animator animator = target.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = target.AddComponent<Animator>();
        }

        GameObject previewObject = GameObject.Find("ExplosionPreview");
        if (previewObject == null)
        {
            previewObject = new GameObject("ExplosionPreview");
        }

        ExplosionEffectPreview preview = previewObject.GetComponent<ExplosionEffectPreview>();
        if (preview == null)
        {
            preview = previewObject.AddComponent<ExplosionEffectPreview>();
        }

        SerializedObject serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("targetEffect").objectReferenceValue = target;
        serializedPreview.FindProperty("effectPrefab").objectReferenceValue = null;
        serializedPreview.FindProperty("animatorController").objectReferenceValue = controller;
        serializedPreview.FindProperty("playOnStart").boolValue = true;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = previewObject;
        Debug.Log($"Explosion FBX preview configured in {ScenePath}. Target: {target.name}");
    }
}
#endif
