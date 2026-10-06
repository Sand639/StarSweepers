using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **タイトル画面のプレハブ（`Assets/Resources/TitleScreen.prefab`）を作るツール。**（2026/10/6・大槻さん
/// 「プレハブで組み直して、エディタで配置もいじれるようにしてほしい」）
///
/// ・プレハブが無いときは、Unity が読み込んだときに**自動で1回だけ作り**、TitleScene に置く
/// ・`Tools > StarSweepers > タイトル画面のプレハブを作り直す` で、最初の状態に作り直せる（**手で直した配置・画像・色は消える**）
///
/// 部品は**決まった位置に置くだけ**（自動で並べる仕組みは、パブリックサーバーの一覧の中だけ）。
/// だから、作ったあとはエディタで**どの部品も自由に動かせる。**
/// コードは部品の名前や並び順ではなく、付いている印（<see cref="TitlePart"/> / <see cref="TitleMenuButton"/> の Action など）で見つける。
///
/// 見た目の初期値は、2026/10/6 までコードで組み立てていた画面と同じ（めっちゃカメレオン風）。
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
    private static readonly Color TitleColor = new Color(1f, 0.95f, 0.55f, 1f);
    private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.10f, 0.75f);
    private static readonly Color AccentColor = new Color(0.58f, 0.10f, 0.95f, 0.95f);
    private static readonly Color TintColor = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Color SliderFillColor = new Color(0.20f, 0.95f, 0.35f, 1f);
    private static readonly Vector2 MainButtonSize = new Vector2(420f, 76f);

    private static Font defaultFont;

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

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

            // **作りが大きく変わったときだけ、作り直す**（パスワードの欄を足した など）。
            // 手で直した配置は消えるので、むやみに番号を上げないこと
            TitleScreen existing = AssetDatabase.LoadAssetAtPath<TitleScreen>(PrefabPath);
            if (existing != null && existing.PrefabVersion < PrefabVersion)
            {
                Debug.LogWarning($"[UI] タイトル画面のプレハブの作りが古い（{existing.PrefabVersion} → {PrefabVersion}）ので、作り直しました。" +
                                 "手で動かした配置や、変えた画像・色は消えています。");
                Build();
            }
        };
    }

    /// <summary>
    /// **プレハブの作りの版。** 作りが大きく変わったとき（部品を足したなど）だけ上げる。
    /// 上げると、古い版のプレハブは Unity を開いたときに自動で作り直される（**手で直した配置は消える**）。
    /// 1：最初の版 ／ 2：パスワードの欄 ／ 3：パブリック・プライベートの2つのチェック（2026/10/6）
    /// </summary>
    public const int PrefabVersion = 3;

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
        BuildMainPage(root.transform);
        BuildCreatePage(root.transform).SetActive(false);
        BuildFindPage(root.transform).SetActive(false);
        BuildFindPrivatePage(root.transform).SetActive(false);
        BuildFindPublicPage(root.transform).SetActive(false);
        BuildFindLanPage(root.transform).SetActive(false);
        BuildSettingsPage(root.transform).SetActive(false);
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

    private static GameObject BuildMainPage(Transform root)
    {
        GameObject page = NewPage("MainPage", root, TitlePartRole.PageMain);

        // ロゴ（画像に変えるときは、この文字を消して Image を置く）
        Text title = NewText("Title", page.transform, "Star Sweepers", 110, TitleColor, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4f, -4f);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -120f), new Vector2(1200f, 200f), new Vector2(0f, 1f));

        // 左上：いまの名前
        Text name = NewText("PlayerName", page.transform, "名前：（再生すると入る）", FontSize - 4, TextColor, TextAnchor.UpperLeft);
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(900f, 40f), new Vector2(0f, 1f));
        Mark(name, TitlePartRole.PlayerNameText);

        // メインのボタン（左下に縦に並べる）
        Column column = new Column("Buttons", page.transform, MainButtonSize.x, 22f,
            new Vector2(0f, 0f), new Vector2(220f, 180f), new Vector2(0f, 0f));
        column.Add(NewButton(column.Rect, "サーバーを作る", MainButtonSize, ButtonFontSize, TitleButtonAction.OpenCreate));
        column.Add(NewButton(column.Rect, "サーバーを探す", MainButtonSize, ButtonFontSize, TitleButtonAction.OpenFind));
        column.Add(NewButton(column.Rect, "設定", MainButtonSize, ButtonFontSize, TitleButtonAction.OpenSettings));
        column.Add(NewButton(column.Rect, "ゲームを終了", MainButtonSize, ButtonFontSize, TitleButtonAction.Quit));
        column.Finish();

        // 右下：バージョン（{version} は再生したときに Project Settings の Version に置き換わる）
        Text version = NewText("Version", page.transform, "version - {version}", FontSize, TextColor, TextAnchor.LowerRight);
        Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(600f, 40f), new Vector2(1f, 0f));
        Mark(version, TitlePartRole.VersionText);

        return page;
    }

    private static GameObject BuildCreatePage(Transform root)
    {
        GameObject page = NewPage("CreatePage", root, TitlePartRole.PageCreate);

        Column column = new Column("Column", page.transform, 1100f, 10f,
            new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(0.5f, 1f));

        AddNameInput(column);

        // 最大人数
        AddLabel(column, "最大人数", "2〜8人");
        Row players = new Row(column, 70f, 12f);
        players.Add(NewButton(players.Rect, "-1", new Vector2(110f, 64f), FontSize, TitleButtonAction.MaxPlayersDown));
        Image playersBox = NewImage("Players", players.Rect, PanelColor);
        playersBox.rectTransform.sizeDelta = new Vector2(300f, 64f);
        players.Add(playersBox.rectTransform);
        Text playersValue = NewText("Value", playersBox.transform, "4 人", FontSize + 6, TextColor, TextAnchor.MiddleCenter);
        Stretch(playersValue.rectTransform);
        Mark(playersValue, TitlePartRole.MaxPlayersText);
        players.Add(NewButton(players.Rect, "+1", new Vector2(110f, 64f), FontSize, TitleButtonAction.MaxPlayersUp));

        // サーバーの名前
        AddLabel(column, "サーバーの名前を入力", "パブリックサーバーの一覧に出る名前。空なら「（名前）のサーバー」");
        AddInput(column, "テキストを入力", TitleInputKind.ServerName);

        // パスワード（インターネットのときだけ。LAN のときは隠す）
        Column passwordGroup = new Column("PasswordRow", column.Rect, column.Width, 6f,
            new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 1f));
        AddLabel(passwordGroup, "パスワード",
            $"空ならパスワードなし。{InternetConnection.PasswordMinLength}〜{InternetConnection.PasswordMaxLength}文字。入る人に伝える");
        AddInput(passwordGroup, "パスワードなし", TitleInputKind.CreatePassword);
        passwordGroup.Finish();
        column.Add(passwordGroup.Rect);
        Mark(passwordGroup.Rect, TitlePartRole.PasswordRow);

        // つなぎかた
        AddLabel(column, "つなぎかた", "展示会場では LAN（同じネットワークのPC同士）");
        Row mode = new Row(column, 64f, 12f);
        mode.Add(NewButton(mode.Rect, "インターネット", new Vector2(360f, 60f), FontSize, TitleButtonAction.UseInternet));
        mode.Add(NewButton(mode.Rect, "LAN", new Vector2(360f, 60f), FontSize, TitleButtonAction.UseLan));

        // パブリック／プライベート（片方を押すと、もう片方のチェックが外れる。LAN のときは隠す。2026/10/6）
        Row privateRow = new Row(column, 60f, 12f, "PrivateRow");
        Mark(privateRow.Rect, TitlePartRole.PrivateRow);
        TitleMenuButton publicCheck = NewButton(privateRow.Rect, "■", new Vector2(64f, 56f), FontSize + 6, TitleButtonAction.SetPublic);
        Mark(publicCheck.Label, TitlePartRole.PublicCheckText);
        privateRow.Add(publicCheck);
        Text publicLabel = NewText("PublicLabel", privateRow.Rect, "パブリック", FontSize, TextColor, TextAnchor.MiddleLeft);
        publicLabel.rectTransform.sizeDelta = new Vector2(150f, 56f);
        privateRow.Add(publicLabel.rectTransform);
        TitleMenuButton check = NewButton(privateRow.Rect, "□", new Vector2(64f, 56f), FontSize + 6, TitleButtonAction.SetPrivate);
        Mark(check.Label, TitlePartRole.PrivateCheckText);
        privateRow.Add(check);
        Text privateLabel = NewText("Label", privateRow.Rect, "プライベート", FontSize, TextColor, TextAnchor.MiddleLeft);
        privateLabel.rectTransform.sizeDelta = new Vector2(170f, 56f);
        privateRow.Add(privateLabel.rectTransform);
        Text privateNote = NewText("Note", privateRow.Rect, "パブリック：一覧に出る ／ プライベート：参加コードを知っている人だけ",
            FontSize - 8, NoteColor, TextAnchor.MiddleLeft);
        privateNote.rectTransform.sizeDelta = new Vector2(560f, 56f);
        privateRow.Add(privateNote.rectTransform);

        // 参加コードの枠
        Image codeBox = NewImage("CodeBox", column.Rect, AccentColor);
        codeBox.rectTransform.sizeDelta = new Vector2(1100f, 120f);
        column.Add(codeBox.rectTransform);
        Text codeTitle = NewText("Title", codeBox.transform, "参加コード", FontSize + 4, TextColor, TextAnchor.UpperCenter);
        Place(codeTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(1060f, 40f), new Vector2(0.5f, 1f));
        Mark(codeTitle, TitlePartRole.CodeBoxTitle);
        Text codeBody = NewText("Body", codeBox.transform, "（再生すると、つなぎかたに合わせた説明が入る）", FontSize - 4, NoteColor, TextAnchor.UpperCenter);
        Place(codeBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1060f, 64f), new Vector2(0.5f, 1f));
        Mark(codeBody, TitlePartRole.CodeBoxBody);

        column.Add(NewButton(column.Rect, "サーバーを作成", new Vector2(1100f, 70f), ButtonFontSize, TitleButtonAction.CreateServer));
        column.Add(NewButton(column.Rect, "1人で練習する（通信を使わない）", new Vector2(1100f, 54f), FontSize - 2, TitleButtonAction.PracticeSolo));
        column.Finish();

        AddBackButton(page.transform);
        return page;
    }

    private static GameObject BuildFindPage(Transform root)
    {
        GameObject page = NewPage("FindPage", root, TitlePartRole.PageFind);

        Column column = new Column("Buttons", page.transform, 640f, 22f,
            new Vector2(0f, 0f), new Vector2(220f, 260f), new Vector2(0f, 0f));
        Vector2 size = new Vector2(640f, MainButtonSize.y);
        column.Add(NewButton(column.Rect, "プライベートサーバーを探す", size, ButtonFontSize, TitleButtonAction.OpenFindPrivate));
        column.Add(NewButton(column.Rect, "パブリックサーバーを探す", size, ButtonFontSize, TitleButtonAction.OpenFindPublic));
        column.Add(NewButton(column.Rect, "LANのサーバーに参加する", size, ButtonFontSize, TitleButtonAction.OpenFindLan));
        column.Finish();

        AddBackButton(page.transform);
        return page;
    }

    private static GameObject BuildFindPrivatePage(Transform root)
    {
        GameObject page = NewPage("FindPrivatePage", root, TitlePartRole.PageFindPrivate);

        Column column = new Column("Column", page.transform, 1000f, 14f,
            new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(0.5f, 1f));
        AddNameInput(column);
        AddLabel(column, "参加コードを入力", "サーバーを作った人に教えてもらう");
        AddInput(column, "例：6FJBMH", TitleInputKind.PrivateCode);
        AddLabel(column, "パスワード", "パスワードの付いた部屋に入るときだけ");
        AddInput(column, "パスワードなし", TitleInputKind.JoinPassword);
        column.Add(NewButton(column.Rect, "参加する", new Vector2(1000f, 70f), ButtonFontSize, TitleButtonAction.JoinPrivate));
        column.Finish();

        AddBackButton(page.transform);
        return page;
    }

    private static GameObject BuildFindPublicPage(Transform root)
    {
        GameObject page = NewPage("FindPublicPage", root, TitlePartRole.PageFindPublic);

        // 左：名前と検索
        Column left = new Column("Left", page.transform, 640f, 14f,
            new Vector2(0f, 1f), new Vector2(120f, -80f), new Vector2(0f, 1f));
        AddNameInput(left);
        AddLabel(left, "パスワード", "（パスワードあり）の部屋に入るとき");
        AddInput(left, "パスワードなし", TitleInputKind.JoinPassword);
        left.Add(NewButton(left.Rect, "パブリックサーバーを検索", new Vector2(640f, 70f), FontSize + 2, TitleButtonAction.SearchPublic));
        Text note = NewText("Note", left.Rect, "プライベートサーバーは一覧に出ません（参加コードで入る）。LAN のサーバーも出ません。",
            FontSize - 8, NoteColor, TextAnchor.UpperLeft);
        note.rectTransform.sizeDelta = new Vector2(640f, 70f);
        left.Add(note.rectTransform);
        left.Finish();

        // 右：検索結果
        Text resultsTitle = NewText("ResultsTitle", page.transform, "検索結果", FontSize + 6, TextColor, TextAnchor.LowerLeft);
        Place(resultsTitle.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -60f), new Vector2(800f, 50f), new Vector2(0f, 1f));

        Image listBox = NewImage("ListBox", page.transform, PanelColor);
        Place(listBox.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -120f), new Vector2(820f, 760f), new Vector2(0f, 1f));

        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, TitlePartRole.PublicListContent);
        BuildServerCardTemplate(content);

        Text message = NewText("Message", listBox.transform, string.Empty, FontSize - 2, TextColor, TextAnchor.UpperCenter);
        Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(780f, 120f), new Vector2(0.5f, 1f));
        Mark(message, TitlePartRole.PublicListMessage);

        AddBackButton(page.transform);
        return page;
    }

    /// <summary>サーバー1つぶんのカードの見本。再生すると隠れ、見つかったサーバーの数だけ複製される。</summary>
    private static void BuildServerCardTemplate(RectTransform content)
    {
        Image card = NewImage("ServerCardTemplate", content, AccentColor);
        SetLayoutSize(card.gameObject, 780f, 150f);
        Mark(card, TitlePartRole.ServerCardTemplate);

        Text name = NewText("Name", card.transform, "サーバーの名前", FontSize + 2, TextColor, TextAnchor.UpperLeft);
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(560f, 44f), new Vector2(0f, 1f));
        Mark(name, TitlePartRole.ServerCardName);

        Text count = NewText("Players", card.transform, "1/4", FontSize + 2, TextColor, TextAnchor.UpperRight);
        Place(count.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -12f), new Vector2(160f, 44f), new Vector2(1f, 1f));
        Mark(count, TitlePartRole.ServerCardPlayers);

        TitleMenuButton join = NewButton(card.transform, "参加する", new Vector2(740f, 64f), FontSize, TitleButtonAction.JoinListedServer);
        Place((RectTransform)join.transform, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(740f, 64f), new Vector2(0.5f, 0f));
    }

    private static GameObject BuildFindLanPage(Transform root)
    {
        GameObject page = NewPage("FindLanPage", root, TitlePartRole.PageFindLan);

        Column column = new Column("Column", page.transform, 1000f, 14f,
            new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(0.5f, 1f));
        AddNameInput(column);
        AddLabel(column, "ホストのIPアドレスを入力", "サーバーを作った人の画面（サーバーを作る → LAN）に出ている番号");
        AddInput(column, "例：192.168.0.10", TitleInputKind.LanAddress);
        column.Add(NewButton(column.Rect, "参加する", new Vector2(1000f, 70f), ButtonFontSize, TitleButtonAction.JoinLan));
        column.Finish();

        AddBackButton(page.transform);
        return page;
    }

    private static GameObject BuildSettingsPage(Transform root)
    {
        GameObject page = NewPage("SettingsPage", root, TitlePartRole.PageSettings);

        // 左：タブ
        Column tabs = new Column("Tabs", page.transform, 300f, 16f,
            new Vector2(0f, 1f), new Vector2(80f, -100f), new Vector2(0f, 1f));
        tabs.Add(NewButton(tabs.Rect, "サウンド", new Vector2(300f, 64f), FontSize, TitleButtonAction.TabSound));
        tabs.Add(NewButton(tabs.Rect, "グラフィック", new Vector2(300f, 64f), FontSize, TitleButtonAction.TabGraphics));
        tabs.Add(NewButton(tabs.Rect, "ゲーム全般", new Vector2(300f, 64f), FontSize, TitleButtonAction.TabGeneral));
        tabs.Finish();

        // 右：タブごとの中身（選んだタブの中身だけが出る）
        Column sound = SettingsGroup(page.transform, "SoundGroup", TitlePartRole.SettingsSound);
        AddSliderRow(sound, "音量", TitleSettingKind.Volume, 0f, 1f, 0.8f);
        sound.Finish();

        Column graphics = SettingsGroup(page.transform, "GraphicsGroup", TitlePartRole.SettingsGraphics);
        AddStepperRow(graphics, "解像度", TitleSettingKind.Resolution);
        AddStepperRow(graphics, "スクリーンモード", TitleSettingKind.ScreenMode);
        graphics.Add(NewButton(graphics.Rect, "解像度／スクリーンモードを適用", new Vector2(1300f, 60f), FontSize, TitleButtonAction.ApplyScreen));
        AddSliderRow(graphics, "フレームレート上限", TitleSettingKind.FrameRateLimit, 30f, 240f, 60f);
        AddStepperRow(graphics, "垂直同期", TitleSettingKind.VSync);
        AddStepperRow(graphics, "画質", TitleSettingKind.Quality);
        Text note = NewText("Note", graphics.Rect, "垂直同期が ON のときは、フレームレート上限は効きません（画面の更新に合わせます）。",
            FontSize - 8, NoteColor, TextAnchor.MiddleLeft);
        note.rectTransform.sizeDelta = new Vector2(1300f, 40f);
        graphics.Add(note.rectTransform);
        graphics.Finish();
        graphics.Rect.gameObject.SetActive(false);

        Column general = SettingsGroup(page.transform, "GeneralGroup", TitlePartRole.SettingsGeneral);
        AddSliderRow(general, "マウス感度", TitleSettingKind.MouseSensitivity, 0.2f, 3f, 1f);
        AddStepperRow(general, "操作タイプ（コントローラー）", TitleSettingKind.ControllerType);
        general.Finish();
        general.Rect.gameObject.SetActive(false);

        AddBackButton(page.transform);
        return page;
    }

    private static Column SettingsGroup(Transform page, string name, TitlePartRole role)
    {
        Column group = new Column(name, page, 1300f, 12f, new Vector2(0f, 1f), new Vector2(460f, -100f), new Vector2(0f, 1f));
        Mark(group.Rect, role);
        return group;
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

        public float Width => width;

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

    /// <summary>左から横に並べる1行。行そのものは <see cref="Column"/> に入る。</summary>
    private sealed class Row
    {
        public readonly RectTransform Rect;
        private readonly float spacing;
        private float x;

        public Row(Column column, float height, float spacing, string name = "Row")
        {
            Rect = NewRect(name, column.Rect);
            Rect.sizeDelta = new Vector2(column.Width, height);
            column.Add(Rect);
            this.spacing = spacing;
        }

        public void Add(RectTransform child)
        {
            child.SetParent(Rect, false);
            Place(child, new Vector2(0f, 0.5f), new Vector2(x, 0f), child.sizeDelta, new Vector2(0f, 0.5f));
            x += child.sizeDelta.x + spacing;
        }

        public void Add(Component child)
        {
            Add((RectTransform)child.transform);
        }
    }

    private static GameObject NewPage(string name, Transform root, TitlePartRole role)
    {
        RectTransform page = NewRect(name, root);
        Stretch(page);
        Mark(page, role);
        return page.gameObject;
    }

    /// <summary>見出し（と小さい説明）を1行。</summary>
    private static void AddLabel(Column column, string label, string note)
    {
        Row row = new Row(column, 40f, 16f, "Label");

        Text title = NewText("Label", row.Rect, label, FontSize, TextColor, TextAnchor.LowerLeft);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.rectTransform.sizeDelta = new Vector2(Mathf.Max(200f, label.Length * FontSize + 10f), 40f);
        row.Add(title.rectTransform);

        if (!string.IsNullOrEmpty(note))
        {
            Text small = NewText("Note", row.Rect, note, FontSize - 8, NoteColor, TextAnchor.LowerLeft);
            small.rectTransform.sizeDelta = new Vector2(800f, 40f);
            row.Add(small.rectTransform);
        }
    }

    /// <summary>「ゲーム内で表示する名前」の入力欄。どの画面で入れても同じ名前（保存される）。</summary>
    private static void AddNameInput(Column column)
    {
        AddLabel(column, "ゲーム内で表示する名前を入力", $"{GameSettings.PlayerNameMaxLength}文字まで");
        AddInput(column, "名前を入力", TitleInputKind.PlayerName);
    }

    private static void AddInput(Column column, string placeholder, TitleInputKind kind)
    {
        Image background = NewImage("Input " + kind, column.Rect, PanelColor);
        background.rectTransform.sizeDelta = new Vector2(Mathf.Min(1000f, column.Width), 64f);
        column.Add(background.rectTransform);

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

    /// <summary>「名前　＜ 値 ＞」の1行。</summary>
    private static void AddStepperRow(Column column, string label, TitleSettingKind kind)
    {
        Image row = NewImage(label, column.Rect, PanelColor);
        row.rectTransform.sizeDelta = new Vector2(1300f, 64f);
        column.Add(row.rectTransform);

        Text title = NewText("Label", row.transform, label, FontSize, TextColor, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(560f, 60f), new Vector2(0f, 0.5f));

        Text value = NewText("Value", row.transform, "—", FontSize, TextColor, TextAnchor.MiddleCenter);
        Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(460f, 60f), new Vector2(1f, 0.5f));

        TitleMenuButton left = NewButton(row.transform, "＜", new Vector2(64f, 52f), FontSize, TitleButtonAction.None);
        Place((RectTransform)left.transform, new Vector2(1f, 0.5f), new Vector2(-560f, 0f), new Vector2(64f, 52f), new Vector2(1f, 0.5f));

        TitleMenuButton right = NewButton(row.transform, "＞", new Vector2(64f, 52f), FontSize, TitleButtonAction.None);
        Place((RectTransform)right.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(64f, 52f), new Vector2(1f, 0.5f));

        row.gameObject.AddComponent<TitleSettingRow>().EditorSetup(kind, value, null,
            left.GetComponent<Button>(), right.GetComponent<Button>());
    }

    /// <summary>「名前　値　━━●━━」の1行。</summary>
    private static void AddSliderRow(Column column, string label, TitleSettingKind kind, float min, float max, float initial)
    {
        Image row = NewImage(label, column.Rect, PanelColor);
        row.rectTransform.sizeDelta = new Vector2(1300f, 64f);
        column.Add(row.rectTransform);

        Text title = NewText("Label", row.transform, label, FontSize, TextColor, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(460f, 60f), new Vector2(0f, 0.5f));

        Text value = NewText("Value", row.transform, "—", FontSize, TextColor, TextAnchor.MiddleRight);
        Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(480f, 0f), new Vector2(200f, 60f), new Vector2(0f, 0.5f));

        RectTransform sliderRect = NewRect("Slider", row.transform);
        Place(sliderRect, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(520f, 16f), new Vector2(1f, 0.5f));

        Image track = NewImage("Track", sliderRect, new Color(1f, 1f, 1f, 0.25f));
        Stretch(track.rectTransform);

        RectTransform fillArea = NewRect("Fill Area", sliderRect);
        Stretch(fillArea);
        Image fill = NewImage("Fill", fillArea, SliderFillColor);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        Image handle = NewImage("Handle", sliderRect, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(24f, 36f);

        Slider slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;

        row.gameObject.AddComponent<TitleSettingRow>().EditorSetup(kind, value, slider, null, null);
    }

    /// <summary>縦に流れる一覧（パブリックサーバーの検索結果）。中身を入れる入れ物を返す。ここだけは自動で並べる。</summary>
    private static RectTransform NewScrollList(Transform parent)
    {
        RectTransform viewRect = NewRect("Viewport", parent);
        viewRect.gameObject.AddComponent<RectMask2D>();
        Stretch(viewRect);
        viewRect.offsetMin = new Vector2(16f, 16f);
        viewRect.offsetMax = new Vector2(-16f, -16f);

        RectTransform contentRect = NewRect("Content", viewRect);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
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
