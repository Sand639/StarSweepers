using System.Collections.Generic;
using UnityEngine;

public class NameDataManager : MonoBehaviour
{
    private const string DefaultCsvResource = "PlayerNames";

    [Header("CSV File")]
    [SerializeField]
    private TextAsset csvFile;

    [Header("Loaded Name Data")]
    [SerializeField]
    private List<string> prefixes = new();

    [SerializeField]
    private List<string> connectors = new();

    [SerializeField]
    private List<string> suffixes = new();

    public IReadOnlyList<string> Prefixes => prefixes;
    public IReadOnlyList<string> Connectors => connectors;
    public IReadOnlyList<string> Suffixes => suffixes;

    private void Awake()
    {
        LoadCSV();
    }

    /// <summary>各列から1つずつ選ぶ。番号はプレイヤーが保存し、全員へ配る。</summary>
    public bool RandomName(out int prefix, out int connector, out int suffix)
    {
        prefix = connector = suffix = -1;
        if (prefixes.Count == 0 || connectors.Count == 0 || suffixes.Count == 0)
        {
            return false;
        }

        prefix = Random.Range(0, prefixes.Count);
        connector = Random.Range(0, connectors.Count);
        suffix = Random.Range(0, suffixes.Count);
        return true;
    }

    /// <summary>CSVに存在する番号だけを受け付ける。</summary>
    public bool IsValidName(int prefix, int connector, int suffix)
    {
        return prefix >= 0 && prefix < prefixes.Count
            && connector >= 0 && connector < connectors.Count
            && suffix >= 0 && suffix < suffixes.Count;
    }

    /// <summary>保存した3つの番号から表示する名前を作る。未設定なら空文字。</summary>
    public string GetName(int prefix, int connector, int suffix)
    {
        return IsValidName(prefix, connector, suffix)
            ? prefixes[prefix] + connectors[connector] + suffixes[suffix]
            : string.Empty;
    }

    public void LoadCSV()
    {
        prefixes.Clear();
        connectors.Clear();
        suffixes.Clear();

        // Inspectorで指定したCSVを優先。検証シーンでも同じ名前一覧を使えるようにする。
        TextAsset source = csvFile != null ? csvFile : Resources.Load<TextAsset>(DefaultCsvResource);
        if (source == null)
        {
            Debug.LogError(
                "[PlayerNameDatabase] CSV file is not assigned.",
                this
            );
            return;
        }

        string[] lines = source.text.Split('\n');

        // First row is the header:
        // Prefix,Connector,Suffix
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] columns = line.Split(',');

            if (columns.Length != 3)
            {
                Debug.LogWarning(
                    $"[PlayerNameDatabase] Invalid CSV row {i + 1}: {line}",
                    this
                );
                continue;
            }

            AddIfValid(prefixes, columns[0]);
            AddIfValid(connectors, columns[1]);
            AddIfValid(suffixes, columns[2]);
        }

        Debug.Log(
            "[PlayerNameDatabase] CSV loaded successfully.\n" +
            $"Prefixes: {prefixes.Count}\n" +
            $"Connectors: {connectors.Count}\n" +
            $"Suffixes: {suffixes.Count}",
            this
        );
    }

    private void AddIfValid(List<string> list, string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
            return;

        list.Add(value);
    }
}
