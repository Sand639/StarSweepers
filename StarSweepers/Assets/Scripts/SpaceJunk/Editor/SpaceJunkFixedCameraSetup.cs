using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **ステージ全体を映す「固定カメラ」のマップを作るツール。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみの全体固定カメラのマップを作る（Map01を複製）」から実行する。
/// （※ いまはアーカイブに移してあり、メニューの場所は
/// 「Tools &gt; StarSweepers &gt; アーカイブ &gt; 宇宙ごみ &gt; 全体固定カメラのマップを作る（Map01を複製）」）
///
/// `SpaceJunkMap01` を複製して `SpaceJunkMap01Fixed` を作り、**カメラだけ**を変える
/// （2026/9/22・大槻さんの依頼）。地形・壁・ゴール・スポナー・進行役は Map01 と同じ。
///
/// | | Map01 | Map01Fixed |
/// | --- | --- | --- |
/// | カメラ | 自分のプレイヤーを追いかける | **ステージ全体が映る位置に止まったまま** |
///
/// ## カメラの置き方
///
/// **ステージの見た目（地面・壁・ゴールなど）がすべて画面に収まる距離を、自動で計算して置く。**
/// 見下ろす角度はふだんのカメラと同じ 60 度。16:9 の画面で、四辺に少し余白が残る。
/// 置いたあとに Unity で `Main Camera` を動かして、好きな位置に直してよい。
///
/// ## ロビーの候補について
///
/// 作ったマップは**マップの一覧（<see cref="SpaceJunkMapList"/>）に自動で入る**ので、
/// ロビーの「使うマップ」の候補にも、検証用ビルドにも、何もしなくても入る。
///
/// ## 釣りを壊さないために
///
/// カメラは釣りのプレハブ（`TopDownFollowCamera.prefab`）として置かれているので、
/// **プレハブから切り離してから**追いかける部品を外す。釣りのプレハブは変わらない。
///
/// **すでに `SpaceJunkMap01Fixed` があるときは作り直さない**（手で直したカメラ位置が消えないように）。
/// 作り直したいときは、そのシーンを消してからもう一度実行する。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkFixedCameraSetup
{
    private const string SourcePath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMap01.unity";
    private const string DestinationPath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMap01Fixed.unity";

    /// <summary>見下ろす角度（度）。ふだんの追いかけるカメラと同じにしてある。</summary>
    private const float PitchDegrees = 60f;

    /// <summary>画面の四辺に残す余白（画面の幅・高さに対する割合）。</summary>
    private const float ScreenMargin = 0.05f;

    /// <summary>計算に使う画面の縦横比。展示・検証ともに 16:9 を想定している。</summary>
    private const float AssumedAspect = 16f / 9f;

    /// <summary>これより大きい見た目（幅か奥行きが m で）は、ステージの外の飾り（背景など）とみなして計算に入れない。</summary>
    private const float IgnoreLargerThan = 300f;

    /// <summary>カメラを置く距離（ステージの真ん中から、m）を探す範囲の、いちばん近い値。</summary>
    private const float MinDistance = 1f;

    /// <summary>カメラを置く距離を探す範囲の、いちばん遠い値（m）。</summary>
    private const float MaxDistance = 500f;

    /// <summary>
    /// 距離を探すときに、範囲を半分に絞る回数。40 回絞れば、500m の範囲でも誤差は 1mm よりずっと小さい。
    /// </summary>
    private const int SearchSteps = 40;

    [MenuItem("Tools/StarSweepers/アーカイブ/宇宙ごみ/全体固定カメラのマップを作る（Map01を複製）")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourcePath) == null)
        {
            Debug.LogError(
                $"[JUNK] 複製元のマップが見つかりません：{SourcePath}\n" +
                "先に `Tools > StarSweepers > 宇宙ごみ集めのシーンを作る（ロビー＋マップ）` を実行してください。");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DestinationPath) != null)
        {
            Debug.LogWarning(
                $"[JUNK] {DestinationPath} はもうあるので、作り直しませんでした" +
                "（手で直したカメラ位置が消えないようにするため）。\n" +
                "作り直したいときは、そのシーンを消してからもう一度実行してください。");
            return;
        }

        // **テキストとしてコピーせず、Unity に複製させる。**
        // シーンの中身は同じでも、Unity が新しいシーンとして扱ってくれる
        if (!AssetDatabase.CopyAsset(SourcePath, DestinationPath))
        {
            Debug.LogError($"[JUNK] シーンを複製できませんでした：{SourcePath} → {DestinationPath}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(DestinationPath, OpenSceneMode.Single);

        if (!MakeCameraFixed(out Vector3 cameraPosition))
        {
            // カメラが見つからなかった。作りかけのシーンを残さないよう消しておく
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(DestinationPath);
            return;
        }

        RefreshNetworkIds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, DestinationPath);

        SpaceJunkSetup.RegisterScenesInBuildSettings(new List<string> { DestinationPath });
        SpaceJunkSetup.VerifyMap(DestinationPath);

        // **マップの一覧に入れる**（入れないとロビーの候補に出ない）
        SpaceJunkMapListSetup.AddMap(DestinationPath, true);

        AssetDatabase.SaveAssets();

        Debug.Log(
            "【宇宙ごみ】全体固定カメラのマップを作りました。\n" +
            $"マップ：{DestinationPath}\n" +
            $"カメラ：位置 {cameraPosition}、見下ろす角度 {PitchDegrees} 度（ステージ全体が映る距離を自動で計算）\n\n" +
            "・ロビーの「使うマップ」に **SpaceJunkMap01Fixed** が出るので、それを選んで遊ぶ\n" +
            "・検証用ビルドにも自動で入る（ビルドし直しが必要）\n" +
            "・カメラの位置は、シーンの `Main Camera` を動かせば好きに直せる");
    }

    /// <summary>
    /// **カメラを「追いかける」から「止まったまま」に変える。**
    /// 見つからなければ false。
    /// </summary>
    private static bool MakeCameraFixed(out Vector3 position)
    {
        position = Vector3.zero;

        TopDownCameraFollow follow = Object.FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);
        Camera camera = follow != null ? follow.GetComponent<Camera>() : Camera.main;

        if (camera == null)
        {
            Debug.LogError("[JUNK] 複製したマップにカメラが見つかりませんでした。作るのをやめます。");
            return false;
        }

        // カメラは釣りのプレハブとして置かれているので、先に切り離す（釣り側を変えないため）
        SpaceJunkSetup.UnpackIfPrefabInstance(camera.gameObject);

        // 取り直す（切り離すと、部品の参照が変わることがあるため）
        follow = camera.GetComponent<TopDownCameraFollow>();

        // **追いかける部品を外す。** 無ければ、プレイヤー側が「追いかける相手」を探しても見つからず、
        // カメラは置いた場所に止まったままになる（SpaceJunkPlayerSetup.FollowWithCamera）
        if (follow != null)
        {
            Object.DestroyImmediate(follow);
        }

        camera.gameObject.name = "Main Camera (Fixed)";

        if (!TryGetStageBounds(camera, out Bounds stage))
        {
            Debug.LogWarning("[JUNK] ステージの大きさが測れなかったので、カメラの位置は Map01 のままです。手で直してください。");
            position = camera.transform.position;
            return true;
        }

        PlaceToFit(camera, stage);
        position = camera.transform.position;
        return true;
    }

    /// <summary>
    /// **ステージの見た目が占める範囲**を測る。
    /// カメラ自身・画面の表示・極端に大きい飾りは入れない。
    /// </summary>
    private static bool TryGetStageBounds(Camera camera, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            // 画面の表示（Canvas の下）や、カメラに付いている物は入れない
            if (renderer.GetComponentInParent<Canvas>() != null || renderer.transform.IsChildOf(camera.transform))
            {
                continue;
            }

            Bounds rendererBounds = renderer.bounds;

            if (rendererBounds.size.x > IgnoreLargerThan || rendererBounds.size.z > IgnoreLargerThan)
            {
                continue;
            }

            // 1つ目はそのまま使い、2つ目からは、今までの範囲を広げて包み込む
            if (!found)
            {
                bounds = rendererBounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return found;
    }

    /// <summary>
    /// **ステージの角8つがすべて画面に収まる、いちばん近い距離にカメラを置く。**
    /// 距離は二分探索で求める（近すぎれば離し、遠すぎれば寄せる）。
    /// </summary>
    private static void PlaceToFit(Camera camera, Bounds stage)
    {
        Quaternion rotation = Quaternion.Euler(PitchDegrees, 0f, 0f);

        // カメラから見て「後ろ」の向き。ステージの真ん中からこの向きに離した所にカメラを置く
        Vector3 back = rotation * Vector3.back;

        Vector3[] corners = CornersOf(stage);

        // 計算のあいだだけ、画面の縦横比を想定の 16:9 にしておく（最後に元へ戻す）
        float savedAspect = camera.aspect;
        camera.aspect = AssumedAspect;

        // near 側は「収まらない」、far 側は「収まる」とみなして、その間を半分ずつ絞っていく
        // （いちばん遠い MaxDistance でも収まらなければ、そこに置く）
        float near = MinDistance;
        float far = MaxDistance;

        for (int i = 0; i < SearchSteps; i++)
        {
            float middle = (near + far) * 0.5f;
            camera.transform.SetPositionAndRotation(stage.center + back * middle, rotation);

            if (AllInView(camera, corners))
            {
                far = middle;
            }
            else
            {
                near = middle;
            }
        }

        camera.transform.SetPositionAndRotation(stage.center + back * far, rotation);

        // 画面の縦横比は、実行したときの窓に合わせて自動で決まるようにしておく
        camera.aspect = savedAspect;
        camera.ResetAspect();

        // 遠くに置くので、奥が切れないよう描く距離を広げておく
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, far + stage.extents.magnitude * 2f);
    }

    /// <summary>
    /// 点がすべて、画面の余白（<see cref="ScreenMargin"/>）より内側に映っているか。
    /// 画面の位置は 0〜1（左下が 0、右上が 1）で比べ、カメラより後ろ・近すぎる点は「映っていない」とみなす。
    /// </summary>
    private static bool AllInView(Camera camera, Vector3[] corners)
    {
        foreach (Vector3 corner in corners)
        {
            // x・y は画面の中の位置（0〜1）、z はカメラからの奥行き（m）
            Vector3 viewport = camera.WorldToViewportPoint(corner);

            if (viewport.z <= camera.nearClipPlane
                || viewport.x < ScreenMargin || viewport.x > 1f - ScreenMargin
                || viewport.y < ScreenMargin || viewport.y > 1f - ScreenMargin)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>箱の角8つの位置（下の面の4つと、上の面の4つ）。</summary>
    private static Vector3[] CornersOf(Bounds box)
    {
        Vector3 min = box.min;
        Vector3 max = box.max;

        return new[]
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
        };
    }

    /// <summary>
    /// **通信の部品（ゴール・進行役）の番号を、この新しいシーン用に付け直す。**
    ///
    /// 複製した直後は、番号が複製元（Map01）と同じ値のまま入っている。
    /// Netcode はこの番号で全員のPCの物を対応づけるので、付け直さないと同期しない
    /// （`AIの申し送り.md` 2026/9/15。`FishingMapSetup.EnsureNetworkIdsInScene` と同じやり方）。
    /// </summary>
    internal static void RefreshNetworkIds()
    {
        // NetworkObject の OnValidate（Unity が値の点検で呼ぶ、外からは呼べない処理）が番号を付け直すので、
        // リフレクション（名前を文字で指定して呼ぶ方法）で呼ぶ。Netcode の版が変わって名前が無くなると、
        // validate が null になり、エラーも出ずに番号が付け直されなくなる点に注意
        System.Reflection.MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        foreach (NetworkObject networkObject in Object.FindObjectsByType<NetworkObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }
    }
}
