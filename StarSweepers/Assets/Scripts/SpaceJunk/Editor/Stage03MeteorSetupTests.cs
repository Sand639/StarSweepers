using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Stage03MeteorSetupTests
{
    private const string ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_03.unity";

    [Test]
    public void Stage03_HasEightRingSafeMeteorAreasAndOneManager()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedByTest = !scene.IsValid() || !scene.isLoaded;
        if (openedByTest)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        try
        {
            List<MeteorManager> managers = FindInScene<MeteorManager>(scene);
            List<MeteorArea> areas = FindInScene<MeteorArea>(scene);

            Assert.That(managers, Has.Count.EqualTo(1));
            Assert.That(areas, Has.Count.EqualTo(8));

            SerializedObject manager = new SerializedObject(managers[0]);
            Assert.That(
                manager.FindProperty("impactRadius").floatValue,
                Is.EqualTo(5f).Within(0.0001f));
            Assert.That(
                manager.FindProperty("intervalSeconds").floatValue,
                Is.EqualTo(4f).Within(0.0001f));
            Assert.That(
                manager.FindProperty("warningSeconds").floatValue,
                Is.EqualTo(4f).Within(0.0001f));
            Assert.That(
                manager.FindProperty("meteorsPerRound").intValue,
                Is.EqualTo(3));
            Assert.That(
                manager.FindProperty("spawnAtAreaCenter").boolValue,
                Is.True);

            SerializedProperty activeAreas = manager.FindProperty("activeAreas");
            Assert.That(activeAreas.arraySize, Is.EqualTo(8));

            foreach (MeteorArea area in areas)
            {
                BoxCollider box = area.GetComponent<BoxCollider>();
                Assert.That(box, Is.Not.Null);
                AssertAreaBoundsStayOnRing(area.transform, box);
            }
        }
        finally
        {
            if (openedByTest)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static void AssertAreaBoundsStayOnRing(Transform area, BoxCollider box)
    {
        float halfX = box.size.x * 0.5f;
        float halfZ = box.size.z * 0.5f;
        Vector3[] boundaryPoints =
        {
            new Vector3(-halfX, 0f, -halfZ),
            new Vector3(-halfX, 0f, halfZ),
            new Vector3(halfX, 0f, -halfZ),
            new Vector3(halfX, 0f, halfZ),
            new Vector3(0f, 0f, -halfZ),
            new Vector3(0f, 0f, halfZ),
            new Vector3(-halfX, 0f, 0f),
            new Vector3(halfX, 0f, 0f),
        };

        foreach (Vector3 offset in boundaryPoints)
        {
            Vector3 worldPoint = area.TransformPoint(box.center + offset);
            float radius = new Vector2(worldPoint.x, worldPoint.z).magnitude;
            Assert.That(radius, Is.GreaterThan(9f));
            Assert.That(radius, Is.LessThan(15f));
        }
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
