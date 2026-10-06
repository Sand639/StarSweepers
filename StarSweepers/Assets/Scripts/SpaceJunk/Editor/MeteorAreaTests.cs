using NUnit.Framework;
using UnityEngine;

public class MeteorAreaTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryGetPoint_InsetsWorldRadiusFromScaledRotatedBoxEdges()
    {
        MeteorArea area = CreateArea(
            new Vector3(10f, 0.2f, 8f),
            new Vector3(1f, 0f, -2f),
            new Vector3(2f, 1f, 1f),
            Quaternion.Euler(0f, 30f, 0f));

        bool result = area.TryGetPoint(2f, 0f, 1f, out Vector3 worldPoint);

        Assert.That(result, Is.True);
        Vector3 localPoint = root.transform.InverseTransformPoint(worldPoint);
        Assert.That(localPoint.x, Is.EqualTo(-3f).Within(0.0001f));
        Assert.That(localPoint.y, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(localPoint.z, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void TryGetPoint_ReturnsFalseWhenImpactCircleCannotFit()
    {
        MeteorArea area = CreateArea(
            new Vector3(4f, 0.2f, 3f),
            Vector3.zero,
            Vector3.one,
            Quaternion.identity);

        bool result = area.TryGetPoint(2f, 0.5f, 0.5f, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryGetPoint_ClampsNormalizedCoordinatesInsideSafeRange()
    {
        MeteorArea area = CreateArea(
            new Vector3(10f, 0.2f, 10f),
            Vector3.zero,
            Vector3.one,
            Quaternion.identity);

        Assert.That(area.TryGetPoint(2f, -5f, 8f, out Vector3 point), Is.True);

        Assert.That(point.x, Is.EqualTo(-3f).Within(0.0001f));
        Assert.That(point.z, Is.EqualTo(3f).Within(0.0001f));
    }

    private MeteorArea CreateArea(Vector3 size, Vector3 center, Vector3 scale, Quaternion rotation)
    {
        root = new GameObject("MeteorAreaTest");
        root.transform.localScale = scale;
        root.transform.rotation = rotation;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = size;
        box.center = center;
        box.isTrigger = true;

        return root.AddComponent<MeteorArea>();
    }
}
