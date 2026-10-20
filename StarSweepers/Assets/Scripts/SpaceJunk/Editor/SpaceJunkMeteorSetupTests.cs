using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 隕石攻撃のプレハブを作るメニュー（<see cref="SpaceJunkMeteorSetup.CreateMeteorPrefabs"/>）を
/// 実際に動かし、**必要な6つのプレハブが、動くのに要る部品付きで出来上がるか**を確かめるテスト。
///
/// 足りないプレハブは本当に `Assets/Prefabs/SpaceJunk/Gimmick/Meteor/` へ作られる
/// （すでにある物は上書きしない）。通信で出す物の一覧への登録も行われる。
/// </summary>
public class SpaceJunkMeteorSetupTests
{
    /// <summary>
    /// プレハブ作成を実行したら → 管理役・落下範囲・攻撃本体・隕石・予告円・着弾エフェクトの6つが、
    /// それぞれの役目に要る部品（範囲はトリガーの当たり判定、攻撃本体は通信用の NetworkObject など）を持っているはず。
    /// </summary>
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

    /// <summary>プレハブを読み込む。見つからなければ、どのパスが作られなかったかを出して失敗させる。</summary>
    private static GameObject LoadPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, $"Prefab was not created: {path}");
        return prefab;
    }
}
