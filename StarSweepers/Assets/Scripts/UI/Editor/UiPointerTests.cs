using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>画面のポインター（<see cref="UiPointer"/>）の見た目の設定が、正しくつながっているかを確かめる。</summary>
public class UiPointerTests
{
    [Test]
    public void UiPointerPrefab_IsInResourcesAndUsesTheScopeLens()
    {
        GameObject prefab = Resources.Load<GameObject>(UiPointer.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/UiPointer.prefab が無い（ゲームの開始時に読み込む）");

        UiPointer pointer = prefab.GetComponent<UiPointer>();
        Assert.That(pointer, Is.Not.Null, "UiPointer が付いていない");

        Sprite sprite = new SerializedObject(pointer).FindProperty("pointerSprite").objectReferenceValue as Sprite;
        Assert.That(sprite, Is.Not.Null, "ポインターの絵が入っていない");
        Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo("Assets/Art/Sprites/ScopeLens.png"),
            "ポインターの絵は、遊びの照準と同じスコープレンズ");
    }
}
