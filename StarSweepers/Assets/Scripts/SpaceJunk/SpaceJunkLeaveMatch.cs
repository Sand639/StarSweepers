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
    /// <summary>ロビーのシーンの名前（試合の係が見つからないときに使う）。</summary>
    private const string DefaultLobbySceneName = "SpaceJunkLobby";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        PauseMenu.CanReturnToLobby = () => SpaceJunkRound.Current != null;
        PauseMenu.ReturnToLobby = ReturnToLobby;
    }

    /// <summary>試合から抜けて、ロビーへ戻る。</summary>
    public static void ReturnToLobby()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        string lobby = session != null ? session.LobbySceneName : DefaultLobbySceneName;

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

            Disconnect(manager);
        }

        Debug.Log($"[JUNK] 試合から抜けて、ロビー（{lobby}）へ戻ります。");
        SceneManager.LoadScene(lobby, LoadSceneMode.Single);
    }

    /// <summary>
    /// 通信を切る。インターネット（合言葉）でつないでいたら、部屋からも抜ける。
    /// 部屋から抜けるのには少し時間がかかるので、**通信だけはすぐ止めて**からシーンを移る。
    /// </summary>
    private static void Disconnect(NetworkManager manager)
    {
        InternetConnection internet = manager.GetComponent<InternetConnection>();
        if (internet != null && internet.State == InternetConnection.Phase.Connected)
        {
            internet.LeaveGame();
        }

        manager.Shutdown();
    }
}
