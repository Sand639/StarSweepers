using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class MeteorStrikeEventTests
{
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

    [Test]
    public void ApplyPlayerImpact_LaunchesAndStunsPlayer()
    {
        GameObject playerObject = new GameObject("Player");
        try
        {
            PlayerStun stun = playerObject.AddComponent<PlayerStun>();
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
