using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **新しい体の「重い物を引きずる」と「ボット」の自動テスト。**（2026/11/5）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と箱だけを置いて確かめる。
/// ・重い物（重さ 2）は投げられず、1回で Heavy Drag Distance ÷ 重さ（初期値 4m ÷ 2 = 2m）ほど手前へ引きずられて、その場に置かれる
/// ・ボット（<see cref="SweeperBot"/>）は、素材 → 自分 → ゴールが並ぶ場所まで歩いて撃ち、後ろへ投げる。
///   1回目に投げた物が止まった距離を覚え、2回目はその距離で立って、ゴールの近くに止める
///
/// 作り方は <c>SweeperBendTests</c> と同じ（画面なしの Unity では、待つのに実際の時間とフレーム番号を見る）。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperHeavyBotTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Common/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator HeavySupply_IsDraggedNotThrown()
    {
        yield return new EnterPlayMode();
        yield return RunHeavy();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Bot_ThrowsSupplyTowardGoal()
    {
        yield return new EnterPlayMode();
        yield return RunBot();
        yield return new ExitPlayMode();
    }

    private static IEnumerator RunHeavy()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            // 正面（+Z）の 7m 先に、重さ 2 の箱を置く（大きさは変えない）
            box.transform.position = new Vector3(0f, 0.5f, 7f);
            box.AddComponent<Rigidbody>();
            box.AddComponent<HookableObject>();
            box.AddComponent<HeavyHookable>().Apply(2f, 1f);
            yield return Wait(0.5f);

            Vector3 start = box.transform.position;
            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            SweeperReel reel = player.GetComponent<SweeperReel>();

            // いちばん長く溜めてから撃つ（7m 先まで届くように）
            Invoke(shooter, "BeginCharge");
            SetField(shooter, "charge", 1f);
            yield return NextFrame();
            Invoke(shooter, "Fire");

            yield return WaitFor("光線が重い箱に当たる", () => shooter.Phase == SweeperShooter.ShotPhase.Reeling);
            Assert.That(reel.IsHeavyDrag, Is.True, "重い物なのに、引きずる動きになっていない");

            float maxHeight = box.transform.position.y;
            float until = Time.realtimeSinceStartup + 6f;
            while (reel.IsActive && Time.realtimeSinceStartup < until)
            {
                maxHeight = Mathf.Max(maxHeight, box.transform.position.y);
                yield return NextFrame();
            }

            Assert.That(reel.IsActive, Is.False, "いつまでも引きずり終わらない");
            yield return Wait(0.5f);

            float moved = start.z - box.transform.position.z;
            Debug.Log($"[SweeperHeavyBotTests] 重い箱：結果={reel.LastResult} 引きずった距離={moved:F2}m いちばん高い所={maxHeight:F2}m");

            Assert.That(reel.LastResult, Is.EqualTo("引きずった"));
            Assert.That(maxHeight, Is.LessThan(1.2f), "重い物が投げられた（浮いた）");
            Assert.That(moved, Is.InRange(1.4f, 2.8f), "引きずった距離が、4m ÷ 重さ 2 ＝ 約 2m になっていない");
            Assert.That(shooter.Phase, Is.Not.EqualTo(SweeperShooter.ShotPhase.Reeling), "離したあとも光線が出たまま");
        }
        finally
        {
            Object.Destroy(player);
            Object.Destroy(box);
            Object.Destroy(ground);
        }

        yield return null;
    }

    private static IEnumerator RunBot()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        // ボットはカメラ（Camera.main）を使ってカーソルと歩く向きを決めるので、上から見下ろすカメラを置く
        GameObject cameraObject = new GameObject("TestCamera");
        cameraObject.tag = "MainCamera";
        Camera view = cameraObject.AddComponent<Camera>();
        view.transform.SetPositionAndRotation(new Vector3(0f, 30f, -18f), Quaternion.Euler(55f, 0f, 0f));

        GameObject goal = new GameObject("TestGoal");
        goal.transform.position = new Vector3(0f, 0f, -4f);

        // ボットは右へ 3m ずれた所から歩き始める
        GameObject player = Object.Instantiate(prefab, new Vector3(3f, 0f, 0f), Quaternion.identity);
        GameObject first = MakeSupply(new Vector3(0f, 0.5f, 10f));
        GameObject second = MakeSupply(new Vector3(-2f, 0.5f, 7f)); // 1回で届く距離（中継にならない）に置く
        try
        {
            // 前のテストで覚えた距離を消しておく（ボットみんなで共有している static の値）
            float[] learned = (float[])typeof(SweeperBot).GetField("Learned", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            for (int i = 0; i < learned.Length; i++)
            {
                learned[i] = -1f;
            }

            SweeperBot bot = SweeperBot.AttachTo(player);
            SetField(bot, "goalOverride", goal.transform);
            SetField(bot, "criticalChance", 0f); // 毎回同じ結果にするため、クリティカルは狙わせない

            SweeperReel reel = player.GetComponent<SweeperReel>();
            SweeperShooter shooter = player.GetComponent<SweeperShooter>();

            // 2回投げるまで待つ。1回目は最初の見積もりで、2回目は1回目に覚えた距離で立つ
            int thrown = 0;
            bool wasReeling = false;
            float until = Time.realtimeSinceStartup + 30f;
            while (thrown < 2 && Time.realtimeSinceStartup < until)
            {
                bool reeling = shooter.Phase == SweeperShooter.ShotPhase.Reeling;
                if (wasReeling && !reeling)
                {
                    thrown++;
                    Debug.Log($"[SweeperHeavyBotTests] ボット：{thrown}回目 結果={reel.LastResult} 立った所={player.transform.position:F2}");
                }
                wasReeling = reeling;
                yield return NextFrame();
            }
            Assert.That(thrown, Is.EqualTo(2), "30秒たっても、ボットが2回投げなかった");

            // 落ちて止まるまで待つ
            yield return Wait(3f);

            float firstToGoal = FlatDistance(first.transform.position, goal.transform.position);
            float secondToGoal = FlatDistance(second.transform.position, goal.transform.position);
            Debug.Log($"[SweeperHeavyBotTests] ボット：1回目の箱 {first.transform.position:F2}（ゴールまで {firstToGoal:F2}m）" +
                      $" 2回目の箱 {second.transform.position:F2}（ゴールまで {secondToGoal:F2}m） 覚えた後ろの距離={SweeperBot.LearnedBack:F2}m");

            Assert.That(reel.LastResult, Is.EqualTo("後ろ"), "ボットが後ろへ投げなかった");
            Assert.That(SweeperBot.LearnedBack, Is.GreaterThan(0f), "投げた距離を覚えていない");
            // 箱は落ちたあと転がるので、止まる所は毎回 1〜2m ほどぶれる。ゴールから 3m 以内なら合格にする
            Assert.That(secondToGoal, Is.LessThan(3f), "覚えた距離で投げた2回目の箱が、ゴールの近くに止まらなかった");
        }
        finally
        {
            Object.Destroy(player);
            Object.Destroy(first);
            Object.Destroy(second);
            Object.Destroy(goal);
            Object.Destroy(cameraObject);
            Object.Destroy(ground);
        }

        yield return null;
    }

    /// <summary>
    /// 宇宙ごみの素材の代わりの箱（素材の印 SpaceJunkMaterial が付いていないと、ボットは狙わない）。
    /// 空気の抵抗は、本物の素材のプレハブ（`SpaceJunkPlate.prefab` など）と同じにする。
    /// </summary>
    private static GameObject MakeSupply(Vector3 position)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.transform.position = position;
        Rigidbody body = box.AddComponent<Rigidbody>();
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;
        box.AddComponent<HookableObject>();
        box.AddComponent<SpaceJunkMaterial>();
        return box;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, method + " が見つからない");
        info.Invoke(target, null);
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, field + " が見つからない");
        info.SetValue(target, value);
    }

    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitFor(string label, System.Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "10秒以内に条件がそろわなかった：" + label);
    }
}
