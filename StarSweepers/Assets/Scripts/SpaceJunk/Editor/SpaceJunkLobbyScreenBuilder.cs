using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **ロビーの画面のプレハブ（`Assets/Resources/LobbyScreen.prefab`）を作るツール。**（2026/10/6・大槻さん
/// 「タイトルと同じ要領で、めっちゃカメレオンを参考に、ゲーム設定とマップ設定ができるように作り直して」）
///
/// ・プレハブが無いときは、Unity が読み込んだときに**自動で1回だけ作り**、ロビーのシーン（SpaceJunkLobby）に置く
/// ・`Tools > StarSweepers > ロビーの画面のプレハブを作り直す` で、最初の状態に作り直せる（**手で直した配置・画像・色は消える**）
///
/// 部品は**決まった位置に置くだけ**（自動で並べるのは、チーム分けとマップの一覧の中だけ）。作ったあとは自由に動かせる。
/// コードは部品の名前ではなく、印（<see cref="SpaceJunkLobbyPart"/> / <see cref="SpaceJunkLobbyButton"/> /
/// <see cref="SpaceJunkLobbyValueRow"/>）で見つける。
/// </summary>
public static class SpaceJunkLobbyScreenBuilder
{
    public const string PrefabPath = "Assets/Resources/LobbyScreen.prefab";

    // 見た目の初期値（作ったあとは、プレハブの中で直接変える）
    private static readonly Color TextColor = Color.white;
    private static readonly Color NoteColor = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color WarnColor = new Color(1f, 0.6f, 0.4f, 1f);
    private static readonly Color PanelColor = new Color(0.04f, 0.04f, 0.06f, 0.88f);
    private static readonly Color BoxColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color PlaceholderColor = new Color(0.16f, 0.16f, 0.2f, 1f);
    private static readonly Color HighlightColor = new Color(0.18f, 0.70f, 0.25f, 0.95f);
    private static readonly Color PlusColor = new Color(0.20f, 0.72f, 0.25f, 1f);
    private static readonly Color MinusColor = new Color(0.82f, 0.16f, 0.22f, 1f);
    private static readonly Color CheckOnColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    private static readonly Color CheckOffColor = new Color(0.85f, 0.15f, 0.2f, 1f);

    private static Font defaultFont;

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

