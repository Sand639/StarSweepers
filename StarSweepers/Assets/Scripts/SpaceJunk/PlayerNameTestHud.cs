using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>名前専用の検証シーン。再生するとホストになり、名前と3つの番号を表示する。</summary>
[RequireComponent(typeof(NetworkManager))]
public class PlayerNameTestHud : MonoBehaviour
{
    [SerializeField] private GameObject sessionPrefab;
    [SerializeField] private bool startAsClient;
    [SerializeField] private string hostAddress = "127.0.0.1";

    private NetworkManager manager;
    private SpaceJunkSession session;

    private void Start()
    {
        manager = GetComponent<NetworkManager>();
        Application.runInBackground = true;
        manager.AddNetworkPrefab(sessionPrefab);
        manager.GetComponent<UnityTransport>().SetConnectionData(hostAddress, 17984);
        if (startAsClient)
        {
            manager.StartClient();
        }
        else if (manager.StartHost())
        {
            session = Instantiate(sessionPrefab).GetComponent<SpaceJunkSession>();
            session.NetworkObject.Spawn();
        }
    }

    private void OnGUI()
    {
        if (manager == null) return;
        if (session == null) session = SpaceJunkSession.Current;
        GUILayout.BeginArea(new Rect(20f, 20f, 650f, Screen.height - 40f), GUI.skin.box);
        GUILayout.Label("Player name / network test");
        GUILayout.Label(startAsClient ? "Client" : "Host (auto started)");
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsSpawned || player.NetworkManager != manager) continue;
            string displayed = session != null ? session.NameOf(player.PlayerKey) : player.DisplayName;
            GUILayout.Label($"{player.PlayerKey}: {displayed}  " +
                $"[{player.NamePrefixIndex}, {player.NameConnectorIndex}, {player.NameSuffixIndex}]");
        }
        GUILayout.EndArea();
    }
}
