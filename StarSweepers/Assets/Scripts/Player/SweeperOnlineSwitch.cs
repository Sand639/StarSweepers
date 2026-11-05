using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙ごみのオンラインで、人のプレイヤーを「新しい体（プレイヤーの本実装）」で出す係。**（2026/10/28。2026/11/3 から設定なしでいつも新しい体）
///
/// **通信がまだ始まっていない間**、NetworkManager の Player Prefab（`SpaceJunkPlayer.prefab`）を、
/// 通信の一覧（Network Prefabs）にある <see cref="SweeperNetSync"/> の付いたプレハブ（`SpaceJunkSweeperPlayer.prefab`）に入れ替えておく。
/// 前はポーズ画面の設定で「今の体／新しい体」を選べたが、小野田さんの依頼で選べなくした（2026/11/3）。
///
/// プレハブのファイルは書き換えない（入れ替えるのは、遊んでいる間の NetworkManager の中身だけ）。
/// **ボットも新しい体で出す**（<see cref="BotPrefabOr"/>。2026/11/5 から。頭は <see cref="SweeperBot"/>）。
/// 新しい体が通信の一覧に無いときだけ、警告を出して今の体で遊ぶ。
///
/// シーンに置く物は無い。再生を始めたときに自分で1つだけ作られる。
/// 起動した最初のフレームで部屋を作る場合（`LanAutoStart` の `-host` など）にも間に合うよう、
/// 作られた瞬間と、NetworkManager が生まれた瞬間（`NetworkManager.OnInstantiated`）にも入れ替える。
/// </summary>
public class SweeperOnlineSwitch : MonoBehaviour
{
    /// <summary>入れ替える前に NetworkManager に入っていた体（今の体）。まだ見ていなければ null。</summary>
    private static GameObject currentBodyPrefab;

    /// <summary>「新しい体が見つからない」をもう知らせたか（毎フレーム出さないため）。</summary>
    private static bool warnedMissing;

    /// <summary>
    /// ボットを出すときのプレハブ。**2026/11/5 から、ボットも人と同じ新しい体**（<paramref name="fallback"/>＝入れ替えたあとの Player Prefab）で出す。
    /// 頭は新しい体のボット（<see cref="SweeperBot"/>）が付く。
    /// 前（2026/10/28〜11/4）はボットの頭がフックの部品で動いたので、ボットだけ今の体にしていた。
    /// 今の体のボットに戻したくなったら、ここで <see cref="CurrentBodyPrefab"/> を返す。
    /// </summary>
    public static GameObject BotPrefabOr(GameObject fallback)
    {
        return fallback != null ? fallback : currentBodyPrefab;
    }

    /// <summary>入れ替える前の Player Prefab（今の体）。まだ見ていなければ null。</summary>
    public static GameObject CurrentBodyPrefab => currentBodyPrefab;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        currentBodyPrefab = null;
        warnedMissing = false;

        GameObject runner = new GameObject(nameof(SweeperOnlineSwitch));
        runner.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(runner);
        runner.AddComponent<SweeperOnlineSwitch>();

        // シーンに置いてある NetworkManager は、もう生まれている（このあと Start で部屋を作ることがあるので、いま入れ替える）
        TryApply(NetworkManager.Singleton);

        // あとから生まれる NetworkManager にも、生まれた瞬間に入れ替える
        NetworkManager.OnInstantiated -= TryApply;
        NetworkManager.OnInstantiated += TryApply;
    }

    private void OnDestroy()
    {
        NetworkManager.OnInstantiated -= TryApply;
    }

    private void Update()
    {
        TryApply(NetworkManager.Singleton);
    }

    private static void TryApply(NetworkManager manager)
    {
        // つながっている間は入れ替えない（もう出ている体と食い違わないように）
        if (manager == null || manager.IsListening || manager.NetworkConfig == null)
        {
            return;
        }

        Apply(manager);
    }

    /// <summary>NetworkManager の Player Prefab を新しい体に入れ替える（もう入れ替えてあれば何もしない）。</summary>
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
        GameObject sweeperPrefab = FindSweeperPrefab(manager);
        if (sweeperPrefab != null)
        {
            wanted = sweeperPrefab;
        }
        else if (!warnedMissing)
        {
            warnedMissing = true;
            Debug.LogWarning("[SweeperPlayer] 新しい体のプレハブが通信の一覧（Network Prefabs）に見つからないので、今の体で遊びます。" +
                             "メニューの「宇宙ごみのオンラインの新しい体のプレハブを作り直す」で作ってください。");
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
