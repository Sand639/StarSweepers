using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerNameTestSetup
{
    public const string ScenePath = "Assets/Scenes/Test/PlayerNameTest.unity";
    private const string VerificationKey = "StarSweepers.VerifyPlayerNameScene";
    private static double verificationDeadline;

    /// <summary>バッチ実行用。実際の検証シーンを再生し、名前が付くまで確かめて終了する。</summary>
    public static void VerifyScene()
    {
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(VerificationKey, true);
        ResumeVerification();
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeVerification()
    {
        if (!SessionState.GetBool(VerificationKey, false)) return;
        verificationDeadline = EditorApplication.timeSinceStartup + 30d;
        EditorApplication.update -= CheckVerification;
        EditorApplication.update += CheckVerification;
    }

    private static void CheckVerification()
    {
        PlayerNameTestHud hud = Object.FindFirstObjectByType<PlayerNameTestHud>();
        NetworkManager manager = hud != null ? hud.GetComponent<NetworkManager>() : null;
        if (EditorApplication.isPlaying && manager != null && manager.IsHost && SpaceJunkSession.Current != null)
        {
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player == null || !player.IsSpawned || player.NetworkManager != manager) continue;
                if (player.NamePrefixIndex < 0 || player.NameConnectorIndex < 0 || player.NameSuffixIndex < 0
                    || SpaceJunkSession.Current.NameOf(player.PlayerKey) != player.DisplayName) continue;
                Debug.Log("[NAME] SCENE VERIFIED: " + player.DisplayName);
                FinishVerification(0);
                return;
            }
        }
        if (EditorApplication.timeSinceStartup >= verificationDeadline)
        {
            Debug.LogError("[NAME] 検証シーンで名前を確認できませんでした。");
            FinishVerification(1);
        }
    }

    private static void FinishVerification(int exitCode)
    {
        SessionState.SetBool(VerificationKey, false);
        EditorApplication.update -= CheckVerification;
        EditorApplication.Exit(exitCode);
    }

    [MenuItem("Tools/StarSweepers/プレイヤーの名前/検証シーンを作る")]
    public static void CreateScene()
    {
        // すでにある検証シーンを勝手に上書きせず、選択するだけにする。
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            return;
        }

        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlayer.prefab");
        GameObject session = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/SpaceJunk/Online/SpaceJunkSession.prefab");
        if (player == null || session == null) throw new System.InvalidOperationException("検証に使うプレハブがありません。");

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            GameObject root = new GameObject("PlayerNameTest");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                PlayerPrefab = player
            };
            root.AddComponent<NameDataManager>();
            PlayerNameTestHud hud = root.AddComponent<PlayerNameTestHud>();
            SerializedObject serialized = new SerializedObject(hud);
            serialized.FindProperty("sessionPrefab").objectReferenceValue = session;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject cameraRoot = new GameObject("MainCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.SetPositionAndRotation(new Vector3(0f, 20f, -10f), Quaternion.Euler(60f, 0f, 0f));

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, scene);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new System.IO.IOException("検証シーンを保存できませんでした。");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log("名前の検証シーンを作りました: " + ScenePath);
    }
}
