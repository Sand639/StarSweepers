using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// **宇宙ごみ集めを確かめるための、Windows用ビルドを作るツール。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみ集めの検証用のビルドを作る」から実行できる。
/// 出力先は `StarSweepers/Build/SpaceJunkTest/`（Gitには入らない場所）。
///
/// ## なぜビルドが要るのか
///
/// **エディタの再生では1人分しか動かない。** チーム戦は確かめられない。
/// `.exe` を作って**いくつも同時に起動する**ことで、1台のPCでも複数人プレイを試せる。
///
/// 宇宙ごみ集めは**最大8人・最大4チーム**なので、
/// `2対2対2対2` や `4対4` を試したいときは8つ起動する `.bat` を使う。
///
/// ## 一緒に作られる .bat
///
/// | ファイル | 何をするか |
/// | --- | --- |
/// | `2人で自動接続.bat` | さっと動きを見たいとき |
/// | `4人で自動接続.bat` | **ふだんはこれ。** 2対2、または1対1対1対1 を試せる |
/// | `8人で自動接続.bat` | 2対2対2対2 や 4対4 を試すとき（窓が小さくなる） |
/// | `4つ起動するだけ.bat` | 合言葉でつなぐ、本番と同じ手順を試すとき |
///
/// ## 遊ぶ前にホストがやること
///
/// **つないだだけでは始まらない。** ホストがロビーの**設定端末に近づいて `E`** を押し、
/// **使うマップを1つ以上選んでから**「ゲーム開始」を押す必要がある。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkBuilder
{
    /// <summary>
    /// ロビーのシーン（タイトルの次に入れる。起動したときに最初に開くのはタイトル）。
    /// このあとに、**マップの一覧（SpaceJunkMapList）に入っているマップ**が全部足される。
    /// </summary>
    private static string LobbyScenePath => SpaceJunkSetup.LobbyScenePath;

    /// <summary>マップのシーンを探す名前の始まり。</summary>
    private const string MapScenePrefix = "SpaceJunkMap";

    /// <summary>シーンを探す場所。</summary>
    private const string SceneSearchFolder = "Assets/Scenes";

    private const string OutputFolder = "Build/SpaceJunkTest";
    private const string ExeName = "SpaceJunkTest.exe";

    /// <summary>
    /// 4つ並べて見られるように、全画面ではなく窓で起動させる。
    /// **960×540 は、1920×1080 の画面にちょうど2×2で並ぶ大きさ。**
    /// </summary>
    private const string WindowArgs4 = "-screen-fullscreen 0 -screen-width 960 -screen-height 540";

    /// <summary>
    /// 8つ並べるとき用。**640×360 なら 1920×1080 に 3×3 で並ぶ**（8つなら1枠余る）。
    ///
    /// 以前は 480×270 にしていたが、**小さすぎて中身が読めない**と報告があったため広げた
    /// （2026/9/20・大槻さん）。
    /// あわせて **Player Settings の Resizable Window を ON** にしたので、
    /// **起動したあとに窓の端をドラッグして好きな大きさに変えられる。**
    /// 画面の表示は窓の大きさに合わせて自動で伸び縮みする。
    /// </summary>
    private const string WindowArgs8 = "-screen-fullscreen 0 -screen-width 640 -screen-height 360";

    [MenuItem("Tools/StarSweepers/宇宙ごみ集めの検証用のビルドを作る")]
    public static void BuildFromMenu()
    {
        if (!File.Exists(LobbyScenePath))
        {
            Debug.LogError(
                $"ロビーのシーンが見つかりません：{LobbyScenePath}\n" +
                "先に `Tools > StarSweepers > 宇宙ごみ集めのシーンを作る（ロビー＋マップ）` を実行してください。");
            return;
        }

        // **どの方角にゴールがあるかを一覧に控えてからビルドする**（一覧はビルドに焼き込まれるので、
        // 古い控えのままだと、ロビーでチーム数に合わないマップを外せない）
        SpaceJunkMapListSetup.RefreshGoalInfo();

        List<string> maps = FindMapScenePaths();

        if (maps.Count == 0)
        {
            Debug.LogError(
                "マップが1つも見つかりません（マップの一覧が空か、まだ作られていません）。\n" +
                "`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で、マップを一覧に足してください。\n" +
                "**マップが無いと、ロビーで「ゲーム開始」を押せません。**");
            return;
        }

        // **タイトルを先頭に置く。** 先頭のシーンが起動時に開く（2026/10/6 からタイトル → ロビー → マップの順）
        string titleScenePath = SpaceJunkSetup.TitleScenePath;
        if (string.IsNullOrEmpty(titleScenePath))
        {
            Debug.LogError(
                $"タイトルのシーン（{SpaceJunkSetup.TitleSceneName}.unity）が見つかりません。\n" +
                "**ゲームはタイトルから始まるので、無いとビルドを作れません。**");
            return;
        }

        List<string> scenes = new List<string> { titleScenePath, LobbyScenePath };
        scenes.AddRange(maps);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(OutputFolder, ExeName),
            target = BuildTarget.StandaloneWindows64,

            // 開発用ビルド。ログが詳しく出るので、つながらないときに原因を追える
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"ビルドに失敗しました：{report.summary.result}");
            return;
        }

        CreateLaunchScripts(OutputFolder);

        // Steam のフレンドの機能用（まだ App ID が無いので、テスト用の 480）。exe の隣に無いと Steam につながらない
        if (File.Exists("steam_appid.txt"))
        {
            File.Copy("steam_appid.txt", Path.Combine(OutputFolder, "steam_appid.txt"), true);
        }

        string mapList = string.Join("\n　　", maps);
        string full = Path.GetFullPath(OutputFolder);

        Debug.Log(
            "宇宙ごみ集めの検証用のビルドができました。\n" + full + "\n\n" +
            $"入れたマップ（{maps.Count} 個）：\n　　{mapList}\n\n" +
            "■ 4人で試す手順\n" +
            "　1. この中の「4人で自動接続.bat」を実行する\n" +
            "　2. 4つの窓が開き、そのまま4人でつながる（合言葉は要らない）\n" +
            "　3. **ホスト（最初に開いた窓）**で、青いカプセルを**設定端末まで歩かせて `E` を押す**\n" +
            "　4. **使うマップを1つ以上選ぶ**（選ばないと「ゲーム開始」が出ない）\n" +
            "　5. チーム分け・何本先取・最大時間を決めて「ゲーム開始」\n" +
            "　6. 全員が1ラウンド目のマップへ移動する\n\n" +
            "■ チーム戦を試すとき\n" +
            "　「8人で自動接続.bat」を使うと、2対2対2対2 や 4対4 を試せる（窓は小さくなる）\n\n" +
            "■ 本番と同じ手順（合言葉）で試すとき\n" +
            "　「4つ起動するだけ.bat」→ 1つ目で部屋を作り、残りは合言葉で参加する");
    }

    /// <summary>
    /// **ビルドに入れるマップを決める。**
    /// マップの一覧（<see cref="SpaceJunkMapList"/>）があればその中身を、一覧の順に返す。
    /// 一覧がまだ無いときだけ、名前が `SpaceJunkMap` で始まるシーンを名前順に返す。
    /// </summary>
    private static List<string> FindMapScenePaths()
    {
        // **マップの一覧があれば、そこに入っているマップを全部入れる**（名前は自由）。
        // 「ロビーに出す」を外したマップも入れるが、ロビーの候補には出ない。
        // 一覧の中身はビルドに焼き込まれるので、**出す・出さないを変えたらビルドし直すこと**
        List<string> listed = SpaceJunkMapListSetup.ScenePaths();

        if (listed.Count > 0)
        {
            return listed;
        }

        // 一覧がまだ無いときだけ、昔どおり名前で拾う
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneSearchFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);

            if (name.StartsWith(MapScenePrefix) && !paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        paths.Sort(string.CompareOrdinal);
        return paths;
    }

    /// <summary>
    /// 起動用の `.bat` を、ビルドの隣に置く。
    ///
    /// **中身は半角英数字だけにする**（日本語を書くと文字化けするため）。
    /// ファイル名は日本語でよい。
    /// </summary>
    private static void CreateLaunchScripts(string outputFolder)
    {
        File.WriteAllText(Path.Combine(outputFolder, "2人で自動接続.bat"),
            AutoConnectScript(2, WindowArgs4));

        File.WriteAllText(Path.Combine(outputFolder, "4人で自動接続.bat"),
            AutoConnectScript(4, WindowArgs4));

        File.WriteAllText(Path.Combine(outputFolder, "8人で自動接続.bat"),
            AutoConnectScript(8, WindowArgs8));

        File.WriteAllText(Path.Combine(outputFolder, "4つ起動するだけ.bat"),
            ManualScript(4, WindowArgs4));
    }

    /// <summary>
    /// **ホスト1つと、参加者（人数-1）を自動でつなぐ `.bat` を組み立てる。**
    ///
    /// ホストを先に立ち上げ、**少し待ってから**参加者を起動する。
    /// 待たないと、ホストの準備が終わる前につなぎに行って失敗する。
    /// </summary>
    private static string AutoConnectScript(int playerCount, string windowArgs)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        text.Append("@echo off\r\n");
        text.Append($"rem Start {playerCount} players on one PC and connect them (1 HOST + {playerCount - 1} CLIENTS).\r\n");
        text.Append("rem Uses LAN loopback (127.0.0.1), so no join code is needed.\r\n");
        text.Append($"start \"HOST\" \"%~dp0{ExeName}\" -host {windowArgs}\r\n");
        text.Append("timeout /t 4 /nobreak >nul\r\n");

        for (int i = 2; i <= playerCount; i++)
        {
            text.Append($"start \"CLIENT {i}\" \"%~dp0{ExeName}\" -client 127.0.0.1 {windowArgs}\r\n");

            if (i < playerCount)
            {
                text.Append("timeout /t 2 /nobreak >nul\r\n");
            }
        }

        text.Append("echo.\r\n");
        text.Append($"echo {playerCount} windows should be open and connected.\r\n");
        text.Append("echo On the HOST window (the first one):\r\n");
        text.Append("echo   1. Walk to the terminal and press E\r\n");
        text.Append("echo   2. Choose at least one map\r\n");
        text.Append("echo   3. Press GAME START\r\n");
        text.Append("pause\r\n");

        return text.ToString();
    }

    /// <summary>画面のボタンで自分でつなぐ `.bat`（本番と同じ手順を試すとき）。</summary>
    private static string ManualScript(int playerCount, string windowArgs)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        text.Append("@echo off\r\n");
        text.Append($"rem Just start {playerCount} players. Connect with the on-screen buttons.\r\n");

        for (int i = 1; i <= playerCount; i++)
        {
            text.Append($"start \"{i}\" \"%~dp0{ExeName}\" {windowArgs}\r\n");

            if (i < playerCount)
            {
                text.Append("timeout /t 3 /nobreak >nul\r\n");
            }
        }

        return text.ToString();
    }
}
