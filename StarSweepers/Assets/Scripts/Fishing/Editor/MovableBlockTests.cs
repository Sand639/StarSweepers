using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MovableBlockTests
{
    private const string PrefabPath = "Assets/Scenes/Test/Sou/MovableBlock.prefab";
    private const string PullAblePlatePrefabPath = "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlate.prefab";

    [Test]
    public void PlayerAwarePullableRejectsOnlyThePlayerItDisallows()
    {
        GameObject pullableObject = new GameObject("PlayerAwarePullable");
        GameObject player = new GameObject("Player");

        try
        {
            PlayerAwarePullableFake pullable = pullableObject.AddComponent<PlayerAwarePullableFake>();
            pullable.CanHook = false;

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.False);

            pullable.CanHook = true;

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(pullableObject);
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void NormalMovableBlockPullPointKeepsUsingGeneralHookCondition()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");
        GameObject player = new GameObject("Player");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<MovableBlock>();
            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint pullable = pointObject.AddComponent<MovableBlockPullPoint>();

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void HookPullContextKeepsTheExactPlayerRoot()
    {
        GameObject player = new GameObject("Player");

        try
        {
            HookPullContext context = new HookPullContext(Vector3.one, 0.5f, player.transform);

            Assert.That(context.PlayerRoot, Is.SameAs(player.transform));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void TowardPlayerUsesHorizontalDirectionFromBlockToPlayer()
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.TowardPlayer,
            Quaternion.identity,
            MovableBlockPullSide.Front,
            Vector3.zero,
            new Vector3(3f, 7f, 4f));

        Assert.That(Vector3.Distance(direction, new Vector3(0.6f, 0f, 0.8f)), Is.LessThan(0.0001f));
    }

    [TestCase(MovableBlockPullSide.Front, 0f, 0f, 1f)]
    [TestCase(MovableBlockPullSide.Back, 0f, 0f, -1f)]
    [TestCase(MovableBlockPullSide.Left, -1f, 0f, 0f)]
    [TestCase(MovableBlockPullSide.Right, 1f, 0f, 0f)]
    public void FourDirectionsUsesNamedLocalSide(
        MovableBlockPullSide side,
        float expectedX,
        float expectedY,
        float expectedZ)
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.FourDirections,
            Quaternion.identity,
            side,
            Vector3.zero,
            new Vector3(-20f, 5f, -20f));

        Assert.That(Vector3.Distance(direction, new Vector3(expectedX, expectedY, expectedZ)), Is.LessThan(0.0001f));
    }

    [Test]
    public void FourDirectionsRotatesWithBlock()
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.FourDirections,
            Quaternion.Euler(0f, 90f, 0f),
            MovableBlockPullSide.Front,
            Vector3.zero,
            Vector3.back);

        Assert.That(Vector3.Distance(direction, Vector3.right), Is.LessThan(0.0001f));
    }

    [TestCase(MovableBlockPullSide.FrontLeft, -1f, 1f)]
    [TestCase(MovableBlockPullSide.FrontRight, 1f, 1f)]
    [TestCase(MovableBlockPullSide.BackLeft, -1f, -1f)]
    [TestCase(MovableBlockPullSide.BackRight, 1f, -1f)]
    public void FourCornersUsesNamedLocalCorner(
        MovableBlockPullSide side,
        float expectedX,
        float expectedZ)
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.FourCorners,
            Quaternion.identity,
            side,
            Vector3.zero,
            new Vector3(-20f, 5f, -20f));

        Vector3 expected = new Vector3(expectedX, 0f, expectedZ).normalized;
        Assert.That(Vector3.Distance(direction, expected), Is.LessThan(0.0001f));
    }

    [Test]
    public void PullPointCompletesPullOnlyWhenHookCallsCompletion()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            MovableBlock block = root.AddComponent<MovableBlock>();
            SerializedObject serializedBlock = new SerializedObject(block);
            serializedBlock.FindProperty("moveMode").enumValueIndex = (int)MovableBlockMoveMode.FourDirections;
            serializedBlock.FindProperty("moveDistance").floatValue = 2f;
            serializedBlock.FindProperty("moveDuration").floatValue = 0f;
            serializedBlock.ApplyModifiedPropertiesWithoutUndo();

            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();
            SerializedObject serializedPoint = new SerializedObject(point);
            serializedPoint.FindProperty("side").enumValueIndex = (int)MovableBlockPullSide.Front;
            serializedPoint.ApplyModifiedPropertiesWithoutUndo();

            IHookPullable pullable = point;
            pullable.SetHooked(true);

            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));

            pullable.CompletePull(new HookPullContext(new Vector3(-20f, 0f, -20f), 0.5f));

            Assert.That(Vector3.Distance(root.transform.position, new Vector3(0f, 0f, 2f)), Is.LessThan(0.0001f));
            Assert.That(pullable.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void HookFindsPullableFromTheColliderItHit()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<MovableBlock>();
            pointObject.transform.SetParent(root.transform, false);
            BoxCollider pointCollider = pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();

            IHookPullable found = HookController.FindPullable(pointCollider);

            Assert.That(found, Is.SameAs(point));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ThrowControllerCallsPullableWhenPullIsCompleted()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            MovableBlock block = root.AddComponent<MovableBlock>();
            SerializedObject serializedBlock = new SerializedObject(block);
            serializedBlock.FindProperty("moveMode").enumValueIndex = (int)MovableBlockMoveMode.FourDirections;
            serializedBlock.FindProperty("moveDistance").floatValue = 2f;
            serializedBlock.FindProperty("moveDuration").floatValue = 0f;
            serializedBlock.ApplyModifiedPropertiesWithoutUndo();

            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();
            SerializedObject serializedPoint = new SerializedObject(point);
            serializedPoint.FindProperty("side").enumValueIndex = (int)MovableBlockPullSide.Right;
            serializedPoint.ApplyModifiedPropertiesWithoutUndo();

            IHookPullable pullable = point;
            pullable.SetHooked(true);
            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));

            ThrowController.CompletePullable(pullable, new Vector3(-10f, 0f, -10f), 0.75f);

            Assert.That(Vector3.Distance(root.transform.position, new Vector3(2f, 0f, 0f)), Is.LessThan(0.0001f));
            Assert.That(pullable.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MovableBlockPrefabHasFourNamedTriggerPullPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<MovableBlock>(), Is.Not.Null);
        AssertPullPoint(prefab.transform, "Front", MovableBlockPullSide.Front);
        AssertPullPoint(prefab.transform, "Back", MovableBlockPullSide.Back);
        AssertPullPoint(prefab.transform, "Left", MovableBlockPullSide.Left);
        AssertPullPoint(prefab.transform, "Right", MovableBlockPullSide.Right);
    }

    [Test]
    public void PullAblePlatePrefabHasFourNamedCornerTriggerPullPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PullAblePlatePrefabPath);

        Assert.That(prefab, Is.Not.Null);

        MovableBlock block = prefab.GetComponent<MovableBlock>();
        Assert.That(block, Is.Not.Null);
        SerializedObject serializedBlock = new SerializedObject(block);
        Assert.That(
            serializedBlock.FindProperty("moveMode").enumValueIndex,
            Is.EqualTo((int)MovableBlockMoveMode.FourCorners));

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);

        AssertPullPoint(prefab.transform, "FrontLeft", MovableBlockPullSide.FrontLeft);
        AssertPullPoint(prefab.transform, "FrontRight", MovableBlockPullSide.FrontRight);
        AssertPullPoint(prefab.transform, "BackLeft", MovableBlockPullSide.BackLeft);
        AssertPullPoint(prefab.transform, "BackRight", MovableBlockPullSide.BackRight);
    }

    private static void AssertPullPoint(Transform root, string childName, MovableBlockPullSide expectedSide)
    {
        Transform child = root.Find(childName);
        Assert.That(child, Is.Not.Null, $"{childName} 拉點不存在");

        BoxCollider trigger = child.GetComponent<BoxCollider>();
        Assert.That(trigger, Is.Not.Null, $"{childName} 沒有 BoxCollider");
        Assert.That(trigger.isTrigger, Is.True, $"{childName} 的 Collider 必須是 Trigger");

        MovableBlockPullPoint pullPoint = child.GetComponent<MovableBlockPullPoint>();
        Assert.That(pullPoint, Is.Not.Null, $"{childName} 沒有 MovableBlockPullPoint");

        SerializedObject serializedPoint = new SerializedObject(pullPoint);
        Assert.That(serializedPoint.FindProperty("side").enumValueIndex, Is.EqualTo((int)expectedSide));
    }
}

public sealed class PlayerAwarePullableFake : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    public bool CanHook { get; set; }
    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked { get; private set; }
    public bool CanBeHooked => true;

    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanHook;
    }

    public void SetHooked(bool hooked)
    {
        IsHooked = hooked;
    }

    public void CompletePull(HookPullContext context)
    {
        IsHooked = false;
    }
}
