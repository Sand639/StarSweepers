using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PullAblePlateOnlineSetup
{
    private const string PlatePrefabPath =
        "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlate.prefab";
    private const string Stage05ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_05.unity";

    [MenuItem("Tools/SpaceJunk/Apply PullAblePlate Online Setup")]
    public static void ApplyOnlineSetup()
    {
        if (!Application.isBatchMode && HasDirtyOpenScene())
        {
            Debug.LogError(
                "PullAblePlate Online Setup was cancelled because an open Scene has unsaved changes.");
            return;
        }

        SceneSetup[] previousSetup = Application.isBatchMode
            ? Array.Empty<SceneSetup>()
            : EditorSceneManager.GetSceneManagerSetup();

        try
        {
            ConfigurePrefab();
            ConfigureStage05();
            AssetDatabase.SaveAssets();
            Debug.Log("PullAblePlate online setup completed.");
        }
        finally
        {
            if (!Application.isBatchMode)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }
    }

    private static void ConfigurePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlatePrefabPath);
        try
        {
            NetworkObject networkObject = root.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = root.AddComponent<NetworkObject>();
            }

            NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
            if (networkTransform == null)
            {
                networkTransform = root.AddComponent<NetworkTransform>();
            }

            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            networkTransform.SyncPositionX = true;
            networkTransform.SyncPositionY = true;
            networkTransform.SyncPositionZ = true;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(networkTransform);
            PrefabUtility.SaveAsPrefabAsset(root, PlatePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(PlatePrefabPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"Configured online components: {PlatePrefabPath}");
    }

    private static void ConfigureStage05()
    {
        Scene scene = EditorSceneManager.OpenScene(Stage05ScenePath, OpenSceneMode.Single);
        PullAblePlate[] plates = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PullAblePlate>(true))
            .ToArray();
        if (plates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one PullAblePlate in STAGE_05, but found {plates.Length}.");
        }

        NetworkObject plateNetworkObject = plates[0].GetComponent<NetworkObject>();
        if (plateNetworkObject == null)
        {
            throw new InvalidOperationException("PullAblePlate has no NetworkObject after prefab setup.");
        }

        MethodInfo onValidate = typeof(NetworkObject).GetMethod(
            "OnValidate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (onValidate == null)
        {
            throw new MissingMethodException(typeof(NetworkObject).FullName, "OnValidate");
        }

        onValidate.Invoke(plateNetworkObject, null);
        EditorUtility.SetDirty(plateNetworkObject);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException($"Failed to save {Stage05ScenePath}.");
        }

        uint globalId = GetSerializedId(plateNetworkObject, "GlobalObjectIdHash");
        uint sourceId = GetSerializedId(
            plateNetworkObject,
            "InScenePlacedSourceGlobalObjectIdHash");
        if (globalId == 0 || sourceId == 0)
        {
            throw new InvalidOperationException(
                $"PullAblePlate scene IDs were not generated (global={globalId}, source={sourceId}).");
        }

        EnsureUniqueSceneIds(scene);
        Debug.Log(
            $"Saved {Stage05ScenePath}: PullAblePlate global={globalId}, source={sourceId}.");
    }

    private static void EnsureUniqueSceneIds(Scene scene)
    {
        HashSet<uint> ids = new HashSet<uint>();
        foreach (NetworkObject networkObject in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<NetworkObject>(true)))
        {
            uint id = GetSerializedId(networkObject, "GlobalObjectIdHash");
            if (id != 0 && !ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"Duplicate NetworkObject GlobalObjectIdHash {id} in {Stage05ScenePath}.");
            }
        }
    }

    private static uint GetSerializedId(NetworkObject networkObject, string propertyName)
    {
        SerializedProperty property = new SerializedObject(networkObject).FindProperty(propertyName);
        if (property == null)
        {
            throw new MissingFieldException(typeof(NetworkObject).FullName, propertyName);
        }

        return (uint)property.longValue;
    }

    private static bool HasDirtyOpenScene()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            if (SceneManager.GetSceneAt(sceneIndex).isDirty)
            {
                return true;
            }
        }

        return false;
    }
}
