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
///
/// ## 位置・大きさの数字の読み方
///
/// どれも **1920×1080 の画面での画素数**（実際の画面の大きさに合わせて、CanvasScaler が伸び縮みさせる）。
/// 位置は「アンカー（親のどこを基準にするか）」からのずれで、**y はマイナスほど下**。
/// 置き方は <see cref="Place"/> にまとめてある。
/// </summary>
public static class SpaceJunkLobbyScreenBuilder
{
    /// <summary>
    /// 作るプレハブの置き場所。`Resources` の下に置くのは、再生中にコード（<see cref="SpaceJunkLobbyUI"/>）から
    /// 名前で読み込めるようにするため。**場所や名前を変えるときは、読み込む側も合わせて直す。**
    /// </summary>
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

    /// <summary>
    /// 文字に使う、Unity に最初から入っているフォント。<see cref="Build"/> と <see cref="Upgrade"/> の最初で読み込む
    /// （<see cref="NewText"/> がこれを使うので、部品を作る前に必ず入れておく）。
    /// </summary>
    private static Font defaultFont;

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

    /// <summary>
    /// Unity がスクリプトを読み込むたびに呼ばれる。プレハブが無ければ作り、古ければ作り直すか部品を足す。
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoBuildIfMissing()
    {
        // 読み込みの最中はアセットを作れないことがあるので、エディタが落ち着いた次の瞬間まで待つ
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!File.Exists(PrefabPath))
            {
                Debug.Log("[JUNK] ロビーの画面のプレハブが無いので作ります（初回だけ）。");
                Build();
                return;
            }

