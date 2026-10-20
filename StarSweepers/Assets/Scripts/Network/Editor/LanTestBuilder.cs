using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// **LAN通信を確かめるための、Windows用ビルドを作るツール。**
///
/// 通信は「2つ以上のゲームを同時に動かす」必要があるため、
/// エディタの再生ボタンだけでは確かめきれない。
/// このツールで .exe を作り、2つ起動して試す。
///
/// Unityのメニュー「Tools > StarSweepers > アーカイブ > 通信の土台 > LAN検証用のビルドを作る」から実行できる。
/// 出力先は `StarSweepers/Build/LanTest/`（Gitには入らない場所）。
///
/// **ビルドと一緒に、2つ起動するための `.bat` も作られる。**
/// `2人で自動接続.bat` を実行すれば、ホストと参加者が1つずつ立ち上がり、
/// そのままつながった状態になる。
///
/// コマンドから動かすときは、引数で出力先を変えられる。
///   -executeMethod LanTestBuilder.BuildFromCommandLine -buildOutput ＜出力先＞
/// </summary>
public static class LanTestBuilder
{
    /// <summary>ビルドに入れるシーン。検証用のこの1つだけ（<see cref="LanPlayTestSetup"/> が作るもの）。</summary>
    private const string ScenePath = "Assets/Scenes/Test/LanPlayTest.unity";

    /// <summary>出力先（プロジェクトのフォルダからの相対）。`-buildOutput` で変えられる。</summary>
    private const string DefaultOutputFolder = "Build/LanTest";

    private const string ExeName = "LanPlayTest.exe";

    // 2つ並べて見られるように、全画面ではなく小さめの窓で起動させる
    private const string WindowArgs = "-screen-fullscreen 0 -screen-width 960 -screen-height 540";

    [MenuItem("Tools/StarSweepers/アーカイブ/通信の土台/LAN検証用のビルドを作る")]
    public static void BuildFromMenu()
    {
        Build(DefaultOutputFolder);
    }

    /// <summary>コマンドから呼ぶ入口。`-buildOutput ＜出力先＞` で場所を指定できる。</summary>
    public static void BuildFromCommandLine()
    {
        string output = DefaultOutputFolder;
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutput")
            {
                output = args[i + 1];
                break;
            }
        }

        Build(output);
    }

    /// <summary>Windows 用にビルドし、成功したら起動用の `.bat` を隣に置く。失敗したらログに理由を出すだけ。</summary>
    private static void Build(string outputFolder)
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = System.IO.Path.Combine(outputFolder, ExeName),
            target = BuildTarget.StandaloneWindows64,

            // 開発用ビルド。ログが詳しく出るので、つながらないときに原因を追える
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            CreateLaunchScripts(outputFolder);

            string full = System.IO.Path.GetFullPath(outputFolder);
            Debug.Log($"LAN検証用のビルドができました。\n{full}\n" +
                      "この中の「2人で自動接続.bat」を実行すると、2つ起動してつながります。");

            // メニューから実行したときは、できたフォルダをそのまま開く
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(options.locationPathName);
            }
        }
        else
        {
            Debug.LogError($"ビルドに失敗しました：{report.summary.result}");
        }
    }

    /// <summary>
    /// 2つ起動するための `.bat` を、ビルドの隣に置く。
    ///
    /// 手で2回ダブルクリックしてもよいが、そのままだと**全画面で起動して並べられない**ので、
    /// 窓の大きさを指定して起動する形にしてある。
    /// </summary>
    private static void CreateLaunchScripts(string outputFolder)
    {
        // ① 起動しただけでつながる版（動作確認はこれが一番速い）
        string autoConnect =
            // .bat の中身は半角英数字だけにする（日本語を書くと文字化けするため）
            "@echo off\r\n" +
            "rem Start two players on one PC and connect them (HOST + CLIENT)\r\n" +
            $"start \"HOST\" \"%~dp0{ExeName}\" -host {WindowArgs}\r\n" +
            "timeout /t 3 /nobreak >nul\r\n" +
            $"start \"CLIENT\" \"%~dp0{ExeName}\" -client 127.0.0.1 {WindowArgs}\r\n";

        // ② 画面のボタンで操作したい版（本番と同じ手順を試すとき）
        //
        //    「同じ人」として扱われる問題は、ゲーム側が起動順で自動的に分けるので
        //    ここでは何も指定しなくてよい
        string manual =
            "@echo off\r\n" +
            "rem Just start two players. Connect with the on-screen buttons.\r\n" +
            $"start \"1\" \"%~dp0{ExeName}\" {WindowArgs}\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"start \"2\" \"%~dp0{ExeName}\" {WindowArgs}\r\n";

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(outputFolder, "2人で自動接続.bat"), autoConnect);

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(outputFolder, "2つ起動するだけ.bat"), manual);
    }
}
