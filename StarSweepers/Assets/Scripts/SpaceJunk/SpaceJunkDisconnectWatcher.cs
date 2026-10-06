using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **参加者が試合中にホストとの通信を失ったら、ロビーへ戻す見張り役。**（2026/10/6・大槻さん「ネットワークが不安定」）
///
/// ホストのネットワークが切れた・ホストがゲームを終えた・自分の回線が切れた、などで通信が止まると、
/// 参加者は**動かない試合のマップに取り残されていた**（物も時間も止まり、何をしてよいか分からない）。
///
/// ・試合中（ラウンドの進行役がいるマップ）に、**参加者として**つながっていたのに通信が止まったら
///   → 部屋の記録を片付けて（あとで入り直せるように）、**ロビーのシーンへ戻す**
///   → 画面の上に「ホストとの接続が切れたので、ロビーに戻りました」を数秒出す
/// ・自分でポーズ画面の「ロビーに戻る」を選んだときは何もしない（<see cref="SpaceJunkLeaveMatch"/> が戻す）
/// ・ホスト自身は対象外（ホストが止まったときは、ホストのPCで止めた理由が分かっている）
///
/// **シーンに置かなくても動く。** 再生したときに自分で1つ作り、シーンが変わっても残る
/// （<see cref="PauseMenu"/> の AutoCreate と同じ考え方）。
/// </summary>
public class SpaceJunkDisconnectWatcher : MonoBehaviour
{
    /// <summary>自分だけ抜けたときに戻るタイトルのシーン（2026/10/6）。</summary>
    private const string TitleSceneName = "TitleScene";

    /// <summary>知らせを出しておく秒数。</summary>
    private const float MessageSeconds = 6f;

    /// <summary>自分で抜けるところなので、見張りを止めてほしいとき true（<see cref="SpaceJunkLeaveMatch"/> が立てる）。</summary>
    public static bool LeavingOnPurpose { get; set; }

    /// <summary>前のフレームで、試合中に参加者としてつながっていたか。</summary>
    private bool wasClientInMatch;

    private string message = string.Empty;
    private float messageUntil;
    private GUIStyle messageStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<SpaceJunkDisconnectWatcher>() != null)
        {
            return;
        }

        GameObject watcher = new GameObject("SpaceJunkDisconnectWatcher (自動)");
        watcher.AddComponent<SpaceJunkDisconnectWatcher>();
        DontDestroyOnLoad(watcher);
    }

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        bool clientInMatch = manager != null && manager.IsConnectedClient && !manager.IsServer &&
                             SpaceJunkRound.Current != null;

        if (clientInMatch)
        {
            LeavingOnPurpose = false;
        }
        else if (wasClientInMatch && !LeavingOnPurpose && (manager == null || !manager.IsConnectedClient))
        {
            // つながっていたのに切れた（試合のマップのまま）
            OnLostConnection(manager);
        }

        wasClientInMatch = clientInMatch;
    }

    private async void OnLostConnection(NetworkManager manager)
    {
        Debug.LogWarning("[NET] 試合中にホストとの接続が切れました。タイトルに戻ります。");

        message = "ホストとの接続が切れたので、タイトルに戻りました";
        messageUntil = Time.unscaledTime + MessageSeconds;

        // ポーズ画面を開いたまま切れたときに、止まったままにならないよう閉じる
        if (PauseMenu.Current != null && PauseMenu.Current.IsOpen)
        {
            PauseMenu.Current.Close();
        }

        // 部屋の記録を片付ける（残っていると、あとで同じ合言葉で入れない）
        InternetConnection internet = manager != null ? manager.GetComponent<InternetConnection>() : null;
        if (internet != null && internet.State == InternetConnection.Phase.Connected)
        {
            await internet.LeaveGameAsync();
        }

        if (manager != null && manager.IsListening)
        {
            manager.Shutdown();
        }

        SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime > messageUntil || string.IsNullOrEmpty(message))
        {
            return;
        }

        if (messageStyle == null)
        {
            messageStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
        }

        float width = Mathf.Min(720f, Screen.width - 40f);
        GUI.Box(new Rect((Screen.width - width) * 0.5f, 40f, width, 56f), message, messageStyle);
    }
}
