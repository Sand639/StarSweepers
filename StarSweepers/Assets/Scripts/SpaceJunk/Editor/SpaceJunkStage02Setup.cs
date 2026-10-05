using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **STAGE_02（真ん中を列車が通るステージ）の仕掛けを、開いているシーンに置くメニュー。**
///
/// 1. 爆弾の置き場所4つ（<see cref="SpaceJunkBombPoints"/>）を、左右の地面の手前と奥に置く
/// 2. 素材のスポナーが、**左右の地面の上にだけ**素材を出すようにする（真ん中の地面には出さない）
///
/// 何度実行してもよい（置き場所は作り直し、地面の指定は上書きする）。
/// </summary>
public static class SpaceJunkStage02Setup
{
    private const string BombPrefabPath = "Assets/Prefabs/Fish/Online/Gimmick/FishingOnlineBomb.prefab";
    private const string BombPointsName = "BombPoints";

    /// <summary>爆弾の置き場所（左右の地面の手前と奥）。地面の上面は高さ0。</summary>
    private static readonly Vector3[] BombPositions =
    {
        new Vector3(-11f, 0.6f, 10f),
        new Vector3(-11f, 0.6f, -10f),
        new Vector3(11f, 0.6f, 10f),
        new Vector3(11f, 0.6f, -10f),
    };

    [MenuItem("Tools/StarSweepers/開いているマップに STAGE_02 の爆弾と素材の出る地面を設定する")]
    private static void Setup()
    {
        PlaceBombPoints();
        SetSpawnGrounds();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    private static void PlaceBombPoints()
    {
        GameObject old = GameObject.Find(BombPointsName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
        }

        GameObject root = new GameObject(BombPointsName);
        Undo.RegisterCreatedObjectUndo(root, "爆弾の置き場所を置く");

        for (int i = 0; i < BombPositions.Length; i++)
        {
            GameObject point = new GameObject($"BombPoint_{i + 1}");
            point.transform.SetParent(root.transform, false);
            point.transform.position = BombPositions[i];
        }

        SpaceJunkBombPoints bombPoints = root.AddComponent<SpaceJunkBombPoints>();
        GameObject bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        if (bombPrefab == null)
        {
            Debug.LogError($"[JUNK] 爆弾のプレハブが見つかりません：{BombPrefabPath}。BombPoints の Bomb Prefab に手で入れてください。");
            return;
        }

        SerializedObject serialized = new SerializedObject(bombPoints);
        serialized.FindProperty("bombPrefab").objectReferenceValue = bombPrefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[JUNK] 爆弾の置き場所を4つ置きました（BombPoints の子を動かせば場所を変えられます）。");
    }

    /// <summary>
    /// 「Ground」の下にある地面のうち、**中心（原点）の真上にかかっていないもの**だけを、素材を出す地面にする。
    /// STAGE_02 では、左右の細長い地面（1・2）が入り、真ん中の地面（3）が外れる。
    /// </summary>
    private static void SetSpawnGrounds()
    {
        SpaceJunkSpawner spawner = Object.FindFirstObjectByType<SpaceJunkSpawner>();
        if (spawner == null)
        {
            Debug.LogError("[JUNK] SpaceJunkSpawner が見つかりません。");
            return;
        }

        GameObject ground = GameObject.Find("Ground");
        if (ground == null)
        {
            Debug.LogError("[JUNK] 「Ground」が見つかりません。SpaceJunkSpawner の Spawn Grounds に、素材を出したい地面を手で入れてください。");
            return;
        }

        List<Collider> grounds = new List<Collider>();
        foreach (Collider collider in ground.GetComponentsInChildren<Collider>())
        {
            Bounds bounds = collider.bounds;
            bool coversCenter = bounds.min.x <= 0f && bounds.max.x >= 0f
                             && bounds.min.z <= 0f && bounds.max.z >= 0f;
            if (!coversCenter)
            {
                grounds.Add(collider);
            }
        }

        SerializedObject serialized = new SerializedObject(spawner);
        SerializedProperty list = serialized.FindProperty("spawnGrounds");
        list.arraySize = grounds.Count;
        for (int i = 0; i < grounds.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = grounds[i];
        }

        // 左右の地面は端（x = ±15）まであるので、範囲もそこまで広げる。
        // 壁の中には出ない（スポナーが空いた場所を探すため）
        serialized.FindProperty("areaHalfSize").vector2Value = new Vector2(15f, 15f);
        serialized.ApplyModifiedProperties();

        List<string> names = grounds.ConvertAll(c => c.name);
        Debug.Log($"[JUNK] 素材は次の地面の上にだけ出ます：{string.Join("、", names)}");
    }
}
