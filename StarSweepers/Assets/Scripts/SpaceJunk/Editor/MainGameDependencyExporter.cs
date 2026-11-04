using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本制作（宇宙ごみ集め）に必要なファイルの一覧を書き出す。新しいリポジトリへ移すときに使う。
/// 起点：SpaceJunk の3フォルダ（シーン・プレハブ・スクリプト）の中身すべて。
/// そこから Unity が参照をたどって（AssetDatabase.GetDependencies）、使っているファイルを集める。
/// ※スクリプト同士の呼び出し（コードの依存）はここでは分からないので、別のスクリプトで補う。
///
/// 使い方（画面なしの Unity）：
///   Unity.exe -batchmode -nographics -quit -projectPath StarSweepers
///     -executeMethod MainGameDependencyExporter.Export -depsOut 書き出し先.txt
/// </summary>
public static class MainGameDependencyExporter
{
    static readonly string[] Roots =
    {
        "Assets/Scenes/Prototype/SpaceJunk",
        "Assets/Prefabs/SpaceJunk",
        "Assets/Scripts/SpaceJunk",
    };

    public static void Export()
    {
        string outPath = GetArg("-depsOut") ?? "main-game-deps.txt";

        var seeds = new HashSet<string>();
        foreach (var guid in AssetDatabase.FindAssets("", Roots))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(path)) seeds.Add(path);
        }

        var all = new HashSet<string>(seeds);
        foreach (var seed in seeds)
        {
            foreach (var dep in AssetDatabase.GetDependencies(seed, true)) all.Add(dep);
        }

        var lines = all.Where(p => p.StartsWith("Assets/")).OrderBy(p => p).ToList();
        File.WriteAllLines(outPath, lines);
        Debug.Log($"[MainGameDependencyExporter] 起点 {seeds.Count} 件 → 依存を含めて {lines.Count} 件を {outPath} に書きました");
    }

    static string GetArg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}
