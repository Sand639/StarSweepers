using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// STAGE_01（土台と上下する床。2026/10/8・大槻さんの設計図）の、**数値を調整しても崩れてはいけない関係**を確かめる。
/// 大きさや高さそのものは調整で変わるので見ない。
///
/// Unity の Test Runner（EditMode）から動かす。シーンが開いていなければ、テストの間だけ裏で開いて、終わったら閉じる。
///
/// ⚠ 部品の値を**フィールドの名前（`riseHeight` / `spawnOnRaisedFloor` / `dropHeight` / `makeHeavy`）を文字で指定して**読んでいる。
/// その部品の側でフィールドを改名すると、このテストが（中身は正しくても）エラーで落ちる。
/// また、土台・床の高さは「箱（Cube）の中心の高さ ± 大きさの半分」で計算しているので、
/// 1m 四方の箱の形を、大きさ（Scale）で伸ばして作っている前提。
/// </summary>
public class Stage01LayoutTests
{
    private const string ScenePath =
        "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_01.unity";

    [Test]
    public void Stage01_LiftFloorsConnectTheGroundAndTheTopOfTheBase()
    {
        Scene scene = OpenStageScene(out bool openedByTest);
        try
        {
            Transform ground = FindByName(scene, "Ground");
            Transform stageBase = FindByName(scene, "Base");
            List<SpaceJunkLiftFloor> lifts = FindInScene<SpaceJunkLiftFloor>(scene);

            Assert.That(ground, Is.Not.Null, "Ground が見つからない");
            Assert.That(stageBase, Is.Not.Null, "土台（Base）が見つからない");
            Assert.That(lifts, Has.Count.EqualTo(4), "上下する床は土台の4辺に1つずつ");

            // 高さはどれも m
            float groundY = ground.position.y;
            float baseTop = TopY(stageBase);
            Assert.That(baseTop, Is.GreaterThan(groundY + 0.3f),
                "土台は、歩いて登れない高さ（段差 0.3m より上）にする");

            foreach (SpaceJunkLiftFloor lift in lifts)
            {
                Transform liftTransform = lift.transform;
                float rise = new SerializedObject(lift).FindProperty("riseHeight").floatValue;
                float halfHeight = liftTransform.lossyScale.y * 0.5f;

                // 下にいるときの上面・上がりきったときの上面・上がりきったときの底面
                float lowTop = liftTransform.position.y + halfHeight;
                float highTop = lowTop + rise;
                float highBottom = liftTransform.position.y - halfHeight + rise;

                // ぴったり同じ高さだと、地面と床の面が重なってちらつく（Zファイティング。2026/10/8 大槻さんの報告）
                Assert.That(lowTop - groundY, Is.GreaterThan(0.001f).And.LessThanOrEqualTo(0.05f),
                    $"{lift.name}: 下にいるときの上面は、地面より少しだけ高い（同じ高さだと面が重なってちらつく）");
                Assert.That(highTop, Is.EqualTo(baseTop).Within(0.01f),
                    $"{lift.name}: 上がりきったときの上面は、土台の上面と同じ高さ（Rise Height を土台に合わせる）");
                Assert.That(highBottom, Is.LessThanOrEqualTo(groundY + 0.01f),
                    $"{lift.name}: 上がりきっても、下にすき間ができない（柱の高さが足りない）");
            }

            // スポナーは1つの前提で、最初に見つかったものだけを見る
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
            CloseIfOpenedByTest(scene, openedByTest);
        }
    }

    [Test]
    public void Stage01_HasOneHeavyDebrisPointOnTheBase()
    {
        Scene scene = OpenStageScene(out bool openedByTest);
        try
        {
            Transform stageBase = FindByName(scene, "Base");
            float baseTop = TopY(stageBase);

            int heavyPoints = 0;
            foreach (SpaceJunkBombPoints points in FindInScene<SpaceJunkBombPoints>(scene))
            {
                if (!new SerializedObject(points).FindProperty("makeHeavy").boolValue)
                {
                    continue;
                }

                // 置き場所は、SpaceJunkBombPoints の子1つ1つ
                heavyPoints++;
                foreach (Transform place in points.transform)
                {
                    Assert.That(place.position.y, Is.GreaterThan(baseTop),
                        $"{place.name}: 重いデブリは土台の上に出す（土台にめり込まない高さ）");
                }
            }

            Assert.That(heavyPoints, Is.EqualTo(1), "真ん中に重いデブリの置き場所が1つ");
        }
        finally
        {
            CloseIfOpenedByTest(scene, openedByTest);
        }
    }

    /// <summary>
    /// STAGE_01 のシーンを返す。もう開いていればそれを使い、開いていなければ裏で（今のシーンに足して）開く。
    /// openedByTest は「このテストが開いたか」。true なら、終わったら <see cref="CloseIfOpenedByTest"/> で閉じる。
    /// </summary>
    private static Scene OpenStageScene(out bool openedByTest)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        openedByTest = !scene.IsValid() || !scene.isLoaded;
        if (openedByTest)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        return scene;
    }

    /// <summary>テストが開いたシーンだけを閉じる（人が開いていたシーンは閉じない）。</summary>
    private static void CloseIfOpenedByTest(Scene scene, bool openedByTest)
    {
        if (openedByTest)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>箱の上面の高さ（m）。中心の高さに、大きさの半分を足す（1m 四方の箱を Scale で伸ばしている前提）。</summary>
    private static float TopY(Transform box)
    {
        return box.position.y + box.lossyScale.y * 0.5f;
    }

    /// <summary>シーンの中から、その名前の物を1つ探す（隠れている物も含める）。同じ名前が複数あれば、先に見つかった方。</summary>
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

    /// <summary>シーンの中から、その部品を全部集める（隠れている物も含める）。</summary>
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
