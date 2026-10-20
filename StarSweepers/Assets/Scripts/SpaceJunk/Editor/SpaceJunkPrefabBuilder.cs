using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **宇宙ごみ集めで使うプレハブを組み立てる置き場。**
///
/// 作るもの：
///   ・`Assets/Prefabs/SpaceJunk/Online/SpaceJunkSession.prefab` … 試合の係（ラウンドをまたいで生き続ける）
///   ・`Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlayer.prefab` … 1人分の本体
///   ・`Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlate/Circuit/Fuel.prefab` … 素材3種類
///
/// ## 釣りのものを壊さないために
///
/// プレイヤーと素材は、**釣りの作り方をそのまま使いつつ、別のファイルとして保存する。**
/// 釣りのプレハブ（`Assets/Prefabs/Fish/` 以下）には一切書き込まない。
///
/// プレイヤーは、**すでにある釣りのプレイヤーを複製してから**上乗せ部品を足す形にしている
/// （同じ組み立てコードを2つ持つと、片方だけ直して食い違うため）。
/// 複製なので、あとから釣り側が変わってもこちらは影響を受けない。
///
/// ## 部品の値の入れ方の注意
///
/// 部品の中身は `FishingSceneBuilder.SetString` / `SetInt` / `SetObjectArray` で、
/// **フィールドの名前（`"lobbySceneName"` など）を文字で指定して**書き込んでいる。
/// 書き込む先のフィールドを改名すると、警告が出るか、何も言わずに値が入らなくなる。改名するときは、ここの文字も直すこと。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
internal static class SpaceJunkPrefabBuilder
{
    public const string PrefabFolder = "Assets/Prefabs/SpaceJunk";
    public const string OnlinePrefabFolder = PrefabFolder + "/Online";

    public const string SessionPrefabPath = OnlinePrefabFolder + "/SpaceJunkSession.prefab";
    public const string PlayerPrefabPath = OnlinePrefabFolder + "/SpaceJunkPlayer.prefab";

    public const string PlatePrefabPath = OnlinePrefabFolder + "/SpaceJunkPlate.prefab";
    public const string CircuitPrefabPath = OnlinePrefabFolder + "/SpaceJunkCircuit.prefab";
    public const string FuelPrefabPath = OnlinePrefabFolder + "/SpaceJunkFuel.prefab";

    /// <summary>
    /// 通信で出せるプレハブの一覧（Netcode が最初に作るファイル）。ここに入っていないプレハブは、ホストが出しても他の人に出ない。
    /// </summary>
    private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

    /// <summary>プレハブの置き場を用意する。</summary>
    public static void EnsureFolders()
    {
        FishingSceneBuilder.EnsureFolder(OnlinePrefabFolder);
    }

    // ------------------------------------------------------------
    // 試合の係
    // ------------------------------------------------------------

    /// <summary>
    /// **試合の係のプレハブを作る。**
    ///
    /// シーンに置かずプレハブにしているのは、**シーンを切り替えても消えないようにする**ため。
    /// ホストが <see cref="SpaceJunkSessionSpawner"/> から1つだけ出す。
    /// </summary>
    public static GameObject CreateSessionPrefab(string lobbySceneName)
    {
        GameObject root = new GameObject("SpaceJunkSession");
        root.AddComponent<NetworkObject>();

        // ロビーへ戻るときに読み込むシーンの名前
        SpaceJunkSession session = root.AddComponent<SpaceJunkSession>();
        FishingSceneBuilder.SetString(session, "lobbySceneName", lobbySceneName);

        // 作業用に置いた物は、プレハブに保存したらすぐ消す（いま開いているシーンに残さない）
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SessionPrefabPath);
        Object.DestroyImmediate(root);

