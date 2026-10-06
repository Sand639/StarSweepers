using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PullAblePlateTests
{
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
}
