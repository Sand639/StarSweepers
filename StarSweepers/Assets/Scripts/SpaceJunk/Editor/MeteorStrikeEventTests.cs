using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 隕石が落ちたときの吹き飛ばし（<see cref="MeteorStrikeEvent"/>）を確かめるテスト。
///
/// 吹き飛ばす速さの計算（爆心から外へ・高さは無視・真上なら予備の向き）と、
/// プレイヤーに当たったら「飛ばす＋スタンさせる」の両方が行われるかを見る。
/// どちらも static なので、シーンや通信を用意せずに呼べる。
/// </summary>
public class MeteorStrikeEventTests
{
    /// <summary>
    /// 本物のプレイヤーの代わりに渡す、飛ばされたか・どの速さで飛ばされたかを覚えておくだけの物。
    /// </summary>
    private sealed class RecordingLaunchable : ILaunchable
    {
        public bool WasLaunched { get; private set; }
        public Vector3 Velocity { get; private set; }

        public void Launch(Vector3 velocity)
        {
            WasLaunched = true;
            Velocity = velocity;
        }
    }

    /// <summary>
    /// 爆心から見て (3, 4) の向きにいる相手を、横 10・上 5 で飛ばしたら →
    /// 横は (3, 4) の向きに長さ 10（= (6, 8)）、上は 5 になるはず。相手の高さ（9）は向きに入らない。
    /// </summary>
    [Test]
    public void CalculateKnockbackVelocity_PointsAwayFromImpactOnHorizontalPlane()
    {
        Vector3 velocity = MeteorStrikeEvent.CalculateKnockbackVelocity(
            Vector3.zero,
            new Vector3(3f, 9f, 4f),
            Vector3.forward,
            10f,
            5f);

        Assert.That(velocity.x, Is.EqualTo(6f).Within(0.0001f));
        Assert.That(velocity.y, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(velocity.z, Is.EqualTo(8f).Within(0.0001f));
    }

    /// <summary>
    /// 相手が爆心のちょうど真上にいて、外へ向かう向きが決まらないときは →
    /// 渡した予備の向き（右）へ横 7・上 2 で飛ばすはず。
    /// </summary>
    [Test]
    public void CalculateKnockbackVelocity_UsesFallbackWhenTargetIsAtImpactCenter()
    {
        Vector3 velocity = MeteorStrikeEvent.CalculateKnockbackVelocity(
            new Vector3(2f, 0f, 3f),
            new Vector3(2f, 8f, 3f),
            Vector3.right,
            7f,
            2f);

        Assert.That(velocity.x, Is.EqualTo(7f).Within(0.0001f));
        Assert.That(velocity.y, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(velocity.z, Is.EqualTo(0f).Within(0.0001f));
    }

    /// <summary>
    /// 爆心より 20m 下・2m 手前にいる相手なら →
    /// 高さの差は無視して、手前（-Z）へ横 9・上 4 で飛ばすはず（下向きには飛ばさない）。
    /// </summary>
    [Test]
    public void CalculateKnockbackVelocity_IgnoresVerticalOffsetForDirection()
    {
        Vector3 velocity = MeteorStrikeEvent.CalculateKnockbackVelocity(
            new Vector3(0f, 10f, 0f),
            new Vector3(0f, -10f, -2f),
            Vector3.right,
            9f,
            4f);

        Assert.That(velocity.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(velocity.y, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(velocity.z, Is.EqualTo(-9f).Within(0.0001f));
    }

    /// <summary>
    /// プレイヤーに隕石が当たったら → 渡した速さのまま飛ばされ、
    /// さらに 2 秒のスタン（操作できない状態）になるはず。
    /// </summary>
    [Test]
    public void ApplyPlayerImpact_LaunchesAndStunsPlayer()
    {
        GameObject playerObject = new GameObject("Player");
        try
        {
            PlayerStun stun = playerObject.AddComponent<PlayerStun>();
            // EditMode のテストでは、AddComponent しても Awake が自動では呼ばれない。
            // そこで private の Awake をリフレクションで名前指定して呼び、準備（空の配列など）を済ませる。
            // ※ PlayerStun の Awake の名前を変えたり消したりすると、このテストが壊れる
            typeof(PlayerStun)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(stun, null);
            RecordingLaunchable launchable = new RecordingLaunchable();
            Vector3 velocity = new Vector3(3f, 6f, -4f);

            MeteorStrikeEvent.ApplyPlayerImpact(launchable, stun, velocity, 2f);

            Assert.That(launchable.WasLaunched, Is.True);
            Assert.That(launchable.Velocity, Is.EqualTo(velocity));
            Assert.That(stun.IsStunned, Is.True);
            Assert.That(stun.Remaining, Is.EqualTo(2f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
        }
    }
}
