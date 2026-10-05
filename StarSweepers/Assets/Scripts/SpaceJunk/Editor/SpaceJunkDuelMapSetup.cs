using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **東と西のゴールだけの、1対1（2チーム）用のマップを作るツール**（2026/9/30・大槻さんの依頼）。
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみの東西1対1のマップを作る（Map01を複製）」から使う。
///
/// ## やること
///
/// 1. `SpaceJunkMap01` を Unity に複製させて `SpaceJunkMapDuelEW` を作る（通信の番号も付け直される）
/// 2. **北と南のゴールを取り除く**（東と西だけ残す）
/// 3. ビルドの一覧とマップの一覧に入れる（ロビーでの名前は「1対1（東西）」）
/// 4. マップの点検を走らせる
///
/// 2チームのときは、北と南が無いマップでは**東＝1チーム目（青）・西＝2チーム目（赤）**になる
/// （<see cref="SpaceJunkTeams.GoalOwners(int, int)"/>）。
/// ゴールが2つなので、**3チーム以上ではロビーの候補に出ない。**
///
/// **すでにあれば作り直さない**（手で直した地形が消えないように）。
/// 作り直したいときは、そのシーンを消してからもう一度実行する。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkDuelMapSetup
{
    private const string SourcePath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMap01.unity";
    public const string DestinationPath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMapDuelEW.unity";

    /// <summary>ロビーに出す名前。</summary>
    private const string DisplayName = "1対1（東西）";

    /// <summary>残すゴールの番号（1=東、3=西）。</summary>
    private static readonly int[] KeepGoals = { 1, 3 };

    [MenuItem("Tools/StarSweepers/アーカイブ/宇宙ごみ/東西1対1のマップを作る（Map01を複製）")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourcePath) == null)
        {
            Debug.LogError(
                $"[JUNK] 複製元のマップが見つかりません：{SourcePath}\n" +
                "先に `Tools > StarSweepers > 宇宙ごみ集めのシーンを作る（ロビー＋マップ）` を実行してください。");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DestinationPath) != null)
        {
            Debug.LogWarning(
                $"[JUNK] {DestinationPath} はもうあるので、作り直しませんでした（手で直した地形が消えないようにするため）。\n" +
                "作り直したいときは、そのシーンを消してからもう一度実行してください。");
            return;
        }

        // **テキストとしてコピーせず、Unity に複製させる**（通信の番号を新しいシーン用に付け直すため）
        if (!AssetDatabase.CopyAsset(SourcePath, DestinationPath))
        {
            Debug.LogError($"[JUNK] シーンを複製できませんでした：{SourcePath} → {DestinationPath}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(DestinationPath, OpenSceneMode.Single);

        List<string> removed = RemoveGoalsExcept(KeepGoals);

        SpaceJunkFixedCameraSetup.RefreshNetworkIds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, DestinationPath);

        SpaceJunkSetup.RegisterScenesInBuildSettings(new List<string> { DestinationPath });

        // マップの一覧に入れ、ロビーでの名前を付ける
        SpaceJunkMapListSetup.AddMap(DestinationPath, true);
        SetDisplayName(DestinationPath, DisplayName);
        SpaceJunkMapListSetup.RefreshGoalInfo();

        SpaceJunkSetup.VerifyMap(DestinationPath);

        AssetDatabase.SaveAssets();

        Debug.Log(
            "【宇宙ごみ】東西1対1のマップを作りました。\n" +
            $"マップ：{DestinationPath}（ロビーでは「{DisplayName}」）\n" +
            $"取り除いたゴール：{(removed.Count > 0 ? string.Join("・", removed) : "なし")}\n\n" +
            "・2チームのとき、東＝青チーム・西＝赤チームのゴールになる\n" +
            "・ゴールが2つなので、3チーム以上ではロビーの候補に出ない\n" +
            "・地形は Map01 のまま。北と南のゴールがあった場所は、必要ならふさぐ・作り変えること");
    }

    /// <summary>残す番号以外のゴールを取り除く。取り除いたゴールの方角を返す。</summary>
    private static List<string> RemoveGoalsExcept(int[] keep)
    {
        List<string> removed = new List<string>();
        HashSet<int> keepSet = new HashSet<int>(keep);

        foreach (SpaceJunkGoal goal in Object.FindObjectsByType<SpaceJunkGoal>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (goal == null || keepSet.Contains(goal.GoalIndex))
            {
                continue;
            }

            removed.Add(SpaceJunkTeams.GoalPlaceName(goal.GoalIndex));

            // ゴールはプレハブとして置かれていることがある。**プレハブの一部だと消せないので、先に切り離す**
            GameObject target = goal.gameObject;
            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(target);

            if (prefabRoot != null && prefabRoot != target)
            {
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Object.DestroyImmediate(target);
        }

        return removed;
    }

    /// <summary>マップの一覧で、そのマップのロビーでの名前を付ける（空のときだけ。手で付けた名前は変えない）。</summary>
    private static void SetDisplayName(string scenePath, string displayName)
    {
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(SpaceJunkMapListSetup.ListPath);

        if (list == null)
        {
            return;
        }

        foreach (SpaceJunkMapList.Entry entry in list.Maps)
        {
            if (entry != null && entry.ScenePath == scenePath && string.IsNullOrEmpty(entry.displayName))
            {
                entry.displayName = displayName;
                EditorUtility.SetDirty(list);
            }
        }
    }
}
