using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **宇宙ごみ集めで使うマップの一覧。**
///
/// ロビーの「使うマップ」の候補と、検証用ビルドに入れるマップは、**ここに書いたものだけ**になる。
///
/// ## 使い方
///
/// メニューの `Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で開き、
/// インスペクターの **Maps** にシーンを足す（Project からドラッグして入れる）。
///
/// | 項目 | 意味 |
/// | --- | --- |
/// | Scene | マップのシーン。**名前も置き場所も自由** |
/// | Show In Lobby | ロビーの候補に出すか。**作りかけのマップは外しておく** |
/// | Display Name | ロビーに出す名前。空ならシーンの名前 |
/// | Thumbnail | ロビーに出すマップの画像。空なら「画像 未設定」と出る |
///
/// **入れたシーンは、ビルドの一覧（Build Profiles）にも自動で登録される**
/// （入っていないと、全員でまとめてシーンを切り替えられないため）。
///
/// ## なぜ一覧にしたのか（2026/9/22・大槻さんの依頼）
///
/// 以前は「名前が `SpaceJunkMap` で始まるシーン」を自動で拾っていた。
/// それだと**名前を自由に付けられず**、作りかけのマップを隠すこともできなかった。
/// </summary>
[CreateAssetMenu(fileName = "SpaceJunkMapList", menuName = "StarSweepers/宇宙ごみのマップの一覧")]
public class SpaceJunkMapList : ScriptableObject
{
    /// <summary>一覧の1行ぶん。マップ1つ。</summary>
    [System.Serializable]
    public class Entry
    {
        [Tooltip("マップのシーン。Project からドラッグして入れる。名前も置き場所も自由")]
        public Object scene;

        [Tooltip("ロビーの「使うマップ」の候補に出すか。作りかけのマップは外しておく")]
        public bool showInLobby = true;

        [Tooltip("ロビーに出す名前。空ならシーンの名前がそのまま出る")]
        public string displayName;

        [Tooltip("ロビーに出すマップの画像（16:9 がおすすめ。Sprite (2D and UI) で読み込む）。空なら「画像 未設定」と出る")]
        public Sprite thumbnail;

        // ---- ゲームを動かしているときに使う控え ----
        //
        // シーンそのもの（SceneAsset）は Unity エディタの中にしか無く、
        // **ビルドしたゲームからは読めない。** そこで、エディタで入れたときに
        // 名前と置き場所を文字として控えておき、ゲームではそちらを使う。

        [SerializeField, HideInInspector] private string sceneName;
        [SerializeField, HideInInspector] private string scenePath;

        /// <summary>
        /// **そのマップにあるゴールの番号**（北=1 / 東=2 / 南=4 / 西=8 を足したもの）。-1 ならまだ調べていない。
        /// シーンの中身はビルドしたゲームから読めないので、エディタで調べて控えておく（2026/9/30）。
        /// ロビーで「そのチーム数では遊べないマップ」を候補から外すのに使う。
        /// </summary>
        [SerializeField, HideInInspector] private int goalMask = -1;

        /// <summary>そのマップにあるゴールの番号（-1 ならまだ調べていない）。</summary>
        public int GoalMask => goalMask;

        /// <summary>そのチーム数で遊べるか（全チームが自分のゴールを持てるか）。まだ調べていなければ遊べる扱い。</summary>
        public bool SupportsTeamCount(int teamCount)
        {
            return SpaceJunkTeams.GoalsSupportTeamCount(goalMask, teamCount);
        }

        /// <summary>遊べるいちばん多いチーム数。まだ調べていなければ最大（4）。</summary>
        public int MaxTeams => goalMask < 0 ? SpaceJunkTeams.MaxTeams : SpaceJunkTeams.MaxPlayableTeams(goalMask);

#if UNITY_EDITOR
        /// <summary>エディタで調べたゴールの番号を控える。</summary>
        public void EditorSetGoalMask(int mask)
        {
            goalMask = mask;
        }
#endif

        /// <summary>シーンの名前（全員でシーンを切り替えるときに使う）。</summary>
        public string SceneName => sceneName;

        /// <summary>シーンの置き場所（`Assets/...unity`）。</summary>
        public string ScenePath => scenePath;

        /// <summary>ロビーに出す名前。</summary>
        public string Label => string.IsNullOrEmpty(displayName) ? sceneName : displayName;

