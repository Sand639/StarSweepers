using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **釣りフックの「基本」検証シーンを作るツール。**（従来からあるツール）
///
/// Unityのメニュー「Tools > StarSweepers > 釣りフックの検証シーンを作る」から実行できる。
/// 作られるシーン：`Assets/Scenes/Test/FishingHookTest.unity`
///
/// **フックの操作だけを確かめるための、最小構成のシーン。**
///   ・見下ろしカメラ、床、まっすぐな四方の壁
///   ・マウス方向を向くプレイヤー（既存の PlayerRig を土台に使用）
///   ・引っ掛けられる物資6個
///   ・チャージ量とスキルチェックのゲージ
///
/// **ポケット・スコア・爆発物は入っていない。**
/// それらを含むシーンは「Tools > StarSweepers > 釣りフックのステージ検証シーンを作る」で作る
/// （<see cref="FishingArenaTestSetup"/>）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingHookTestSetup
{
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingHookTest.unity";

    [MenuItem("Tools/StarSweepers/釣りフックの検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();
        FishingSceneBuilder.CreatePlainWalls();

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);

        // 物資6個を、中心から 7.5m の円の上に並べる（消えても6秒で戻ってくる）
        FishingSceneBuilder.CreateSupplyRing(6, 7.5f);

        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        // このシーンには点数の仕組みが無いので、点数の表示と点数表はつながない
        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions,
            statusUI: null, scoreBoard: null);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【基本】釣りフックの検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・WASDで移動、マウスで向き。左クリックを押し込んでチャージ→離してフック発射\n" +
            "・物資に当たると引き寄せが始まり、ゲージが1回だけ左から右へ動く\n" +
            "・前半（青）の枠で押すと後ろへ、後半（橙）の枠で押すと前へ飛ばす（枠を外すとミス）\n" +
            "※ ポケット・スコア・爆発物はこのシーンには入っていません。\n" +
            "　 それらを試すときは Tools > StarSweepers > 釣りフックのステージ検証シーンを作る を使ってください。");
    }
}