        RegisterNetworkPrefab(saved);
        return saved;
    }

    // ------------------------------------------------------------
    // プレイヤー
    // ------------------------------------------------------------

    /// <summary>
    /// **1人分の本体のプレハブを作る。**
    ///
    /// 釣りのオンラインプレイヤーを複製し、宇宙ごみ用の上乗せ部品
    /// （<see cref="SpaceJunkPlayerSetup"/>）を足しただけのもの。
    /// 釣りのプレハブがまだ無ければ、釣りのツールに作らせてから複製する。
    /// </summary>
    public static GameObject CreatePlayerPrefab(InputActionAsset inputActions)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
            FishingNetSceneBuilder.OnlinePlayerPrefabPath);

        if (source == null)
        {
            Debug.Log("[JUNK] 釣りのオンラインプレイヤーがまだ無いので、先に作ります。");
            source = FishingNetSceneBuilder.CreateOnlinePlayerPrefab(inputActions);
        }

        // **プレハブとのつながりを切った複製**を作る（あとから釣り側が変わっても影響を受けない）
        GameObject root = Object.Instantiate(source);
        root.name = "SpaceJunkPlayer";

        SpaceJunkPlayerSetup setup = root.AddComponent<SpaceJunkPlayerSetup>();

        // チームの色に塗る見た目（釣りのプレハブと同じ2つ）。
        // 子の**名前**（Body / その下の FrontMark）で探すので、釣りのプレイヤーの子を改名すると見つからなくなる（警告は出る）
        Transform body = root.transform.Find("Body");
        Transform frontMark = body != null ? body.Find("FrontMark") : null;

        if (body != null && frontMark != null)
        {
            FishingSceneBuilder.SetObjectArray(setup, "teamRenderers", new Renderer[]
            {
                body.GetComponent<MeshRenderer>(),
                frontMark.GetComponent<MeshRenderer>()
            });
        }
        else
        {
            Debug.LogWarning("[JUNK] プレイヤーの Body / FrontMark が見つかりませんでした。" +
                             "チームの色が塗られません。SpaceJunkPlayerSetup の Team Renderers を手で入れてください。");
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);

        RegisterNetworkPrefab(saved);
        return saved;
    }

    // ------------------------------------------------------------
    // 素材3種類
    // ------------------------------------------------------------

    /// <summary>
    /// **素材3種類のプレハブを作る。**
    ///
    /// 中身は釣りの「物資」と同じ（フックで引っ掛けられて、投げられて、通信で共有される）。
    /// 違いは**種類の目印と、見た目の色・形**だけ。
    ///
    /// 戻り値は 装甲板・回路基板・燃料タンク の順。
    /// </summary>
    public static GameObject[] CreateMaterialPrefabs()
    {
        // 大きさは（幅, 厚み, 奥行き）の m。板は平たく、基板は小さく薄く、燃料タンクは縦長にしている。
        // **この順番は変えない**（SpaceJunkSetup.ReplaceSpawner が 0・1・2 番目で受け取っている）
        return new[]
        {
            CreateMaterialPrefab(SpaceJunkMaterialKind.Plate, PlatePrefabPath,
                new Vector3(1.1f, 0.25f, 1.1f)),
            CreateMaterialPrefab(SpaceJunkMaterialKind.Circuit, CircuitPrefabPath,
                new Vector3(0.9f, 0.2f, 0.6f)),
            CreateMaterialPrefab(SpaceJunkMaterialKind.Fuel, FuelPrefabPath,
                new Vector3(0.6f, 1.0f, 0.6f))
        };
    }

    /// <summary>
    /// 素材1種類ぶんのプレハブを作って保存し、通信の一覧に登録する。
    /// 作業用の箱はシーンに一時的に置くので、保存したらすぐ消す。
    /// </summary>
    /// <param name="size">箱の大きさ（localScale。Unity の箱（Cube）の形は 1m 四方なので、そのまま m になる）。</param>
    private static GameObject CreateMaterialPrefab(SpaceJunkMaterialKind kind, string path, Vector3 size)
    {
        // 釣りの物資と同じ部品（Rigidbody / HookableObject / NetworkObject /
        // NetworkTransform / FishingNetSupply）が付いた箱を作ってもらう
        GameObject item = FishingNetSceneBuilder.CreateNetworkSupply(
            SpaceJunkMaterials.Name(kind), Vector3.zero);

        item.name = $"SpaceJunk{kind}";
        item.transform.localScale = size;

        // 種類ごとの色に塗り替える
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            $"{FishingSceneBuilder.MaterialFolder}/SpaceJunk{kind}.mat",
            SpaceJunkMaterials.Color(kind));

        MeshRenderer renderer = item.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        SpaceJunkMaterial marker = item.AddComponent<SpaceJunkMaterial>();
        FishingSceneBuilder.SetInt(marker, "kind", (int)kind);
        FishingSceneBuilder.SetObjectArray(marker, "tintRenderers", new Renderer[] { renderer });

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(item, path);
        Object.DestroyImmediate(item);

        RegisterNetworkPrefab(saved);
        return saved;
    }

    // ------------------------------------------------------------
    // 通信への登録
    // ------------------------------------------------------------

    /// <summary>
    /// **全員の画面に出せるように、プレハブを通信の一覧へ登録する。**
    /// 登録しないと、ホストが出しても他の人の画面には現れない。
    /// </summary>
    public static void RegisterNetworkPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);

        if (list == null)
        {
            Debug.LogWarning($"[JUNK] {NetworkPrefabsListPath} が見つかりません。" +
                             $"{prefab.name} を NetworkManager の Network Prefabs に手で登録してください。");
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }
    }
}
