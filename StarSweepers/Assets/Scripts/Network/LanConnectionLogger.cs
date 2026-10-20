using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **つながった・切れたを記録に残すだけ**の部品。
///
/// 展示当日に「参加できない」と言われたとき、
/// ログを見れば**どこまで進んで失敗したのか**が分かるようにしておくためのもの。
///
/// ゲームの動きには一切影響しない。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanConnectionLogger : MonoBehaviour
{
    /// <summary>通信のまとめ役。同じ GameObject に付いている（RequireComponent）。これの知らせ（つながった・切れた）を記録する。</summary>
    private NetworkManager manager;

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        if (manager == null)
        {
            return;
        }

        manager.OnServerStarted += HandleServerStarted;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        if (manager == null)
        {
            return;
        }

        manager.OnServerStarted -= HandleServerStarted;
        manager.OnClientConnectedCallback -= HandleClientConnected;
        manager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    [Tooltip("遅れ（ping）をログに残す間隔（秒）。0にすると残さない")]
    [Range(0f, 60f)]
    [SerializeField] private float pingLogSeconds = 10f;

    private void HandleServerStarted()
    {
        Debug.Log("[LAN] ホストとして待ち受けを始めました");
    }

    private void Start()
    {
        if (pingLogSeconds > 0f)
        {
            StartCoroutine(LogPingLoop());
        }
    }

    /// <summary>
    /// 遅れ（ping）を、ときどきログに残す。
    ///
    /// **画面を見られない状況で役に立つ。**
    /// 展示当日に「なんか重い」と言われたとき、
    /// ログを見れば通信のせいかどうかが分かる。
    /// </summary>
    private IEnumerator LogPingLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(pingLogSeconds);

        while (true)
        {
            yield return wait;

            // つながっていない間は何も残さず、待ち続ける（止めないのは、つなぎ直したときにまた記録するため）
            if (manager == null || (!manager.IsClient && !manager.IsServer))
            {
                continue;
            }

            NetworkTransport transport = manager.NetworkConfig.NetworkTransport;

            if (transport == null)
            {
                continue;
            }

            if (manager.IsServer)
            {
                foreach (ulong id in manager.ConnectedClientsIds)
                {
                    if (id == manager.LocalClientId)
                    {
                        continue;
                    }

                    Debug.Log($"[PING] 番号 {id} までの遅れ：{transport.GetCurrentRtt(id)} ms");
                }
            }
            else
            {
                Debug.Log($"[PING] ホストまでの遅れ：{transport.GetCurrentRtt(NetworkManager.ServerClientId)} ms");
            }
        }
    }

    /// <summary>誰かがつながったとき。ホストでは「誰が・いま何人」、参加者では「自分がつながった」の意味になる。</summary>
    private void HandleClientConnected(ulong clientId)
    {
        // 人数が分かるのはホストだけ。参加者側は -1 にして、文面を変える
        int count = manager.IsServer ? manager.ConnectedClientsIds.Count : -1;

        Debug.Log(count >= 0
            ? $"[LAN] 番号 {clientId} がつながりました（いま {count} 人）"
            : $"[LAN] つながりました（自分の番号：{clientId}）");
    }

    /// <summary>誰かが切れたとき。参加者側では「自分が切られた」ときにも呼ばれる（理由があれば一緒に残す）。</summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        string reason = string.IsNullOrEmpty(manager.DisconnectReason)
            ? "（理由の記載なし）"
            : manager.DisconnectReason;

        Debug.Log($"[LAN] 番号 {clientId} が切断されました {reason}");
    }
}
