using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PullAblePlateTests
{
    private const string PlatePrefabPath = "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlate.prefab";
    private const string AnchorPrefabPath = "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlateAnchor.prefab";

    [TestCase(PullAblePlateDirection.Front, 0f, 1f)]
    [TestCase(PullAblePlateDirection.FrontRight, 1f, 1f)]
    [TestCase(PullAblePlateDirection.Right, 1f, 0f)]
    [TestCase(PullAblePlateDirection.BackRight, 1f, -1f)]
    [TestCase(PullAblePlateDirection.Back, 0f, -1f)]
    [TestCase(PullAblePlateDirection.BackLeft, -1f, -1f)]
    [TestCase(PullAblePlateDirection.Left, -1f, 0f)]
    [TestCase(PullAblePlateDirection.FrontLeft, -1f, 1f)]
    public void CalculateDirectionReturnsTheNamedHorizontalDirection(
        PullAblePlateDirection direction,
        float expectedX,
        float expectedZ)
    {
        Vector3 expected = new Vector3(expectedX, 0f, expectedZ).normalized;

        Vector3 actual = PullAblePlate.CalculateDirection(direction, Quaternion.identity);

        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
    }

    [Test]
    public void CalculateDirectionRotatesWithThePlate()
    {
        Vector3 actual = PullAblePlate.CalculateDirection(
            PullAblePlateDirection.Front,
            Quaternion.Euler(0f, 90f, 0f));

        Assert.That(Vector3.Distance(actual, Vector3.right), Is.LessThan(0.0001f));
    }

    [Test]
    public void PullPointRejectsPassengerButAllowsAnotherPlayer()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController passengerController);
        GameObject outsider = CreatePlayer("Outsider", out _);
        GameObject pointObject = CreatePullPoint(plate, PullAblePlateDirection.Front, out PullAblePlatePullPoint point);

        try
        {
            plate.RegisterPassenger(passenger.transform, passengerController);

            Assert.That(point.CanBeHookedBy(passenger.transform), Is.False);
            Assert.That(point.CanBeHookedBy(outsider.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(outsider);
        }
    }

    [Test]
    public void DisablingDirectionHidesItsPullPointAndPreventsHooking()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject pointObject = CreatePullPoint(plate, PullAblePlateDirection.Front, out PullAblePlatePullPoint point);

        try
        {
            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("frontPullPoint").objectReferenceValue = point;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            plate.SetPullPointActive(PullAblePlateDirection.Front, false);

            Assert.That(pointObject.activeSelf, Is.False);
            Assert.That(point.CanBeHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    [Test]
    public void ImmediatePullMovesEveryUniquePassengerByThePlateDelta()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject firstPlayer = CreatePlayer("FirstPlayer", out CharacterController firstController);
        GameObject secondPlayer = CreatePlayer("SecondPlayer", out CharacterController secondController);

        try
        {
            firstPlayer.transform.position = new Vector3(0f, 1f, 0f);
            secondPlayer.transform.position = new Vector3(0f, 1f, 1f);
            Physics.SyncTransforms();
            Vector3 firstStart = firstPlayer.transform.position;
            Vector3 secondStart = secondPlayer.transform.position;

            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("moveDistance").floatValue = 2f;
            serializedPlate.FindProperty("moveDuration").floatValue = 0f;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            plate.RegisterPassenger(firstPlayer.transform, firstController);
            plate.RegisterPassenger(firstPlayer.transform, firstController);
            plate.RegisterPassenger(secondPlayer.transform, secondController);

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            Vector3 expectedDelta = new Vector3(2f, 0f, 0f);
            Assert.That(Vector3.Distance(firstPlayer.transform.position, firstStart + expectedDelta), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(secondPlayer.transform.position, secondStart + expectedDelta), Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(firstPlayer);
            Object.DestroyImmediate(secondPlayer);
        }
    }

    [Test]
    public void AnchorCanOnlyBeHookedByAPlayerRidingAPlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController passengerController);
        GameObject outsider = CreatePlayer("Outsider", out _);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 0f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.False);

            plate.RegisterPassenger(passenger.transform, passengerController);

            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.True);
            Assert.That(anchor.CanBeHookedBy(outsider.transform), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(outsider);
            Object.DestroyImmediate(anchorObject);
        }
    }

    [Test]
    public void AnchorCompletionMovesPlateAndPassengerTowardAnchor()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 4f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            passenger.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            Vector3 passengerStart = passenger.transform.position;
            plate.RegisterPassenger(passenger.transform, controller);

            anchor.SetHooked(true);
            anchor.CompletePull(new HookPullContext(passengerStart, 1f, passenger.transform));

            Vector3 expectedDelta = new Vector3(2f, 0f, 0f);
            Assert.That(Vector3.Distance(plateObject.transform.position, expectedDelta), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(passenger.transform.position, passengerStart + expectedDelta), Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    [Test]
    public void AnchorCompletionDoesNothingAfterPlayerLeavesThePlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 0f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            plate.RegisterPassenger(passenger.transform, controller);
            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.True);
            anchor.SetHooked(true);
            plate.UnregisterPassenger(passenger.transform);

            anchor.CompletePull(new HookPullContext(passenger.transform.position, 1f, passenger.transform));

            Assert.That(plateObject.transform.position, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    [Test]
    public void AnchorAtTheSameHorizontalPositionDoesNotMoveThePlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(0f, 10f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            plate.RegisterPassenger(passenger.transform, controller);
            anchor.SetHooked(true);

            anchor.CompletePull(new HookPullContext(passenger.transform.position, 1f, passenger.transform));

            Assert.That(plateObject.transform.position, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    [Test]
    public void CollisionPolicyIgnoresOnlyPlatesAndFixedSolidColliders()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject otherPlateObject = CreatePlate(out _);
        GameObject fixedObject = new GameObject("FixedMap");
        GameObject playerObject = CreatePlayer("Player", out CharacterController playerController);
        GameObject dynamicObject = new GameObject("DynamicDebris");
        GameObject triggerObject = new GameObject("Trigger");

        try
        {
            BoxCollider otherPlateCollider = otherPlateObject.AddComponent<BoxCollider>();
            BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();
            dynamicObject.AddComponent<Rigidbody>();
            BoxCollider dynamicCollider = dynamicObject.AddComponent<BoxCollider>();
            BoxCollider triggerCollider = triggerObject.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;

            Assert.That(plate.ShouldIgnoreCollision(otherPlateCollider), Is.True);
            Assert.That(plate.ShouldIgnoreCollision(fixedCollider), Is.True);
            Assert.That(plate.ShouldIgnoreCollision(playerController), Is.False);
            Assert.That(plate.ShouldIgnoreCollision(dynamicCollider), Is.False);
            Assert.That(plate.ShouldIgnoreCollision(triggerCollider), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(otherPlateObject);
            Object.DestroyImmediate(fixedObject);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(dynamicObject);
            Object.DestroyImmediate(triggerObject);
        }
    }

    [Test]
    public void RefreshIgnoredCollisionsFindsAFixedColliderAddedAfterTheFirstRefresh()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject fixedObject = null;

        try
        {
            plate.RefreshIgnoredCollisions();

            fixedObject = new GameObject("LateFixedMap");
            BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            Assert.That(Physics.GetIgnoreCollision(plateCollider, fixedCollider), Is.False);

            plate.RefreshIgnoredCollisions();

            Assert.That(Physics.GetIgnoreCollision(plateCollider, fixedCollider), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            if (fixedObject != null)
            {
                Object.DestroyImmediate(fixedObject);
            }
        }
    }

    [Test]
    public void PlatePrefabUsesDedicatedComponentsAndEightActivePullPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);

        Assert.That(prefab, Is.Not.Null);
        PullAblePlate plate = prefab.GetComponent<PullAblePlate>();
        Assert.That(plate, Is.Not.Null);
        Assert.That(prefab.GetComponent<MovableBlock>(), Is.Null);

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);

        MeshCollider floor = prefab.GetComponent<MeshCollider>();
        Assert.That(floor, Is.Not.Null);
        Assert.That(floor.sharedMesh, Is.Not.Null);

        AssertPlatePullPoint(prefab.transform, "Front", PullAblePlateDirection.Front);
        AssertPlatePullPoint(prefab.transform, "FrontRight", PullAblePlateDirection.FrontRight);
        AssertPlatePullPoint(prefab.transform, "Right", PullAblePlateDirection.Right);
        AssertPlatePullPoint(prefab.transform, "BackRight", PullAblePlateDirection.BackRight);
        AssertPlatePullPoint(prefab.transform, "Back", PullAblePlateDirection.Back);
        AssertPlatePullPoint(prefab.transform, "BackLeft", PullAblePlateDirection.BackLeft);
        AssertPlatePullPoint(prefab.transform, "Left", PullAblePlateDirection.Left);
        AssertPlatePullPoint(prefab.transform, "FrontLeft", PullAblePlateDirection.FrontLeft);

        Transform passengerArea = prefab.transform.Find("PassengerArea");
        Assert.That(passengerArea, Is.Not.Null);
        Collider passengerTrigger = passengerArea.GetComponent<Collider>();
        Assert.That(passengerTrigger, Is.Not.Null);
        Assert.That(passengerTrigger.isTrigger, Is.True);
        Assert.That(passengerArea.GetComponent<PullAblePlatePassengerArea>(), Is.Not.Null);

        SerializedObject serializedPlate = new SerializedObject(plate);
        string[] activeProperties =
        {
            "frontActive",
            "frontRightActive",
            "rightActive",
            "backRightActive",
            "backActive",
            "backLeftActive",
            "leftActive",
            "frontLeftActive"
        };
        foreach (string propertyName in activeProperties)
        {
            Assert.That(serializedPlate.FindProperty(propertyName).boolValue, Is.True, propertyName);
        }
    }

    [Test]
    public void AnchorPrefabIsVisibleAndHookableThroughATrigger()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<PullAblePlateAnchor>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MeshRenderer>(), Is.Not.Null);
        Collider trigger = prefab.GetComponent<Collider>();
        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.isTrigger, Is.True);
    }

    private static GameObject CreatePlate(out PullAblePlate plate)
    {
        GameObject plateObject = new GameObject("PullAblePlate");
        Rigidbody body = plateObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        plate = plateObject.AddComponent<PullAblePlate>();
        return plateObject;
    }

    private static GameObject CreatePlayer(string name, out CharacterController controller)
    {
        GameObject player = new GameObject(name);
        controller = player.AddComponent<CharacterController>();
        return player;
    }

    private static GameObject CreatePullPoint(
        PullAblePlate plate,
        PullAblePlateDirection direction,
        out PullAblePlatePullPoint point)
    {
        GameObject pointObject = new GameObject(direction.ToString());
        pointObject.transform.SetParent(plate.transform, false);
        BoxCollider trigger = pointObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        point = pointObject.AddComponent<PullAblePlatePullPoint>();

        SerializedObject serializedPoint = new SerializedObject(point);
        serializedPoint.FindProperty("direction").enumValueIndex = (int)direction;
        serializedPoint.ApplyModifiedPropertiesWithoutUndo();
        return pointObject;
    }

    private static GameObject CreateAnchor(Vector3 position, out PullAblePlateAnchor anchor)
    {
        GameObject anchorObject = new GameObject("PullAblePlateAnchor");
        anchorObject.transform.position = position;
        BoxCollider trigger = anchorObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        anchor = anchorObject.AddComponent<PullAblePlateAnchor>();
        return anchorObject;
    }

    private static void AssertPlatePullPoint(
        Transform root,
        string childName,
        PullAblePlateDirection expectedDirection)
    {
        Transform child = root.Find(childName);
        Assert.That(child, Is.Not.Null, $"{childName} 拉點不存在");
        Assert.That(child.gameObject.activeSelf, Is.True, $"{childName} 必須預設啟用");

        Collider trigger = child.GetComponent<Collider>();
        Assert.That(trigger, Is.Not.Null, $"{childName} 沒有 Collider");
        Assert.That(trigger.isTrigger, Is.True, $"{childName} 的 Collider 必須是 Trigger");

        PullAblePlatePullPoint pullPoint = child.GetComponent<PullAblePlatePullPoint>();
        Assert.That(pullPoint, Is.Not.Null, $"{childName} 沒有 PullAblePlatePullPoint");
        Assert.That(pullPoint.Direction, Is.EqualTo(expectedDirection));
    }
}
