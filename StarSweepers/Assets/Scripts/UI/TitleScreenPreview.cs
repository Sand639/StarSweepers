using UnityEngine;

/// <summary>
/// **使わなくなった。**（2026/10/6。タイトル画面をプレハブ `Assets/Resources/TitleScreen.prefab` に組み直したため、編集中の見本は要らなくなった）
/// Unity の Project ウィンドウで、このファイルを削除してよい（OS のエクスプローラーでは消さないこと）。
///
/// 前の版が編集中のシーンに出していた見本が残っていたら、ここで消す。
/// </summary>
[ExecuteAlways]
[AddComponentMenu("")]
public class TitleScreenPreview : MonoBehaviour
{
    private void OnEnable()
    {
#if UNITY_EDITOR
        GameObject target = gameObject;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (target != null)
            {
                DestroyImmediate(target);
            }
        };
#else
        Destroy(gameObject);
#endif
    }
}
