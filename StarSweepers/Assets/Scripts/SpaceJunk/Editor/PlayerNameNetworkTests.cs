using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerNameNetworkTests
{
    [UnityTest]
    public IEnumerator Names_AreAssignedPerPlayerAndReachClientsIncludingLateJoiners()
    {
        // 未実装の段階でもコンパイルでき、欠けている入口を失敗として示す。
        Assert.That(typeof(FishingNetPlayer).GetMethod("SetName"), Is.Not.Null);
        yield return new EnterPlayMode();

        GameObject template = new GameObject("NameTestPlayer", typeof(NetworkObject), typeof(FishingNetPlayer));
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(template.GetComponent<NetworkObject>(), 314159u);
        GameObject sessionTemplate = new GameObject("NameTestSession", typeof(NetworkObject), typeof(SpaceJunkSession));
        typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(sessionTemplate.GetComponent<NetworkObject>(), 314160u);
        var managers = new List<NetworkManager>();
        try
        {
            NetworkManager host = CreateManager("NameTestHost", template, sessionTemplate);
            managers.Add(host);
            Assert.That(host.StartHost(), Is.True);
            yield return WaitFor(() => FindPlayer(host, 0, 0) != null);
            FishingNetPlayer hostPlayer = FindPlayer(host, 0, 0);
            Assert.That(HasName(hostPlayer), Is.True);
            Assert.That(SetName(hostPlayer, 1, 0, 2), Is.True);
            Assert.That(hostPlayer.DisplayName, Is.EqualTo("深淵の勇者"));

            GameObject terminalObject = new GameObject("NameTerminalTest");
            try
            {
                NameChangeTerminal terminal = terminalObject.AddComponent<NameChangeTerminal>();
                MethodInfo randomize = typeof(NameChangeTerminal).GetMethod("TryRandomName");
                Assert.That(randomize, Is.Not.Null);
                terminalObject.transform.position = hostPlayer.transform.position + Vector3.right * 100f;
                Assert.That(randomize.Invoke(terminal, new object[] { hostPlayer }), Is.EqualTo(false));
                terminalObject.transform.position = hostPlayer.transform.position;
                bool changed = false;
                for (int i = 0; i < 16; i++)
                {
                    Assert.That(randomize.Invoke(terminal, new object[] { hostPlayer }), Is.EqualTo(true));
                    changed |= hostPlayer.DisplayName != "深淵の勇者";
                }
                Assert.That(changed, Is.True, "端末で名前を抽選できていない");
                SetName(hostPlayer, 1, 0, 2);
            }
            finally
            {
                Object.Destroy(terminalObject);
            }
            GameObject sessionObject = Object.Instantiate(sessionTemplate);
            sessionObject.GetComponent<NetworkObject>().Spawn();
            SpaceJunkSession hostSession = sessionObject.GetComponent<SpaceJunkSession>();
            Assert.That(hostSession.NameOf(hostPlayer.PlayerKey), Is.EqualTo("深淵の勇者"));

            NetworkManager client = CreateManager("NameTestClient", template, sessionTemplate);
            managers.Add(client);
            Assert.That(client.StartClient(), Is.True);
            yield return WaitFor(() => client.IsConnectedClient && FindPlayer(client, 0, 0) != null);
            yield return WaitFor(() => FindPlayer(client, 0, 0).DisplayName == "深淵の勇者");
            yield return WaitFor(() => FindPlayer(client, client.LocalClientId, 0) != null);
            FishingNetPlayer clientPlayer = FindPlayer(client, client.LocalClientId, 0);
            yield return WaitFor(() => HasName(clientPlayer));

            Assert.That(SetName(clientPlayer, 2, 0, 0), Is.True);
            yield return WaitFor(() => FindPlayer(host, client.LocalClientId, 0).DisplayName == "最強のアリス");
            yield return WaitFor(() => clientPlayer.DisplayName == "最強のアリス");
            yield return WaitFor(() => FindSession(client) != null);
            SpaceJunkSession clientSession = FindSession(client);
            Assert.That(clientSession.NameOf(clientPlayer.PlayerKey), Is.EqualTo("最強のアリス"));
            Assert.That(hostSession.NameOf(clientPlayer.PlayerKey), Is.EqualTo("最強のアリス"));
            Assert.That(SetName(FindPlayer(client, 0, 0), 0, 0, 0), Is.False,
                "参加者は他の人の名前を変えられない");
            Assert.That(SetName(clientPlayer, -1, 0, 0), Is.False);
            Assert.That(SetName(hostPlayer, 9999, 0, 0), Is.False);
            Assert.That(hostPlayer.DisplayName, Is.EqualTo("深淵の勇者"));

            // 同じ接続の2人目も、1人目と別の番号を持つ。
            GameObject extraObject = Object.Instantiate(template);
            NetworkObject extraNetwork = extraObject.GetComponent<NetworkObject>();
            FishingNetPlayer extra = extraObject.GetComponent<FishingNetPlayer>();
            extra.ServerInitLocalSeat(1);
            extraNetwork.SpawnWithOwnership(0);
            Assert.That(SetName(extra, 0, 0, 1), Is.True);
            yield return WaitFor(() => FindPlayer(client, 0, 1) != null);
            yield return WaitFor(() => FindPlayer(client, 0, 1).DisplayName == "漆黒のウイ");
            Assert.That(clientSession.NameOf(extra.PlayerKey), Is.EqualTo("漆黒のウイ"));
            Assert.That(hostPlayer.DisplayName, Is.EqualTo("深淵の勇者"));

            NetworkManager lateClient = CreateManager("NameTestLateClient", template, sessionTemplate);
            managers.Add(lateClient);
            Assert.That(lateClient.StartClient(), Is.True);
            yield return WaitFor(() => lateClient.IsConnectedClient && FindSession(lateClient) != null
                && FindPlayer(lateClient, client.LocalClientId, 0) != null && FindPlayer(lateClient, 0, 1) != null);
            yield return WaitFor(() => FindPlayer(lateClient, client.LocalClientId, 0).DisplayName == "最強のアリス"
                && FindPlayer(lateClient, 0, 1).DisplayName == "漆黒のウイ");
            Assert.That(FindSession(lateClient).NameOf(clientPlayer.PlayerKey), Is.EqualTo("最強のアリス"));
            Assert.That(FindSession(lateClient).NameOf(hostPlayer.PlayerKey), Is.EqualTo("深淵の勇者"));

            // 旧設定から2秒ごとに上書きされないことも確かめる。
            yield return new WaitForSeconds(2.2f);
            Assert.That(clientPlayer.DisplayName, Is.EqualTo("最強のアリス"));
            Assert.That(clientSession.NameOf(clientPlayer.PlayerKey), Is.EqualTo("最強のアリス"));
        }
        finally
        {
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                if (managers[i] == null) continue;
                managers[i].Shutdown();
                Object.Destroy(managers[i].gameObject);
            }
            Object.Destroy(template);
            Object.Destroy(sessionTemplate);
        }
        yield return null;
        yield return new ExitPlayMode();
    }

    private static NetworkManager CreateManager(string name, GameObject template, GameObject sessionTemplate)
    {
        GameObject root = new GameObject(name);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17983);
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            PlayerPrefab = template
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = template });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sessionTemplate });
        return manager;
    }

    private static SpaceJunkSession FindSession(NetworkManager manager)
    {
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject.TryGetComponent(out SpaceJunkSession session)) return session;
        }
        return null;
    }

    private static FishingNetPlayer FindPlayer(NetworkManager manager, ulong owner, int seat)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.NetworkManager == manager
                && player.OwnerClientId == owner && player.LocalSeat == seat) return player;
        }
        return null;
    }

    private static bool HasName(FishingNetPlayer player)
    {
        return (int)typeof(FishingNetPlayer).GetProperty("NamePrefixIndex").GetValue(player) >= 0
            && (int)typeof(FishingNetPlayer).GetProperty("NameConnectorIndex").GetValue(player) >= 0
            && (int)typeof(FishingNetPlayer).GetProperty("NameSuffixIndex").GetValue(player) >= 0;
    }

    private static bool SetName(FishingNetPlayer player, int prefix, int connector, int suffix)
    {
        return (bool)typeof(FishingNetPlayer).GetMethod("SetName")
            .Invoke(player, new object[] { prefix, connector, suffix });
    }

    private static IEnumerator WaitFor(System.Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "ネットワークの反映が10秒以内に終わらなかった");
    }
}
