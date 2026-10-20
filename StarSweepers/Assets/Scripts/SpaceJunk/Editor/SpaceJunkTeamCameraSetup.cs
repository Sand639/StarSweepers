using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **自分の陣地が画面の手前に来るカメラ（ワイルドリフト風）のマップを作るツール。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみの自陣向きカメラのマップを作る（Map01を複製）」から実行する。
/// （※ いまはアーカイブに移してあり、メニューの場所は
/// 「Tools &gt; StarSweepers &gt; アーカイブ &gt; 宇宙ごみ &gt; 自陣向きカメラのマップを作る（Map01を複製）」）
///
/// `SpaceJunkMap01` を複製して `SpaceJunkMap01TeamCam` を作り、**カメラだけ**を変える
/// （2026/9/22・大槻さんの依頼）。
///
/// | | Map01 | Map01TeamCam |
/// | --- | --- | --- |
/// | カメラ | 自分を追いかける。**全員北向き** | 自分を追いかける。**自陣のゴールが手前に来るよう、チームごとに向きが回る** |
///
/// 高さ・後ろへの距離・見下ろす角度・追いかけるなめらかさは、**Map01 のカメラの値をそのまま引き継ぐ。**
/// 向きの決め方は <see cref="SpaceJunkTeamFollowCamera"/> を参照。
///
/// カメラは釣りのプレハブ（`TopDownFollowCamera.prefab`）なので、
/// **プレハブから切り離してから**部品を差し替える。釣り側は変わらない。
///
/// **すでにあれば作り直さない。** 作ったマップはマップの一覧に自動で入る。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkTeamCameraSetup
{
    private const string SourcePath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMap01.unity";
    private const string DestinationPath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMap01TeamCam.unity";

    /// <summary>
    /// Map01 を複製してカメラを差し替え、ビルドの一覧・マップの一覧に入れて点検する。
    /// 複製元が無いとき・もう作ってあるときは何もしない。カメラが見つからなければ、作りかけのシーンを消してやめる。
    /// </summary>
    [MenuItem("Tools/StarSweepers/アーカイブ/宇宙ごみ/自陣向きカメラのマップを作る（Map01を複製）")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourcePath) == null)
        {
            Debug.LogError($"[JUNK] 複製元のマップが見つかりません：{SourcePath}");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DestinationPath) != null)
        {
            Debug.LogWarning(
                $"[JUNK] {DestinationPath} はもうあるので、作り直しませんでした" +
                "（手で直した中身が消えないようにするため）。\n" +
                "作り直したいときは、そのシーンを消してからもう一度実行してください。");
            return;
        }

        // テキストのコピーではなく、Unity に複製させる（通信の番号を付け直せるように）
        if (!AssetDatabase.CopyAsset(SourcePath, DestinationPath))
        {
            Debug.LogError($"[JUNK] シーンを複製できませんでした：{SourcePath} → {DestinationPath}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(DestinationPath, OpenSceneMode.Single);

        if (!SwapCamera())
        {
            // カメラが見つからなかった。作りかけのシーンを残さないよう消しておく
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(DestinationPath);
            return;
        }

        SpaceJunkFixedCameraSetup.RefreshNetworkIds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, DestinationPath);

        SpaceJunkSetup.RegisterScenesInBuildSettings(new List<string> { DestinationPath });
        SpaceJunkSetup.VerifyMap(DestinationPath);
        SpaceJunkMapListSetup.AddMap(DestinationPath, true);

        AssetDatabase.SaveAssets();

        Debug.Log(
            "【宇宙ごみ】自陣向きカメラのマップを作りました。\n" +
            $"マップ：{DestinationPath}\n\n" +
            "・自分のゴールが画面の手前に来るよう、チームごとにカメラの向きが回る\n" +
            "　（自分のゴール1つ＝真正面から、全部＝北向きのまま）\n" +
            "・W はどのチームでも画面の奥へ進む\n" +
            "・マップの一覧に自動で入ったので、ロビーの候補に出る（検証用ビルドはビルドし直し）");
    }

    /// <summary>
    /// **追いかけるカメラを、自陣向きに回るカメラに差し替える。**
    /// 高さ・距離・見下ろす角度・なめらかさは、元のカメラの値を引き継ぐ。
    /// カメラが見つからなければ false。
    ///
    /// ⚠ 値は**フィールドの名前（`offset` / `followSharpness` / `pitch`）を文字で指定して**読み書きしている。
    /// `TopDownCameraFollow` か `SpaceJunkTeamFollowCamera` のどちらかでこの名前を変えると、ここでエラーになって止まる。
    /// </summary>
    private static bool SwapCamera()
    {
        TopDownCameraFollow follow = Object.FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);

        if (follow == null)
        {
            Debug.LogError("[JUNK] 複製したマップに、追いかけるカメラ（TopDownCameraFollow）が見つかりませんでした。作るのをやめます。");
            return false;
        }

        GameObject cameraObject = follow.gameObject;

        // 釣りのプレハブから切り離す（釣り側を変えないため）。
        // 切り離すと部品の参照が変わることがあるので、取り直す
        SpaceJunkSetup.UnpackIfPrefabInstance(cameraObject);
        follow = cameraObject.GetComponent<TopDownCameraFollow>();

        // 元の値を控えてから、部品を差し替える。
        // offset は追いかける相手から見たカメラの位置（高さと後ろへの距離、m）、pitch は見下ろす角度（度）
        SerializedObject old = new SerializedObject(follow);
        Vector3 offset = old.FindProperty("offset").vector3Value;
        float sharpness = old.FindProperty("followSharpness").floatValue;
        float pitch = cameraObject.transform.eulerAngles.x;

        Object.DestroyImmediate(follow);

        SpaceJunkTeamFollowCamera teamCamera = cameraObject.AddComponent<SpaceJunkTeamFollowCamera>();

        SerializedObject created = new SerializedObject(teamCamera);
        created.FindProperty("offset").vector3Value = offset;
        created.FindProperty("followSharpness").floatValue = sharpness;
        created.FindProperty("pitch").floatValue = pitch;
        created.ApplyModifiedPropertiesWithoutUndo();

        cameraObject.name = "Main Camera (TeamView)";

        Debug.Log($"[JUNK] カメラを差し替えました（高さと距離 {offset}、見下ろす角度 {pitch:0} 度を引き継ぎ）。");
        return true;
    }
}
