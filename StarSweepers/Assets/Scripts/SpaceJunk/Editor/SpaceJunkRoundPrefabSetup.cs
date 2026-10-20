using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **ラウンドの進行役（SpaceJunkRound）をプレハブにして、宇宙ごみのマップ全部をそのプレハブに置き換えるツール**（2026/10/21）。
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; ラウンドの進行役をプレハブにして全マップを置き換える」から使う。
///
/// ## なぜ要るか
///
/// 前はマップのシーンごとに SpaceJunkRound を直接付けていた（釣りの FishingMatch.prefab を置いてシーン側で付け足したもの、
/// ふつうのオブジェクトに付けたもの が混ざっていた）。これだと「始まりの演出」などの値を試すとき、**マップの数だけ直す**必要があった。
/// プレハブにすると、**`SpaceJunkRound.prefab` の値を1か所変えるだけで、全マップに効く**（大槻さんの依頼）。
///
/// ## やること
///
/// 1. `Assets/Prefabs/SpaceJunk/Online/SpaceJunkRound.prefab` が無ければ作る（NetworkObject ＋ SpaceJunkRound）
/// 2. `Assets/Scenes/Prototype/SpaceJunk/` の下のシーンのうち、SpaceJunkRound があるものを1つずつ開き、
///    今の進行役を**プレハブを置いたものに入れ替える**
///    ・位置・親・並び順・名前・レイヤー・タグ・オン／オフは引き継ぐ
///    ・SpaceJunkRound の値は引き継ぐ（**プレハブと違う値だけ、そのマップの上書きとして残る**）
///    ・ほかの物から進行役への参照、子のオブジェクト、ほかの部品も引き継ぐ
/// 3. 保存する。すでにプレハブに置き換わっているマップは何もしない（何回実行してもよい）
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkRoundPrefabSetup
{
    public const string PrefabPath = "Assets/Prefabs/SpaceJunk/Online/SpaceJunkRound.prefab";

    [MenuItem("Tools/StarSweepers/ラウンドの進行役をプレハブにして全マップを置き換える")]
    public static void ReplaceAllMaps()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        string previousScene = SceneManager.GetActiveScene().path;

        GameObject prefab = EnsurePrefab();
        if (prefab == null)
        {
            return;
        }

        string scriptGuid = ScriptGuid();
        List<string> report = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SpaceJunkSetup.MapSceneFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 開く前に、ファイルの中に SpaceJunkRound があるかだけ見る（無いシーンは開かない）
            if (string.IsNullOrEmpty(scriptGuid) || !File.ReadAllText(path).Contains(scriptGuid))
            {
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            report.Add($"{scene.name}：{ReplaceInScene(scene, prefab)}");
        }

        if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        Debug.Log("[JUNK] ラウンドの進行役をプレハブに置き換えました。\n" + string.Join("\n", report));
    }

    /// <summary>バッチモード（コマンドから Unity を動かすとき）用。終わったら Unity を閉じる。</summary>
    public static void ReplaceAllMapsBatch()
    {
        ReplaceAllMaps();
        AssetDatabase.SaveAssets();
    }

    /// <summary>プレハブを読み込む。無ければ作る。</summary>
    public static GameObject EnsurePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            return prefab;
        }

        string folder = Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError($"[JUNK] {folder} がありません。プレハブを作れませんでした。");
            return null;
        }

        GameObject temp = new GameObject("SpaceJunkRound");
        temp.AddComponent<NetworkObject>();
        temp.AddComponent<SpaceJunkRound>();

        prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
        Object.DestroyImmediate(temp);

        Debug.Log($"[JUNK] {PrefabPath} を作りました。");
        return prefab;
    }

    /// <summary>
    /// 開いているシーンの進行役を、プレハブを置いたものに入れ替えて保存する。結果を一言で返す。
    /// </summary>
    public static string ReplaceInScene(Scene scene, GameObject prefab)
    {
        SpaceJunkRound[] rounds = Object.FindObjectsByType<SpaceJunkRound>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (rounds.Length != 1)
        {
            return $"進行役が {rounds.Length} 個あるので触らなかった（1個のときだけ置き換える）";
        }

        GameObject old = rounds[0].gameObject;
        if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(old) == prefab &&
            PrefabUtility.GetOutermostPrefabInstanceRoot(old) == old)
        {
            return "もうプレハブになっている（何もしない）";
        }

        GameObject created = ReplaceWithPrefab(old, prefab);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"置き換えた（{created.name}）";
    }

    /// <summary>
    /// <paramref name="old"/> を、<paramref name="prefab"/> を置いたものに入れ替える。
    /// 位置・値・参照・子・ほかの部品を引き継ぎ、古い物は消す。
    /// </summary>
    public static GameObject ReplaceWithPrefab(GameObject old, GameObject prefab)
    {
        // ほかのプレハブの中の一部だと消せないので、先に切り離す（元のプレハブ自体は変わらない）
        GameObject outerRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(old);
        if (outerRoot != null && outerRoot != old)
        {
            PrefabUtility.UnpackPrefabInstance(outerRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        Transform oldTransform = old.transform;
        GameObject created = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
        Transform createdTransform = created.transform;

        // 置き場所
        createdTransform.SetParent(oldTransform.parent, false);
        createdTransform.SetSiblingIndex(oldTransform.GetSiblingIndex());
        createdTransform.localPosition = oldTransform.localPosition;
        createdTransform.localRotation = oldTransform.localRotation;
        createdTransform.localScale = oldTransform.localScale;
        created.name = old.name;
        created.layer = old.layer;
        created.tag = old.tag;

        // 部品の値（プレハブと違う値だけ、上書きとして残る）
        Dictionary<Object, Object> map = new Dictionary<Object, Object> { { old, created } };
        map[oldTransform] = createdTransform;

        Dictionary<System.Type, int> seen = new Dictionary<System.Type, int>();
        foreach (Component component in old.GetComponents<Component>())
        {
            if (component == null || component is Transform)
            {
                continue;
            }

            System.Type type = component.GetType();
            seen.TryGetValue(type, out int index);
            seen[type] = index + 1;

            Component[] same = created.GetComponents(type);
            Component target = index < same.Length ? same[index] : created.AddComponent(type);
            CopyValues(component, target);
            map[component] = target;
        }

        // 子のオブジェクトを移す
        List<Transform> children = new List<Transform>();
        foreach (Transform child in oldTransform)
        {
            children.Add(child);
        }

        foreach (Transform child in children)
        {
            child.SetParent(createdTransform, true);
        }

        // ほかの物から古い進行役への参照を、新しいほうへ付け替える
        RedirectReferences(old.scene, old, map);

        created.SetActive(old.activeSelf);
        Object.DestroyImmediate(old);
        return created;
    }

    /// <summary>見えている値を写す。NetworkObject の番号（Hash）は、置いた物ごとに決まるので写さない。</summary>
    private static void CopyValues(Component from, Component to)
    {
        SerializedObject source = new SerializedObject(from);
        SerializedObject target = new SerializedObject(to);

        SerializedProperty property = source.GetIterator();
        bool enter = true;
        while (property.NextVisible(enter))
        {
            enter = false;
            if (property.name == "m_Script" || property.name.Contains("Hash"))
            {
                continue;
            }

            target.CopyFromSerializedProperty(property);
        }

        target.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>シーンの中の参照のうち、古い進行役（とその部品）を指しているものを、新しいほうへ付け替える。</summary>
    private static void RedirectReferences(Scene scene, GameObject old, Dictionary<Object, Object> map)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.gameObject == old)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool changed = false;

                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue != null &&
                        map.TryGetValue(property.objectReferenceValue, out Object replacement))
                    {
                        property.objectReferenceValue = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[JUNK] {scene.name}：{component.gameObject.name} の {component.GetType().Name} の参照を新しい進行役へ付け替えました。");
                }
            }
        }
    }

    /// <summary>SpaceJunkRound.cs の番号（シーンのファイルの中では、この番号で部品が書かれている）。</summary>
    private static string ScriptGuid()
    {
        foreach (string guid in AssetDatabase.FindAssets("SpaceJunkRound t:MonoScript"))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == typeof(SpaceJunkRound))
            {
                return guid;
            }
        }

        Debug.LogError("[JUNK] SpaceJunkRound.cs が見つかりませんでした。");
        return string.Empty;
    }
}
