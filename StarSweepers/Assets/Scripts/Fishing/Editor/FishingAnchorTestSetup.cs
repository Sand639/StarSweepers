using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 固定アンカーのプレハブと単体検証シーンを作るメニュー。
///
/// Unityのメニュー「Tools > StarSweepers > 釣りのアンカー検証シーンを作る」から実行できる。
/// できるもの：
///   ・`Assets/Prefabs/FishingAnchor.prefab` … 固定アンカー（青い箱。フックを掛けると、プレイヤーのほうが引っ張られる）
///   ・`Assets/Scenes/Test/FishingAnchorTest.unity` … まっすぐな四方の壁・プレイヤー・アンカー1つだけの検証シーン
///
/// **プレハブもシーンも、実行するたびに作り直す**（手で変えた所は消える）。
/// 仕組みの説明は <see cref="AnchorGimmick"/> と `Documents/機能ドキュメント/釣りの固定アンカー.md`。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingAnchorTestSetup
{
    // ※ プレハブは `Assets/Prefabs` の直下に作る（釣りのほかのプレハブがある `Prefabs/Fish` の下ではない）
    private const string AnchorPrefabPath = FishingSceneBuilder.PrefabFolder + "/FishingAnchor.prefab";
    private const string AnchorMaterialPath = FishingSceneBuilder.MaterialFolder + "/FishingAnchor.mat";
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingAnchorTest.unity";

    [MenuItem("Tools/StarSweepers/釣りのアンカー検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();
        // ※ ほかのツールと違い、新しいシーンを開く**前に**プレハブを作るので、
        //   作業用の箱が一瞬だけ「今開いているシーン」に置かれる（すぐ消す）
        GameObject anchorPrefab = CreateOrUpdatePrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();
        FishingSceneBuilder.CreatePlainWalls();

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);
        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        // プレハブとのつながりを残したまま置く（プレハブを直せば、このシーンのアンカーにも効く）。
        // 位置は、プレイヤー（原点）の 10m 北。高さ 0.75m は 1.5m の箱の半分で、床にちょうど乗る
        GameObject anchor = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab);
        anchor.name = "Anchor";
        anchor.transform.position = new Vector3(0f, 0.75f, 10f);

        // このシーンには点数の仕組みが無いので、点数の表示と点数表はつながない
        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions,
            statusUI: null, scoreBoard: null);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("【アンカー】検証シーンを作りました。WASDで移動し、左クリックで青いキューブへフックを飛ばしてください。アンカーは動かず、プレイヤーが引っ張られます。");
    }

    /// <summary>
    /// アンカーのプレハブ（1.5m の青い箱）を作って保存する（すでにあれば上書きする）。
    /// フックが掛かるよう HookableObject を付け、引っ張られる側をプレイヤーにする AnchorGimmick を付ける。
    /// </summary>
    private static GameObject CreateOrUpdatePrefab()
    {
        Material material = FishingSceneBuilder.GetOrCreateMaterial(AnchorMaterialPath, new Color(0.1f, 0.38f, 0.95f));
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        anchor.name = "FishingAnchor";
        anchor.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        anchor.GetComponent<MeshRenderer>().sharedMaterial = material;

        // アンカー自身はまったく動かない（押されても、引っ張られても、重力でも）
        Rigidbody body = anchor.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;

        anchor.AddComponent<HookableObject>();
        anchor.AddComponent<AnchorGimmick>();

        // プレイヤーを引き寄せる先の目印（箱の中心）。
        // ※ AnchorGimmick の Pull Point にはつないでいない。空のときも箱の中心を使うので、今は結果が同じ
        GameObject pullPoint = new GameObject("PullPoint");
        pullPoint.transform.SetParent(anchor.transform, false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(anchor, AnchorPrefabPath);
        Object.DestroyImmediate(anchor);
        return prefab;
    }
}
