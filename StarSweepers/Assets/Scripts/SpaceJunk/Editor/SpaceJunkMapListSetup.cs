using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **宇宙ごみのマップの一覧（<see cref="SpaceJunkMapList"/>）を用意して、ロビーに結びつける係。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみのマップの一覧を開く」から使う。
///
/// ## やること
///
/// 1. 一覧が無ければ作る（`Assets/Scenes/Prototype/SpaceJunk/SpaceJunkMapList.asset`）
///    - **最初に作るときだけ**、いまある `SpaceJunkMap〜` のシーンを入れておく
///    - **Map03・Map04 は作りかけなので「ロビーに出す」を外して入れる**（2026/9/22・大槻さん）
/// 2. ロビーのシーンの画面（`SpaceJunkLobbyUI`）に、この一覧を結びつける
/// 3. 一覧を選んだ状態にして、インスペクターに出す
///
/// ほかのツール（マップを作るツール・全体固定カメラのマップを作るツール）も、
/// ここの <see cref="AddMap"/> で作ったマップを一覧に足している。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkMapListSetup
{
    /// <summary>マップの一覧のファイルの置き場所（1つだけ。ロビー・ビルド・各ツールがここを見る）。</summary>
    public const string ListPath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMapList.asset";

    /// <summary>最初に一覧を作るとき、ロビーに出さずに入れるマップ（作りかけのもの）。拡張子なしのシーン名で書く。</summary>
    private static readonly string[] HiddenAtFirst = { "SpaceJunkMap03", "SpaceJunkMap04" };

    /// <summary>
    /// メニューから一覧を開く。無ければ作り、ロビーに結びつけ、ゴールの方角を控え直してから、インスペクターに出す。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのマップの一覧を開く")]
    public static void Open()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[JUNK] 再生を止めてから実行してください（ロビーのシーンを書き換えるため）。");
            return;
        }

        SpaceJunkMapList list = EnsureList();
        WireLobby(list);

        // どの方角にゴールがあるかを控える（ロビーでチーム数に合わないマップを外すため）
        RefreshGoalInfo();

        Selection.activeObject = list;
        EditorGUIUtility.PingObject(list);

        Debug.Log(
            "【宇宙ごみ】マップの一覧を開きました。\n" +
            $"場所：{ListPath}\n\n" +
            "・インスペクターの Maps に、シーンを Project からドラッグして足す（名前・置き場所は自由）\n" +
            "・Show In Lobby を外すと、ロビーの候補に出なくなる（作りかけのマップ用）\n" +
            "・Display Name を入れると、ロビーではその名前で出る\n" +
            "・入れたシーンは、ビルドの一覧にも自動で登録される");
    }

    /// <summary>
    /// 一覧を読み込む。**無ければ作る。**
    /// 作るときは、いまある `SpaceJunkMap〜` のシーンを入れておく（それまでと同じ候補になるように）。
    /// </summary>
    /// <param name="wireLobby">
    /// 新しく作ったときに、ロビーにも結びつけるか。
    /// **ロビーを作っている最中のツールは false にする**（作り終えたロビーに自分で結びつけるため）。
    /// </param>
    public static SpaceJunkMapList EnsureList(bool wireLobby = true)
    {
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(ListPath);

        if (list != null)
        {
            return list;
        }

        FishingSceneBuilder.EnsureFolder(SpaceJunkSetup.MapSceneFolder);

        list = ScriptableObject.CreateInstance<SpaceJunkMapList>();
        AssetDatabase.CreateAsset(list, ListPath);

        foreach (string path in FindExistingMaps())
        {
            list.EditorAdd(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), DefaultShow(path));
        }

        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();

        // **作ったらすぐロビーに結びつける。** 結びつけないと、ロビーは昔の見分け方
        // （名前が SpaceJunkMap で始まるもの）のままになり、一覧のチェックが効かない
        if (wireLobby)
        {
            WireLobby(list);
        }

        Debug.Log($"[JUNK] マップの一覧を作りました：{ListPath}（{list.Maps.Count} 個。Map03・Map04 はロビーに出さない設定で入れた）");
        return list;
    }

    /// <summary>
    /// 一覧に初めて入れるとき、ロビーに出すかどうかの初期値。
    /// 作りかけのマップ（<see cref="HiddenAtFirst"/>）だけ出さない。
    /// </summary>
    private static bool DefaultShow(string scenePath)
    {
        // HiddenAtFirst に名前が無ければ（IndexOf が -1 なら）出す
        return System.Array.IndexOf(HiddenAtFirst, Path.GetFileNameWithoutExtension(scenePath)) < 0;
    }

    /// <summary>
    /// **マップを一覧に足す。** すでに入っていれば何もしない（チェックや表示名を上書きしない）。
    /// マップを作るツールから呼ばれる。ロビーに出すかどうかは <see cref="DefaultShow"/> で決まる。
    /// </summary>
    public static void AddMap(string scenePath)
    {
        AddMap(scenePath, DefaultShow(scenePath));
    }

    /// <summary>マップを一覧に足す（ロビーに出すかどうかを指定する版）。</summary>
    public static void AddMap(string scenePath, bool showInLobby)
    {
        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

        if (scene == null)
        {
            return;
        }

        SpaceJunkMapList list = EnsureList();

        if (list.EditorAdd(scene, showInLobby))
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[JUNK] マップの一覧に足しました：{Path.GetFileNameWithoutExtension(scenePath)}");
        }
    }

    /// <summary>
    /// 一覧に入っているシーンの置き場所（ビルドに入れるため）。一覧の順のまま、消えたシーンと重複は除く。
    /// 呼ぶたびに新しいリストを作って返すので、受け取った側で足したり消したりしてよい。
    /// </summary>
    public static List<string> ScenePaths()
    {
        List<string> paths = new List<string>();
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(ListPath);

        if (list == null)
        {
            return paths;
        }

        foreach (SpaceJunkMapList.Entry entry in list.Maps)
        {
            if (entry != null && entry.IsValid && File.Exists(entry.ScenePath) && !paths.Contains(entry.ScenePath))
            {
                paths.Add(entry.ScenePath);
            }
        }

        return paths;
    }

    /// <summary>
    /// **一覧のマップを1つずつ開いて、どの方角にゴールがあるかを控える**（2026/9/30）。
    ///
    /// ロビーは、これを見て「そのチーム数では遊べないマップ」を候補から外す。
    /// シーンの中身はビルドしたゲームから読めないので、ここ（エディタ）で調べておく必要がある。
    /// いま開いているシーンは閉じない（裏で開いて、調べたら閉じる）。
    ///
    /// 一覧を開いたとき・マップを点検したとき・検証用ビルドを作るとき・マップを作ったときに自動で呼ばれる。
    /// **ゴールを足したり消したりしたら、どれか1つを実行し直すこと**（控えが古いままになるため）。
    /// </summary>
    public static void RefreshGoalInfo()
    {
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(ListPath);

        if (list == null)
        {
            return;
        }

        bool changed = false;

        foreach (SpaceJunkMapList.Entry entry in list.Maps)
        {
            if (entry == null || !entry.IsValid || !File.Exists(entry.ScenePath))
            {
                continue;
            }

            int mask = ReadGoalMask(entry.ScenePath);

            if (mask != entry.GoalMask)
            {
                entry.EditorSetGoalMask(mask);
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log("[JUNK] マップの一覧に、各マップのゴールの場所を控えました（ロビーでチーム数に合わないマップを外すため）。");
        }
    }

    /// <summary>そのシーンにあるゴールの番号（北=1 / 東=2 / 南=4 / 西=8 を足したもの）。</summary>
    private static int ReadGoalMask(string scenePath)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;

        if (openedHere)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        int mask = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (SpaceJunkGoal goal in root.GetComponentsInChildren<SpaceJunkGoal>(true))
            {
                // 番号が範囲の外のゴールは数えない（点検 SpaceJunkSetup.VerifyMap がエラーで知らせる）
                if (goal.GoalIndex >= 0 && goal.GoalIndex < SpaceJunkTeams.GoalCount)
                {
                    mask |= 1 << goal.GoalIndex;
                }
            }
        }

        if (openedHere)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        return mask;
    }

    /// <summary>ゴールの控えを、人が読める形にする（例：「ゴール番号 0・1（2個） → 2チームまで」）。</summary>
    public static string DescribeGoals(int mask)
    {
        List<string> places = new List<string>();

        for (int i = 0; i < SpaceJunkTeams.GoalCount; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                places.Add(i.ToString());
            }
        }

        int maxTeams = SpaceJunkTeams.MaxPlayableTeams(mask);
        string placeText = places.Count > 0 ? string.Join("・", places) : "なし";

        return maxTeams > 0 ? $"ゴール番号 {placeText}（{places.Count}個） → {maxTeams}チームまで" : $"ゴール番号 {placeText} → 遊べない";
    }

    /// <summary>
    /// **ロビーのシーンの画面に、一覧を結びつける。**
    ///
    /// ロビーが開いていなければ、裏で開いて書き換えてから閉じる（いま開いているシーンは変えない）。
    ///
    /// ⚠ 結びつける先を **`SpaceJunkLobbyUI` のフィールド名 `mapList`（文字）** で指定している。
    /// そのフィールドを改名すると、警告が出るだけで結びつかなくなる（ロビーが一覧を使えない）。
    /// </summary>
    public static void WireLobby(SpaceJunkMapList list)
    {
        if (list == null || !File.Exists(SpaceJunkSetup.LobbyScenePath))
        {
            return;
        }

        Scene lobby = SceneManager.GetSceneByPath(SpaceJunkSetup.LobbyScenePath);
        bool wasOpen = lobby.isLoaded;

        if (!wasOpen)
        {
            lobby = EditorSceneManager.OpenScene(SpaceJunkSetup.LobbyScenePath, OpenSceneMode.Additive);
        }

        int wired = 0;

        foreach (GameObject root in lobby.GetRootGameObjects())
        {
            foreach (SpaceJunkLobbyUI ui in root.GetComponentsInChildren<SpaceJunkLobbyUI>(true))
            {
                FishingSceneBuilder.SetRef(ui, "mapList", list);
                wired++;
            }
        }

        if (wired > 0)
        {
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
        }
        else
        {
            Debug.LogWarning("[JUNK] ロビーのシーンに SpaceJunkLobbyUI が見つからず、マップの一覧を結びつけられませんでした。");
        }

        if (!wasOpen)
        {
            EditorSceneManager.CloseScene(lobby, true);
        }
    }

    /// <summary>`SpaceJunkMap〜` という名前の既存のシーン（一覧を最初に作るときだけ使う）。</summary>
    private static List<string> FindExistingMaps()
    {
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetFileNameWithoutExtension(path).StartsWith("SpaceJunkMap") && !paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        paths.Sort(string.CompareOrdinal);
        return paths;
    }
}
