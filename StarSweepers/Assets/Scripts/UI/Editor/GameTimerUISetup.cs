using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>タイマーのプレハブと単体確認用シーンを作成する。</summary>
public static class GameTimerUISetup
{
    private const string AssetFolder = "Assets/Scenes/Test/shotaro";
    private const string BackdropPath = AssetFolder + "/UI_Timer_Back.png";
    private const string GaugePath = AssetFolder + "/UI_Timer_IMG.png";
    private const string PrefabPath = "Assets/Resources/GameTimerUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/GameTimerUITest.unity";

    [MenuItem("Tools/StarSweepers/タイマーUIの検証シーンを作る")]
    public static void CreateTestScene()
    {
        ConfigureSprite(BackdropPath);
        ConfigureSprite(GaugePath);
        AssetDatabase.Refresh();

        GameObject prefab = CreatePrefab();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Canvas canvas = CreateCanvas();
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        instance.name = "GameTimerUI";
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"タイマーUIのプレハブと検証シーンを作成しました: {PrefabPath} / {ScenePath}");
    }

    private static void ConfigureSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

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

    private static GameObject CreatePrefab()
    {
        var root = new GameObject("GameTimerUI", typeof(RectTransform), typeof(GameTimerUI));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(36f, -36f);
        rootRect.sizeDelta = new Vector2(240f, 240f);

        Image backdrop = CreateImage("Backdrop", root.transform, BackdropPath, 240f);
        Image gauge = CreateImage("Gauge", root.transform, GaugePath, 190f);
        Text text = CreateText(root.transform);

        GameTimerUI timer = root.GetComponent<GameTimerUI>();
        var serialized = new SerializedObject(timer);
        serialized.FindProperty("backdropImage").objectReferenceValue = backdrop;
        serialized.FindProperty("gaugeImage").objectReferenceValue = gauge;
        serialized.FindProperty("timeText").objectReferenceValue = text;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static Image CreateImage(string name, Transform parent, string spritePath, float size)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        image.preserveAspect = true;
        return image;
    }

    private static Text CreateText(Transform parent)
    {
        var gameObject = new GameObject("TimeText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(170f, 70f);
        Text text = gameObject.GetComponent<Text>();
        text.text = "2:00";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 42;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return text;
    }
}
