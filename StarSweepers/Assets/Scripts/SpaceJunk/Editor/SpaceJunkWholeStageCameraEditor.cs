using UnityEditor;

/// <summary>
/// **全体カメラ（<see cref="SpaceJunkWholeStageCamera"/>）のインスペクターから、「参照」の Target の欄を外す。**（2026/11/2・大槻さん）
///
/// Target（追いかける相手）はカメラの種類の共通部品にある欄だが、全体カメラは誰も追いかけない
/// （見る先は「手で位置を決める」の Look Target で決める）ので、出しておくと迷うため隠す。
/// ほかの欄は、ふだんのインスペクターと同じに出す（見出し・説明・値の範囲もそのまま）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
[CustomEditor(typeof(SpaceJunkWholeStageCamera))]
public class SpaceJunkWholeStageCameraEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "target");
        serializedObject.ApplyModifiedProperties();
    }
}
