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

/// <summary>
/// **引っ張れる移動床（<see cref="PullAblePlate"/>）を、オンラインで動かせる状態にするツール。**
///
/// Unityのメニュー「Tools > SpaceJunk > Apply PullAblePlate Online Setup」から実行できる。
/// 次の2つをまとめて行う。
/// ・プレハブ `PullAblePlate.prefab` に、通信用の NetworkObject と、位置を配る NetworkTransform（ホスト権限）を付ける
/// ・STAGE_05 に置いてある床の、通信用の番号（GlobalObjectIdHash など）を作り直して保存する
///
/// コマンドラインから Unity を動かす（バッチモード）ことも想定していて、
/// そのときは「保存していないシーンがあれば止める」「終わったら開いていたシーンに戻す」を飛ばす。
/// </summary>
public static class PullAblePlateOnlineSetup
{
    private const string PlatePrefabPath =
        "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlate.prefab";
    private const string Stage05ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_05.unity";

    /// <summary>
    /// プレハブの設定 → STAGE_05 の保存、の順に行う。
    /// STAGE_05 を Single で開き直すので、作業中のシーンに保存していない変更があれば、消さないよう最初に止める。
    /// </summary>
    [MenuItem("Tools/SpaceJunk/Apply PullAblePlate Online Setup")]
    public static void ApplyOnlineSetup()
    {
        if (!Application.isBatchMode && HasDirtyOpenScene())
        {
            Debug.LogError(
                "PullAblePlate Online Setup was cancelled because an open Scene has unsaved changes.");
            return;
        }

        // 終わったあと、実行前に開いていたシーンの並びへ戻すために控えておく
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

    /// <summary>
    /// プレハブに NetworkObject と NetworkTransform を付け（すでにあれば使い回す）、
    /// 位置だけを、ホストから全員へ配る設定にして保存する。
    /// </summary>
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

            // 床を動かすのはホストだけ（PullAblePlate の決まり）なので、位置の持ち主はホストにする。
            // 床は横へずれるだけで回ったり大きさが変わったりしないため、回転と拡大は配らない
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

    /// <summary>
    /// STAGE_05 を開き、置いてある床（1つだけのはず）の通信用の番号を作らせてから保存する。
    /// 床が1つでない・番号が 0 のまま・ほかの物と番号が重なる、のどれかなら例外を出して知らせる。
    /// </summary>
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

        // シーンに置いた NetworkObject の番号（GlobalObjectIdHash）は、NetworkObject の OnValidate で作られる。
        // プレハブに後から NetworkObject を足した直後や、バッチモードでは自動で呼ばれないことがあるため、
        // private の OnValidate をリフレクションで名前指定して呼ぶ。
        // ※ Netcode の版を上げてこの名前が無くなると、MissingMethodException で止まる
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

        // 保存したあとで、番号が本当に入ったかを確かめる。0 のままだと、参加者のPCで床が見つからない
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

    /// <summary>
    /// シーンの中の NetworkObject の番号が重なっていないかを確かめる。
    /// 重なっていると、通信のときにどちらの物か取り違えるため、見つけたら例外で止める（0 は未設定として見ない）。
    /// </summary>
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

    /// <summary>
    /// NetworkObject の番号を、インスペクターと同じ読み方で名前を指定して読む（外からは見えない値のため）。
    /// ※ Netcode の版を上げて項目名が変わると、MissingFieldException で止まる
    /// </summary>
    private static uint GetSerializedId(NetworkObject networkObject, string propertyName)
    {
        SerializedProperty property = new SerializedObject(networkObject).FindProperty(propertyName);
        if (property == null)
        {
            throw new MissingFieldException(typeof(NetworkObject).FullName, propertyName);
        }

        return (uint)property.longValue;
    }

    /// <summary>開いているシーンのどれかに、保存していない変更があるか。</summary>
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