        /// <summary>シーンが入っていて、使える状態か。</summary>
        public bool IsValid => !string.IsNullOrEmpty(sceneName);

#if UNITY_EDITOR
        /// <summary>入れられたシーンから、名前と置き場所の控えを作り直す。</summary>
        public void Refresh()
        {
            string path = scene != null ? UnityEditor.AssetDatabase.GetAssetPath(scene) : string.Empty;

            if (!string.IsNullOrEmpty(path) && !path.EndsWith(".unity"))
            {
                Debug.LogWarning($"[JUNK] マップの一覧に、シーンでない物（{path}）が入っています。外してください。");
                path = string.Empty;
            }

            scenePath = path;
            sceneName = string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>ツールから1行作るとき用。</summary>
        public static Entry Create(Object sceneAsset, bool show)
        {
            Entry entry = new Entry { scene = sceneAsset, showInLobby = show };
            entry.Refresh();
            return entry;
        }
#endif
    }

    [Tooltip("マップの一覧。上から順にロビーの候補に並ぶ")]
    [SerializeField] private List<Entry> maps = new List<Entry>();

    /// <summary>一覧の中身。</summary>
    public IReadOnlyList<Entry> Maps => maps;

    /// <summary>シーンの名前から、その行を探す。無ければ null。</summary>
    public Entry Find(string sceneName)
    {
        foreach (Entry entry in maps)
        {
            if (entry != null && entry.SceneName == sceneName)
            {
                return entry;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    /// <summary>一覧に無ければ足す。**すでにあれば何もしない**（チェックや表示名を上書きしないため）。</summary>
    public bool EditorAdd(Object sceneAsset, bool show)
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);

        foreach (Entry entry in maps)
        {
            if (entry != null && entry.ScenePath == path)
            {
                return false;
            }
        }

        maps.Add(Entry.Create(sceneAsset, show));
        UnityEditor.EditorUtility.SetDirty(this);
        RegisterInBuildSettings();
        return true;
    }

    /// <summary>
    /// インスペクターで中身が変わったとき。
    /// 控えを作り直し、名前の重なりを知らせ、ビルドの一覧に登録する。
    /// </summary>
    private void OnValidate()
    {
        HashSet<string> names = new HashSet<string>();

        foreach (Entry entry in maps)
        {
            if (entry == null)
            {
                continue;
            }

            entry.Refresh();

            // **同じ名前のシーンが2つあると、どちらを読み込むか決まらない**
            // （全員でシーンを切り替えるときは名前で指定するため）
            if (entry.IsValid && !names.Add(entry.SceneName))
            {
                Debug.LogWarning(
                    $"[JUNK] マップの一覧に、同じ名前のシーン「{entry.SceneName}」が2つあります。" +
                    "別のフォルダにあっても、名前が同じだと区別できません。どちらかの名前を変えてください。");
            }
        }

        // インスペクターを触っている最中に設定を書き換えると警告が出ることがあるので、少し後に回す
        UnityEditor.EditorApplication.delayCall += RegisterInBuildSettings;
    }

    /// <summary>
    /// 一覧のシーンを、ビルドの一覧に登録する（入っていないと、全員でシーンを切り替えられない）。
    /// すでに入っていれば何もしない。
    /// </summary>
    public void RegisterInBuildSettings()
    {
        if (this == null)
        {
            return;
        }

        List<UnityEditor.EditorBuildSettingsScene> scenes =
            new List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);

        bool changed = false;

        foreach (Entry entry in maps)
        {
            if (entry == null || !entry.IsValid)
            {
                continue;
            }

            bool found = false;

            foreach (UnityEditor.EditorBuildSettingsScene existing in scenes)
            {
                if (existing.path == entry.ScenePath)
                {
                    found = true;

                    if (!existing.enabled)
                    {
                        existing.enabled = true;
                        changed = true;
                    }
                    break;
                }
            }

            if (!found)
            {
                scenes.Add(new UnityEditor.EditorBuildSettingsScene(entry.ScenePath, true));
                changed = true;
            }
        }

        if (changed)
        {
            UnityEditor.EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[JUNK] マップの一覧に入っているシーンを、ビルドの一覧に登録しました。");
        }
    }
#endif
}
