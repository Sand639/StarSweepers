using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

public class SpaceJunkMeteorSetupTests
{
    [Test]
    public void CreateMeteorPrefabs_CreatesAllRequiredPrefabsWithRuntimeComponents()
    {
        SpaceJunkMeteorSetup.CreateMeteorPrefabs();

        GameObject manager = LoadPrefab(SpaceJunkMeteorSetup.ManagerPrefabPath);
        GameObject area = LoadPrefab(SpaceJunkMeteorSetup.AreaPrefabPath);
        GameObject strike = LoadPrefab(SpaceJunkMeteorSetup.StrikePrefabPath);
        GameObject meteor = LoadPrefab(SpaceJunkMeteorSetup.MeteorPrefabPath);
        GameObject warning = LoadPrefab(SpaceJunkMeteorSetup.WarningPrefabPath);
        GameObject vfx = LoadPrefab(SpaceJunkMeteorSetup.ImpactVfxPrefabPath);

        Assert.That(manager.GetComponent<MeteorManager>(), Is.Not.Null);
        Assert.That(area.GetComponent<MeteorArea>(), Is.Not.Null);
        Assert.That(area.GetComponent<BoxCollider>().isTrigger, Is.True);
        Assert.That(strike.GetComponent<MeteorStrikeEvent>(), Is.Not.Null);
        Assert.That(strike.GetComponent<NetworkObject>(), Is.Not.Null);
        Assert.That(meteor.GetComponent<MeshRenderer>(), Is.Not.Null);
        Assert.That(warning.GetComponent<MeshRenderer>(), Is.Not.Null);
        Assert.That(vfx.GetComponent<ParticleSystem>(), Is.Not.Null);
    }

    private static GameObject LoadPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, $"Prefab was not created: {path}");
        return prefab;
    }
}