    [InitializeOnLoadMethod]
    private static void AutoBuildIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(PrefabPath))
            {
                return;
            }

            Debug.Log("[JUNK] ロビーの画面のプレハブが無いので作ります（初回だけ）。");
            Build();
        };
    }

    [MenuItem("Tools/StarSweepers/ロビーの画面のプレハブを作り直す")]
    private static void BuildFromMenu()
    {
        if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog(
                "ロビーの画面のプレハブを作り直す",
                "Assets/Resources/LobbyScreen.prefab を最初の状態で作り直します。\n" +
                "手で動かした配置や、変えた画像・色は消えます。よろしいですか？",
                "作り直す", "やめる"))
        {
            return;
        }

        Build();
    }

    private static void Build()
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 開いているシーンを汚さないように、見えない作業用のシーンで組み立てる
        Scene stage = EditorSceneManager.NewPreviewScene();
        GameObject saved;

        try
        {
            GameObject root = BuildRoot(stage);
            saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (!success || saved == null)
            {
                Debug.LogError($"[JUNK] ロビーの画面のプレハブを保存できませんでした：{PrefabPath}");
                return;
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        Debug.Log($"[JUNK] ロビーの画面のプレハブを作りました：{PrefabPath}\nダブルクリックで開くと、ボタンや文字を自由に動かせます。");

        PlaceInLobbyScene(saved);
    }

    /// <summary>
    /// ロビーのシーンに画面が無ければ置く。開いていなければ開いて置いて保存し、閉じる。
    /// 開いていて**まだ保存していない変更があれば、保存はしない**（メンバーの作業を勝手に保存しないため）。
    /// </summary>
    private static void PlaceInLobbyScene(GameObject prefab)
    {
        string path = SpaceJunkSetup.LobbyScenePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning("[JUNK] ロビーのシーンが見つからないので、ロビーの画面をシーンに置けませんでした。");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.GetComponentInChildren<SpaceJunkLobbyScreen>(true) != null)
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
                return;
            }
        }

        bool wasDirty = scene.isDirty;
        PrefabUtility.InstantiatePrefab(prefab, scene);
        EditorSceneManager.MarkSceneDirty(scene);

        if (!wasDirty)
        {
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[JUNK] ロビーのシーンに画面を置いて保存しました：{path}");
        }
        else
        {
            Debug.LogWarning("[JUNK] ロビーのシーンに画面を置きました。**シーンを保存してください**（ほかの変更があったので、自動では保存していません）。");
        }

        if (openedHere)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    private static GameObject BuildRoot(Scene stage)
    {
        GameObject root = new GameObject("LobbyScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, stage);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80; // タイトル画面（90）・ポーズ画面（100）より後ろ

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<SpaceJunkLobbyScreen>();

        BuildHud(root.transform);
        BuildSettingsWindow(root.transform);

        return root;
    }

    /// <summary>ふだん左上に出ている案内（次のマップ・人数・参加者・設定・案内）。</summary>
    private static void BuildHud(Transform root)
    {
        Image hud = NewImage("Hud", root, new Color(0f, 0f, 0f, 0.55f));
        Place(hud.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(460f, 720f), new Vector2(0f, 1f));
        hud.raycastTarget = false;
        Mark(hud, SpaceJunkLobbyPartRole.Hud);

        BuildPicture(hud.transform, new Vector2(10f, -10f), new Vector2(440f, 248f), 26,
            SpaceJunkLobbyPartRole.HudMapImage, SpaceJunkLobbyPartRole.HudMapPlaceholder);

        Text mapName = NewText("MapName", hud.transform, "ランダム", 30, TextColor, TextAnchor.MiddleCenter);
        mapName.fontStyle = FontStyle.Bold;
        Place(mapName.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -266f), new Vector2(440f, 44f), new Vector2(0f, 1f));
        Mark(mapName, SpaceJunkLobbyPartRole.HudMapName);

        Text count = NewText("PlayerCount", hud.transform, "1/8", 56, TextColor, TextAnchor.MiddleCenter);
        count.fontStyle = FontStyle.Bold;
        Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -312f), new Vector2(440f, 64f), new Vector2(0f, 1f));
        Mark(count, SpaceJunkLobbyPartRole.HudPlayerCount);

        Text roster = NewText("Roster", hud.transform, "▶ プレイヤー1（赤チーム）", 20, TextColor, TextAnchor.UpperLeft);
        roster.supportRichText = true;
        Place(roster.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -386f), new Vector2(420f, 220f), new Vector2(0f, 1f));
        Mark(roster, SpaceJunkLobbyPartRole.HudRoster);

        Text summary = NewText("Summary", hud.transform, "2 チーム ／ 2 本先取 ／ 1ラウンド 60 秒", 20, NoteColor, TextAnchor.UpperLeft);
        Place(summary.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -614f), new Vector2(420f, 32f), new Vector2(0f, 1f));
        Mark(summary, SpaceJunkLobbyPartRole.HudSummary);

        Text hint = NewText("Hint", hud.transform, "設定端末に近づくと、ゲーム設定を開けます", 18, TextColor, TextAnchor.UpperLeft);
        Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -650f), new Vector2(420f, 60f), new Vector2(0f, 1f));
        Mark(hint, SpaceJunkLobbyPartRole.HudHint);
    }

    /// <summary>ゲーム設定（左）とマップ（右）。ホストが設定端末で開く。</summary>
    private static void BuildSettingsWindow(Transform root)
    {
        RectTransform window = NewRect("SettingsWindow", root);
        Stretch(window);
        Mark(window, SpaceJunkLobbyPartRole.SettingsWindow);

        Image shade = NewImage("Shade", window, new Color(0f, 0f, 0f, 0.5f));
        Stretch(shade.rectTransform);

        BuildGameSettings(window);
        BuildMapPanel(window);

        // 下：ゲームを始める
        TitleMenuButton start = NewThemedButton(window, "ゲームを始める", new Vector2(1480f, 64f), 34);
        Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(1480f, 64f), new Vector2(0.5f, 0f));
        AddAction(start, SpaceJunkLobbyAction.StartMatch, 0);
        Mark(start, SpaceJunkLobbyPartRole.StartButton);
        Mark(start.Label, SpaceJunkLobbyPartRole.StartLabel);

        Text message = NewText("StartMessage", window, string.Empty, 22, WarnColor, TextAnchor.MiddleCenter);
        Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1480f, 36f), new Vector2(0.5f, 0f));
        Mark(message, SpaceJunkLobbyPartRole.StartMessage);

        // 左下：戻る
        TitleMenuButton back = NewThemedButton(window, "Esc／B  戻る", new Vector2(220f, 60f), 22);
        Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(220f, 60f), new Vector2(0f, 0f));
        AddAction(back, SpaceJunkLobbyAction.Close, 0);

        window.gameObject.SetActive(false);
    }

    private static void BuildGameSettings(Transform window)
    {
        Image panel = NewImage("GameSettings", window, PanelColor);
        Place(panel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(1180f, 900f), new Vector2(0f, 1f));
        Transform p = panel.transform;

        Text title = NewText("Title", p, "ゲーム設定", 40, TextColor, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -20f), new Vector2(600f, 60f), new Vector2(0f, 1f));

        // ---- 左の列：数の設定 ----
        Heading(p, "チーム数", new Vector2(40f, -100f), 520f);
        RectTransform teamRow = ValueRow(p, "TeamCount", new Vector2(40f, -150f), SpaceJunkLobbyValueKind.TeamCount,
            new Vector2(90f, 0f), 340f, "2 チーム", "1〜4");
        ArrowButton(teamRow, "＜", new Vector2(0f, 0f), SpaceJunkLobbyAction.Step, -1);
        ArrowButton(teamRow, "＞", new Vector2(450f, 0f), SpaceJunkLobbyAction.Step, 1);

        Heading(p, "何本先取", new Vector2(40f, -250f), 520f);
        RectTransform winsRow = ValueRow(p, "RoundsToWin", new Vector2(40f, -300f), SpaceJunkLobbyValueKind.RoundsToWin,
            Vector2.zero, 340f, "2 本先取", "1〜5");
        StepButtons(winsRow, 350f, 1);

        Heading(p, "1ラウンドの最大時間（秒）", new Vector2(40f, -400f), 520f);
        RectTransform secondsRow = ValueRow(p, "RoundSeconds", new Vector2(40f, -450f), SpaceJunkLobbyValueKind.RoundSeconds,
            Vector2.zero, 260f, "60", $"{SpaceJunkSession.MinRoundSeconds}〜{SpaceJunkSession.MaxRoundSeconds}");
        StepButtons(secondsRow, 270f, 1);
        StepButtons(secondsRow, 354f, 10);
        StepButtons(secondsRow, 438f, 50);

        Heading(p, "ルール", new Vector2(40f, -560f), 520f);
        Text rule = NewText("Rule", p, "得点制：時間いっぱい宇宙ごみを集め、得点の高いチームがラウンドを取る。", 20, TextColor, TextAnchor.UpperLeft);
        Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -610f), new Vector2(520f, 200f), new Vector2(0f, 1f));
        Mark(rule, SpaceJunkLobbyPartRole.RuleText);

        // ---- 右の列：チーム分け ----
        Text teamsTitle = NewText("TeamsTitle", p, "チーム分け", 28, TextColor, TextAnchor.MiddleLeft);
        Place(teamsTitle.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -100f), new Vector2(340f, 44f), new Vector2(0f, 1f));

        TitleMenuButton randomAssign = NewThemedButton(p, "ランダムに割り振る", new Vector2(200f, 44f), 20);
        Place((RectTransform)randomAssign.transform, new Vector2(0f, 1f), new Vector2(940f, -100f), new Vector2(200f, 44f), new Vector2(0f, 1f));
        AddAction(randomAssign, SpaceJunkLobbyAction.RandomAssign, 0);

        Image listBox = NewImage("PlayerList", p, BoxColor);
        Place(listBox.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -156f), new Vector2(540f, 560f), new Vector2(0f, 1f));
        RectTransform content = NewRect("Content", listBox.transform);
        Stretch(content);
        content.offsetMin = new Vector2(8f, 8f);
        content.offsetMax = new Vector2(-8f, -8f);
        AddVerticalLayout(content.gameObject, 8f);
        Mark(content, SpaceJunkLobbyPartRole.PlayerListContent);
        BuildPlayerRowTemplate(content);

        Text teamsNote = NewText("TeamsNote", p,
            "名前の横の ＜ ＞ でチームを変える。人数の偏り（3人対1人など）も許しています。誰もいないチームのゴールは数えません。",
            16, NoteColor, TextAnchor.UpperLeft);
        Place(teamsNote.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -726f), new Vector2(540f, 60f), new Vector2(0f, 1f));
    }

    private static void BuildPlayerRowTemplate(RectTransform content)
    {
        Image row = NewImage("PlayerRowTemplate", content, RowColor);
        SetLayoutSize(row.gameObject, 524f, 56f);
        Mark(row, SpaceJunkLobbyPartRole.PlayerRowTemplate);

        Text name = NewText("Name", row.transform, "プレイヤー1", 24, TextColor, TextAnchor.MiddleLeft);
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(250f, 56f), new Vector2(0f, 0.5f));
        Mark(name, SpaceJunkLobbyPartRole.PlayerRowName);

        ArrowButton(row.rectTransform, "＜", new Vector2(276f, 0f), SpaceJunkLobbyAction.TeamPrevious, 0, 48f, 44f);

        Text team = NewText("Team", row.transform, "赤チーム", 22, TextColor, TextAnchor.MiddleCenter);
        Place(team.rectTransform, new Vector2(0f, 0.5f), new Vector2(330f, 0f), new Vector2(136f, 56f), new Vector2(0f, 0.5f));
        Mark(team, SpaceJunkLobbyPartRole.PlayerRowTeam);

        ArrowButton(row.rectTransform, "＞", new Vector2(470f, 0f), SpaceJunkLobbyAction.TeamNext, 0, 48f, 44f);
    }

    private static void BuildMapPanel(Transform window)
    {
        Image panel = NewImage("MapSettings", window, PanelColor);
        Place(panel.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(660f, 900f), new Vector2(1f, 1f));
        Transform p = panel.transform;

        Text title = NewText("Title", p, "マップ", 40, TextColor, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(600f, 60f), new Vector2(0f, 1f));

        BuildPicture(p, new Vector2(20f, -90f), new Vector2(620f, 349f), 36,
            SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder);

        Text previewName = NewText("PreviewName", p, "ランダム", 24, TextColor, TextAnchor.MiddleCenter);
        Place(previewName.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -444f), new Vector2(620f, 44f), new Vector2(0f, 1f));
        Mark(previewName, SpaceJunkLobbyPartRole.MapPreviewName);

        // 一覧（いちばん上が「ランダム」。その下に、見本を複製したマップの行が並ぶ）
        Image listBox = NewImage("MapList", p, BoxColor);
        Place(listBox.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -496f), new Vector2(620f, 330f), new Vector2(0f, 1f));
        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, SpaceJunkLobbyPartRole.MapListContent);

        BuildRandomRow(content);
        BuildMapRowTemplate(content);

        Text hidden = NewText("HiddenNote", p, string.Empty, 16, WarnColor, TextAnchor.MiddleLeft);
        Place(hidden.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -832f), new Vector2(620f, 26f), new Vector2(0f, 1f));
        Mark(hidden, SpaceJunkLobbyPartRole.MapHiddenNote);

        Text help = NewText("Help", p, "名前を押す：そのマップで遊ぶ ／ 右の四角：ランダムの候補に入れる（緑）・外す（赤）",
            16, NoteColor, TextAnchor.MiddleLeft);
        Place(help.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -860f), new Vector2(620f, 26f), new Vector2(0f, 1f));
    }

    /// <summary>「ランダム」の行。押すと、チェックの付いたマップからラウンドごとに選ぶ。</summary>
    private static void BuildRandomRow(RectTransform content)
    {
        Image row = NewImage("RandomRow", content, RowColor);
        SetLayoutSize(row.gameObject, 590f, 52f);
        AddButton(row, SpaceJunkLobbyAction.SelectRandomMap, 0);

        Image highlight = NewImage("Highlight", row.transform, HighlightColor);
        Stretch(highlight.rectTransform);
        highlight.raycastTarget = false;
        Mark(highlight, SpaceJunkLobbyPartRole.MapRandomHighlight);

        Text label = NewText("Label", row.transform, "ランダム", 26, TextColor, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform);
        Mark(label, SpaceJunkLobbyPartRole.MapRandomLabel);
    }

    /// <summary>マップ1つぶんの行の見本。再生すると隠れ、マップの数だけ複製される。</summary>
    private static void BuildMapRowTemplate(RectTransform content)
    {
        Image row = NewImage("MapRowTemplate", content, RowColor);
        SetLayoutSize(row.gameObject, 590f, 52f);
        AddButton(row, SpaceJunkLobbyAction.SelectMap, 0);
        Mark(row, SpaceJunkLobbyPartRole.MapRowTemplate);

        Image highlight = NewImage("Highlight", row.transform, HighlightColor);
        Place(highlight.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(524f, 52f), new Vector2(0f, 0.5f));
        highlight.raycastTarget = false;
        Mark(highlight, SpaceJunkLobbyPartRole.MapRowHighlight);

        Text name = NewText("Name", row.transform, "マップの名前", 26, TextColor, TextAnchor.MiddleCenter);
        Place(name.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(524f, 52f), new Vector2(0f, 0.5f));
        Mark(name, SpaceJunkLobbyPartRole.MapRowName);

        // 右の四角：ランダムの候補に入れるか
        Image check = NewImage("Check", row.transform, new Color(0f, 0f, 0f, 0.35f));
        Place(check.rectTransform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(48f, 44f), new Vector2(1f, 0.5f));
        AddButton(check, SpaceJunkLobbyAction.ToggleMapCheck, 0);

        Image on = NewImage("On", check.transform, CheckOnColor);
        Stretch(on.rectTransform);
        on.raycastTarget = false;
        Text onMark = NewText("Mark", on.transform, "✓", 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(onMark.rectTransform);
        Mark(on, SpaceJunkLobbyPartRole.MapRowCheckOn);

        Image off = NewImage("Off", check.transform, CheckOffColor);
        Stretch(off.rectTransform);
        off.raycastTarget = false;
        Text offMark = NewText("Mark", off.transform, "×", 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(offMark.rectTransform);
        Mark(off, SpaceJunkLobbyPartRole.MapRowCheckOff);
    }

    // ------------------------------------------------------------
    // 部品づくり
    // ------------------------------------------------------------

    /// <summary>画像と、画像が無いときに出す代わりの枠（「画像 未設定」）。</summary>
    private static void BuildPicture(Transform parent, Vector2 position, Vector2 size, int fontSize,
        SpaceJunkLobbyPartRole imageRole, SpaceJunkLobbyPartRole placeholderRole)
    {
        Image frame = NewImage("Picture", parent, Color.black);
        Place(frame.rectTransform, new Vector2(0f, 1f), position, size, new Vector2(0f, 1f));
        frame.raycastTarget = false;

        Image image = NewImage("Image", frame.transform, Color.white);
        Stretch(image.rectTransform);
        image.preserveAspect = true;
        image.raycastTarget = false;
        Mark(image, imageRole);
        image.gameObject.SetActive(false);

        Image placeholder = NewImage("Placeholder", frame.transform, PlaceholderColor);
        Stretch(placeholder.rectTransform);
        placeholder.raycastTarget = false;
        Text text = NewText("Text", placeholder.transform, "画像 未設定", fontSize, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        Mark(placeholder, placeholderRole);
    }

    private static void Heading(Transform parent, string label, Vector2 position, float width)
    {
        Text text = NewText(label, parent, label, 28, TextColor, TextAnchor.MiddleCenter);
        Place(text.rectTransform, new Vector2(0f, 1f), position, new Vector2(width, 44f), new Vector2(0f, 1f));
    }

    /// <summary>値の1行（入れ物）と、値を出す箱。ボタンはあとから入れ物に足す。</summary>
    private static RectTransform ValueRow(Transform parent, string name, Vector2 position, SpaceJunkLobbyValueKind kind,
        Vector2 boxPosition, float boxWidth, string sample, string range)
    {
        RectTransform row = NewRect(name, parent);
        Place(row, new Vector2(0f, 1f), position, new Vector2(520f, 70f), new Vector2(0f, 1f));

        Image box = NewImage("Box", row, BoxColor);
        Place(box.rectTransform, new Vector2(0f, 0.5f), boxPosition, new Vector2(boxWidth, 70f), new Vector2(0f, 0.5f));

        Text value = NewText("Value", box.transform, sample, 36, TextColor, TextAnchor.MiddleCenter);
        Stretch(value.rectTransform);

        Text rangeText = NewText("Range", box.transform, range, 16, NoteColor, TextAnchor.LowerRight);
        Stretch(rangeText.rectTransform);
        rangeText.rectTransform.offsetMin = new Vector2(8f, 6f);
        rangeText.rectTransform.offsetMax = new Vector2(-10f, -6f);

        row.gameObject.AddComponent<SpaceJunkLobbyValueRow>().EditorSetup(kind, value);
        return row;
    }

    /// <summary>「+n」（上・緑）と「-n」（下・赤）の2つ。</summary>
    private static void StepButtons(RectTransform row, float x, int amount)
    {
        ColorButton(row, $"+{amount}", new Vector2(x, 18f), PlusColor, amount);
        ColorButton(row, $"-{amount}", new Vector2(x, -18f), MinusColor, -amount);
    }

    private static void ColorButton(RectTransform row, string label, Vector2 position, Color color, int amount)
    {
        Image image = NewImage(label, row, color);
        Place(image.rectTransform, new Vector2(0f, 0.5f), position, new Vector2(80f, 34f), new Vector2(0f, 0.5f));

        Text text = NewText("Label", image.transform, label, 24, Color.white, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform);

        AddButton(image, SpaceJunkLobbyAction.Step, amount);
    }

    /// <summary>「＜」「＞」のボタン。</summary>
    private static void ArrowButton(RectTransform row, string label, Vector2 position, SpaceJunkLobbyAction action, int amount,
        float width = 70f, float height = 70f)
    {
        Image image = NewImage(label, row, new Color(0.12f, 0.12f, 0.14f, 0.95f));
        Place(image.rectTransform, new Vector2(0f, 0.5f), position, new Vector2(width, height), new Vector2(0f, 0.5f));
        image.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.6f);

        Text text = NewText("Label", image.transform, label, 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);

        AddButton(image, action, amount);
    }

    /// <summary>テーマ（TitleScreenTheme）の見た目のボタン。タイトル画面と同じ。</summary>
    private static TitleMenuButton NewThemedButton(Transform parent, string label, Vector2 size, int fontSize)
    {
        Image background = NewImage(label, parent, new Color(0.10f, 0.12f, 0.12f, 0.92f));
        background.rectTransform.sizeDelta = size;
        background.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);

        Text text = NewText("Label", background.transform, label, fontSize, TextColor, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);

        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None; // 見た目は TitleMenuButton が変える

        TitleMenuButton look = background.gameObject.AddComponent<TitleMenuButton>();
        look.EditorSetup(TitleButtonAction.None, background, text);
        return look;
    }

    private static void AddAction(Component target, SpaceJunkLobbyAction action, int amount)
    {
        target.gameObject.AddComponent<SpaceJunkLobbyButton>().EditorSetup(action, amount);
    }

    /// <summary>画像を押せるボタンにする（色が少し変わる）。</summary>
    private static void AddButton(Image image, SpaceJunkLobbyAction action, int amount)
    {
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.colorMultiplier = 1.5f;
        button.colors = colors;

        AddAction(image, action, amount);
    }

    private static void AddVerticalLayout(GameObject target, float spacing)
    {
        VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    /// <summary>縦に流れる一覧。中身を入れる入れ物を返す。</summary>
    private static RectTransform NewScrollList(Transform parent)
    {
        RectTransform viewRect = NewRect("Viewport", parent);
        viewRect.gameObject.AddComponent<RectMask2D>();
        Stretch(viewRect);
        viewRect.offsetMin = new Vector2(8f, 8f);
        viewRect.offsetMax = new Vector2(-8f, -8f);

        // 行のすき間でもホイールで流せるように、透明な下地を敷く
        Image hit = viewRect.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        RectTransform contentRect = NewRect("Content", viewRect);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        AddVerticalLayout(contentRect.gameObject, 6f);
        contentRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewRect.gameObject.AddComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewRect;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        return contentRect;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject created = new GameObject(name, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        return (RectTransform)created.transform;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text NewText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rect = NewRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = defaultFont; // 再生すると SpaceJunkLobbyScreen がパソコンの日本語フォントに差し替える
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    private static void Mark(Component target, SpaceJunkLobbyPartRole role)
    {
        target.gameObject.AddComponent<SpaceJunkLobbyPart>().EditorSetRole(role);
    }

    private static void SetLayoutSize(GameObject target, float width, float height)
    {
        ((RectTransform)target.transform).sizeDelta = new Vector2(width, height);
        LayoutElement element = target.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    /// <summary>アンカー（画面のどこを基準にするか）・位置・大きさ・中心をまとめて決める。</summary>
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
