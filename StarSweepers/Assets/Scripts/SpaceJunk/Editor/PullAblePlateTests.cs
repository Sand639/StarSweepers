using System.Collections;
using System.Reflection;
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
    public void ImmediatePullSkipsADisabledPassengerController()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject remotePlayer = CreatePlayer("RemotePlayer", out CharacterController controller);

        try
        {
            remotePlayer.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            Vector3 start = remotePlayer.transform.position;
            controller.enabled = false;

            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("moveDistance").floatValue = 2f;
            serializedPlate.FindProperty("moveDuration").floatValue = 0f;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            plate.RegisterPassenger(remotePlayer.transform, controller);

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);
            Assert.That(remotePlayer.transform.position, Is.EqualTo(start));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(remotePlayer);
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
    public void ImmediatePullStopsBeforeAFixedCube()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject fixedObject = new GameObject("FixedMap");
        BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();

        try
        {
            fixedObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            Assert.That(Physics.GetIgnoreCollision(plateCollider, fixedCollider), Is.False);
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);
            Assert.That(plateObject.transform.position.x, Is.LessThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(fixedObject);
        }
    }

    [Test]
    public void PullPointCanCompleteThreeConsecutivePulls()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject pointObject = CreatePullPoint(
            plate,
            PullAblePlateDirection.Right,
            out PullAblePlatePullPoint point);

        try
        {
            ConfigureImmediatePull(plate);

            for (int pullIndex = 0; pullIndex < 3; pullIndex++)
            {
                Assert.That(point.CanBeHooked, Is.True, $"拉扯 {pullIndex + 1} 次前應可勾中");
                point.SetHooked(true);
                point.CompletePull(new HookPullContext(Vector3.left, 1f));
                Assert.That(point.IsHooked, Is.False, $"拉扯 {pullIndex + 1} 次後應解除 Hook");
                Physics.SyncTransforms();
            }

            Assert.That(Vector3.Distance(plateObject.transform.position, new Vector3(6f, 0f, 0f)),
                Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    [Test]
    public void DisablingPlateDuringMoveClearsMovingState()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);

        try
        {
            typeof(PullAblePlate).GetField(
                    "body",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(plate, plateObject.GetComponent<Rigidbody>());
            MethodInfo moveTo = typeof(PullAblePlate).GetMethod(
                "MoveTo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(moveTo, Is.Not.Null);

            IEnumerator moveRoutine = (IEnumerator)moveTo.Invoke(
                plate,
                new object[] { new Vector3(2f, 0f, 0f) });
            Assert.That(moveRoutine.MoveNext(), Is.True);
            Assert.That(plate.IsMoving, Is.True);

            typeof(PullAblePlate).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(plate, null);

            Assert.That(plate.IsMoving, Is.False,
                "移動中に物件が停用されても、再啟用後は再次拉扯できる必要がある");
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    [Test]
    public void DisablingPlateClearsHookedPullPointState()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        CreatePullPoint(plate, PullAblePlateDirection.Right, out PullAblePlatePullPoint point);

        try
        {
            point.SetHooked(true);
            Assert.That(point.IsHooked, Is.True);

            typeof(PullAblePlatePullPoint).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(point, null);

            Assert.That(point.IsHooked, Is.False);
            Assert.That(point.CanBeHooked, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    [Test]
    public void DisablingHookControllerReleasesAttachedPullPoint()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        CreatePullPoint(plate, PullAblePlateDirection.Right, out PullAblePlatePullPoint point);
        GameObject playerObject = new GameObject("Player");
        playerObject.SetActive(false);
        HookController hook = playerObject.AddComponent<HookController>();

        try
        {
            point.SetHooked(true);
            typeof(HookController).GetField(
                    "attachedPullable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(hook, point);

            typeof(HookController).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(hook, null);

            Assert.That(point.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(playerObject);
        }
    }

    [Test]
    public void PlateCanMoveAwayAfterStoppingAtAFixedCube()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject fixedObject = new GameObject("FixedMap");
        BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();

        try
        {
            fixedObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);
            float stoppedX = plateObject.transform.position.x;
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);

            Assert.That(plate.Pull(PullAblePlateDirection.Left), Is.True);

            Assert.That(plateObject.transform.position.x, Is.LessThan(stoppedX - 1.9f));
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(fixedObject);
        }
    }

    [Test]
    public void ImmediatePullStopsBeforeAnotherPlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject otherPlateObject = CreatePlate(out _);
        BoxCollider otherPlateCollider = otherPlateObject.AddComponent<BoxCollider>();

        try
        {
            otherPlateObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            Assert.That(Physics.GetIgnoreCollision(plateCollider, otherPlateCollider), Is.False);
            Assert.That(AreOverlapping(plateCollider, otherPlateCollider), Is.False);
            Assert.That(plateObject.transform.position.x, Is.LessThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(otherPlateObject);
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
        Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);

        Assert.That(prefab.GetComponent<MeshFilter>(), Is.Null);
        Assert.That(prefab.GetComponent<MeshRenderer>(), Is.Null);
        Assert.That(prefab.GetComponent<Collider>(), Is.Null);

        Transform plateBody = prefab.transform.Find("PlateBody");
        Assert.That(plateBody, Is.Not.Null);
        Assert.That(Vector3.Distance(plateBody.localPosition, new Vector3(0f, -0.25f, 0f)),
            Is.LessThan(0.0001f));
        Assert.That(Vector3.Distance(plateBody.localScale, new Vector3(10f, 0.5f, 10f)),
            Is.LessThan(0.0001f));

        MeshFilter plateMesh = plateBody.GetComponent<MeshFilter>();
        Assert.That(plateMesh, Is.Not.Null);
        Assert.That(plateMesh.sharedMesh, Is.Not.Null);
        Assert.That(plateMesh.sharedMesh.name, Is.EqualTo("Cube"));
        Assert.That(plateBody.GetComponent<MeshRenderer>(), Is.Not.Null);

        BoxCollider plateCollider = plateBody.GetComponent<BoxCollider>();
        Assert.That(plateCollider, Is.Not.Null);
        Assert.That(plateCollider.isTrigger, Is.False);

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

    private static void ConfigureImmediatePull(PullAblePlate plate)
    {
        SerializedObject serializedPlate = new SerializedObject(plate);
        serializedPlate.FindProperty("moveDistance").floatValue = 2f;
        serializedPlate.FindProperty("moveDuration").floatValue = 0f;
        serializedPlate.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool AreOverlapping(Collider first, Collider second)
    {
        return Physics.ComputePenetration(
            first,
            first.transform.position,
            first.transform.rotation,
            second,
            second.transform.position,
            second.transform.rotation,
            out _,
            out _);
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
