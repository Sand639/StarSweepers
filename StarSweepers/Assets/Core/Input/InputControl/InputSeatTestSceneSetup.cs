#if UNITY_EDITOR
using ProjectEL4S.MultiKeyboard;
using ProjectEL4S.MultiMouse;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectEL4S.InputControl
{
    /// <summary>
    /// **席ごとの入力（InputSeatManager）の検証シーンを作るツール。**
    ///
    /// Unityのメニュー「Tools > StarSweepers > 席ごとの入力の検証シーンを作る」から実行できる。
    /// 作られるシーン：`Assets/Scenes/Test/InputSeatTest.unity`
    ///
    /// カメラと、管理役（キーボード・マウス・席）＋確認用 HUD を付けた GameObject が 1 つだけ入る。
    /// 再生すれば、キーボード 2 台・マウス 2 台・コントローラで P1 / P2 を動かして確かめられる。
    ///
    /// ※ Editor フォルダの外にあるが、全体を UNITY_EDITOR で囲んであるのでゲームのビルドには含まれない
    ///   （Assets/ に新しいフォルダを作らないため）。
    /// ※ 何度実行しても作り直せる（シーンを上書きする）。
    /// </summary>
    public static class InputSeatTestSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/Test/InputSeatTest.unity";

        [MenuItem("Tools/StarSweepers/席ごとの入力の検証シーンを作る")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            // 管理役（-100）→ 席（-90）→ HUD の順に動く。付ける順番は関係ない
            var root = new GameObject("InputSeats");
            root.AddComponent<MultiKeyboardManager>();
            root.AddComponent<MultiMouseManager>();
            root.AddComponent<InputSeatManager>();
            root.AddComponent<InputSeatTestHud>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "席ごとの入力の検証シーンを作りました。\n" +
                "シーン: " + ScenePath + "\n" +
                "・キーボードのキーを押す／マウスを動かす／コントローラのボタンを押すと、P1・P2 の順に入る\n" +
                "・F6 で固定登録（キーボード P1→P2、マウス P1→P2 の順に押す）。次からは起動しただけでその番号になる\n" +
                "・F5 割り当てやり直し / F7 登録をやめる / F8 登録を消す");
        }
    }
}
#endif
