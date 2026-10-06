using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **ポーズ画面の「ロビーに戻る」で、宇宙ごみの試合から抜ける処理。**（2026/10/6・大槻さん）
///
/// ポーズ画面（<see cref="PauseMenu"/>）は釣りと共通の部品なので、宇宙ごみのことは知らない。
/// ここで、ポーズ画面の差し込み口（<see cref="PauseMenu.CanReturnToLobby"/> / <see cref="PauseMenu.ReturnToLobby"/>）に
/// 宇宙ごみの処理を入れておく。**宇宙ごみの試合中（ラウンドの進行役がいるとき）だけ**ボタンが出る。
///
/// | 押した人 | どうなるか |
/// | --- | --- |
/// | ホスト | **試合を打ち切って、全員がロビーへ戻る** |
/// | 参加者 | **自分だけ通信を切って部屋から抜け、ロビーの画面（つなぐ前の画面）に戻る**。ほかの人の試合は続く |
/// | 通信していない（エディタで直接マップを再生） | ロビーのシーンを開くだけ |
/// </summary>
public static class SpaceJunkLeaveMatch
{
    /// <summary>自分だけ抜けたときに戻るタイトルのシーン（2026/10/6）。</summary>
    private const string TitleSceneName = "TitleScene";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        PauseMenu.CanReturnToLobby = () => SpaceJunkRound.Current != null;
        PauseMenu.ReturnToLobby = ReturnToLobby;
    }

    /// <summary>試合から抜けて、ロビーへ戻る。</summary>
    public static async void ReturnToLobby()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            if (manager.IsServer)
            {
                // ホスト：全員でロビーへ戻る（試合が終わったときと同じ戻り方）
                if (session != null && session.ServerReturnToLobby())
                {
                    Debug.Log("[JUNK] ホストがポーズ画面から試合を打ち切りました。全員でロビーへ戻ります。");
                    return;
                }

                Debug.LogWarning("[JUNK] 全員でロビーへ戻れませんでした。通信を切って、このPCだけロビーへ戻ります。");
            }

            // 自分で抜けるので、「通信が切れた」の見張り役には反応させない
            SpaceJunkDisconnectWatcher.LeavingOnPurpose = true;
            await Disconnect(manager);
        }

        // 自分だけ抜けたので、タイトルへ戻る（2026/10/6 まではロビーへ戻っていた。サーバーを作る・探すはタイトルで行うため）
        Debug.Log($"[JUNK] 試合から抜けて、タイトル（{TitleSceneName}）へ戻ります。");
        SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
    }

    /// <summary>
    /// 通信を切る。インターネット（合言葉）でつないでいたら、**部屋から抜け終わるのを待ってから**通信を止める。
    ///
    /// 2026/10/6 までは、抜け始めた直後に通信を止めていた。そのせいで抜ける処理が失敗し、
    /// 中継サーバーに「まだ参加している」記録が残って、**もう一度同じ合言葉で入れなくなっていた**（SessionConflict）。
    /// </summary>
    private static async Task Disconnect(NetworkManager manager)
    {
        InternetConnection internet = manager.GetComponent<InternetConnection>();
        if (internet != null && internet.State == InternetConnection.Phase.Connected)
        {
            // 抜け終わったら、LeaveGameAsync が通信も止める
            await internet.LeaveGameAsync();
        }

        if (manager != null && manager.IsListening)
        {
            manager.Shutdown();
        }
    }
}
