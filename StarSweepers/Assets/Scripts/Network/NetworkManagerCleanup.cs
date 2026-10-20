using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **重なってしまった `NetworkManager` を片付ける係。**
///
/// ## 何が起きていたか
///
/// ロビーのシーンには `NetworkManager` が置いてある。
/// そして `NetworkManager` は**シーンを切り替えても生き残る**（`DontDestroyOnLoad`）。
///
/// ここまでは狙いどおりだが、**試合が終わってロビーへ戻ると、
/// ロビーのシーンに置いてある `NetworkManager` がもう1つ生まれる。**
///
/// Netcode 側は、2つ目を見つけても**消してくれない。**
///
/// <code>
/// if (Singleton == null) SetSingleton();                 // 2つ目は Singleton にならない
/// if (!NetworkManagerCheckForParent()) DontDestroyOnLoad(gameObject);  // それでも生き残る
/// </code>
///
/// そのため、**ロビーへ戻るたびに `NetworkManager` が1つずつ積み上がる。**
/// これらには接続画面・通信の様子・ロビーの画面といった**確認用の表示が付いている**ので、
/// **戻るたびに同じ窓が増えていく**（2026/9/20・大槻さんの報告）。
/// 放っておくと画面が窓で埋まり、通信の部品も無駄に増え続ける。
///
/// ## どう直したか
///
/// **シーンが読み込まれるたびに、本物ではない `NetworkManager` を消す。**
///
/// **シーンに何も置かなくても動く**ようにしてある（`PauseMenu.AutoCreate` と同じ考え方）。
/// 釣り・宇宙ごみのどちらのロビーでも、置き忘れの心配なく効く。
/// </summary>
public static class NetworkManagerCleanup
{
    /// <summary>
    /// 再生を始めるときに、シーンの読み込みを見張り始める。
    ///
    /// **同じ関数を二度登録しないよう、先に外してから付ける**
    /// （再生のたびに静的な値が消えない設定でも、二重にならないようにするため）。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// シーンが読み込まれるたびに呼ばれる。
    /// 本物（`Singleton`。先に生き残っていたほう）以外の `NetworkManager` を、GameObject ごと消す。
    /// </summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        NetworkManager singleton = NetworkManager.Singleton;

        // まだ1つも無ければ、これから読み込まれたものが本物になる
        if (singleton == null)
        {
            return;
        }

        NetworkManager[] all = Object.FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (NetworkManager manager in all)
        {
            if (manager == null || manager == singleton)
            {
                continue;
            }

            Debug.Log(
                $"[NET] 重なっていた NetworkManager を消しました（{manager.gameObject.name}／シーン：{scene.name}）。\n" +
                "つながったまま戻ってきたときに、シーン側の NetworkManager が" +
                "そのまま残ってしまうため（接続画面やロビーの窓が増える原因）。");

            Object.Destroy(manager.gameObject);
        }
    }
}
