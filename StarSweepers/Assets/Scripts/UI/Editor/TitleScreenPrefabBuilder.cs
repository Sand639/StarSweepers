using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **タイトル画面のプレハブ（`Assets/Resources/TitleScreen.prefab`）を作るツール。**
///
/// ・プレハブが無いとき・作りが古いときは、Unity が読み込んだときに**自動で作り**、TitleScene に置く
/// ・`Tools > StarSweepers > タイトル画面のプレハブを作り直す` で、最初の状態に作り直せる（**手で直した配置・画像・色は消える**）
///
/// 部品は**決まった位置に置くだけ**（自動で並べる仕組みは、部屋の一覧の中だけ）。
/// だから、作ったあとはエディタで**どの部品も自由に動かせる。**
/// コードは部品の名前や並び順ではなく、付いている印（<see cref="TitlePart"/> / <see cref="TitleMenuButton"/> の Action など）で見つける。
///
/// 2026/10/13 に、大槻さんの絵コンテどおりの画面（名前とスタート → 1人プレイ／マルチプレイ → 部屋の一覧）に作り直した。
/// 前の画面（サーバーを作る・探す・設定）は、ブランチ `backup/title-before-redesign-1013` に残してある。
/// </summary>
public static class TitleScreenPrefabBuilder
{
    public const string PrefabPath = "Assets/Resources/TitleScreen.prefab";

    /// <summary>背景の画像（Assets/Art/Sprites/title.png）。場所が変わっても見つかるように番号で探す。</summary>
    private const string BackgroundGuid = "0051ed586d64b7a409d1a5f3f40e6713";

    private const string TitleSceneName = "TitleScene";

