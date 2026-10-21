using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// **ボット（<see cref="SpaceJunkBot"/>）が「どこを歩けるか」を知るための、歩ける床の地図（NavMesh）を作る係。** （2026/10/21）
///
/// ボットが壁に向かって押し続けたり、上の段にある素材へ下から近づこうとしたりしないよう、
/// **壁を回り込む道**と**同じ高さの床か**を、この地図で調べる（大槻さんの報告：STAGE_07）。
///
/// ## シーンには何も置かない
///
/// 地図は**再生中に、そのマップの当たり判定から自動で作る**（Unity の `NavMeshBuilder`）。
/// だからマップのシーンを直したり、地図を焼いて保存したりしなくてよい。マップが変わったら作り直す。
///
/// 地図に入れないもの：素材（動く物）・プレイヤーの体・すり抜ける当たり判定（ゴールの判定など）。
/// 動く床・回る床は、**地図を作った瞬間の場所**のまま（簡易版）。
///
/// 歩ける坂の角度・段差・体の太さは、ボットの体（CharacterController）に合わせる。
/// </summary>
public static class SpaceJunkBotNavMesh
{
    /// <summary>地図を作る範囲（マップの真ん中を原点として、この大きさの箱の中）。</summary>
    private static readonly Vector3 BuildSize = new Vector3(300f, 120f, 300f);

    private static NavMeshDataInstance instance;
    private static int builtSceneHandle = -1;
    private static bool hasSettings;
    private static NavMeshBuildSettings settings;

    /// <summary>いまのマップの地図ができているか。</summary>
    public static bool Ready => instance.valid && builtSceneHandle == SceneManager.GetActiveScene().handle;

    /// <summary>道を調べるときの条件（ボットの体に合わせた種類）。</summary>
    public static NavMeshQueryFilter Filter => new NavMeshQueryFilter
    {
        agentTypeID = settings.agentTypeID,
        areaMask = NavMesh.AllAreas
    };

    /// <summary>
    /// **いまのマップの地図が無ければ作る。** マップ（いま動いているシーン）が変わっていたら作り直す。
    /// 作れなければ false（そのときボットは、まっすぐ歩く簡易版の動きになる）。
    /// </summary>
    public static bool EnsureBuilt(CharacterController body)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (instance.valid && builtSceneHandle == scene.handle)
        {
            return true;
        }

        if (instance.valid)
        {
            instance.Remove();
        }

        if (!hasSettings)
        {
            // 新しい「歩く人の種類」を1つ作り、ボットの体の大きさに合わせる（1回だけ）
            settings = NavMesh.CreateSettings();
            if (body != null)
            {
                settings.agentRadius = body.radius;
                settings.agentHeight = body.height;
                settings.agentClimb = body.stepOffset;
                settings.agentSlope = body.slopeLimit;
            }
            hasSettings = true;
        }

        Bounds bounds = new Bounds(Vector3.zero, BuildSize);
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(IsNotGround);

        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null)
        {
            Debug.LogWarning("[BOT] 歩ける床の地図を作れませんでした。ボットはまっすぐ歩きます。");
            builtSceneHandle = -1;
            return false;
        }

        instance = NavMesh.AddNavMeshData(data);
        builtSceneHandle = scene.handle;
        Debug.Log($"[BOT] {scene.name} の歩ける床の地図を作りました（当たり判定 {sources.Count} 個から）。");
        return instance.valid;
    }

    /// <summary>
    /// **地図に入れない当たり判定か。** すり抜ける判定・動く物（素材など）・プレイヤーの体は、床や壁ではないので外す。
    /// </summary>
    private static bool IsNotGround(NavMeshBuildSource source)
    {
        if (!(source.component is Collider collider))
        {
            return false;
        }

        if (collider.isTrigger || collider is CharacterController)
        {
            return true;
        }

        Rigidbody body = collider.attachedRigidbody;
        if (body != null && !body.isKinematic)
        {
            return true;
        }

        return collider.GetComponentInParent<HookableObject>() != null
            || collider.GetComponentInParent<CharacterController>() != null;
    }

    /// <summary><paramref name="point"/> のいちばん近くの、歩ける床の点。<paramref name="maxDistance"/> m 以内に無ければ false。</summary>
    public static bool TryFindFloor(Vector3 point, float maxDistance, out Vector3 floor)
    {
        if (Ready && NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, Filter))
        {
            floor = hit.position;
            return true;
        }

        floor = point;
        return false;
    }

    /// <summary>
    /// <paramref name="from"/> から <paramref name="to"/> まで**歩いて行ける道**の曲がり角（最初は出発点）。
    /// 行けなければ false。
    /// </summary>
    public static bool TryFindPath(Vector3 from, Vector3 to, NavMeshPath path)
    {
        return Ready && NavMesh.CalculatePath(from, to, Filter, path) && path.status == NavMeshPathStatus.PathComplete;
    }
}
