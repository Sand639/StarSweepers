using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **宇宙ごみのオンラインで使う「新しい体」（プレイヤーの本実装）のプレハブを作るツール。**（2026/10/28・小野田さんの依頼）
///
/// メニュー `Tools > StarSweepers > 宇宙ごみのオンラインの新しい体のプレハブを作り直す` で、
/// `Assets/Prefabs/SpaceJunk/Online/SpaceJunkSweeperPlayer.prefab` を作り直し、通信の一覧（`DefaultNetworkPrefabs.asset`）に登録する。
///
/// 作り方：
/// 1. 今の体 `SpaceJunkPlayer.prefab` を**つながりを切って複製**する（通信・出てくる場所・カメラ・レーダー・名前のタグはそのまま使う）
/// 2. フックの部品（HookController・ThrowController・PlayerAimController・フック・糸・手元）を外す
/// 3. `FishingPlayerController` は**重力と吹き飛ばしの受け口として残し**、歩く速さと向き直る速さを 0 にする（歩く・向くは SweeperMover）
/// 4. Sweeper の部品（<see cref="SweeperPlayerSetup.AddSweeperParts"/>。頭の上の名前は付けない）と <see cref="SweeperNetSync"/> を付ける
/// 5. 体（カプセル）を薄い灰色にし、チームの色は顔の板（FrontMark）だけに塗る
///
/// ⚠️ **作り直すと、このプレハブでインスペクターから調整した値は消える。** 今の体（`SpaceJunkPlayer.prefab`）は変えない。
/// どの体で遊ぶかは、ポーズ画面の「設定」→「プレイヤー」で選ぶ（<see cref="SweeperOnlineSwitch"/>）。
/// </summary>
public static class SweeperOnlineSetup
{
    private const string SourcePrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkPlayer.prefab";
    public const string PrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkSweeperPlayer.prefab";

    [MenuItem("Tools/StarSweepers/宇宙ごみのオンラインの新しい体のプレハブを作り直す")]
    public static GameObject CreateOnlinePrefab()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (source == null)
        {
            Debug.LogError($"[SweeperPlayer] {SourcePrefabPath} が見つかりません。先に宇宙ごみのプレハブを作ってください。");
            return null;
        }

        // **プレハブとのつながりを切った複製**（今の体があとで変わっても、こちらは作り直すまで変わらない）
        GameObject root = Object.Instantiate(source);
        root.name = "SpaceJunkSweeperPlayer";

        RemoveHookParts(root);
        SetupBody(root);

        SweeperPlayerSetup.AddSweeperParts(root, false);
        root.AddComponent<SweeperNetSync>();

        SetOwnerOnlyScripts(root);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        SpaceJunkPrefabBuilder.RegisterNetworkPrefab(saved);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SweeperPlayer] 宇宙ごみのオンラインの新しい体を作りました：{PrefabPath}\n" +
                  "ポーズ画面の「設定」→「プレイヤー：新しい体」にしてから部屋を作ると、この体で遊べます。");
        return saved;
    }

    /// <summary>フックの部品と、その見た目（フック・糸・手元）を外す。外したものを指していた参照は空にする。</summary>
    private static void RemoveHookParts(GameObject root)
    {
        // ThrowController は HookController を使うので先に外す
        DestroyAll<ThrowController>(root);
        DestroyAll<HookController>(root);
        DestroyAll<HookAimAssist>(root);
        DestroyAll<PlayerAimController>(root);

        foreach (string childName in new[] { "Hook", "HookLine", "HandPoint" })
        {
            Transform child = root.transform.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        FishingNetPlayer netPlayer = root.GetComponent<FishingNetPlayer>();
        if (netPlayer != null)
        {
            FishingSceneBuilder.SetRef(netPlayer, "hookVisual", null);
            FishingSceneBuilder.SetRef(netPlayer, "line", null);
            FishingSceneBuilder.SetRef(netPlayer, "handPoint", null);

            // FishingNetPlayer は釣りの決まり（参加順に2人ずつ）の色で体を塗るので、何も塗らせない。
            // チームの色は SpaceJunkPlayerSetup（顔の板）と SweeperPlayer（リング・光線）が塗る
            FishingSceneBuilder.SetObjectArray(netPlayer, "teamRenderers", new Object[0]);
        }
    }

    /// <summary>
    /// 今の歩く部品は「重力と吹き飛ばしの受け口」だけにする（歩かない・向かない）。
    /// 体を薄い灰色にし、チームの色は顔の板だけに塗る（リングと光線にもチームの色が付く）。
    /// </summary>
    private static void SetupBody(GameObject root)
    {
        FishingPlayerController body = root.GetComponent<FishingPlayerController>();
        if (body != null)
        {
            FishingSceneBuilder.SetFloat(body, "moveSpeed", 0f);
            FishingSceneBuilder.SetFloat(body, "turnSpeed", 0f);
            FishingSceneBuilder.SetRef(body, "aim", null);
        }
        else
        {
            Debug.LogWarning("[SweeperPlayer] FishingPlayerController が見つかりません。ベルトコンベアや爆弾の吹き飛ばしが効かず、重力もかかりません。");
        }

        Transform bodyVisual = root.transform.Find("Body");
        Transform frontMark = bodyVisual != null ? bodyVisual.Find("FrontMark") : root.transform.Find("FrontMark");

        if (bodyVisual != null && bodyVisual.TryGetComponent(out MeshRenderer bodyRenderer))
        {
            bodyRenderer.sharedMaterial = SweeperPlayerSetup.BodyMaterial();
        }

        SpaceJunkPlayerSetup setup = root.GetComponent<SpaceJunkPlayerSetup>();
        if (setup != null)
        {
            Renderer[] teamRenderers = frontMark != null && frontMark.TryGetComponent(out MeshRenderer markRenderer)
                ? new Renderer[] { markRenderer }
                : new Renderer[0];
            FishingSceneBuilder.SetObjectArray(setup, "teamRenderers", teamRenderers);
        }
    }

    /// <summary>
    /// ほかの人の体では止める部品を入れ直す（外したフックの部品を抜き、Sweeper の操作の部品を足す）。
    /// 歩く部品（FishingPlayerController）はそのまま残る。
    /// </summary>
    private static void SetOwnerOnlyScripts(GameObject root)
    {
        FishingNetPlayer netPlayer = root.GetComponent<FishingNetPlayer>();
        if (netPlayer == null)
        {
            Debug.LogError("[SweeperPlayer] FishingNetPlayer が見つかりません。通信で動きません。");
            return;
        }

        SerializedObject serialized = new SerializedObject(netPlayer);
        SerializedProperty array = serialized.FindProperty("ownerOnlyScripts");

        List<Object> scripts = new List<Object>();
        for (int i = 0; i < array.arraySize; i++)
        {
            Object script = array.GetArrayElementAtIndex(i).objectReferenceValue;
            if (script != null)
            {
                scripts.Add(script);
            }
        }

        scripts.Add(root.GetComponent<SweeperMover>());
        scripts.Add(root.GetComponent<SweeperAim>());
        scripts.Add(root.GetComponent<SweeperShooter>());

        array.arraySize = scripts.Count;
        for (int i = 0; i < scripts.Count; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = scripts[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DestroyAll<T>(GameObject root) where T : Component
    {
        foreach (T component in root.GetComponentsInChildren<T>(true))
        {
            Object.DestroyImmediate(component);
        }
    }
}
