using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ラウンド勝利の転場（<see cref="RoundWinWipe"/>）の検証シーンを作るツール。**（試作・2026/10/28）
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > 勝利の転場 > 検証シーンを作る（作り直す）` | シーンを作り直す（`Assets/Scenes/Test/RoundWinWipeTest.unity`） |
/// | `Tools > StarSweepers > 勝利の転場 > 縁のマテリアルを作り直す` | シーンは触らず、縁のシェーダーのマテリアルだけ作り直す |
///
/// ## 注意点
///
/// - 実行すると **いま開いているシーンは閉じられ、新しいシーンに差し替わる。**
///   保存していない変更があれば、Unity が先に「保存しますか？」と聞いてくる
/// - すでに `RoundWinWipeTest.unity` がある場合は **上書きして作り直す**（手で直した値は消える）。
///   値を残したければ、シーンを複製してから実行すること
/// - **このシーンに置いてある文字（Text）の Font はわざと空にしてある。**
///   日本語を出せる文字は保存できないため、再生されたときに <see cref="RoundWinWipe"/> が入れる
///   （プロジェクト中の他の画面と同じやり方。`UiFont`）
/// - 縁の形（水波・炎）と火星は **シェーダーで描いている**（`Assets/Art/Shaders/UIWipeEdge.shader`）。
///   パーティクルを使っていないのは、Overlay の Canvas の下に隠れて見えないため
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class RoundWinWipeTestSetup
{
    private const string ScenePath = "Assets/Scenes/Test/RoundWinWipeTest.unity";
    private const string MaterialPath = "Assets/Art/Materials/UIWipeEdge.mat";

    // 背景の色（インスペクターで変えられる。初期値だけここで決める）
    private static readonly Color BackgroundGray = new Color(0.28f, 0.28f, 0.31f);
    private static readonly Color BackgroundYellow = new Color(0.85f, 0.72f, 0.25f);

    // チームの色。**SpaceJunkTeams の初期値と同じ**（このシーンを単体で動くように、あえて参照していない）
    private static readonly Color TeamRed = new Color(0.95f, 0.45f, 0.35f);
    private static readonly Color TeamBlue = new Color(0.30f, 0.60f, 0.95f);

    // 縁の光と火星の色（赤は暖かい色、青は冷たい色）
    private static readonly Color RedRim = new Color(1f, 0.78f, 0.35f);
    private static readonly Color RedSpark = new Color(1f, 0.55f, 0.20f);
    private static readonly Color BlueRim = new Color(0.70f, 0.92f, 1f);
    private static readonly Color BlueSpark = new Color(0.55f, 0.85f, 1f);

    // 炎の内側の2枚の色（Balatro のスコア盤の炎と同じ「外側＝帯の色・真ん中＝明るい色・芯＝ほぼ白」の3枚重ね）
    private static readonly Color RedFlameMid = new Color(1f, 0.75f, 0.20f);
    private static readonly Color RedFlameCore = new Color(1f, 0.95f, 0.75f);
    private static readonly Color BlueFlameMid = new Color(0.40f, 0.90f, 1f);
    private static readonly Color BlueFlameCore = new Color(0.85f, 0.97f, 1f);

    /// <summary>
    /// 検証シーンを新しく作って保存する。
    /// 背景・純色の帯・文字・CHANGE ボタン・案内の文字を置き、<see cref="RoundWinWipe"/> へつなぐ。
    /// </summary>
    [MenuItem("Tools/StarSweepers/勝利の転場/検証シーンを作る（作り直す）")]
    public static void CreateTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[WINWIPE] シーン作りを中止しました（いま開いているシーンをそのままにしています）。");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        Canvas canvas = CreateCanvas();

        // 描かれる順番は「子の並び順」。あとに並ぶ物ほど手前に描かれる。
        // 背景 → ボタン → 帯 → 文字 → 案内　（帯がボタンの上を通るので、転場中はボタンが隠れる）
        Image background = CreateFullScreenImage(canvas.transform, "Background", BackgroundGray);

        Button changeButton = CreateChangeButton(canvas.transform, out Text buttonText);

        // 帯。縁のシェーダーを入れる（入らなければ RoundWinWipe がまっすぐな縁に落とす）
        Image band = CreateFullScreenImage(canvas.transform, "WipeBand", TeamRed);
        Material wipeMaterial = GetOrCreateWipeMaterial();
        bool shaderReady = wipeMaterial != null;
        if (shaderReady)
        {
            band.material = wipeMaterial;
        }

        Text label = CreateLabelText(canvas.transform);
        Text hint = CreateHintText(canvas.transform);

        RoundWinWipe wipe = CreateWipe(canvas.transform, background, band, label, changeButton, buttonText, hint);

        CreateEventSystem();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = wipe.gameObject;
        EditorGUIUtility.PingObject(wipe.gameObject);

        Debug.Log($"[WINWIPE] 勝利の転場の検証シーンを作りました：{ScenePath}\n" +
                  (shaderReady
                      ? $"　縁のシェーダー（{RoundWinWipe.WipeShaderName}）を入れてあるので、水波・炎・火星が出ます。\n"
                      : "　**縁のマテリアルが作れませんでした。** まっすぐな縁で動きます（Console の赤い文字を見てください）。\n") +
                  "そのまま ▶（再生）を押して、画面の下の「きりかえ / CHANGE」ボタンを押してください。\n" +
                  "　1回目 … 赤い帯が左→右へ扫けて「RED WIN」、背景が灰から黄に変わる\n" +
                  "　2回目 … 青い帯が右→左へ扫けて「BLUE WIN」、背景が黄から灰に戻る\n" +
                  "ボタンを押さなくても、スペースキーで同じことができます。\n" +
                  "調整：← → ＝掃く速さ、↑ ↓ ＝文字の秒数、E ＝縁の形（まっすぐ／水波／炎）、R ＝最初に戻す。\n" +
                  "　縁の細かさ・火星の量などは、RoundWinWipe のインスペクターで変えられます（再生中に変えるとすぐ反映されます）。\n" +
                  "※再生中に変えた値は保存されません。気に入った値は停止してからインスペクターに書き写してください。");

        // コマンドラインから Unity を起動して実行したときは、終わったら Unity を閉じる
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(0);
        }
    }

    /// <summary>縁のマテリアルだけを作り直す（シーンは触らない）。シェーダーの値をいじったあとに使う。</summary>
    [MenuItem("Tools/StarSweepers/勝利の転場/縁のマテリアルを作り直す")]
    public static void RecreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
        {
            AssetDatabase.DeleteAsset(MaterialPath);
        }

        Material material = GetOrCreateWipeMaterial();

        if (material == null)
        {
            Debug.LogError($"[WINWIPE] 縁のマテリアルを作れませんでした。シェーダー（{RoundWinWipe.WipeShaderName}）が見つかりません。\n" +
                           "`Assets/Art/Shaders/UIWipeEdge.shader` があるか、Console にシェーダーの赤いエラーが出ていないかを確認してください。");
            return;
        }

        AssetDatabase.SaveAssets();
        Selection.activeObject = material;
        EditorGUIUtility.PingObject(material);
        Debug.Log($"[WINWIPE] 縁のマテリアルを作り直しました：{MaterialPath}\n" +
                  "すでにシーンへ置いた帯の Material は、この資産を指したままなので差し替わりません（値は初期値に戻っています）。");
    }

    // ------------------------------------------------------------
    // 素材
    // ------------------------------------------------------------

    /// <summary>
    /// 縁を描くシェーダーのマテリアル。無ければ作って保存する。
    /// シェーダーが見つからなければ null（呼び出した側が、まっすぐな縁に落とす）。
    /// </summary>
    private static Material GetOrCreateWipeMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(RoundWinWipe.WipeShaderName);

        if (shader == null)
        {
            Debug.LogError($"[WINWIPE] シェーダー（{RoundWinWipe.WipeShaderName}）が見つかりません。\n" +
                           "`Assets/Art/Shaders/UIWipeEdge.shader` があるか、Console にシェーダーの赤いエラーが出ていないかを確認してください。\n" +
                           "　シェーダーのコンパイルに失敗していると、Shader.Find は null を返します。");
            return null;
        }

        Material material = new Material(shader) { name = "UIWipeEdge" };

        // 再生していないときにも帯が見えるように、境界を画面の真ん中にしておく
        // （再生されると RoundWinWipe が毎フレーム入れ直すので、この値は編集画面のための物）
        material.SetFloat("_Boundary", 0.5f);
        material.SetFloat("_CoverSign", 1f);
        material.SetFloat("_EdgeStyle", 2f); // 炎

        AssetDatabase.CreateAsset(material, MaterialPath);

        Debug.Log($"[WINWIPE] 縁のマテリアルを作りました：{MaterialPath}");
        return material;
    }

    // ------------------------------------------------------------
    // 部品を作る
    // ------------------------------------------------------------

    /// <summary>カメラ。**画面の物はすべて Overlay の Canvas なので、このカメラは背景を塗るだけの役割。**</summary>
    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
        camera.orthographic = true;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();
    }

    /// <summary>画面の Canvas（1920×1080 を基準に、窓の大きさに合わせて伸び縮みする）。</summary>
    private static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // タイトル画面（90）・ポーズ画面（100）より後ろ（ラウンドの始まりの演出と同じ）
        canvas.sortingOrder = 85;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    /// <summary>画面いっぱいに広がる純色の絵。背景と帯の両方をこれで作る。</summary>
    private static Image CreateFullScreenImage(Transform parent, string name, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)imageObject.transform;
        // 四隅を画面の四隅に貼り付ける（これで窓の大きさを変えても画面いっぱいになる）
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        // 画像は貼らずに純色だけ（Sprite を空にすると、白い四角が Color の色で塗られる）
        image.sprite = null;
        image.raycastTarget = false;

        return image;
    }

    /// <summary>画面の下に出る「きりかえ / CHANGE」ボタン。</summary>
    private static Button CreateChangeButton(Transform parent, out Text buttonText)
    {
        GameObject buttonObject = new GameObject("ChangeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)buttonObject.transform;
        // 画面の下の中央を基準にする
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 48f);
        rect.sizeDelta = new Vector2(360f, 92f);

        Image back = buttonObject.GetComponent<Image>();
        back.color = new Color(0.12f, 0.12f, 0.15f, 0.88f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.90f, 1f);
        colors.pressedColor = new Color(0.65f, 0.70f, 0.85f);
        colors.selectedColor = new Color(0.85f, 0.90f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;

        buttonText = CreateChildText(buttonObject.transform, "Label", "きりかえ / CHANGE", 34);

        return button;
    }

    /// <summary>親の枠いっぱいに広がる文字（ボタンの名前など）。</summary>
    private static Text CreateChildText(Transform parent, string name, string content, int size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)textObject.transform;
        // 四隅を親の四隅に貼り付ける（ボタンと同じ大きさになる）
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        // **Font はわざと空。** 再生されたときに RoundWinWipe が日本語の出せる文字を入れる
        text.font = null;

        return text;
    }

    /// <summary>
    /// 画面の真ん中に出る「RED WIN」の文字。
    /// **縁取り（Outline）は付けない。純白だけ**（2026/10/28・呉さん「黒い枠はいらない」）。
    /// </summary>
    private static Text CreateLabelText(Transform parent)
    {
        GameObject textObject = new GameObject("WipeLabel", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1800f, 260f);

        Text text = textObject.GetComponent<Text>();
        text.text = "RED WIN";
        text.fontSize = 150;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white; // 純白。色は付けない
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        // **Font はわざと空。** 再生されたときに RoundWinWipe が入れる
        text.font = null;
        // 待機中は出さない（再生されると中身が入って描かれる）
        text.enabled = false;

        return text;
    }

    /// <summary>画面の左上に出る、いまの値と段階の案内。</summary>
    private static Text CreateHintText(Transform parent)
    {
        GameObject textObject = new GameObject("HintText", typeof(RectTransform), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(28f, -24f);
        rect.sizeDelta = new Vector2(1300f, 180f);

        Text text = textObject.GetComponent<Text>();
        text.fontSize = 26;
        text.alignment = TextAnchor.UpperLeft;
        text.color = new Color(1f, 1f, 1f, 0.92f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.font = null;
        text.text = "（再生すると、いまの段階と値がここに出ます）";

        // 案内は明るい背景の上にも乗るので、こちらは縁取りを残す（読み物であって演出ではないため）
        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        return text;
    }

    /// <summary>
    /// 転場の本体を置いて、作った部品を全部つなぐ。
    /// 項目は private なので、**名前を指定して書き込む**（シーンに保存させるため）。
    /// ※ <see cref="RoundWinWipe"/> の変数名を変えると、ここが null で落ちる
    /// </summary>
    private static RoundWinWipe CreateWipe(Transform parent, Image background, Image band, Text label,
        Button changeButton, Text buttonText, Text hint)
    {
        GameObject wipeObject = new GameObject("RoundWinWipe");
        wipeObject.transform.SetParent(parent, false);
        RoundWinWipe wipe = wipeObject.AddComponent<RoundWinWipe>();

        SerializedObject serialized = new SerializedObject(wipe);
        serialized.FindProperty("background").objectReferenceValue = background;
        serialized.FindProperty("band").objectReferenceValue = band;
        serialized.FindProperty("labelText").objectReferenceValue = label;
        serialized.FindProperty("changeButton").objectReferenceValue = changeButton;
        serialized.FindProperty("buttonText").objectReferenceValue = buttonText;
        serialized.FindProperty("hintText").objectReferenceValue = hint;

        // 掃く速さと文字の秒数（インスペクターで変えられる初期値）
        serialized.FindProperty("sweepSpeed").floatValue = 1.6f;
        serialized.FindProperty("textSeconds").floatValue = 1.2f;

        // 縁の形は「炎」から（Balatro のスコア盤の炎のような、とがった火舌の3枚重ね）。
        // まっすぐ・水波と見比べたいときは E キーかインスペクターで切り替える
        serialized.FindProperty("edgeStyle").enumValueIndex = (int)RoundWinWipeEdge.Flame;
        serialized.FindProperty("edgeAmplitude").floatValue = 0.06f;
        serialized.FindProperty("edgeDetail").floatValue = 8f;
        serialized.FindProperty("edgeSpeed").floatValue = 1.5f;
        serialized.FindProperty("flameHeight").floatValue = 0.10f;
        serialized.FindProperty("flameDetail").floatValue = 6f;
        serialized.FindProperty("flameSpeed").floatValue = 2.2f;
        serialized.FindProperty("rimStrength").floatValue = 0f;

        // 転場のかたまり：① 赤（左→右・背景を黄に）② 青（右→左・背景を灰に）
        SerializedProperty steps = serialized.FindProperty("steps");
        steps.arraySize = 2;
        SetStep(steps.GetArrayElementAtIndex(0), TeamRed, RoundWinWipeDirection.LeftToRight, "RED WIN",
            BackgroundYellow, RedRim, RedSpark, RedFlameMid, RedFlameCore);
        SetStep(steps.GetArrayElementAtIndex(1), TeamBlue, RoundWinWipeDirection.RightToLeft, "BLUE WIN",
            BackgroundGray, BlueRim, BlueSpark, BlueFlameMid, BlueFlameCore);

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return wipe;
    }

    /// <summary>かたまり1つぶんの中身を書く。</summary>
    private static void SetStep(SerializedProperty element, Color bandColor, RoundWinWipeDirection direction,
        string label, Color nextBackground, Color rimColor, Color sparkColor, Color flameMidColor, Color flameCoreColor)
    {
        element.FindPropertyRelative("bandColor").colorValue = bandColor;
        element.FindPropertyRelative("direction").enumValueIndex = (int)direction;
        element.FindPropertyRelative("label").stringValue = label;
        element.FindPropertyRelative("nextBackground").colorValue = nextBackground;
        element.FindPropertyRelative("rimColor").colorValue = rimColor;
        element.FindPropertyRelative("sparkColor").colorValue = sparkColor;
        element.FindPropertyRelative("flameMidColor").colorValue = flameMidColor;
        element.FindPropertyRelative("flameCoreColor").colorValue = flameCoreColor;
    }

    /// <summary>
    /// UI のクリックを受け取る仕組み。**新しい入力システム（Input System）の物を使う。**
    /// 古い物（`StandaloneInputModule`）を付けるとエラーになる（このプロジェクトは新しい物だけに設定されている）。
    /// </summary>
    private static void CreateEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
