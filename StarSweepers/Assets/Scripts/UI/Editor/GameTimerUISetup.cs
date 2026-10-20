using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **タイマー（<see cref="GameTimerUI"/>）のプレハブと、単体確認用のシーンを作成するツール。**
///
/// Unityのメニューから実行する。
/// ・`Tools > StarSweepers > タイマーUIの検証シーンを作る` … プレハブを更新し、検証シーンを作り直す
/// ・`Tools > StarSweepers > タイマーUIプレハブを更新` … プレハブだけ更新する
///
/// 文字に TextMeshPro を使うので、その標準フォント（LiberationSans SDF）が無ければ先に取り込む。
/// 取り込みは非同期なので、終わってから改めてシーンを作る（<see cref="OnTmpResourcesImported"/>）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class GameTimerUISetup
{
    // 画像素材の置き場（背景と、白いリングのゲージ）
    private const string AssetFolder = "Assets/Scenes/Test/shotaro";
    private const string BackdropPath = AssetFolder + "/UI_Timer_Back.png";
    private const string GaugePath = AssetFolder + "/UI_Timer_Radial.png";
    private const string PrefabPath = "Assets/Resources/GameTimerUI.prefab";

    // TextMeshPro の標準の置き場
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    // 以前に Resources の直下へ仮置きしていた場所。残っていれば標準の置き場へ戻す
    private const string TemporaryFontPath = "Assets/Resources/GameTimerFont.asset";
    private const string TemporarySettingsPath = "Assets/Resources/TMP Settings.asset";
    private const string ScenePath = "Assets/Scenes/Test/GameTimerUITest.unity";

    /// <summary>プレハブを更新してから、それを置いただけの検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/タイマーUIの検証シーンを作る")]
    public static void CreateTestScene()
    {
        if (!EnsureTmpFont()) return;

        ConfigureSprite(BackdropPath);
        CreateGaugeSpriteIfMissing();
        ConfigureSprite(GaugePath);
        AssetDatabase.Refresh();

        GameObject prefab = UpdatePrefab();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Canvas canvas = CreateCanvas();
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        instance.name = "GameTimerUI";
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"タイマーUIのプレハブと検証シーンを作成しました: {PrefabPath} / {ScenePath}");
        // コマンドラインから Unity を起動して実行したときは、終わったら Unity を閉じる
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>画像の設定とプレハブだけを更新する（検証シーンは作り直さない）。</summary>
    [MenuItem("Tools/StarSweepers/タイマーUIプレハブを更新")]
    public static void UpdateTimerPrefabOnly()
    {
        ConfigureSprite(GaugePath);
        AssetDatabase.Refresh();
        UpdatePrefab();
        AssetDatabase.SaveAssets();
        Debug.Log($"タイマーUIプレハブを更新しました: {PrefabPath}");
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// TextMeshPro の標準フォントが使える状態にする。
    /// すでにあれば true。無ければ標準の素材パッケージの取り込みを始めて false を返す
    /// （取り込みが終わると <see cref="OnTmpResourcesImported"/> が呼ばれ、そこからシーン作成をやり直す）。
    /// </summary>
    private static bool EnsureTmpFont()
    {
        RestoreTmpResourceLocation(TemporarySettingsPath, TmpSettingsPath);
        RestoreTmpResourceLocation(TemporaryFontPath, FontPath);
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) != null) return true;

        string settingsPath = FindTmpAsset("t:TMP_Settings", "TMP Settings");
        string fontPath = FindTmpAsset("t:TMP_FontAsset", "LiberationSans SDF");
        if (!string.IsNullOrEmpty(settingsPath) && !string.IsNullOrEmpty(fontPath))
        {
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) != null;
        }

        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui/Runtime/TMP/TMP_Text.cs");
        if (package == null)
        {
            Debug.LogError("TextMeshProの必須素材パッケージが見つかりません。");
            return false;
        }

        string resourcesPackage = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
        if (!File.Exists(resourcesPackage))
        {
            Debug.LogError($"TextMeshProの標準フォント素材がありません: {resourcesPackage}");
            return false;
        }

        AssetDatabase.importPackageCompleted += OnTmpResourcesImported;
        AssetDatabase.ImportPackage(resourcesPackage, false);
        Debug.Log("TextMeshPro標準フォントを取り込みます。取り込み完了後、タイマーUIを作成します。");
        return false;
    }

    /// <summary>標準の素材パッケージの取り込みが終わったら、フォントがあることを確かめて、シーン作成をやり直す。</summary>
    private static void OnTmpResourcesImported(string packageName)
    {
        AssetDatabase.importPackageCompleted -= OnTmpResourcesImported;
        string settingsPath = FindTmpAsset("t:TMP_Settings", "TMP Settings");
        string fontPath = FindTmpAsset("t:TMP_FontAsset", "LiberationSans SDF");
        if (string.IsNullOrEmpty(settingsPath) || string.IsNullOrEmpty(fontPath))
        {
            Debug.LogError("TextMeshProの設定または標準フォントが見つかりません。");
            return;
        }

        AssetDatabase.Refresh();
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) != null)
            CreateTestScene();
    }

    /// <summary>プロジェクトの中から、その種類・その名前の素材を探してパスを返す。無ければ空文字。</summary>
    private static string FindTmpAsset(string filter, string assetName)
    {
        foreach (string guid in AssetDatabase.FindAssets(filter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == assetName) return path;
        }
        return string.Empty;
    }

    /// <summary>仮置きの場所に素材が残っていて、標準の場所に無ければ、標準の場所へ移す（参照が外れないよう Unity の機能で移す）。</summary>
    private static void RestoreTmpResourceLocation(string temporaryPath, string standardPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(temporaryPath) == null || AssetDatabase.LoadMainAssetAtPath(standardPath) != null) return;
        string error = AssetDatabase.MoveAsset(temporaryPath, standardPath);
        if (!string.IsNullOrEmpty(error)) Debug.LogError($"TextMeshPro素材を移動できませんでした: {error}");
    }

    /// <summary>
    /// 白いリングの画像（GameTimerGauge.png）を、無ければコードで描いて保存する。
    /// 512×512 で、外側の半径は 46%、内側は 39%。ふちは ±1.5px なめらかにぼかす。
    /// （いまのプレハブは提供された UI_Timer_Radial.png を使っているので、この画像は予備）
    /// </summary>
    private static void CreateGaugeSpriteIfMissing()
    {
        if (File.Exists(Path.Combine(Application.dataPath, "Scenes", "Test", "shotaro", "GameTimerGauge.png"))) return;

        const int resolution = 512;
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
        texture.name = "GameTimerGauge";
        float center = (resolution - 1) * 0.5f;
        float outerRadius = resolution * 0.46f;
        float innerRadius = resolution * 0.39f;
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            // 中心からの距離が「内側の半径〜外側の半径」の間だけ不透明にする（リングの形）
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
            float outer = 1f - Mathf.SmoothStep(outerRadius - 1.5f, outerRadius + 1.5f, distance);
            float inner = Mathf.SmoothStep(innerRadius - 1.5f, innerRadius + 1.5f, distance);
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, outer * inner));
        }

        texture.Apply();
        string fullPath = Path.Combine(Application.dataPath, "Scenes", "Test", "shotaro", "GameTimerGauge.png");
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(GaugePath);
    }

    /// <summary>画像を UI 用のスプライト（1枚絵・透過あり）として読み込む設定にする。</summary>
    private static void ConfigureSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    /// <summary>検証シーン用の Canvas を作る（1920×1080 を基準に、画面の大きさに合わせて伸び縮みする）。</summary>
    private static Canvas CreateCanvas()
    {
        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return canvas;
    }

    /// <summary>
    /// 既存のプレハブ（Backdrop / Gauge / TimeText の3つの子を持つ）を開いて、画像・文字・部品のつなぎを設定し直して保存する。
    /// 古い uGUI の Text が残っていれば、TextMeshPro の文字に置き換える。
    /// </summary>
    private static GameObject UpdatePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        root.name = "GameTimerUI";
        // 画面の左上を基準にする
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);

        Transform backdropTransform = root.transform.Find("Backdrop");
        Transform gaugeTransform = root.transform.Find("Gauge");
        Transform textTransform = root.transform.Find("TimeText");
        Image backdrop = backdropTransform.GetComponent<Image>();
        Image gauge = gaugeTransform.GetComponent<Image>();
        TMP_Text oldTmpText = textTransform.GetComponent<TMP_Text>();
        if (oldTmpText == null)
        {
            Text oldText = textTransform.GetComponent<Text>();
            if (oldText != null) Object.DestroyImmediate(oldText, true);
            oldTmpText = textTransform.gameObject.AddComponent<TextMeshProUGUI>();
        }
        TMP_Text text = oldTmpText;

        // 背景は 240px 角、ゲージはその内側に収まる 220px 角
        ConfigureImage(backdrop, BackdropPath, 240f);
        ConfigureImage(gauge, GaugePath, 220f);
        gauge.type = Image.Type.Filled;
        gauge.fillMethod = Image.FillMethod.Radial360;
        gauge.fillOrigin = (int)Image.Origin360.Top;
        // 向き（時計回りか）は、再生したときに GameTimerUI.UpdateVisuals が決め直す
        gauge.fillClockwise = true;
        gauge.fillAmount = 1f;
        RectTransform textRect = textTransform.GetComponent<RectTransform>();
        textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(170f, 70f);
        text.text = "2:00";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 36f;
        text.color = Color.white;
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        // 描く順番：背景 → ゲージ → 文字（後ろの物ほど手前に描かれる）
        backdropTransform.SetAsFirstSibling();
        gaugeTransform.SetSiblingIndex(1);
        textTransform.SetAsLastSibling();

        GameTimerUI timer = root.GetComponent<GameTimerUI>();
        if (timer == null) timer = root.AddComponent<GameTimerUI>();
        // private な [SerializeField] にエディタから値を入れる。文字列はフィールド名そのもの
        var serialized = new SerializedObject(timer);
        serialized.FindProperty("backdropImage").objectReferenceValue = backdrop;
        serialized.FindProperty("gaugeImage").objectReferenceValue = gauge;
        serialized.FindProperty("timeText").objectReferenceValue = text;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return saved;
    }

    /// <summary>画像を差し替えて、親の中央に <paramref name="size"/> px 角で置く。</summary>
    private static void ConfigureImage(Image image, string spritePath, float size)
    {
        RectTransform rect = image.GetComponent<RectTransform>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        image.preserveAspect = true;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
    }
}
