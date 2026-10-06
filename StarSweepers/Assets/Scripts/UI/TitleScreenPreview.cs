using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// **編集中（再生していないとき）に、タイトル画面の見本を Scene・Game ビューへ出す。**（2026/10/6・大槻さん
/// 「実行前の Scene と Game ビューにオブジェクトが一切なくて、どこから編集するのか分からない」）
///
/// タイトル画面（<see cref="TitleScreen"/>）は、日本語のフォントを再生したときに借りる都合で、**再生してからコードで組み立てている。**
/// そのままでは編集中に何も見えないので、**TitleScene を開いている間だけ**、同じ組み立て方で作った見本を出す。
///
/// ・Hierarchy に「タイトル画面の見本（編集用・保存されない）」が出る。**これを選ぶと、インスペクターでテーマ（画像・色）を直接変えられる**
/// ・テーマを変えると、すぐ見本に反映される。見本に出す画面（メイン・サーバーを作る・設定 など）も選べる
/// ・見本は**シーンに保存されない**。再生すると消え、本物のタイトル画面が出る。ほかのシーンを開いても消える
/// ・見本のボタンを押しても何も起きない。ボタンの並びや文言を変えたいときは <see cref="TitleScreen"/> のコードを直す
///
/// シーンに置く必要はない（TitleScene を開くと自動で出る）。
/// </summary>
[ExecuteAlways]
[AddComponentMenu("")]
public class TitleScreenPreview : MonoBehaviour
{
    [Tooltip("見本に出す画面")]
    [SerializeField] private TitleScreen.Page page = TitleScreen.Page.Main;

    [Tooltip("見た目の設定（Assets/Resources/TitleScreenTheme.asset）。下に中身が出るので、ここで画像や色を変えられる")]
    [SerializeField] private TitleScreenTheme theme;

    /// <summary>見本に使っているテーマ。</summary>
    public TitleScreenTheme Theme => theme;

    /// <summary>見本に出している画面。</summary>
    public TitleScreen.Page Page => page;

#if UNITY_EDITOR
    private const string TitleSceneName = "TitleScene";
    private const string PreviewName = "タイトル画面の見本（編集用・保存されない）";
    private const string ThemeResourcePath = "TitleScreenTheme";

    private static bool refreshQueued;

    /// <summary>いま出ている見本（2つ作らないように持っておく）。</summary>
    private static TitleScreenPreview current;
    private bool rebuildQueued;

    // ------------------------------------------------------------
    // TitleScene を開いている間だけ、見本を置く
    // ------------------------------------------------------------

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => QueueRefresh();
        EditorSceneManager.sceneClosing += (scene, removing) => RemoveAll();
        EditorSceneManager.activeSceneChangedInEditMode += (before, after) => QueueRefresh();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        QueueRefresh();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            // 再生に入る前に消す（残っていると、再生中も見本が出たままになる）
            RemoveAll();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            QueueRefresh();
        }
    }

    private static void QueueRefresh()
    {
        if (refreshQueued)
        {
            return;
        }

        refreshQueued = true;
        EditorApplication.delayCall += () =>
        {
            refreshQueued = false;
            Refresh();
        };
    }

    /// <summary>TitleScene を開いていれば見本を1つ置き、そうでなければ消す。</summary>
    private static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (SceneManager.GetActiveScene().name != TitleSceneName)
        {
            RemoveAll();
            return;
        }

        if (current != null ||
            FindObjectsByType<TitleScreenPreview>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            return;
        }

        GameObject holder = new GameObject(PreviewName);
        holder.hideFlags = HideFlags.DontSave;

        TitleScreenPreview preview = holder.AddComponent<TitleScreenPreview>();
        preview.hideFlags = HideFlags.DontSave;
    }

    private static void RemoveAll()
    {
        if (current != null)
        {
            DestroyImmediate(current.gameObject);
            current = null;
        }

        foreach (TitleScreenPreview preview in FindObjectsByType<TitleScreenPreview>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (preview != null)
            {
                DestroyImmediate(preview.gameObject);
            }
        }
    }

    // ------------------------------------------------------------
    // 見本を組み立てる
    // ------------------------------------------------------------

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            // 再生中には要らない（本物のタイトル画面が出る）
            Destroy(gameObject);
            return;
        }

        current = this;
        TitleScreenTheme.EditorChanged += QueueRebuild;
        Rebuild();
    }

    private void OnDisable()
    {
        TitleScreenTheme.EditorChanged -= QueueRebuild;
    }

    private void OnValidate()
    {
        // 見本に出す画面を変えたとき（OnValidate の中では作り直せないので、少し後で）
        QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (rebuildQueued)
        {
            return;
        }

        rebuildQueued = true;
        EditorApplication.delayCall += () =>
        {
            rebuildQueued = false;
            if (this != null && isActiveAndEnabled && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Rebuild();
            }
        };
    }

    private void Rebuild()
    {
        // 前の見本を消す（スクリプトを直して読み込み直したあとも、古い見本が残っているので全部消す）
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        if (theme == null)
        {
            theme = Resources.Load<TitleScreenTheme>(ThemeResourcePath);
        }

        GameObject screenObject = new GameObject("TitleScreen（見本）");
        screenObject.transform.SetParent(transform, false);

        TitleScreen screen = screenObject.AddComponent<TitleScreen>();
        screen.BuildPreview(theme, page);

        // 見本の中身は保存しない・触れない（直しても作り直すと消えるので、触れないようにしておく）
        foreach (Transform child in screenObject.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

            foreach (Component component in child.GetComponents<Component>())
            {
                if (component != null)
                {
                    component.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                }
            }
        }

        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
#endif
}

#if UNITY_EDITOR
/// <summary>見本のインスペクター。テーマの中身をそのまま出して、ここで変えられるようにする。</summary>
[CustomEditor(typeof(TitleScreenPreview))]
public class TitleScreenPreviewEditor : Editor
{
    private Editor themeEditor;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "これは編集用の見本です（シーンに保存されず、再生すると消えます）。\n" +
            "見た目は下のテーマ（Assets/Resources/TitleScreenTheme.asset）で変えます。変えるとすぐ見本に反映されます。\n" +
            "Game ビューで見るのがおすすめです。ボタンの並びや文言は TitleScreen.cs で組み立てています。",
            MessageType.Info);

        DrawDefaultInspector();

        TitleScreenTheme theme = ((TitleScreenPreview)target).Theme;
        if (theme == null)
        {
            EditorGUILayout.HelpBox("テーマが見つかりません（Assets/Resources/TitleScreenTheme.asset）。", MessageType.Warning);
            return;
        }

        if (GUILayout.Button("テーマのファイルを Project で見せる"))
        {
            EditorGUIUtility.PingObject(theme);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("テーマ（TitleScreenTheme）", EditorStyles.boldLabel);

        CreateCachedEditor(theme, null, ref themeEditor);
        themeEditor.OnInspectorGUI();
    }

    private void OnDisable()
    {
        if (themeEditor != null)
        {
            DestroyImmediate(themeEditor);
        }
    }
}
#endif
