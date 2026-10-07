using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **タイトルとロビーの行き来をする係。**（2026/10/6。タイトル画面をプレハブにしたときに、<see cref="TitleScreen"/> から分けた）
///
/// ・タイトルでホストになった（サーバーを作った・1人で練習）→ **全員でロビーへ移る**（参加者はホストのシーンに自動で移る）
/// ・ロビーにいるのに、どこにもつながっていない（ロビーを直接再生した など）→ タイトルへ移る
/// ・TitleScene にタイトル画面が置かれていなければ、`Assets/Resources/TitleScreen.prefab` から出す（置き忘れの保険）
///
/// 画面（<see cref="TitleScreen"/>）は TitleScene の中にあり、シーンが変わると消える。
/// この係は**シーンをまたいで残る**必要があるので、別にしてある。シーンに置かなくても、再生したときに自分で1つ作る。
/// </summary>
public class TitleSceneFlow : MonoBehaviour
{
    /// <summary>ロビーを直接開いたときに、つながっていなければタイトルへ移るまで待つ秒数。</summary>
    private const float IdleLobbySeconds = 0.5f;

    private float notConnectedInLobbyTime;

    /// <summary>ホストがロビーへ移す指示を、このタイトルのシーンでもう出したか。</summary>
    private bool lobbyLoadRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<TitleSceneFlow>() != null)
        {
            return;
        }

        GameObject created = new GameObject("TitleSceneFlow (自動)");
        created.AddComponent<TitleSceneFlow>();
        DontDestroyOnLoad(created);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        // 最初のシーンは、作られた時点で読み込み終わっている
        EnsureTitleScreen(SceneManager.GetActiveScene());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureTitleScreen(scene);
    }

    /// <summary>TitleScene にタイトル画面が無ければ、プレハブから出す。</summary>
    private static void EnsureTitleScreen(Scene scene)
    {
        if (scene.name != TitleScreen.TitleSceneName || FindFirstObjectByType<TitleScreen>() != null)
        {
            return;
        }

        TitleScreen prefab = Resources.Load<TitleScreen>(TitleScreen.PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[UI] タイトル画面のプレハブ（Assets/Resources/TitleScreen.prefab）が見つかりません。" +
                           "`Tools > StarSweepers > タイトル画面のプレハブを作り直す` で作ってください。");
            return;
        }

        Debug.LogWarning("[UI] TitleScene にタイトル画面が置かれていないので、プレハブから出しました（シーンに置いておくと、編集中も見えます）。");
        TitleScreen created = Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(created.gameObject, scene);
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        NetworkManager manager = NetworkManager.Singleton;

        if (activeScene != TitleScreen.TitleSceneName)
        {
            lobbyLoadRequested = false;
        }

        if (activeScene == TitleScreen.TitleSceneName && manager != null && manager.IsServer && !lobbyLoadRequested &&
            manager.SceneManager != null)
        {
            lobbyLoadRequested = true;
            SceneEventProgressStatus status = manager.SceneManager.LoadScene(TitleScreen.LobbySceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[UI] ロビー（{TitleScreen.LobbySceneName}）へ移れませんでした（{status}）。" +
                               "ビルドのシーン一覧に入っているか確かめてください。");
                lobbyLoadRequested = false;
            }
        }

        bool idleInLobby = activeScene == TitleScreen.LobbySceneName && (manager == null || !manager.IsListening);
        notConnectedInLobbyTime = idleInLobby ? notConnectedInLobbyTime + Time.unscaledDeltaTime : 0f;

        if (notConnectedInLobbyTime > IdleLobbySeconds)
        {
            notConnectedInLobbyTime = 0f;
            Debug.Log("[UI] ロビーでどこにもつながっていないので、タイトルへ移ります。");
            SceneManager.LoadScene(TitleScreen.TitleSceneName, LoadSceneMode.Single);
        }
    }
}
