using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// STAGE_01（土台と上下する床。2026/10/8・大槻さんの設計図）の、**数値を調整しても崩れてはいけない関係**を確かめる。
/// 大きさや高さそのものは調整で変わるので見ない。
/// </summary>
public class Stage01LayoutTests
{
    private const string ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_01.unity";

    [Test]
    public void Stage01_LiftFloorsConnectTheGroundAndTheTopOfTheBase()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedByTest = !scene.IsValid() || !scene.isLoaded;
        if (openedByTest)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        try
        {
            Transform ground = FindByName(scene, "Ground");
            Transform stageBase = FindByName(scene, "Base");
            List<SpaceJunkLiftFloor> lifts = FindInScene<SpaceJunkLiftFloor>(scene);

            Assert.That(ground, Is.Not.Null, "Ground が見つからない");
            Assert.That(stageBase, Is.Not.Null, "土台（Base）が見つからない");
            Assert.That(lifts, Has.Count.EqualTo(4), "上下する床は土台の4辺に1つずつ");

            float groundY = ground.position.y;
            float baseTop = stageBase.position.y + stageBase.lossyScale.y * 0.5f;
            Assert.That(baseTop, Is.GreaterThan(groundY + 0.3f),
                "土台は、歩いて登れない高さ（段差 0.3m より上）にする");

            foreach (SpaceJunkLiftFloor lift in lifts)
            {
                Transform t = lift.transform;
                float rise = new SerializedObject(lift).FindProperty("riseHeight").floatValue;
                float halfHeight = t.lossyScale.y * 0.5f;
                float lowTop = t.position.y + halfHeight;
                float highTop = lowTop + rise;
                float highBottom = t.position.y - halfHeight + rise;

                Assert.That(lowTop, Is.EqualTo(groundY).Within(0.01f),
                    $"{lift.name}: 下にいるときの上面は、地面と同じ高さ");
                Assert.That(highTop, Is.EqualTo(baseTop).Within(0.01f),
                    $"{lift.name}: 上がりきったときの上面は、土台の上面と同じ高さ（Rise Height を土台に合わせる）");
                Assert.That(highBottom, Is.LessThanOrEqualTo(groundY + 0.01f),
                    $"{lift.name}: 上がりきっても、下にすき間ができない（柱の高さが足りない）");
            }

            SpaceJunkSpawner spawner = FindInScene<SpaceJunkSpawner>(scene)[0];
            SerializedObject spawnerObject = new SerializedObject(spawner);
            Assert.That(spawnerObject.FindProperty("spawnOnRaisedFloor").boolValue, Is.True,
                "土台の上にも物資を出す");
            Assert.That(baseTop - groundY,
                Is.LessThan(spawnerObject.FindProperty("dropHeight").floatValue),
                "土台は、スポナーの Drop Height より低くする");
        }
        finally
        {
            if (openedByTest)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static Transform FindByName(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                {
                    return child;
                }
            }
        }
        return null;
    }

    private static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        List<T> result = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            result.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return result;
    }
}