            // 版 3 より古い（手で直す前の）プレハブは作り直す。版 3 からは**作り直さずに、足りない部品だけを足す**（手で直した配置を消さない）
            SpaceJunkLobbyScreen existing = AssetDatabase.LoadAssetAtPath<SpaceJunkLobbyScreen>(PrefabPath);
            if (existing != null && existing.PrefabVersion < OldestUpgradableVersion)
            {
                Debug.LogWarning($"[JUNK] ロビーの画面のプレハブの作りが古い（{existing.PrefabVersion} → {PrefabVersion}）ので、作り直しました。" +
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
    /// **プレハブの作りの版。** 作りが大きく変わったとき（部品を足したなど）だけ上げる。
    /// 上げると、Unity を開いたときに古い版のプレハブが新しくなる。
    /// 1：最初の版 ／ 2：数字を打ち込める入力欄 ／ 3：数字を真ん中に・パスワードの欄 ／ 4：左上に参加コード（2026/10/6）
    /// **版 3 からは作り直さず、<see cref="Upgrade"/> で足りない部品だけを足す**（手で直した配置を消さない）。版を上げたら Upgrade に足す処理を書くこと
    /// </summary>
    public const int PrefabVersion = 4;

    /// <summary>
    /// **これより古い版は、足りない部品を足すのではなく作り直す。**
    /// 版 3 より前は部品の付け足しに対応していないため（<see cref="PrefabVersion"/> の説明を参照）。
    /// </summary>
    private const int OldestUpgradableVersion = 3;

    /// <summary>左上の案内の高さ（参加コードの行を足して 2026/10/6 に 720 → 800）。</summary>
    private const float HudHeight = 800f;

    /// <summary>左上の案内に「参加コード（ホストにはパスワード・LAN の IP）」の文字を足す（版 4）。</summary>
    private static void AddHudJoinCode(Transform hud)
    {
        Text joinCode = NewText("JoinCode", hud, "参加コード：ABCDEF", 24, TextColor, TextAnchor.UpperLeft);
        joinCode.fontStyle = FontStyle.Bold;
        joinCode.horizontalOverflow = HorizontalWrapMode.Overflow;
        // 案内の文（Hint：上から 650、高さ 60）のすぐ下。下端は上から 786 で、案内の高さ（HudHeight）の中に収まる
        Place(joinCode.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -716f), new Vector2(420f, 70f), new Vector2(0f, 1f));
        Mark(joinCode, SpaceJunkLobbyPartRole.HudJoinCode);
    }

    /// <summary>
    /// **作り直さずに、足りない部品だけを足す**（手で直した配置を消さないため。2026/10/6 から）。
    /// 版 3 → 4：左上の案内に参加コードの行を足す。
    ///
    /// **名前と引数を変えないこと。** 一時スクリプトからリフレクション（名前を文字で指定して呼ぶ方法）で
    /// 呼んだ記録があるため、変えるとそれが静かに動かなくなる。
    /// </summary>
    private static void Upgrade(int fromVersion)
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            if (fromVersion < 4)
            {
                // 足す前に「もう参加コードがあるか」を見る（手で足してあった場合に二重にしないため）
                SpaceJunkLobbyPart hud = FindPart(root, SpaceJunkLobbyPartRole.Hud);
                if (hud != null && FindPart(root, SpaceJunkLobbyPartRole.HudJoinCode) == null)
                {
                    // 参加コードの行が入るように、案内を縦に伸ばす（手で大きくしてあれば、そのまま）
                    RectTransform hudRect = (RectTransform)hud.transform;
                    if (hudRect.sizeDelta.y < HudHeight)
                    {
                        hudRect.sizeDelta = new Vector2(hudRect.sizeDelta.x, HudHeight);
                    }

                    AddHudJoinCode(hud.transform);
                }
            }

            root.GetComponent<SpaceJunkLobbyScreen>().EditorSetPrefabVersion(PrefabVersion);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[JUNK] ロビーの画面のプレハブに、足りない部品を足しました（版 {fromVersion} → {PrefabVersion}。手で直した配置はそのまま）。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>プレハブの中から、その役目の印が付いた部品を1つ探す（隠れている部品も含める）。無ければ null。</summary>
    private static SpaceJunkLobbyPart FindPart(GameObject root, SpaceJunkLobbyPartRole role)
    {
        foreach (SpaceJunkLobbyPart part in root.GetComponentsInChildren<SpaceJunkLobbyPart>(true))
        {
            if (part.Role == role)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>メニューから作り直す。プレハブがもうあるときは、手で直した中身が消えるので先に確かめる。</summary>
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

    /// <summary>
    /// **プレハブを最初の状態で作り直して保存し、ロビーのシーンに置く**（置いてなければ）。
    /// すでにあるプレハブは上書きされる（手で直した配置・画像・色は消える）。
    /// </summary>
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

        // もう置いてあれば何もしない（二重に置かない）。自分で開いたシーンなら閉じて元に戻す
        if (HasLobbyScreen(scene))
        {
            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            return;
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

    /// <summary>そのシーンに、ロビーの画面（<see cref="SpaceJunkLobbyScreen"/>）がもう置いてあるか（隠れていても数える）。</summary>
    private static bool HasLobbyScreen(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.GetComponentInChildren<SpaceJunkLobbyScreen>(true) != null)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    /// <summary>
    /// 画面のいちばん外側（Canvas）を作り、中身を組み立てて返す。
    /// 作る順番がそのままプレハブの中の並び順になる（**後に作った物ほど手前に描かれる**）。
    /// </summary>
    private static GameObject BuildRoot(Scene stage)
    {
        GameObject root = new GameObject("LobbyScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, stage);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80; // タイトル画面（90）・ポーズ画面（100）より後ろ

        // 1920×1080 を基準に作り、実際の画面の大きさに合わせて伸び縮みさせる（0.5＝幅と高さの中間で合わせる）
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<SpaceJunkLobbyScreen>().EditorSetPrefabVersion(PrefabVersion);

        BuildHud(root.transform);
        BuildSettingsWindow(root.transform);

        return root;
    }

    /// <summary>
    /// ふだん左上に出ている案内（次のマップ・人数・参加者・設定・案内）。
    /// 中身は上から順に積んである（画像 → マップ名 → 人数 → 参加者 → 設定のまとめ → 案内 → 参加コード）。
    /// 1つを高くするときは、その下の部品の y もずらし、最後に <see cref="HudHeight"/> を見直すこと。
    /// </summary>
    private static void BuildHud(Transform root)
    {
        Image hud = NewImage("Hud", root, new Color(0f, 0f, 0f, 0.55f));
        Place(hud.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(460f, HudHeight), new Vector2(0f, 1f));
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

        AddHudJoinCode(hud.transform);
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

        // 始められない理由や、誰もいないチームの知らせ（ボタン（下から 20・高さ 64）のすぐ上に出す）
        Text message = NewText("StartMessage", window, string.Empty, 22, WarnColor, TextAnchor.MiddleCenter);
        Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1480f, 36f), new Vector2(0.5f, 0f));
        Mark(message, SpaceJunkLobbyPartRole.StartMessage);

        // 左下：戻る
        TitleMenuButton back = NewThemedButton(window, "Esc／B  戻る", new Vector2(220f, 60f), 22);
        Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(220f, 60f), new Vector2(0f, 0f));
        AddAction(back, SpaceJunkLobbyAction.Close, 0);

        // 最初は隠しておく（ホストが設定端末で開いたときだけ出す）
        window.gameObject.SetActive(false);
    }

    /// <summary>
    /// **左の大きいパネル「ゲーム設定」。** パネルの中は2列で、
    /// 左の列（x=40・幅 520）に数の設定・ルール・パスワード、右の列（x=600・幅 540）にチーム分けを置く。
    /// </summary>
    private static void BuildGameSettings(Transform window)
    {
        Image panel = NewImage("GameSettings", window, PanelColor);
        Place(panel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(1180f, 900f), new Vector2(0f, 1f));
        Transform panelRoot = panel.transform;

        Text title = NewText("Title", panelRoot, "ゲーム設定", 40, TextColor, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -20f), new Vector2(600f, 60f), new Vector2(0f, 1f));

        // 作る順番がそのままプレハブの中の並び順になるので、
        // 「数の設定 → ルール → パスワード → チーム分け」の順は変えない
        BuildNumberSettings(panelRoot);
        BuildPasswordSection(panelRoot);
        BuildTeamAssignment(panelRoot);
    }

    /// <summary>
    /// ゲーム設定の左の列の上側：チーム数・何本先取・1ラウンドの最大時間と、ルールの説明。
    /// 見出し（高さ 44）の 50 下に値の行（高さ 70）を置き、数の3項目は 150 ずつ下へずらしている。
    /// </summary>
    private static void BuildNumberSettings(Transform panelRoot)
    {
        // チーム数だけは「＜ ＞」で変える。箱を右へ 90 ずらして、左に ＜（x=0）、右に ＞（x=450）を置く
        Heading(panelRoot, "チーム数", new Vector2(40f, -100f), 520f);
        RectTransform teamRow = ValueRow(panelRoot, "TeamCount", new Vector2(40f, -150f), SpaceJunkLobbyValueKind.TeamCount,
            new Vector2(90f, 0f), 340f, "2", "チーム", "1〜4");
        ArrowButton(teamRow, "＜", new Vector2(0f, 0f), SpaceJunkLobbyAction.Step, -1);
        ArrowButton(teamRow, "＞", new Vector2(450f, 0f), SpaceJunkLobbyAction.Step, 1);

        // ほかは、箱の右に「+n / -n」のボタンを並べる（第2引数はボタンの左端の x。ボタンの幅は 80）
        Heading(panelRoot, "何本先取", new Vector2(40f, -250f), 520f);
        RectTransform winsRow = ValueRow(panelRoot, "RoundsToWin", new Vector2(40f, -300f), SpaceJunkLobbyValueKind.RoundsToWin,
            Vector2.zero, 340f, "2", "本先取", "1〜5");
        StepButtons(winsRow, 350f, 1);

        // 最大時間は ±1・±10・±50 の3組。箱を短く（260）して、84 ずつ右へ並べる
        Heading(panelRoot, "1ラウンドの最大時間（秒）", new Vector2(40f, -400f), 520f);
        RectTransform secondsRow = ValueRow(panelRoot, "RoundSeconds", new Vector2(40f, -450f), SpaceJunkLobbyValueKind.RoundSeconds,
            Vector2.zero, 260f, "60", "秒", $"{SpaceJunkSession.MinRoundSeconds}〜{SpaceJunkSession.MaxRoundSeconds}");
        StepButtons(secondsRow, 270f, 1);
        StepButtons(secondsRow, 354f, 10);
        StepButtons(secondsRow, 438f, 50);

        Heading(panelRoot, "ルール", new Vector2(40f, -560f), 520f);
        Text rule = NewText("Rule", panelRoot, "得点制：時間いっぱい宇宙ごみを集め、得点の高いチームがラウンドを取る。", 20, TextColor, TextAnchor.UpperLeft);
        Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -610f), new Vector2(520f, 110f), new Vector2(0f, 1f));
        Mark(rule, SpaceJunkLobbyPartRole.RuleText);
    }

    /// <summary>
    /// ゲーム設定の左の列の下側：**パスワード**（パスワードありの部屋のときだけ出る。ホストが変えられる。2026/10/6）。
    /// 出す・隠すは、再生中に <see cref="SpaceJunkLobbyPartRole.PasswordSection"/> の印で見つけて切り替える。
    /// </summary>
    private static void BuildPasswordSection(Transform panelRoot)
    {
        RectTransform password = NewRect("PasswordSection", panelRoot);
        Place(password, new Vector2(0f, 1f), new Vector2(40f, -730f), new Vector2(520f, 150f), new Vector2(0f, 1f));
        Mark(password, SpaceJunkLobbyPartRole.PasswordSection);

        Text passwordTitle = NewText("Title", password, "パスワード", 28, TextColor, TextAnchor.MiddleCenter);
        Place(passwordTitle.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(520f, 40f), new Vector2(0f, 1f));

        // 入力欄（左右に 12 の余白を取った文字と、空のときに出す薄い案内）
        Image inputBox = NewImage("Input", password, BoxColor);
        Place(inputBox.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(370f, 56f), new Vector2(0f, 1f));
        Text inputText = NewText("Text", inputBox.transform, string.Empty, 24, TextColor, TextAnchor.MiddleLeft);
        StretchInside(inputText.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        Text inputHint = NewText("Placeholder", inputBox.transform, "新しいパスワード", 22, new Color(1f, 1f, 1f, 0.3f), TextAnchor.MiddleLeft);
        StretchInside(inputHint.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        InputField passwordField = inputBox.gameObject.AddComponent<InputField>();
        passwordField.textComponent = inputText;
        passwordField.placeholder = inputHint;
        passwordField.targetGraphic = inputBox;
        passwordField.characterLimit = InternetConnection.PasswordMaxLength;
        Mark(inputBox, SpaceJunkLobbyPartRole.PasswordInput);

        // 入力欄（幅 370）の右に、10 空けて「変更する」
        TitleMenuButton change = NewThemedButton(password, "変更する", new Vector2(140f, 56f), 22);
        Place((RectTransform)change.transform, new Vector2(0f, 1f), new Vector2(380f, -46f), new Vector2(140f, 56f), new Vector2(0f, 1f));
        AddAction(change, SpaceJunkLobbyAction.ChangePassword, 0);

        Text passwordMessage = NewText("Message", password,
            $"{InternetConnection.PasswordMinLength}〜{InternetConnection.PasswordMaxLength}文字。変えたら、入る人に伝えてください",
            16, NoteColor, TextAnchor.UpperLeft);
        Place(passwordMessage.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -108f), new Vector2(520f, 40f), new Vector2(0f, 1f));
        Mark(passwordMessage, SpaceJunkLobbyPartRole.PasswordMessage);
    }

    /// <summary>
    /// ゲーム設定の右の列：**チーム分け**。参加者の一覧は、行の見本（<see cref="BuildPlayerRowTemplate"/>）を
    /// 再生中に人数ぶん複製して、縦に自動で並べる。
    /// </summary>
    private static void BuildTeamAssignment(Transform panelRoot)
    {
        Text teamsTitle = NewText("TeamsTitle", panelRoot, "チーム分け", 28, TextColor, TextAnchor.MiddleLeft);
        Place(teamsTitle.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -100f), new Vector2(340f, 44f), new Vector2(0f, 1f));

        TitleMenuButton randomAssign = NewThemedButton(panelRoot, "ランダムに割り振る", new Vector2(200f, 44f), 20);
        Place((RectTransform)randomAssign.transform, new Vector2(0f, 1f), new Vector2(940f, -100f), new Vector2(200f, 44f), new Vector2(0f, 1f));
        AddAction(randomAssign, SpaceJunkLobbyAction.RandomAssign, 0);

        // 一覧の枠と、その内側（四辺に 8 の余白）に行を縦に並べる入れ物
        Image listBox = NewImage("PlayerList", panelRoot, BoxColor);
        Place(listBox.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -156f), new Vector2(540f, 560f), new Vector2(0f, 1f));
        RectTransform content = NewRect("Content", listBox.transform);
        StretchInside(content, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        AddVerticalLayout(content.gameObject, 8f);
        Mark(content, SpaceJunkLobbyPartRole.PlayerListContent);
        BuildPlayerRowTemplate(content);

        Text teamsNote = NewText("TeamsNote", panelRoot,
            "名前の横の ＜ ＞ でチームを変える。人数の偏り（3人対1人など）も許しています。誰もいないチームのゴールは数えません。",
            16, NoteColor, TextAnchor.UpperLeft);
        Place(teamsNote.rectTransform, new Vector2(0f, 1f), new Vector2(600f, -726f), new Vector2(540f, 60f), new Vector2(0f, 1f));
    }

    /// <summary>
    /// チーム分けの1人ぶんの行の見本（名前・＜・チーム名・＞）。
    /// 再生すると隠れ、参加者の数だけ複製される（<see cref="SpaceJunkLobbyScreen"/> が印で見つける）。
    /// </summary>
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

    /// <summary>
    /// **右のパネル「マップ」。** 上から、選んだマップの画像・名前、マップの一覧（流せる）、注意書き・使い方。
    /// パネルは画面の右上を基準に置く（アンカーとパネルの中心を右上 (1, 1) にしている）。
    /// </summary>
    private static void BuildMapPanel(Transform window)
    {
        Image panel = NewImage("MapSettings", window, PanelColor);
        Place(panel.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(660f, 900f), new Vector2(1f, 1f));
        Transform panelRoot = panel.transform;

        Text title = NewText("Title", panelRoot, "マップ", 40, TextColor, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(600f, 60f), new Vector2(0f, 1f));

        // 画像の枠は 620×349（およそ 16:9）
        BuildPicture(panelRoot, new Vector2(20f, -90f), new Vector2(620f, 349f), 36,
            SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder);

        Text previewName = NewText("PreviewName", panelRoot, "ランダム", 24, TextColor, TextAnchor.MiddleCenter);
        Place(previewName.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -444f), new Vector2(620f, 44f), new Vector2(0f, 1f));
        Mark(previewName, SpaceJunkLobbyPartRole.MapPreviewName);

        // 一覧（いちばん上が「ランダム」。その下に、見本を複製したマップの行が並ぶ）
        Image listBox = NewImage("MapList", panelRoot, BoxColor);
        Place(listBox.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -496f), new Vector2(620f, 330f), new Vector2(0f, 1f));
        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, SpaceJunkLobbyPartRole.MapListContent);

        BuildRandomRow(content);
        BuildMapRowTemplate(content);

        Text hidden = NewText("HiddenNote", panelRoot, string.Empty, 16, WarnColor, TextAnchor.MiddleLeft);
        Place(hidden.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -832f), new Vector2(620f, 26f), new Vector2(0f, 1f));
        Mark(hidden, SpaceJunkLobbyPartRole.MapHiddenNote);

        Text help = NewText("Help", panelRoot, "名前を押す：そのマップで遊ぶ ／ 右の四角：ランダムの候補に入れる（緑）・外す（赤）",
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

        // 色付け（選んだとき）と名前は、行の幅 590 のうち左の 524 だけにする（右の残りにチェックの四角を置くため）
        Image highlight = NewImage("Highlight", row.transform, HighlightColor);
        Place(highlight.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(524f, 52f), new Vector2(0f, 0.5f));
        highlight.raycastTarget = false;
        Mark(highlight, SpaceJunkLobbyPartRole.MapRowHighlight);

        Text name = NewText("Name", row.transform, "マップの名前", 26, TextColor, TextAnchor.MiddleCenter);
        Place(name.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(524f, 52f), new Vector2(0f, 0.5f));
        Mark(name, SpaceJunkLobbyPartRole.MapRowName);

        // 右の四角：ランダムの候補に入れるか。中に「✓（緑）」と「×（赤）」を重ねて作り、再生中にどちらか一方だけを出す
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

    /// <summary>見出しの文字（高さ 44）。部品の名前は、表示する文字そのものにしている。</summary>
    private static void Heading(Transform parent, string label, Vector2 position, float width)
    {
        Text text = NewText(label, parent, label, 28, TextColor, TextAnchor.MiddleCenter);
        Place(text.rectTransform, new Vector2(0f, 1f), position, new Vector2(width, 44f), new Vector2(0f, 1f));
    }

    /// <summary>
    /// 値の1行（入れ物）と、値を出す箱。**箱は数字を打ち込める入力欄**（めっちゃカメレオンと同じ。2026/10/6）。
    /// ボタンはあとから入れ物に足す。
    /// </summary>
    /// <param name="boxPosition">行（幅 520・高さ 70）の左端の中ほどから見た、箱の左端の位置。</param>
    /// <param name="sample">作ったときに箱に入れておく数字（再生すると本当の値に置き換わる）。</param>
    /// <param name="unit">箱の右端に出す単位（「秒」など）。</param>
    /// <param name="range">箱の左下に小さく出す、入れられる範囲（「1〜4」など）。</param>
    private static RectTransform ValueRow(Transform parent, string name, Vector2 position, SpaceJunkLobbyValueKind kind,
        Vector2 boxPosition, float boxWidth, string sample, string unit, string range)
    {
        RectTransform row = NewRect(name, parent);
        Place(row, new Vector2(0f, 1f), position, new Vector2(520f, 70f), new Vector2(0f, 1f));

        Image box = NewImage("Box", row, BoxColor);
        Place(box.rectTransform, new Vector2(0f, 0.5f), boxPosition, new Vector2(boxWidth, 70f), new Vector2(0f, 0.5f));

        Text value = NewText("Value", box.transform, sample, 36, TextColor, TextAnchor.MiddleCenter);
        Stretch(value.rectTransform);

        Text placeholder = NewText("Placeholder", box.transform, "数字", 28, new Color(1f, 1f, 1f, 0.3f), TextAnchor.MiddleCenter);
        Stretch(placeholder.rectTransform);

        // 単位は右端から 12 内側、範囲は左下の角から少し内側に出す
        Text unitText = NewText("Unit", box.transform, unit, 22, TextColor, TextAnchor.MiddleRight);
        StretchInside(unitText.rectTransform, Vector2.zero, new Vector2(-12f, 0f));

        Text rangeText = NewText("Range", box.transform, range, 14, NoteColor, TextAnchor.LowerLeft);
        StretchInside(rangeText.rectTransform, new Vector2(8f, 4f), new Vector2(-10f, -4f));

        InputField field = box.gameObject.AddComponent<InputField>();
        field.textComponent = value;
        field.placeholder = placeholder;
        field.targetGraphic = box;
        field.characterLimit = 3; // 3けたまで（いちばん大きい値は、最大時間の上限 300 秒）
        field.text = sample;

        row.gameObject.AddComponent<SpaceJunkLobbyValueRow>().EditorSetup(kind, value, field);
        return row;
    }

    /// <summary>
    /// 「+n」（上・緑）と「-n」（下・赤）の2つ。
    /// 高さ 34 のボタンを、行（高さ 70）の真ん中から上下に 18 ずつずらして縦に並べる（すき間は 2）。
    /// </summary>
    private static void StepButtons(RectTransform row, float x, int amount)
    {
        ColorButton(row, $"+{amount}", new Vector2(x, 18f), PlusColor, amount);
        ColorButton(row, $"-{amount}", new Vector2(x, -18f), MinusColor, -amount);
    }

    /// <summary>色の付いた小さいボタン（幅 80・高さ 34）。押すと値が amount だけ増える（マイナスなら減る）。</summary>
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

    /// <summary>
    /// テーマ（TitleScreenTheme）の見た目のボタン。タイトル画面と同じ。
    /// ここでは大きさだけ決める。置く場所は、呼んだ側が <see cref="Place"/> で決める。
    /// </summary>
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

    /// <summary>
    /// 押したときに何をするかの印（<see cref="SpaceJunkLobbyButton"/>）を付ける。
    /// amount は増やす量（Step）など、action によって意味が変わる数。使わない action では 0。
    /// </summary>
    private static void AddAction(Component target, SpaceJunkLobbyAction action, int amount)
    {
        target.gameObject.AddComponent<SpaceJunkLobbyButton>().EditorSetup(action, amount);
    }

    /// <summary>画像を押せるボタンにする（色が少し変わる）。</summary>
    private static void AddButton(Image image, SpaceJunkLobbyAction action, int amount)
    {
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        // 状態ごとの色は、画像の色に掛け算される（1 より大きいと明るく、小さいと暗くなる）。
        // マウスが乗る・選ぶで明るく、押すと暗く。倍率（colorMultiplier）はすべての状態に掛かる
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.colorMultiplier = 1.5f;
        button.colors = colors;

        AddAction(image, action, amount);
    }

    /// <summary>
    /// 子を上から縦に自動で並べる。**子の大きさは変えない**（大きさは各行の <see cref="SetLayoutSize"/> で決める）。
    /// </summary>
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
        // 見える窓（四辺に 8 の余白）。RectMask2D で、窓からはみ出た行を隠す
        RectTransform viewRect = NewRect("Viewport", parent);
        viewRect.gameObject.AddComponent<RectMask2D>();
        StretchInside(viewRect, new Vector2(8f, 8f), new Vector2(-8f, -8f));

        // 行のすき間でもホイールで流せるように、透明な下地を敷く
        Image hit = viewRect.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        // 行を入れる入れ物。窓の上端にくっつけて横幅いっぱいにし、高さは中の行の数に合わせて自動で伸びる
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
        scroll.movementType = ScrollRect.MovementType.Clamped; // 端で跳ね返らせない
        scroll.scrollSensitivity = 40f; // ホイール1目盛りで流れる量（画素）

        return contentRect;
    }

    /// <summary>
    /// 画面の部品の入れ物（RectTransform だけ）を作る。
    /// `SetParent(parent, false)` なのは、親に付けたときに「元の見た目の場所」を保とうとして
    /// 位置や大きさが勝手に直されないようにするため（あとで <see cref="Place"/> で決める値を、そのまま効かせる）。
    /// </summary>
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

    /// <summary>
    /// 文字の部品。**押しても反応しない・装飾の書式（リッチテキスト）を読まない**のが初期値
    /// （必要な所だけ、呼んだ側で切り替える。例：参加者の一覧は色付けのために書式を読む）。
    /// </summary>
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

    /// <summary>
    /// 役目の印（<see cref="SpaceJunkLobbyPart"/>）を付ける。再生中のコードは、名前ではなくこの印で部品を見つけるので、
    /// **部品の名前を変えても動きは変わらない**（印を外すと見つからなくなる）。
    /// </summary>
    private static void Mark(Component target, SpaceJunkLobbyPartRole role)
    {
        target.gameObject.AddComponent<SpaceJunkLobbyPart>().EditorSetRole(role);
    }

    /// <summary>
    /// 自動で並べる一覧の中の、1行の大きさを決める。
    /// 並べる係（<see cref="AddVerticalLayout"/>）は LayoutElement の値を見るので、そちらにも同じ大きさを入れる。
    /// </summary>
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

    /// <summary>親いっぱいに広げる（親と同じ位置・同じ大きさになる）。</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 親いっぱいに広げてから、内側に余白を取る。
    /// `offsetMin` は左下の角のずれ（右・上へ正）、`offsetMax` は右上の角のずれ（左・下へは負）。
    /// 例：(12, 0) と (-12, 0) なら、左右に 12 ずつ余白ができる。
    /// </summary>
    private static void StretchInside(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        Stretch(rect);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
