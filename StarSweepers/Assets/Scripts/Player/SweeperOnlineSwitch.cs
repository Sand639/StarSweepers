using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙ごみのオンラインで、プレイヤーの体を「今の体」と「新しい体（プレイヤーの本実装）」から選ぶ係。**（2026/10/28）
///
/// ポーズ画面の「設定」→「プレイヤー：今の体／新しい体」（<see cref="GameSettings.PlayerBody"/>）を見て、
/// **通信がまだ始まっていない間**、NetworkManager の Player Prefab を入れ替えておく。
/// プレイヤーの体はホストが出すので、**効くのは部屋を作る人（ホスト）の設定だけ**。参加する人の設定は関係ない。
///
/// | 設定 | 出る体 |
/// | --- | --- |
/// | 今の体（初期値） | NetworkManager に入っているもの（`SpaceJunkPlayer.prefab`）。**何も入れ替えない** |
/// | 新しい体 | 通信の一覧（Network Prefabs）にある、<see cref="SweeperNetSync"/> の付いたプレハブ（`SpaceJunkSweeperPlayer.prefab`） |
///
/// プレハブのファイルは書き換えない（入れ替えるのは、遊んでいる間の NetworkManager の中身だけ）。
/// **ボットは今の体のまま出す**（<see cref="BotPrefabOr"/>。ボットの頭はフックの部品で動くため）。
///
/// シーンに置く物は無い。再生を始めたときに自分で1つだけ作られる。
/// </summary>
public class SweeperOnlineSwitch : MonoBehaviour
{
    /// <summary>入れ替える前に NetworkManager に入っていた体（今の体）。まだ見ていなければ null。</summary>
    private static GameObject currentBodyPrefab;

    /// <summary>
    /// ボットを出すときのプレハブ。新しい体を出していても、**ボットだけは今の体**にする。
    /// 入れ替えていなければ <paramref name="fallback"/>（NetworkManager の Player Prefab）をそのまま返す。
    /// </summary>
    public static GameObject BotPrefabOr(GameObject fallback)
    {
        return currentBodyPrefab != null ? currentBodyPrefab : fallback;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        currentBodyPrefab = null;

        GameObject runner = new GameObject(nameof(SweeperOnlineSwitch));
        runner.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(runner);
        runner.AddComponent<SweeperOnlineSwitch>();
    }

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;

        // つながっている間は入れ替えない（もう出ている体と食い違わないように）
        if (manager == null || manager.IsListening || manager.NetworkConfig == null)
        {
            return;
        }

        Apply(manager);
    }

    /// <summary>設定に合わせて、NetworkManager の Player Prefab を入れ替える（同じなら何もしない）。</summary>
    private static void Apply(NetworkManager manager)
    {
        GameObject playerPrefab = manager.NetworkConfig.PlayerPrefab;
        if (playerPrefab == null)
        {
            return;
        }

        // 最初に見た「新しい体ではない」プレハブを、今の体として覚えておく
        if (currentBodyPrefab == null && playerPrefab.GetComponent<SweeperNetSync>() == null)
        {
            currentBodyPrefab = playerPrefab;
        }

        // 宇宙ごみの体（SpaceJunkPlayerSetup 付き）のときだけ入れ替える。釣りなど、ほかの遊びの NetworkManager には触らない
        if (currentBodyPrefab == null || currentBodyPrefab.GetComponent<SpaceJunkPlayerSetup>() == null)
        {
            return;
        }

        GameObject wanted = currentBodyPrefab;
        if (GameSettings.PlayerBody == PlayerBodyType.Sweeper)
        {
            GameObject sweeperPrefab = FindSweeperPrefab(manager);
            if (sweeperPrefab != null)
            {
                wanted = sweeperPrefab;
            }
        }

        if (manager.NetworkConfig.PlayerPrefab != wanted)
        {
            manager.NetworkConfig.PlayerPrefab = wanted;
            Debug.Log($"[SweeperPlayer] 次に部屋を作ったときのプレイヤーの体：{wanted.name}");
        }
    }

    /// <summary>通信の一覧から、新しい体（<see cref="SweeperNetSync"/> の付いたプレハブ）を探す。無ければ null。</summary>
    private static GameObject FindSweeperPrefab(NetworkManager manager)
    {
        NetworkPrefabs prefabs = manager.NetworkConfig.Prefabs;
        if (prefabs == null)
        {
            return null;
        }

        // 一覧のアセット（DefaultNetworkPrefabs.asset）を先に見る。通信を始める前は、下のまとめた一覧がまだ空のことがあるため
        if (prefabs.NetworkPrefabsLists != null)
        {
            foreach (NetworkPrefabsList list in prefabs.NetworkPrefabsLists)
            {
                if (list == null)
                {
                    continue;
                }

                GameObject found = FindIn(list.PrefabList);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return FindIn(prefabs.Prefabs);
    }

    private static GameObject FindIn(System.Collections.Generic.IReadOnlyList<NetworkPrefab> entries)
    {
        if (entries == null)
        {
            return null;
        }

        foreach (NetworkPrefab entry in entries)
        {
            if (entry != null && entry.Prefab != null && entry.Prefab.GetComponent<SweeperNetSync>() != null)
            {
                return entry.Prefab;
            }
        }

        return null;
    }
}
