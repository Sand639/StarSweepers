using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// STAGE_03 に置いた隕石攻撃の設定が、`隕石攻撃.md` の「STAGE_03への設置」のとおりになっているかを確かめるテスト。
///
/// ドーナツ形の床（内周の半径 9m・外周の半径 15m）の上に落下範囲が8か所あり、
/// 管理役が1つだけで、間隔や個数などの数値が決めた値のままかを見る。
/// シーンを開いて中身を読むだけで、シーンは保存しない。
/// </summary>
public class Stage03MeteorSetupTests
{
    private const string ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_03.unity";

    /// <summary>
    /// STAGE_03 を開いたら → 管理役（MeteorManager）が1つ、落下範囲（MeteorArea）が8つあり、
    /// 爆風の半径 5m・4秒ごと・予告4秒・1回に3個・範囲の中央に落とす、の設定で、
    /// どの範囲もドーナツ床の上からはみ出していないはず。
    /// </summary>
    [Test]
    public void Stage03_HasEightRingSafeMeteorAreasAndOneManager()
    {
        // すでにエディタで開いているならそれを使い、開いていなければ追加で開く。
        // 自分で開いたときだけ最後に閉じ、作業中のシーンを勝手に閉じないようにする
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

            // 設定値は private なので、インスペクターと同じ読み方（SerializedObject）で名前を指定して読む。
            // ※ MeteorManager の項目名（impactRadius など）を変えると、このテストが壊れる
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

    /// <summary>
    /// 落下範囲の四隅と各辺の真ん中（計8点）が、どれもドーナツ床の上にあるかを確かめる。
    /// ドーナツの中心はワールドの原点として、横方向の距離が 9m より大きく 15m より小さければ床の上とみなす
    /// （高さは見ない）。
    /// </summary>
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

    /// <summary>
    /// 指定したシーンの中から、部品をすべて集める。
    /// 他に開いているシーンの物を混ぜないよう、そのシーンのルートからたどる（非表示の物も含める）。
    /// </summary>
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
