using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **宇宙ごみのオンラインの新しい体（プレイヤーの本実装）の自動テスト。**（2026/10/28）
///
/// 1つの Unity の中でホストと参加者を両方つなぎ、本物のプレハブ（`SpaceJunkSweeperPlayer.prefab`・`SpaceJunkPlate.prefab`）で確かめる。
/// ・参加者が光線を撃つと、ホストの画面でもその人の光線の段階が変わる
/// ・光線が物資に当たると、ホストが「その人が引っ掛け中」と記録し、持ち主が参加者へ移る
/// ・巻き取り中に押すと、ホストが物資を受け取り（記録が消え、持ち主がホストへ戻る）、物資が上へ飛ぶ（頭を越える放物線）
/// ・体の歩く部品が止められると、光線の部品も止まる
/// ・設定「プレイヤー：新しい体」で、部屋を作る前の Player Prefab が新しい体に入れ替わる（ボットは今の体のまま）
///
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperOnlineTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkSweeperPlayer.prefab";
    private const string SupplyPrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlate.prefab";
    private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    private const ushort Port = 17991;

    [Test]
    public void SweeperBody_IsRegisteredInNetworkPrefabs()
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath)), Is.True,
            "新しい体が通信の一覧（DefaultNetworkPrefabs.asset）に登録されていない");
    }

    [UnityTest]
    public IEnumerator Switch_ReplacesPlayerPrefabBeforeHosting_AndKeepsBotsOnCurrentBody()
    {
        yield return new EnterPlayMode();
        yield return CheckSwitch();
        yield return new ExitPlayMode();
    }

    /// <summary>
    /// 切り替えの中身。**再生を始めてから呼ぶ別の関数にしてある**（同じ関数の中で、あとで使う値をラムダに渡すと、
    /// 再生の切り替えのときにその入れ物が消えて NullReferenceException になるため）。
    /// </summary>
    private static IEnumerator CheckSwitch()
    {
        // 今の体の代わり（宇宙ごみの体の印 SpaceJunkPlayerSetup だけ付いた、止めてある物）。
        // 切り替えの係は「宇宙ごみの体か」をこの印で見るだけなので、本物のプレハブ（SpaceJunkPlayer.prefab）は読まない
        GameObject currentBody = new GameObject("CurrentBodyStandIn");
        currentBody.SetActive(false);
        currentBody.AddComponent<SpaceJunkPlayerSetup>();
        GameObject sweeperBody = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(currentBody, Is.Not.Null);
        Assert.That(sweeperBody, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        PlayerBodyType saved = GameSettings.PlayerBody;

        GameObject root = new GameObject("SwitchTestManager");
        try
        {
            root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = root.GetComponent<UnityTransport>(),
                PlayerPrefab = currentBody
            };
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = currentBody });
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = sweeperBody });
            yield return WaitFor("singleton", () => NetworkManager.Singleton == manager);

            GameSettings.PlayerBody = PlayerBodyType.Current;
            yield return Wait(0.3f);
            Assert.That(manager.NetworkConfig.PlayerPrefab, Is.EqualTo(currentBody), "今の体のときは入れ替えない");

            GameSettings.PlayerBody = PlayerBodyType.Sweeper;
            yield return WaitFor("swap", () => manager.NetworkConfig.PlayerPrefab == sweeperBody);
            Assert.That(Object.FindObjectsByType<SweeperOnlineSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.GreaterThan(0), "切り替えの係が作られていない");
            Assert.That(manager.NetworkConfig.PlayerPrefab, Is.EqualTo(sweeperBody), "新しい体に入れ替わっていない");
            Assert.That(SweeperOnlineSwitch.BotPrefabOr(manager.NetworkConfig.PlayerPrefab), Is.EqualTo(currentBody),
                "ボットは今の体のままにする");

            GameSettings.PlayerBody = PlayerBodyType.Current;
            yield return WaitFor("swap back", () => manager.NetworkConfig.PlayerPrefab == currentBody);
        }
        finally
        {
            GameSettings.PlayerBody = saved;
            Object.Destroy(root);
            Object.Destroy(currentBody);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator ClientBeam_ReelsAndThrowsSupplyThroughHost()
    {
        yield return new EnterPlayMode();

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject supplyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SupplyPrefabPath);
        Assert.That(playerPrefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");
        Assert.That(supplyPrefab, Is.Not.Null);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        var managers = new List<NetworkManager>();
        try
        {
            NetworkManager host = CreateManager("SweeperTestHost", playerPrefab, supplyPrefab);
            managers.Add(host);
            Assert.That(host.StartHost(), Is.True);

            NetworkManager client = CreateManager("SweeperTestClient", playerPrefab, supplyPrefab);
            managers.Add(client);
            Assert.That(client.StartClient(), Is.True);

            yield return WaitFor("step1", () => client.IsConnectedClient && FindPlayer(client, client.LocalClientId) != null
                && FindPlayer(host, client.LocalClientId) != null);

            FishingNetPlayer clientPlayer = FindPlayer(client, client.LocalClientId);
            FishingNetPlayer clientPlayerOnHost = FindPlayer(host, client.LocalClientId);
            Assert.That(clientPlayer.IsOwner, Is.True);
            Assert.That(clientPlayer.GetComponent<SweeperNetSync>(), Is.Not.Null);
            yield return WaitFor("step2", () => clientPlayer.PlayerIndex >= 0);

            // 立ち位置が落ち着くまで待ってから、参加者の狙う向きの 3m 先（撃った瞬間に届く長さ）に物資を置く（ホストが出す）
            yield return Wait(0.5f);
            SweeperAim aim = clientPlayer.GetComponent<SweeperAim>();
            Vector3 target = clientPlayer.transform.position + aim.Direction * 3f + Vector3.up * 0.5f;

            GameObject supplyObject = Object.Instantiate(supplyPrefab, target, Quaternion.identity);
            NetworkObject supplyNetwork = supplyObject.GetComponent<NetworkObject>();
            supplyNetwork.Spawn();
            FishingNetSupply hostSupply = supplyObject.GetComponent<FishingNetSupply>();
            ulong supplyId = supplyNetwork.NetworkObjectId;

            yield return WaitFor("step3", () => client.SpawnManager.SpawnedObjects.ContainsKey(supplyId));

            // 1つの Unity の中にホストと参加者の物資が同じ場所に2つあるので、押し合わないようにする（本番は別々のPCなので起きない）
            Physics.IgnoreCollision(supplyObject.GetComponent<Collider>(),
                client.SpawnManager.SpawnedObjects[supplyId].GetComponent<Collider>());
            // 同じ理由で、参加者の光線にはホスト側の物資が見えないようにする（Ignore Raycast の層に移し、光線の当たる層から外す）
            supplyObject.layer = 2;
            typeof(SweeperShooter).GetField("hitMask", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(clientPlayer.GetComponent<SweeperShooter>(), (LayerMask)~(1 << 2));
            yield return Wait(0.5f); // 物資が床に落ち着くまで

            // ---- 参加者が光線を撃つ（押す → 少し溜める → 離す） ----
            SweeperShooter shooter = clientPlayer.GetComponent<SweeperShooter>();
            SweeperReel reel = clientPlayer.GetComponent<SweeperReel>();
            Invoke(shooter, "BeginCharge");
            yield return Wait(0.3f);
            Assert.That(shooter.Phase, Is.EqualTo(SweeperShooter.ShotPhase.Charging));
            yield return WaitFor("step4", () => SyncedPhase(clientPlayerOnHost) == SweeperShooter.ShotPhase.Charging);

            Invoke(shooter, "Fire");

            // 当たって巻き取りに入り、ホストが「参加者が引っ掛け中」と記録して持ち主を移す
            yield return WaitFor("step5", () => shooter.Phase == SweeperShooter.ShotPhase.Reeling);
            yield return WaitFor("step6", () => hostSupply.HookedByPlayerIndex == clientPlayer.PlayerIndex
                && supplyNetwork.OwnerClientId == client.LocalClientId);
            yield return WaitFor("step7", () => SyncedPhase(clientPlayerOnHost) == SweeperShooter.ShotPhase.Reeling);

            yield return WaitFor("step8", () => reel.Elapsed > 0.4f);

            // 引き寄せられている（ホストから見ても近づいている）
            float startDistance = Vector3.Distance(Flat(target), Flat(clientPlayer.transform.position));
            yield return WaitFor("step9", () => Vector3.Distance(Flat(supplyObject.transform.position),
                Flat(clientPlayerOnHost.transform.position)) < startDistance - 0.5f);

            // ---- 巻き取り中に押す → 後ろへ投げる ----
            reel.Press();
            Assert.That(reel.IsActive, Is.False);

            yield return WaitFor("step10", () => hostSupply.HookedByPlayerIndex == -1
                && supplyNetwork.OwnerClientId == NetworkManager.ServerClientId);
            Rigidbody hostBody = supplyObject.GetComponent<Rigidbody>();
            Assert.That(hostBody.isKinematic, Is.False);
            Assert.That(hostBody.linearVelocity.y, Is.GreaterThan(1f), "ホストで物資が上へ飛んでいない（頭を越える放物線）");
            yield return WaitFor("step11", () => SyncedPhase(clientPlayerOnHost) == SweeperShooter.ShotPhase.Idle);

            // ---- 体の歩く部品が止められたら、光線の部品も止まる ----
            FishingPlayerController body = clientPlayer.GetComponent<FishingPlayerController>();
            body.enabled = false;
            yield return WaitFor("lock", () => !shooter.enabled);
            body.enabled = true;
            yield return WaitFor("unlock", () => shooter.enabled);
        }
        finally
        {
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                if (managers[i] == null) continue;
                managers[i].Shutdown();
                Object.Destroy(managers[i].gameObject);
            }
            Object.Destroy(ground);
        }

        yield return null;
        yield return new ExitPlayMode();
    }

    private static NetworkManager CreateManager(string name, GameObject playerPrefab, GameObject supplyPrefab)
    {
        GameObject root = new GameObject(name);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", Port);
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            PlayerPrefab = playerPrefab
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = supplyPrefab });
        return manager;
    }

    private static FishingNetPlayer FindPlayer(NetworkManager manager, ulong owner)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.NetworkManager == manager
                && player.OwnerClientId == owner && player.LocalSeat == 0) return player;
        }
        return null;
    }

    /// <summary>その体の <see cref="SweeperNetSync"/> が配っている光線の段階（ほかのPCから見た値）。</summary>
    private static SweeperShooter.ShotPhase SyncedPhase(FishingNetPlayer player)
    {
        SweeperNetSync sync = player.GetComponent<SweeperNetSync>();
        FieldInfo field = typeof(SweeperNetSync).GetField("shotPhase", BindingFlags.Instance | BindingFlags.NonPublic);
        return (SweeperShooter.ShotPhase)((NetworkVariable<int>)field.GetValue(sync)).Value;
    }

    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, method + " が見つからない");
        info.Invoke(target, null);
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    /// <summary>実際の時間で待つ（テストの中では WaitForSeconds だとゲームのフレームが進まないことがあるため）。</summary>
    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitFor(string label, System.Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "10秒以内に条件がそろわなかった：" + label);
    }
}
