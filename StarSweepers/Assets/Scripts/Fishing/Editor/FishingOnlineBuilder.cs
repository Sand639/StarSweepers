using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// **釣りのオンライン対戦を確かめるための、Windows用ビルドを作るツール。**
///
/// Unityのメニュー「Tools > StarSweepers > 釣りのオンライン検証用のビルドを作る」から実行できる。
/// 出力先は `StarSweepers/Build/FishingOnlineTest/`（Gitには入らない場所）。
///
/// ## なぜビルドが要るのか
///
/// **エディタの再生では1人分しか動かない。** 4人で遊ぶ形は確かめられない。
/// `.exe` を作って**4つ同時に起動する**ことで、1台のPCでも4人プレイを試せる。
///
/// ## 一緒に作られる .bat
///
/// | ファイル | 何をするか |
/// | --- | --- |
/// | `4人で自動接続.bat` | **これが一番速い。** 4つ起動して、そのまま4人でつながる（合言葉は要らない） |
/// | `4つ起動するだけ.bat` | 4つ起動するだけ。画面のボタンで自分でつなぐ（本番と同じ手順を試すとき） |
/// | `2つ起動するだけ.bat` | 2人でさっと確かめたいとき |
///
/// **既存の `LAN検証用のビルドを作る`（`LanTestBuilder`）とは別物。**
/// あちらは `LanPlayTest.unity` だけをビルドするので、釣りのシーンが入っていない。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingOnlineBuilder
{
    /// <summary>
    /// ビルドに含めるシーン。**最初のものが起動時に開くシーン**になるので、
    /// ロビーを先頭にしておくこと。
    /// このあとに、`FishingMap〜.unity` のマップがすべて足される（`Assets/Scenes` の下ならどこにあってもよい）。
    /// </summary>
    private static readonly string[] ScenePaths =
    {
        // ※ FishingOnlineSetup・FishingMapSetup が作る／持っている場所と同じ。どちらかを変えたらそろえる
        FishingSceneBuilder.SceneFolder + "/FishingLobby.unity",
        FishingSceneBuilder.SceneFolder + "/FishingOnline.unity",
    };

    /// <summary>
    /// ビルドの出力先。**プロジェクトのフォルダ（`StarSweepers/`）から見た場所**
    /// （エディタの「今いるフォルダ」がプロジェクトのフォルダなので、`Assets/…` と同じ書き方で通じる）。
    /// </summary>
    private const string OutputFolder = "Build/FishingOnlineTest";
    private const string ExeName = "FishingOnlineTest.exe";

    /// <summary>
    /// 4つ並べて見られるように、全画面ではなく窓で起動させる。
    ///
    /// **960×540 は、1920×1080 の画面にちょうど2×2で並ぶ大きさ。**
    /// 画面がこれより小さい場合は、`.bat` の数字を直接書き換えてよい
    /// （UI側は窓の大きさに合わせて自動で縮むので、小さくしても押せなくならない）。
    /// </summary>
    private const string WindowArgs = "-screen-fullscreen 0 -screen-width 960 -screen-height 540";

    /// <summary>
    /// ロビー・最初の会場・すべてのマップを入れて Windows 用にビルドし、成功したら起動用の `.bat` を隣に置く。
    /// **Build Settings のシーン一覧は使わず、ここで決めたシーンだけを入れる**（一覧の中身に左右されない）。
    /// </summary>
    [MenuItem("Tools/StarSweepers/釣りのオンライン検証用のビルドを作る")]
    public static void BuildFromMenu()
    {
        if (!CheckScenesExist())
        {
            return;
        }

        // `釣りの新しいマップを作る` で作ったマップ（FishingMap01 など）も一緒に入れる。
        // 入れないと、ロビーでマップを選んでも読み込めない
        List<string> scenes = new List<string>(ScenePaths);
        scenes.AddRange(FishingMapSetup.FindMapScenePaths());

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

        string full = Path.GetFullPath(OutputFolder);
        Debug.Log(
            "釣りのオンライン検証用のビルドができました。\n" + full + "\n\n" +
            "■ 4人で試す手順（一番速い方法）\n" +
            "　1. この中の「4人で自動接続.bat」を実行する\n" +
            "　2. 4つの窓が開き、そのまま4人でつながる（合言葉は要らない）\n" +
            "　3. ホスト（最初に開いた窓）のロビーで「ゲーム開始」を押す\n" +
            "　4. 4つとも釣り会場へ移動する（＝2チームに分かれて対戦）\n\n" +
            "■ 本番と同じ手順（合言葉）で試すとき\n" +
            "　「4つ起動するだけ.bat」→ 1つ目で部屋を作り、残りは合言葉で参加する");
    }

    /// <summary>
    /// ビルドするシーンが実際にあるかを先に確かめる。
    /// **無いまま進むと、原因の分かりにくいビルドエラーになる。**
    /// </summary>
    private static bool CheckScenesExist()
    {
        // マップ（FishingMap〜）は実際にあるシーンを探して足すので、ここでは確かめなくてよい
        foreach (string path in ScenePaths)
        {
            if (!File.Exists(path))
            {
                Debug.LogError(
                    $"シーンが見つかりません：{path}\n" +
                    "先に `Tools > StarSweepers > 釣りのオンライン用シーンを作る（ロビー＋会場）` を実行してください。");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 起動用の `.bat` を、ビルドの隣に置く。
    ///
    /// **中身は半角英数字だけにする**（日本語を書くと文字化けするため）。
    /// ファイル名は日本語でよい。
    ///
    /// 1台のPCで複数起動しても「同じ人」と扱われない仕組みは、
    /// ゲーム側が起動順で自動的に分けてくれる（`インターネットでの複数人プレイ.md`）。
    /// **だから .bat 側では何も指定しなくてよい。**
    /// </summary>
    private static void CreateLaunchScripts(string outputFolder)
    {
        // ① 4人で自動接続（LANの折り返し接続を使う。合言葉が要らないので一番速い）
        //
        //    ホストを先に立ち上げ、少し待ってから参加者を3つ起動する。
        //    待たないと、ホストの準備が終わる前に参加者がつなぎに行って失敗する
        //    （`timeout /t 4` が4秒待つ、という意味。遅いPCでつながらないときは、この数字を大きくする）
        string autoConnect4 =
            "@echo off\r\n" +
            "rem Start 4 players on one PC and connect them (1 HOST + 3 CLIENTS).\r\n" +
            "rem Uses LAN loopback (127.0.0.1), so no join code is needed.\r\n" +
            $"start \"HOST\" \"%~dp0{ExeName}\" -host {WindowArgs}\r\n" +
            "timeout /t 4 /nobreak >nul\r\n" +
            $"start \"CLIENT 2\" \"%~dp0{ExeName}\" -client 127.0.0.1 {WindowArgs}\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"start \"CLIENT 3\" \"%~dp0{ExeName}\" -client 127.0.0.1 {WindowArgs}\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"start \"CLIENT 4\" \"%~dp0{ExeName}\" -client 127.0.0.1 {WindowArgs}\r\n" +
            "echo.\r\n" +
            "echo 4 windows should be open and connected.\r\n" +
            "echo Press \"GAME START\" on the HOST window (the first one).\r\n" +
            "pause\r\n";

        // ② 4つ起動するだけ（画面のボタンで操作する。本番と同じ手順を試すとき）
        string manual4 =
            "@echo off\r\n" +
            "rem Just start 4 players. Connect with the on-screen buttons.\r\n" +
            $"start \"1\" \"%~dp0{ExeName}\" {WindowArgs}\r\n" +
            "timeout /t 3 /nobreak >nul\r\n" +
            $"start \"2\" \"%~dp0{ExeName}\" {WindowArgs}\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"start \"3\" \"%~dp0{ExeName}\" {WindowArgs}\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"start \"4\" \"%~dp0{ExeName}\" {WindowArgs}\r\n";

        // ③ 2つ起動するだけ（さっと確かめたいとき）
        string manual2 =
            "@echo off\r\n" +
            "rem Just start 2 players. Connect with the on-screen buttons.\r\n" +
            $"start \"1\" \"%~dp0{ExeName}\" {WindowArgs}\r\n" +
            "timeout /t 3 /nobreak >nul\r\n" +
            $"start \"2\" \"%~dp0{ExeName}\" {WindowArgs}\r\n";

        File.WriteAllText(Path.Combine(outputFolder, "4人で自動接続.bat"), autoConnect4);
        File.WriteAllText(Path.Combine(outputFolder, "4つ起動するだけ.bat"), manual4);
        File.WriteAllText(Path.Combine(outputFolder, "2つ起動するだけ.bat"), manual2);
    }
}
