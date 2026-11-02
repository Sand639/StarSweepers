using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GravityGlassFloorTests
{
    private GameObject floorObject;
    private GravityGlassFloor floor;
    private GameObject playerA;
    private GameObject playerB;

    [SetUp]
    public void SetUp()
    {
        floorObject = new GameObject("GlassTest");
        floorObject.AddComponent<BoxCollider>().size = new Vector3(8f, 1f, 8f);
        floor = floorObject.AddComponent<GravityGlassFloor>();
        Call("Awake");
        Set("logChanges", false);
        Set("crackMaterial", UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/GlassCrack.mat"));
        playerA = Player("A", -1f);
        playerB = Player("B", 1f);
        playerB.SetActive(false);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerA);
        Object.DestroyImmediate(playerB);
        Object.DestroyImmediate(floorObject);
    }

    [Test]
    public void CounterCountsEntriesAndKeepsSeparateCrackOrigins()
    {
        Set("glasstype", GravityGlassFloor.GlassType.CounterBreak);
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Cracked));
        int firstCracks = floorObject.GetComponentsInChildren<MeshRenderer>(true).Length;
        Assert.That(firstCracks, Is.GreaterThan(0));
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Cracked));
        playerB.SetActive(true);
        Physics.SyncTransforms();
        Call("Update");
        Assert.That(floorObject.GetComponentsInChildren<MeshRenderer>(true).Length, Is.GreaterThan(firstCracks));
        playerA.SetActive(false);
        Physics.SyncTransforms();
        Call("Update");
        playerA.SetActive(true);
        Physics.SyncTransforms();
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Broken));
        Assert.That(floorObject.GetComponent<BoxCollider>().enabled, Is.False);
        floor.Restore();
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Cracked));
    }

    [Test]
    public void StayTimeGrowsBothOriginsSmoothlyAndFreezesTheDepartedOrigin()
    {
        Set("glasstype", GravityGlassFloor.GlassType.StayTime);
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Cracked));
        Call("UpdateStayTime", 0.03f);
        MeshRenderer[] first = floorObject.GetComponentsInChildren<MeshRenderer>(true);
        float firstLength = first[0].transform.localScale.z;
        Assert.That(firstLength, Is.GreaterThan(0f));
        Call("UpdateStayTime", 0.03f);
        Assert.That(first[0].transform.localScale.z, Is.GreaterThan(firstLength));

        playerB.SetActive(true);
        Physics.SyncTransforms();
        Call("UpdateStayTime", 0.03f);
        MeshRenderer[] both = floorObject.GetComponentsInChildren<MeshRenderer>(true);
        Assert.That(both.Length, Is.GreaterThan(first.Length));
        Transform secondOriginSegment = both[first.Length].transform;
        Assert.That(secondOriginSegment.localScale.z, Is.GreaterThan(0f));
        Assert.That(secondOriginSegment.localPosition.x, Is.EqualTo(1f).Within(0.5f));

        float frozenLength = first[0].transform.localScale.z;
        float secondLength = secondOriginSegment.localScale.z;
        playerA.SetActive(false);
        Physics.SyncTransforms();
        Call("UpdateStayTime", 0.03f);
        Assert.That(first[0].transform.localScale.z, Is.EqualTo(frozenLength));
        Assert.That(secondOriginSegment.localScale.z, Is.GreaterThan(secondLength));
    }

    [Test]
    public void MultipleCollidersOnOnePlayerDoNotDoubleTheStayTime()
    {
        Set("glasstype", GravityGlassFloor.GlassType.StayTime);
        GameObject extra = new GameObject("ExtraCollider");
        extra.transform.SetParent(playerA.transform, false);
        extra.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        Call("UpdateStayTime", 0.5f);
        Assert.That(floor.BreakRemaining, Is.EqualTo(2.5f).Within(0.001f));
    }

    [Test]
    public void StepBreakStillUsesItsGravityThreshold()
    {
        Set("glasstype", GravityGlassFloor.GlassType.StepBreak);
        Set("breakGravity", Mathf.Abs(WorldGravity.Value) + 1f);
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Intact));
        Set("breakGravity", 0f);
        Set("secondsUntilBreak", 0f);
        Call("Update");
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Broken));
    }

    [Test]
    public void StayTimeAccumulatesPlayerSecondsAndStopsWhenEmpty()
    {
        Set("glasstype", GravityGlassFloor.GlassType.StayTime);
        Call("UpdateStayTime", 0.5f);
        Assert.That(floor.BreakRemaining, Is.EqualTo(2.5f).Within(0.001f));
        playerA.SetActive(false);
        Physics.SyncTransforms();
        Call("UpdateStayTime", 1f);
        Assert.That(floor.BreakRemaining, Is.EqualTo(2.5f).Within(0.001f));
        playerA.SetActive(true);
        playerB.SetActive(true);
        Physics.SyncTransforms();
        Call("UpdateStayTime", 1f);
        Assert.That(floor.BreakRemaining, Is.EqualTo(0.5f).Within(0.001f));
        Call("UpdateStayTime", 0.25f);
        Assert.That(floor.State, Is.EqualTo(GravityGlassFloor.FloorState.Broken));
        floor.Restore();
        Call("UpdateStayTime", 0.25f);
        Assert.That(floor.BreakRemaining, Is.EqualTo(2.5f).Within(0.001f));
    }

    private GameObject Player(string name, float x)
    {
        GameObject player = new GameObject(name);
        player.transform.position = new Vector3(x, 0.5f, 0f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.3f;
        controller.center = Vector3.up;
        return player;
    }

    private void Set(string name, object value) => typeof(GravityGlassFloor)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(floor, value);

    private void Call(string name, params object[] args)
    {
        MethodInfo method = typeof(GravityGlassFloor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Missing glass behavior: " + name);
        method.Invoke(floor, args);
    }
}
