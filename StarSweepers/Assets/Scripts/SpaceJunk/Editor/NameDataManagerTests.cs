using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class NameDataManagerTests
{
    private GameObject root;
    private TextAsset csv;
    private NameDataManager database;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("NameDatabaseTest");
        root.SetActive(false);
        database = root.AddComponent<NameDataManager>();
        SetCsv("Prefix,Connector,Suffix\n赤,の,猫\n青,,犬\n緑,,鳥\n");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(csv);
    }

    // 空の RandomName、列ごとの長さの取り違え、最後の候補の取りこぼしを検出する。
    [Test]
    public void RandomName_ReturnsUsableIndicesForUnequalColumnLengths()
    {
        Random.State saved = Random.state;
        try
        {
            Random.InitState(1234);
            bool sawLastPrefix = false;
            bool sawLastSuffix = false;
            for (int i = 0; i < 128; i++)
            {
                object[] indices = { -1, -1, -1 };
                typeof(NameDataManager).GetMethod("RandomName").Invoke(database, indices);
                Assert.That(indices[0], Is.InRange(0, 2), "前の語の番号が返っていない");
                Assert.That(indices[1], Is.EqualTo(0), "中の語は1個だけ");
                Assert.That(indices[2], Is.InRange(0, 2));
                sawLastPrefix |= (int)indices[0] == 2;
                sawLastSuffix |= (int)indices[2] == 2;
            }
            Assert.That(sawLastPrefix && sawLastSuffix, Is.True);
        }
        finally
        {
            Random.state = saved;
        }
    }

    [Test]
    public void RandomName_ReturnsFailureInsteadOfIndexZeroForMissingColumn()
    {
        SetCsv("Prefix,Connector,Suffix\n赤,,猫\n");
        object[] indices = { 99, 99, 99 };
        object success = typeof(NameDataManager).GetMethod("RandomName").Invoke(database, indices);
        Assert.That(success, Is.EqualTo(false));
        Assert.That(indices, Is.EqualTo(new object[] { -1, -1, -1 }));
    }

    [Test]
    public void GetName_JoinsChosenPartsInOrder()
    {
        MethodInfo method = typeof(NameDataManager).GetMethod("GetName");
        Assert.That(method, Is.Not.Null, "番号から名前を組み立てる入口が必要");
        Assert.That(method.Invoke(database, new object[] { 1, 0, 2 }), Is.EqualTo("青の鳥"));
    }

    [TestCase(-1, 0, 0)]
    [TestCase(3, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, 3)]
    public void GetName_InvalidIndicesReturnEmpty(int prefix, int connector, int suffix)
    {
        MethodInfo method = typeof(NameDataManager).GetMethod("GetName");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(database, new object[] { prefix, connector, suffix }), Is.EqualTo(string.Empty));
    }

    [Test]
    public void LoadCsv_UsesBundledDataWhenInspectorReferenceIsEmpty()
    {
        SerializedObject serialized = new SerializedObject(database);
        serialized.FindProperty("csvFile").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        database.LoadCSV();
        Assert.That(database.Prefixes.Count, Is.GreaterThan(0));
        Assert.That(database.Connectors.Count, Is.GreaterThan(0));
        Assert.That(database.Suffixes.Count, Is.GreaterThan(0));
    }

    private void SetCsv(string text)
    {
        if (csv != null) Object.DestroyImmediate(csv);
        csv = new TextAsset(text);
        SerializedObject serialized = new SerializedObject(database);
        serialized.FindProperty("csvFile").objectReferenceValue = csv;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        database.LoadCSV();
    }
}