    // 見た目の初期値（作ったあとは、プレハブの中で直接変える）
    private const int FontSize = 24;
    private const int ButtonFontSize = 30;
    private static readonly Color TextColor = Color.white;
    private static readonly Color NoteColor = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color WarningColor = new Color(1f, 0.45f, 0.4f, 1f);
    private static readonly Color TitleColor = new Color(1f, 0.95f, 0.55f, 1f);
    private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.10f, 0.75f);
    private static readonly Color InputColor = new Color(0.20f, 0.21f, 0.25f, 0.95f);
    private static readonly Color RoomPanelColor = new Color(0f, 0.59f, 0.70f, 0.92f);
    private static readonly Color TintColor = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Vector2 ModeButtonSize = new Vector2(380f, 90f);

    private static Font defaultFont;

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

    /// <summary>
    /// **プレハブの作りの版。** 作りが大きく変わったときだけ上げる。
    /// 1〜4：サーバーを作る・探す・設定の画面（〜2026/10/12）／ 5：名前とスタート → 1人プレイ／マルチプレイ → 部屋の一覧（2026/10/13）
    /// ／ 6：部屋を作る窓のパスワードの欄を、部屋の名前の欄に変えた（2026/10/13）
    /// **版 5 からは作り直さず、<see cref="Upgrade"/> で変わった所だけを直す**（手で直した配置を消さない）。版を上げたら Upgrade に直す処理を書くこと
    /// </summary>
    public const int PrefabVersion = 6;

    /// <summary>この版より古いプレハブは、作り直す（画面の作りがまるごと変わったため。手で直した配置は消える）。</summary>
    private const int RebuildBelowVersion = 5;

    [InitializeOnLoadMethod]
    private static void AutoBuildIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!File.Exists(PrefabPath))
            {
                Debug.Log("[UI] タイトル画面のプレハブが無いので作ります（初回だけ）。");
                Build();
                return;
            }

            TitleScreen existing = AssetDatabase.LoadAssetAtPath<TitleScreen>(PrefabPath);
            if (existing != null && existing.PrefabVersion < RebuildBelowVersion)
            {
                Debug.LogWarning($"[UI] タイトル画面のプレハブの作りが古い（{existing.PrefabVersion} → {PrefabVersion}）ので、作り直しました。" +
                                 "手で動かした配置や、変えた画像・色は消えています。");
                Build();
            }
            else if (existing != null && existing.PrefabVersion < PrefabVersion)
            {
                Upgrade(existing.PrefabVersion);
            }
        };
    }

    /// <summary>
    /// **作り直さずに、変わった所だけを直す。**（手で動かした配置・画像・色はそのまま）
    /// 版 5 → 6：部屋を作る窓の「パスワード」の欄を「部屋の名前」の欄に変える（入れる物・見出し・薄い字）。
    /// </summary>
    private static void Upgrade(int fromVersion)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            if (fromVersion < 6)
            {
                ChangeCreatePasswordToRoomName(root);
            }

            root.GetComponent<TitleScreen>().EditorSetPrefabVersion(PrefabVersion);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[UI] タイトル画面のプレハブを直しました（版 {fromVersion} → {PrefabVersion}。手で直した配置はそのまま）。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>部屋を作る窓の、パスワードの欄（Kind：CreatePassword）を、部屋の名前の欄（Kind：ServerName）に変える。</summary>
    private static void ChangeCreatePasswordToRoomName(GameObject root)
    {
        foreach (TitleInput input in root.GetComponentsInChildren<TitleInput>(true))
        {
            if (input.Kind != TitleInputKind.CreatePassword)
            {
                continue;
            }

            input.EditorSetup(TitleInputKind.ServerName, input.Counter);
            input.gameObject.name = "Input " + TitleInputKind.ServerName;

            if (input.Field.placeholder is Text placeholder)
            {
                placeholder.text = RoomNamePlaceholder;
            }

            // 同じ窓の見出し（PasswordLabel）
            Transform label = input.transform.parent != null ? input.transform.parent.Find("PasswordLabel") : null;
            if (label != null && label.TryGetComponent(out Text labelText))
            {
                labelText.text = RoomNameLabel;
                label.name = "RoomNameLabel";
            }
        }
    }

    /// <summary>部屋を作る窓の、部屋の名前の欄の見出しと薄い字。</summary>
    private const string RoomNameLabel = "部屋の名前（空なら「自分の名前の部屋」）";
    private const string RoomNamePlaceholder = "部屋の名前";

    [MenuItem("Tools/StarSweepers/タイトル画面のプレハブを作り直す")]
    private static void BuildFromMenu()
    {
        if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog(
                "タイトル画面のプレハブを作り直す",
                "Assets/Resources/TitleScreen.prefab を最初の状態で作り直します。\n" +
                "手で動かした配置や、変えた画像・色は消えます。よろしいですか？",
                "作り直す", "やめる"))
        {
            return;
        }

        Build();
    }

    /// <summary>
    /// コマンドから、変わった所だけを直す（Unity を画面なしで動かすとき用：
    /// `Unity.exe -batchmode -quit -projectPath ... -executeMethod TitleScreenPrefabBuilder.UpgradeFromCommandLine`）。
    /// 作り直すときは BuildFromCommandLine。
    /// </summary>
    public static void UpgradeFromCommandLine()
    {
        TitleScreen existing = AssetDatabase.LoadAssetAtPath<TitleScreen>(PrefabPath);
        if (existing != null && existing.PrefabVersion < PrefabVersion)
        {
            Upgrade(existing.PrefabVersion);
        }
    }

    /// <summary>コマンドから最初の状態で作り直す（手で直した配置は消える）。</summary>
    public static void BuildFromCommandLine()
    {
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
                Debug.LogError($"[UI] タイトル画面のプレハブを保存できませんでした：{PrefabPath}");
                return;
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        Debug.Log($"[UI] タイトル画面のプレハブを作りました：{PrefabPath}\n" +
                  "ダブルクリックで開くと、ボタンや文字を自由に動かせます。");

        PlaceInTitleScene(saved);
    }

    /// <summary>
    /// TitleScene にタイトル画面が無ければ置く。
    /// 開いていなければ開いて置いて保存し、閉じる。開いていて**まだ保存していない変更があれば、保存はしない**（メンバーの作業を勝手に保存しないため）。
    /// </summary>
    private static void PlaceInTitleScene(GameObject prefab)
    {
        string path = FindTitleScenePath();
        if (path == null)
        {
            Debug.LogWarning("[UI] TitleScene が見つからないので、タイトル画面をシーンに置けませんでした。");
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
            if (rootObject.GetComponentInChildren<TitleScreen>(true) != null)
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
            Debug.Log($"[UI] TitleScene にタイトル画面を置いて保存しました：{path}");
        }
        else
        {
            Debug.LogWarning("[UI] TitleScene にタイトル画面を置きました。**シーンを保存してください**（ほかの変更があったので、自動では保存していません）。");
        }

        if (openedHere)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string FindTitleScenePath()
    {
        foreach (string guid in AssetDatabase.FindAssets(TitleSceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == TitleSceneName)
            {
                return path;
            }
        }

        return null;
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    private static GameObject BuildRoot(Scene stage)
    {
        GameObject root = new GameObject("TitleScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, stage);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // ポーズ画面（100）より後ろ、ほかのUIより手前

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<TitleScreen>().EditorSetPrefabVersion(PrefabVersion);

        BuildBackground(root.transform);
        BuildStartPage(root.transform);
        BuildModePage(root.transform).SetActive(false);
        BuildCreateWindow(root.transform).SetActive(false);
        BuildPasswordWindow(root.transform).SetActive(false);
        BuildBusyOverlay(root.transform).SetActive(false);

        return root;
    }

    private static void BuildBackground(Transform root)
    {
        // 背景の画像（画面いっぱい。縦横比を保ったまま、はみ出す分は切る）
        Image background = NewImage("Background", root, Color.black);
        Stretch(background.rectTransform);

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(BackgroundGuid));
        if (sprite != null)
        {
            Image picture = NewImage("Picture", background.transform, Color.white, sprite);
            AspectRatioFitter fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        }

        // 文字を読みやすくする暗さ
        Image tint = NewImage("Tint", root, TintColor);
        Stretch(tint.rectTransform);
    }

    /// <summary>はじめの画面：タイトル・名前を入れる欄・START。</summary>
    private static GameObject BuildStartPage(Transform root)
    {
        GameObject page = NewPage("StartPage", root, TitlePartRole.PageStart);

        // タイトルのロゴは背景の絵（title.png）に入っているので、文字は置かない。
        // 名前の欄と START の後ろに暗い箱を置いて、絵の上でも読めるようにする
        Image box = NewImage("StartBox", page.transform, new Color(0.04f, 0.05f, 0.08f, 0.82f));
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -135f), new Vector2(760f, 300f), new Vector2(0.5f, 0.5f));

        NewInput(page.transform, "名前を入力する", TitleInputKind.PlayerName, new Vector2(0f, -60f), new Vector2(640f, 80f));

        TitleMenuButton start = NewButton(page.transform, "START", new Vector2(320f, 90f), ButtonFontSize + 6, TitleButtonAction.Start);
        Place((RectTransform)start.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(320f, 90f), new Vector2(0.5f, 0.5f));

        Text message = NewText("Message", page.transform, string.Empty, FontSize, WarningColor, TextAnchor.MiddleCenter);
        Place(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -275f), new Vector2(900f, 50f), new Vector2(0.5f, 0.5f));
        Mark(message, TitlePartRole.StartMessage);

        // 右下：バージョン（{version} は再生したときに Project Settings の Version に置き換わる）
        Text version = NewText("Version", page.transform, "version - {version}", FontSize, TextColor, TextAnchor.LowerRight);
        Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(600f, 40f), new Vector2(1f, 0f));
        Mark(version, TitlePartRole.VersionText);

        return page;
    }

    /// <summary>
    /// えらぶ画面：左に「1人プレイ」「マルチプレイ」、右に選んでいるほうの表示（いまは文字。のちのち人の絵）。
    /// 「マルチプレイ」を押すと、右が部屋の一覧に変わる。
    /// </summary>
    private static GameObject BuildModePage(Transform root)
    {
        GameObject page = NewPage("ModePage", root, TitlePartRole.PageMode);

        // 背景のタイトルの絵が、右の表示や部屋の一覧と重ならないように暗くする
        Image backdrop = NewImage("Backdrop", page.transform, new Color(0.03f, 0.04f, 0.07f, 0.9f));
        Stretch(backdrop.rectTransform);

        Column column = new Column("Buttons", page.transform, ModeButtonSize.x, 26f,
            new Vector2(0f, 1f), new Vector2(160f, -220f), new Vector2(0f, 1f));
        column.Add(NewButton(column.Rect, "1人プレイ", ModeButtonSize, ButtonFontSize, TitleButtonAction.PlaySolo));
        column.Add(NewButton(column.Rect, "マルチプレイ", ModeButtonSize, ButtonFontSize, TitleButtonAction.ShowMulti));
        column.Finish();

        // 右：選んでいるほうの表示（人の絵に変えるときは、この文字を消して Image を置く）
        Text solo = NewText("SoloPreview", page.transform, "シングルプレイ", 96, TitleColor, TextAnchor.MiddleCenter);
        solo.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
        Place(solo.rectTransform, new Vector2(0.62f, 0.5f), Vector2.zero, new Vector2(1000f, 300f), new Vector2(0.5f, 0.5f));
        Mark(solo, TitlePartRole.SoloPreview);

        Text multi = NewText("MultiPreview", page.transform, "マルチプレイ", 96, TitleColor, TextAnchor.MiddleCenter);
        multi.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
        Place(multi.rectTransform, new Vector2(0.62f, 0.5f), Vector2.zero, new Vector2(1000f, 300f), new Vector2(0.5f, 0.5f));
        Mark(multi, TitlePartRole.MultiPreview);
        multi.gameObject.SetActive(false);

        BuildRoomListPanel(page.transform).SetActive(false);

        AddBackButton(page.transform);
        return page;
    }

    /// <summary>部屋の一覧（右の青い枠）。部屋のカードが並び、一番下に「＋」（部屋を作る）。</summary>
    private static GameObject BuildRoomListPanel(Transform page)
    {
        Image panel = NewImage("RoomListPanel", page, RoomPanelColor);
        Place(panel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(880f, 840f), new Vector2(1f, 0.5f));
        Mark(panel, TitlePartRole.RoomListPanel);

        RectTransform content = NewScrollList(panel.transform);
        Mark(content, TitlePartRole.RoomListContent);

        Text message = NewText("Message", content, string.Empty, FontSize - 2, TextColor, TextAnchor.MiddleCenter);
        SetLayoutSize(message.gameObject, 800f, 90f);
        Mark(message, TitlePartRole.RoomListMessage);

        BuildRoomCardTemplate(content);

        TitleMenuButton plus = NewButton(content, "＋", new Vector2(800f, 76f), ButtonFontSize + 10, TitleButtonAction.OpenCreateRoom);
        SetLayoutSize(plus.gameObject, 800f, 76f);
        Mark(plus, TitlePartRole.CreateRoomButton);

        return panel.gameObject;
    }

    /// <summary>部屋1つぶんのカードの見本（押すとその部屋に入る）。再生すると隠れ、見つかった部屋の数だけ複製される。</summary>
    private static void BuildRoomCardTemplate(RectTransform content)
    {
        TitleMenuButton card = NewButton(content, "部屋名", new Vector2(800f, 84f), FontSize + 4, TitleButtonAction.JoinRoom);
        card.gameObject.name = "RoomCardTemplate";
        SetLayoutSize(card.gameObject, 800f, 84f);
        Mark(card, TitlePartRole.RoomCardTemplate);

        // 部屋の名前（左寄せ）と人数（右寄せ）
        Text name = card.Label;
        name.alignment = TextAnchor.MiddleLeft;
        name.rectTransform.offsetMin = new Vector2(28f, 0f);
        name.rectTransform.offsetMax = new Vector2(-200f, 0f);
        Mark(name, TitlePartRole.RoomCardName);

        Text players = NewText("Players", card.transform, "1/4人", FontSize + 4, TextColor, TextAnchor.MiddleRight);
        Place(players.rectTransform, new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(180f, 60f), new Vector2(1f, 0.5f));
        Mark(players, TitlePartRole.RoomCardPlayers);
    }

    /// <summary>「＋」で開く小さい窓：部屋の名前（空なら「自分の名前の部屋」）と、インターネット／LAN のどちらで作るか。パスワードは付けない。</summary>
    private static GameObject BuildCreateWindow(Transform root)
    {
        RectTransform box = NewWindow("CreateWindow", root, TitlePartRole.CreateWindow, new Vector2(960f, 600f), "部屋を作る");

        Text label = NewText("RoomNameLabel", box, RoomNameLabel, FontSize, TextColor, TextAnchor.LowerLeft);
        Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(800f, 40f), new Vector2(0.5f, 1f));
        NewInput(box, RoomNamePlaceholder, TitleInputKind.ServerName, new Vector2(0f, 95f), new Vector2(800f, 70f));

        TitleMenuButton internet = NewButton(box, "インターネットで作る", new Vector2(390f, 90f), ButtonFontSize - 2, TitleButtonAction.CreateRoomInternet);
        Place((RectTransform)internet.transform, new Vector2(0.5f, 0.5f), new Vector2(-200f, 10f), new Vector2(390f, 90f), new Vector2(0.5f, 0.5f));

        TitleMenuButton lan = NewButton(box, "LANで作る", new Vector2(390f, 90f), ButtonFontSize - 2, TitleButtonAction.CreateRoomLan);
        Place((RectTransform)lan.transform, new Vector2(0.5f, 0.5f), new Vector2(200f, 10f), new Vector2(390f, 90f), new Vector2(0.5f, 0.5f));

        Text note = NewText("Note", box, string.Empty, FontSize - 4, NoteColor, TextAnchor.UpperCenter);
        Place(note.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(880f, 90f), new Vector2(0.5f, 1f));
        Mark(note, TitlePartRole.CreateWindowNote);

        TitleMenuButton close = NewButton(box, "やめる", new Vector2(300f, 64f), FontSize, TitleButtonAction.CloseWindow);
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(300f, 64f), new Vector2(0.5f, 0f));

        return box.parent.gameObject;
    }

    /// <summary>パスワードの付いた部屋に入るときの小さい窓。</summary>
    private static GameObject BuildPasswordWindow(Transform root)
    {
        RectTransform box = NewWindow("PasswordWindow", root, TitlePartRole.PasswordWindow, new Vector2(960f, 440f), "パスワードを入力");

        // 窓の見出しを、部屋の名前入りに書き換えられるように印を付ける
        Text title = box.Find("Title").GetComponent<Text>();
        Mark(title, TitlePartRole.PasswordWindowTitle);

        NewInput(box, "パスワード", TitleInputKind.JoinPassword, new Vector2(0f, 40f), new Vector2(800f, 70f));

        TitleMenuButton join = NewButton(box, "参加する", new Vector2(320f, 76f), ButtonFontSize - 2, TitleButtonAction.ConfirmJoinPassword);
        Place((RectTransform)join.transform, new Vector2(0.5f, 0f), new Vector2(-180f, 40f), new Vector2(320f, 76f), new Vector2(0.5f, 0f));

        TitleMenuButton close = NewButton(box, "やめる", new Vector2(320f, 76f), ButtonFontSize - 2, TitleButtonAction.CloseWindow);
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(180f, 40f), new Vector2(320f, 76f), new Vector2(0.5f, 0f));

        return box.parent.gameObject;
    }

    private static GameObject BuildBusyOverlay(Transform root)
    {
        RectTransform overlay = NewRect("Busy", root);
        Stretch(overlay);
        Mark(overlay, TitlePartRole.BusyOverlay);

        Image shade = NewImage("Shade", overlay, new Color(0f, 0f, 0f, 0.7f));
        Stretch(shade.rectTransform);

        Image box = NewImage("Box", overlay, PanelColor);
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 360f), new Vector2(0.5f, 0.5f));

        Text message = NewText("Message", box.transform, "つないでいます…", FontSize, TextColor, TextAnchor.MiddleCenter);
        Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1040f, 200f), new Vector2(0.5f, 1f));
        Mark(message, TitlePartRole.BusyMessage);

        TitleMenuButton button = NewButton(box.transform, "戻る", new Vector2(400f, 64f), FontSize, TitleButtonAction.None);
        Place((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(400f, 64f), new Vector2(0.5f, 0f));
        Mark(button, TitlePartRole.BusyButton);
        Mark(button.Label, TitlePartRole.BusyButtonLabel);

        return overlay.gameObject;
    }

    // ------------------------------------------------------------
    // 部品づくり
    // ------------------------------------------------------------

    /// <summary>
    /// 上から縦に並べる入れ物。**部品は決まった位置に置くだけ**（作ったあとは自由に動かせる）。
    /// </summary>
    private sealed class Column
    {
        public readonly RectTransform Rect;
        private readonly float width;
        private readonly float spacing;
        private float y;

        public Column(string name, Transform parent, float width, float spacing, Vector2 anchor, Vector2 position, Vector2 pivot)
        {
            Rect = NewRect(name, parent);
            Place(Rect, anchor, position, new Vector2(width, 0f), pivot);
            this.width = width;
            this.spacing = spacing;
        }

        /// <summary>次の部品を、いまの一番下に置く（大きさは部品の sizeDelta のまま）。</summary>
        public void Add(RectTransform child)
        {
            child.SetParent(Rect, false);
            float height = child.sizeDelta.y;
            Place(child, new Vector2(0f, 1f), new Vector2(0f, -y), child.sizeDelta, new Vector2(0f, 1f));
            y += height + spacing;
        }

        public void Add(Component child)
        {
            Add((RectTransform)child.transform);
        }

        /// <summary>入れ物の高さを、中身に合わせる。</summary>
        public void Finish()
        {
            Rect.sizeDelta = new Vector2(width, Mathf.Max(0f, y - spacing));
        }
    }

    private static GameObject NewPage(string name, Transform root, TitlePartRole role)
    {
        RectTransform page = NewRect(name, root);
        Stretch(page);
        Mark(page, role);
        return page.gameObject;
    }

    /// <summary>
    /// 小さい窓（後ろを暗くして、真ん中に箱）。印は窓全体（暗い所も含む）に付ける。
    /// 中身を置く箱を返す。箱の上に見出し（名前は "Title"）を置く。
    /// </summary>
    private static RectTransform NewWindow(string name, Transform root, TitlePartRole role, Vector2 size, string heading)
    {
        RectTransform window = NewRect(name, root);
        Stretch(window);
        Mark(window, role);

        // 後ろの画面を押せないように、暗い幕で覆う
        Image shade = NewImage("Shade", window, new Color(0f, 0f, 0f, 0.6f));
        Stretch(shade.rectTransform);

        Image box = NewImage("Box", window, new Color(0.08f, 0.08f, 0.10f, 0.95f));
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size, new Vector2(0.5f, 0.5f));

        Text title = NewText("Title", box.transform, heading, FontSize + 10, TitleColor, TextAnchor.MiddleCenter);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(size.x - 60f, 60f), new Vector2(0.5f, 1f));

        return box.rectTransform;
    }

    /// <summary>入力欄（右端に「いま何文字か」）。位置は親の真ん中からのずれ。</summary>
    private static void NewInput(Transform parent, string placeholder, TitleInputKind kind, Vector2 position, Vector2 size)
    {
        Image background = NewImage("Input " + kind, parent, InputColor);
        Place(background.rectTransform, new Vector2(0.5f, 0.5f), position, size, new Vector2(0.5f, 0.5f));

        Text text = NewText("Text", background.transform, string.Empty, FontSize + 4, TextColor, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        text.supportRichText = false;

        Text hint = NewText("Placeholder", background.transform, placeholder, FontSize + 2,
            new Color(TextColor.r, TextColor.g, TextColor.b, 0.35f), TextAnchor.MiddleCenter);
        Stretch(hint.rectTransform);

        InputField field = background.gameObject.AddComponent<InputField>();
        field.textComponent = text;
        field.placeholder = hint;
        field.targetGraphic = background;

        // 右端に「いま何文字か」
        Text counter = NewText("Counter", background.transform, string.Empty, FontSize - 8, NoteColor, TextAnchor.MiddleRight);
        Place(counter.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(120f, 40f), new Vector2(1f, 0.5f));

        background.gameObject.AddComponent<TitleInput>().EditorSetup(kind, counter);
    }

    private static TitleMenuButton NewButton(Transform parent, string label, Vector2 size, int fontSize, TitleButtonAction action)
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
        look.EditorSetup(action, background, text);
        return look;
    }

    /// <summary>左下の「Esc 戻る」。</summary>
    private static void AddBackButton(Transform page)
    {
        TitleMenuButton back = NewButton(page, "Esc／B  戻る", new Vector2(260f, 56f), FontSize - 2, TitleButtonAction.Back);
        Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(60f, 30f), new Vector2(260f, 56f), new Vector2(0f, 0f));
    }

    /// <summary>縦に流れる一覧（部屋の一覧）。中身を入れる入れ物を返す。ここだけは自動で並べる。</summary>
    private static RectTransform NewScrollList(Transform parent)
    {
        RectTransform viewRect = NewRect("Viewport", parent);
        viewRect.gameObject.AddComponent<RectMask2D>();
        Stretch(viewRect);
        viewRect.offsetMin = new Vector2(24f, 24f);
        viewRect.offsetMax = new Vector2(-24f, -24f);

        RectTransform contentRect = NewRect("Content", viewRect);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
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

    private static Image NewImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        return image;
    }

    private static Text NewText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rect = NewRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = defaultFont; // 再生すると TitleScreen がパソコンの日本語フォントに差し替える
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        return text;
    }

    private static void Mark(Component target, TitlePartRole role)
    {
        target.gameObject.AddComponent<TitlePart>().EditorSetRole(role);
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
