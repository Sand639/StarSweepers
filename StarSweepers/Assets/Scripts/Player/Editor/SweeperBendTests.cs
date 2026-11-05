using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **巻き取り中のしなり（向きを変えたとき）の自動テスト。**（2026/11/4）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と箱1つだけを置いて確かめる。
/// ・向きを変えなければ、光線はまっすぐのままで、物資もまっすぐ手前へ来る（今までどおり）
/// ・途中で 90° 向きを変えると、光線の出る向きは少し遅れて付いていき、物資は前の側へ回り込む。着く時間は変わらない
///
/// 体の向きはテストが手で決める（歩く部品 SweeperMover は止めておく）。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperBendTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Common/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator NoTurn_PullsStraight()
    {
        yield return new EnterPlayMode();
        yield return Run(false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Turn_BendsBeamAndSwingsSupplyToFront()
    {
        yield return new EnterPlayMode();
        yield return Run(true);
        yield return new ExitPlayMode();
    }

    /// <summary>
    /// 中身。**再生を始めてから呼ぶ別の関数にしてある**（同じ関数の中で、あとで使う値をラムダに渡すと、
    /// 再生の切り替えのときにその入れ物が消えて NullReferenceException になるため。SweeperOnlineTests と同じ）。
    /// </summary>
    private static IEnumerator Run(bool turn)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            // 体の向きはテストが決めるので、向きを変える部品は止める
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            // 正面（+Z）の 4m 先に箱（物資）を置く
            box.transform.position = new Vector3(0f, 0.5f, 4f);
            box.AddComponent<Rigidbody>();
            box.AddComponent<HookableObject>();
            yield return Wait(0.5f);

            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            SweeperReel reel = player.GetComponent<SweeperReel>();
            Invoke(shooter, "BeginCharge");
            yield return NextFrame();
            Invoke(shooter, "Fire");
            float until = Time.realtimeSinceStartup + 5f;
            while (shooter.Phase != SweeperShooter.ShotPhase.Reeling && Time.realtimeSinceStartup < until)
            {
                yield return NextFrame();
            }
            Assert.That(shooter.Phase, Is.EqualTo(SweeperShooter.ShotPhase.Reeling),
                $"光線が箱に当たらなかった（段階 {shooter.Phase}・光線の先 {shooter.BeamTip}・箱 {box.transform.position}）");

            float maxBoxYaw = 0f;
            float bendJustAfterTurn = 0f;
            bool turned = false;
            bool bentBeam = false;
            float lastBoxYaw = 0f;
            float lastDistance = 99f;

            // 前へ投げる直前（1.5 秒の少し手前）まで見る
            while (reel.IsActive && reel.Elapsed < 1.4f)
            {
                if (turn && !turned && reel.Elapsed > 0.2f)
                {
                    turned = true;
                    player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    yield return NextFrame();
                    Assert.That(shooter.TryGetBendDirection(out Vector3 justAfter), Is.True);
                    bendJustAfterTurn = Yaw(justAfter);
                    continue;
                }

                Vector3 fromPlayer = box.transform.position - player.transform.position;
                fromPlayer.y = 0f;
                lastBoxYaw = Yaw(fromPlayer);
                lastDistance = fromPlayer.magnitude;
                maxBoxYaw = Mathf.Max(maxBoxYaw, Mathf.Abs(lastBoxYaw));
                bentBeam |= BeamIsBent(player);
                yield return NextFrame();
            }

            Debug.Log($"[SweeperBendTests] turn={turn} 向きを変えた直後のしなりの向き={bendJustAfterTurn:F1}° 最後の物資の向き={lastBoxYaw:F1}° 距離={lastDistance:F2}m 光線が曲がった={bentBeam}");

            if (!turn)
            {
                Assert.That(maxBoxYaw, Is.LessThan(5f), "向きを変えていないのに、物資が横へずれた");
                Assert.That(bentBeam, Is.False, "向きを変えていないのに、光線が曲がった");
            }
            else
            {
                Assert.That(Mathf.Abs(bendJustAfterTurn), Is.LessThan(45f), "しなりが遅れずに、すぐ体の向きになった");
                Assert.That(bentBeam, Is.True, "向きを変えたのに、光線が曲がらなかった");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(lastBoxYaw, 90f)), Is.LessThan(30f), "物資が前（体の向き）の側へ回り込んでいない");
            }

            Assert.That(lastDistance, Is.LessThan(2.5f), "物資が手前まで来ていない（着く時間が変わった）");
        }
        finally
        {
            Object.Destroy(player);
            Object.Destroy(box);
            Object.Destroy(ground);
        }

        yield return null;
    }

    /// <summary>光線（LiquidBeam）が曲がっているか（曲げる量が 5cm より大きいか）。</summary>
    private static bool BeamIsBent(GameObject player)
    {
        LiquidBeam beam = player.GetComponentInChildren<LiquidBeam>();
        if (beam == null)
        {
            return false;
        }

        FieldInfo field = typeof(LiquidBeam).GetField("bendOffset", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((Vector3)field.GetValue(beam)).magnitude > 0.05f;
    }

    private static float Yaw(Vector3 direction)
    {
        return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }

    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, method + " が見つからない");
        info.Invoke(target, null);
    }

    /// <summary>
    /// ゲームのフレームが1つ進むまで待つ。
    /// **画面なしの Unity で動かすテストでは、`yield return null` だけだとゲームのフレームが進まない**（フレーム番号が変わらないまま何十回も戻ってくる）ため。
    /// </summary>
    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
    }

    /// <summary>実際の時間で待つ（テストの中では WaitForSeconds だとゲームのフレームが進まないことがあるため）。</summary>
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
