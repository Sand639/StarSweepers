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
/// **ゲームを止めないよう、裏で少しずつ作る**（`UpdateNavMeshDataAsync`）。作り終わるまでの間、ボットはまっすぐ歩く。
/// 作る範囲は、マップの当たり判定がある所だけ（最初は 300m 四方を細かく作っていて重かった。2026/10/21・大槻さんの報告）。
///
/// 地図に入れないもの：素材（動く物）・プレイヤーの体・すり抜ける当たり判定（ゴールの判定など）。
/// 動く床・回る床は、**地図を作った瞬間の場所**のまま（簡易版）。
///
/// 歩ける坂の角度・段差・体の太さは、ボットの体（CharacterController）に合わせる。
/// </summary>
public static class SpaceJunkBotNavMesh
{
    /// <summary>地図を作る範囲の上限（マップがこれより大きくても、ここまでしか作らない）。</summary>
    private const float MaxBuildSize = 200f;

    /// <summary>
    /// 地図の細かさ（m）。小さいほど細かいが、作るのに時間がかかる。
    /// Unity の初期値は「体の半径 ÷ 3」（約 0.13m）で、マップ全体を作るには細かすぎた。
    /// </summary>
    private const float VoxelSize = 0.2f;

    private static NavMeshDataInstance instance;
    private static AsyncOperation building;
    private static int builtSceneHandle = -1;
    private static float buildStartedAt;
    private static bool reported;
    private static bool hasSettings;
    private static NavMeshBuildSettings settings;

    /// <summary>いまのマップの地図ができている（作り終わっている）か。</summary>
    public static bool Ready => instance.valid && builtSceneHandle == SceneManager.GetActiveScene().handle
                                && (building == null || building.isDone);

    /// <summary>道を調べるときの条件（ボットの体に合わせた種類）。</summary>
    public static NavMeshQueryFilter Filter => new NavMeshQueryFilter
    {
        agentTypeID = settings.agentTypeID,
        areaMask = NavMesh.AllAreas
    };

    /// <summary>
    /// **いまのマップの地図が無ければ、裏で作り始める。** マップ（いま動いているシーン）が変わっていたら作り直す。
    /// 作り終わっていれば true。作っている途中・作れなかったときは false（そのときボットは、まっすぐ歩く）。
    /// </summary>
    public static bool EnsureBuilt(CharacterController body)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (builtSceneHandle == scene.handle)
        {
            ReportWhenDone(scene);
            return Ready;
        }

        if (instance.valid)
        {
            instance.Remove();
        }

        building = null;
        reported = false;
        builtSceneHandle = scene.handle; // 作れなかったときも、同じマップで何度も作り直さない

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
            settings.overrideVoxelSize = true;
            settings.voxelSize = VoxelSize;
            hasSettings = true;
        }

        // まず広めに当たり判定を集め、床や壁ではない物を外してから、残った物を囲む範囲だけで作る
        Bounds search = new Bounds(Vector3.zero, Vector3.one * MaxBuildSize * 2f);
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(search, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(IsNotGround);
        sources.RemoveAll(IsUnreadableMesh);

        if (!TryGetBounds(sources, out Bounds bounds))
        {
            Debug.LogWarning($"[BOT] {scene.name} に床が見つからないので、歩ける床の地図を作りませんでした。ボットはまっすぐ歩きます。");
            return false;
        }

        NavMeshData data = new NavMeshData(settings.agentTypeID);
        instance = NavMesh.AddNavMeshData(data);
        building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        buildStartedAt = Time.realtimeSinceStartup;

        Debug.Log($"[BOT] {scene.name} の歩ける床の地図を作り始めました（当たり判定 {sources.Count} 個・範囲 {bounds.size.x:0}×{bounds.size.z:0}m）。");
        return false;
    }

    /// <summary>作り終わったら、かかった時間を1回だけ知らせる（重さを調べるとき用）。</summary>
    private static void ReportWhenDone(Scene scene)
    {
        if (reported || building == null || !building.isDone)
        {
            return;
        }

        reported = true;
        Debug.Log($"[BOT] {scene.name} の歩ける床の地図ができました（{Time.realtimeSinceStartup - buildStartedAt:0.0} 秒）。");
    }

    /// <summary>集めた当たり判定をすべて囲む範囲（少し広げる。上限 <see cref="MaxBuildSize"/>）。1つも無ければ false。</summary>
    private static bool TryGetBounds(List<NavMeshBuildSource> sources, out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        foreach (NavMeshBuildSource source in sources)
        {
            if (!(source.component is Collider collider))
            {
                continue;
            }

            if (any)
            {
                bounds.Encapsulate(collider.bounds);
            }
            else
            {
                bounds = collider.bounds;
                any = true;
            }
        }

        if (!any)
        {
            return false;
        }

        bounds.Expand(4f);
        Vector3 size = bounds.size;
        bounds.size = new Vector3(Mathf.Min(size.x, MaxBuildSize), Mathf.Min(size.y, MaxBuildSize), Mathf.Min(size.z, MaxBuildSize));
        return true;
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

    /// <summary>
    /// **読み取りが許されていない形（Read/Write が OFF のモデル）の当たり判定か。** そうなら地図から外し、注意を出す（2026/10/28）。
    ///
    /// そのまま入れると、Unity が「RuntimeNavMeshBuilder: Source mesh … does not allow read access」の**エラー**を出す。
    /// 地図はラウンドごとに作り直すので、エラーもラウンドごとに出て、Console の Error Pause が ON だと**ゲームが一時停止**した
    /// （STAGE_03 のドーナツ `Assets/Art/Models/Donut.obj`。大槻さんの報告）。しかもビルドではその形は地図に入らない。
    /// 直すときは、そのモデルを選んで、インスペクターの Model タブの **Read/Write** を ON にして Apply する。
    /// </summary>
    private static bool IsUnreadableMesh(NavMeshBuildSource source)
    {
        if (!(source.sourceObject is Mesh mesh) || mesh.isReadable)
        {
            return false;
        }

        string owner = source.component != null ? source.component.name : "?";
        Debug.LogWarning($"[BOT] 「{owner}」の形（{mesh.name}）は読み取りが許されていないので、歩ける床の地図に入れません。" +
                         "ボットがその上を歩けるようにするには、モデルの Read/Write を ON にしてください。", source.component);
        return true;
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
