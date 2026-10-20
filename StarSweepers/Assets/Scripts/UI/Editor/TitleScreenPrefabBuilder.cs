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
///
/// ## 流れ
///
/// 1. Unity がスクリプトを読み込むと <see cref="AutoBuildIfMissing"/> が動き、**エディタが落ち着いてから**（`EditorApplication.delayCall`）次のどれかをする
///    ・プレハブが無い → <see cref="Build"/>（作って TitleScene に置く）
///    ・版が 3 より古い → <see cref="Build"/>（作り直す。手で直した配置は消える）
///    ・版が 3 以上で <see cref="PrefabVersion"/> より古い → <see cref="Upgrade"/>（足りない部品だけを足す。手で直した配置は残る）
/// 2. <see cref="Build"/> … 見えない作業用のシーンで <see cref="BuildRoot"/> が画面を丸ごと組み立て、プレハブに保存して、TitleScene に置く
/// 3. <see cref="Upgrade"/> … 保存してあるプレハブを開き、古い版から1つずつ順に部品を足す・消す・組み替えて、版を書き換えて保存する
///
/// ## 版を上げるとき
///
/// ・**新しく作るとき**（<see cref="BuildRoot"/> とその下の Build…Page）と、**今あるプレハブを上げるとき**（<see cref="Upgrade"/> に足す処理）の両方を書く
/// ・`-batchmode -quit` で Unity を開くだけでは `delayCall` が呼ばれる前に終わり、版上げは動かない（2026/10/13 の申し送り）。
///   コマンドから上げるときは <see cref="UpgradeFromCommandLine"/> を `-executeMethod` で呼ぶ
/// ・`Upgrade` と `PrefabVersion` は、リフレクションで名前を指定して呼ばれたことがあるので、**名前を変えない**
///
/// ## 部品の重なり順
///
/// 同じ親の中では、**後ろに並んだ部品ほど手前に出る。** だから、つないでいる途中の表示（Busy）は一番最後に置き、
/// あとから足す画面や窓は、その**前**に差し込む（<see cref="PutBehindBusyOverlay"/>）。
/// </summary>
public static class TitleScreenPrefabBuilder
{
    /// <summary>作るプレハブの場所（Resources の中なので、<see cref="TitleScreen.PrefabResourcePath"/> で読み込める）。</summary>
    public const string PrefabPath = "Assets/Resources/TitleScreen.prefab";

    /// <summary>背景の画像（Assets/Art/Sprites/title.png）。場所が変わっても見つかるように番号で探す。</summary>
    private const string BackgroundGuid = "0051ed586d64b7a409d1a5f3f40e6713";

    private const string TitleSceneName = "TitleScene";

    // 見た目の初期値（作ったあとは、プレハブの中で直接変える）
    // 大きさ・位置の数字は、画面を 1920×1080 としたときのピクセル（<see cref="BuildRoot"/> の CanvasScaler）
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

    /// <summary>作る文字に入れるフォント（Unity 標準）。<see cref="Build"/> と <see cref="Upgrade"/> の最初に読み込む。</summary>
    private static Font defaultFont;

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

    /// <summary>
    /// Unity がスクリプトを読み込むたびに呼ばれる。プレハブが無ければ作り、古ければ作り直すか部品を足す（クラスの説明の「流れ」）。
    /// 読み込みの最中にアセットを作ると失敗することがあるので、`delayCall` で落ち着いてから動かす。再生に入るときは何もしない。
    /// </summary>
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

            // 版 3 より古い（手で直す前の）プレハブは作り直す。版 3 からは**作り直さずに、足りない部品だけを足す**（手で直した配置を消さない）
            TitleScreen existing = AssetDatabase.LoadAssetAtPath<TitleScreen>(PrefabPath);
            if (existing != null && existing.PrefabVersion < 3)
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
    /// **プレハブの作りの版。** 作りが大きく変わったとき（部品を足したなど）だけ上げる。
    /// 1：最初の版 ／ 2：パスワードの欄 ／ 3：パブリック・プライベートの2つのチェック ／ 4：フレンドのサーバー（Steam）（2026/10/6）
    /// ／ 5：「LANのサーバーに参加する」に、見つかった LAN の部屋の一覧（2026/10/15）
    /// ／ 6：「サーバーを作る」のつなぎかたを □パブリック □プライベート □LAN の3つのチェックに（2026/10/15）
    /// ／ 7：メインに「クイックプレイ」（2026/10/15）
    /// ／ 8：メインを ソロプレイ／マルチプレイ／設定／ゲームを終了 にし、マルチプレイの画面を足して、サーバーを探す画面（ハブ・パブリック・LAN・フレンド）を消した（2026/10/19）
    /// ／ 9：参加の確認の窓（参加しますか？／パスワードを入力してください）を足し、「1人で練習する」とマルチプレイの画面のパスワードの欄を消した（2026/10/19）
    /// **版 3 からは作り直さず、<see cref="Upgrade"/> で足りない部品だけを足す**（手で直した配置を消さない）。版を上げたら Upgrade に足す処理を書くこと
    /// </summary>
    public const int PrefabVersion = 9;

    /// <summary>
    /// **作り直さずに、足りない部品だけを足す。**
    /// 版 3 → 4：「サーバーを探す」に「フレンドのサーバーを探す」のボタンと、その画面を足す。
    /// 版 4 → 5：「LANのサーバーに参加する」画面に、見つかった LAN の部屋の一覧を足す。
    /// 版 5 → 6：「サーバーを作る」のつなぎかたのボタンを消し、□LAN のチェックを足す（<see cref="ChangeConnectionChoiceToChecks"/>）。
    /// 版 6 → 7：メインの「サーバーを作る」の上に「クイックプレイ」を足す（<see cref="AddQuickPlayButton"/>）。
    /// 版 7 → 8：メインをソロプレイ／マルチプレイに組み替え、マルチプレイの画面を足し、使わなくなった画面を消す（<see cref="RestructureForSoloMulti"/>）。
    /// 版 8 → 9：参加の確認の窓を足し、「1人で練習する」とマルチプレイの画面のパスワードの欄を消す（<see cref="AddJoinDialog"/>）。
    /// </summary>
    private static void Upgrade(int fromVersion)
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            // ※ 版ごとの処理を、古いほうから1つずつ順に通す。だから版 3・4 から上げるときは、
            //    ここで足した画面（フレンド・LAN の一覧）を、あとの版 8 の組み替えで消すことになる
            // どの段も「もう足してあれば何もしない」ので、途中まで上がったプレハブでも二重には足さない
            if (fromVersion < 4 && FindPart(root, TitlePartRole.PageFindFriends) == null)
            {
                AddFriendsButton(root);

                GameObject page = BuildFindFriendsPage(root.transform);
                page.SetActive(false);

                // つないでいる途中の表示より手前に出ないように、その前に並べる
                PutBehindBusyOverlay(root, page);
            }

            // 古い版から順に直す（4 → 5 → 6 → 7 → 8 → 9）
            if (fromVersion < 5 && FindPart(root, TitlePartRole.LanListContent) == null)
            {
                TitlePart lanPage = FindPart(root, TitlePartRole.PageFindLan);
                if (lanPage != null)
                {
                    BuildLanRoomList(lanPage.transform);
                }
            }

            if (fromVersion < 6)
            {
                ChangeConnectionChoiceToChecks(root);
            }

            if (fromVersion < 7)
            {
                AddQuickPlayButton(root);
            }

            if (fromVersion < 8)
            {
                RestructureForSoloMulti(root);
            }

            if (fromVersion < 9)
            {
                AddJoinDialog(root);
            }

            root.GetComponent<TitleScreen>().EditorSetPrefabVersion(PrefabVersion);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[UI] タイトル画面のプレハブに、足りない部品を足しました（版 {fromVersion} → {PrefabVersion}。手で直した配置はそのまま）。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 「サーバーを探す」の「LANのサーバーに参加する」を複製して「フレンドのサーバーを探す」にし、
    /// その場所に置く（LAN のボタンは1つ下へずらす）。
    /// 版 3 → 4 のときだけ使う（「サーバーを探す」の画面は版 8 で消えた）。
    /// </summary>
    private static void AddFriendsButton(GameObject root)
    {
        TitlePart findPage = FindPart(root, TitlePartRole.PageFind);
        if (findPage == null)
        {
            return;
        }

        foreach (TitleMenuButton look in findPage.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action != TitleButtonAction.OpenFindLan)
            {
                continue;
            }

            RectTransform lanRect = (RectTransform)look.transform;
            GameObject copy = Object.Instantiate(look.gameObject, lanRect.parent);
            copy.name = "フレンドのサーバーを探す";
            copy.transform.SetSiblingIndex(lanRect.GetSiblingIndex());

            TitleMenuButton friends = copy.GetComponent<TitleMenuButton>();
            friends.EditorSetup(TitleButtonAction.OpenFindFriends, copy.GetComponent<Image>(), friends.Label);
            if (friends.Label != null)
            {
                friends.Label.text = "フレンドのサーバーを探す";
            }

            // 1つぶん下げる：ボタンの高さ＋ボタンどうしの間（22）
            lanRect.anchoredPosition -= new Vector2(0f, lanRect.sizeDelta.y + 22f);
            return;
        }
    }

    /// <summary>その印（Role）の付いた部品を探す（隠れている部品も含める）。無ければ null。</summary>
    private static TitlePart FindPart(GameObject root, TitlePartRole role)
    {
        foreach (TitlePart part in root.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == role)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>
    /// あとから足した画面や窓を、つないでいる途中の表示（Busy）の**すぐ前**に並べ替える。
    /// 後ろに並んだ部品ほど手前に出るので、こうしておけば、つないでいる途中の表示がいつも一番手前に出る。
    /// </summary>
    private static void PutBehindBusyOverlay(GameObject root, GameObject added)
    {
        TitlePart busy = FindPart(root, TitlePartRole.BusyOverlay);
        if (busy != null)
        {
            added.transform.SetSiblingIndex(busy.transform.GetSiblingIndex());
        }
    }

    /// <summary>
    /// column の中で、index 番より後ろに並んでいる部品を、distance だけ上へずらす（消した部品のぶんを詰めるとき）。
    /// </summary>
    private static void MoveUpSiblingsAfter(Transform column, int index, float distance)
    {
        for (int i = index + 1; i < column.childCount; i++)
        {
            if (column.GetChild(i) is RectTransform below)
            {
                below.anchoredPosition += new Vector2(0f, distance);
            }
        }
    }

    /// <summary>
    /// **マルチプレイの画面**（2026/10/19・大槻さんの絵コンテ）。
    /// 左：名前・クイックプレイ・プライベートサーバーを探す・サーバーを作る・サーバー検索設定（■パブリックサーバー ■フレンドのサーバー）。
    /// 右：参加可能なサーバーの一覧（LAN の部屋はいつも一番上）と、パスワードの欄。
    /// ※ パスワードの欄は、版 9（2026/10/19）で参加の確認の窓へ移したので、いまはここでは作らない。
    /// 版 7 → 8 の <see cref="Upgrade"/> でも、これを使って足す。
    /// </summary>
    private static GameObject BuildMultiPage(Transform root)
    {
        GameObject page = NewPage("MultiPage", root, TitlePartRole.PageMulti);

        // 左
        Column left = new Column("Left", page.transform, 640f, 18f,
            new Vector2(0f, 1f), new Vector2(140f, -90f), new Vector2(0f, 1f));
        AddNameInput(left);
        left.Add(NewButton(left.Rect, "クイックプレイ", new Vector2(640f, 96f), ButtonFontSize + 4, TitleButtonAction.QuickPlay));
        left.Add(NewButton(left.Rect, "プライベートサーバーを探す", new Vector2(640f, 70f), ButtonFontSize - 2, TitleButtonAction.OpenFindPrivate));
        left.Add(NewButton(left.Rect, "サーバーを作る", new Vector2(640f, 70f), ButtonFontSize - 2, TitleButtonAction.OpenCreate));
        AddLabel(left, "サーバー検索設定", "LAN の部屋はいつも一番上に出る");
        AddCheckRow(left, "パブリックサーバー", TitleButtonAction.ToggleSearchPublic, TitlePartRole.SearchPublicCheckText);
        AddCheckRow(left, "フレンドのサーバー", TitleButtonAction.ToggleSearchFriends, TitlePartRole.SearchFriendsCheckText);
        left.Finish();

        // 右：参加可能なサーバー
        Text title = NewText("JoinableTitle", page.transform, "参加可能なサーバー", FontSize + 6, TextColor, TextAnchor.LowerLeft);
        Place(title.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -60f), new Vector2(820f, 50f), new Vector2(0f, 1f));

        Image listBox = NewImage("JoinableBox", page.transform, PanelColor);
        Place(listBox.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -120f), new Vector2(820f, 640f), new Vector2(0f, 1f));

        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, TitlePartRole.JoinableListContent);

        // サーバー1つぶんのカードの見本（再生すると隠れ、見つかった数だけ複製される）
        Image card = NewImage("JoinableCardTemplate", content, AccentColor);
        SetLayoutSize(card.gameObject, 780f, 84f);
        Mark(card, TitlePartRole.JoinableCardTemplate);

        Text name = NewText("Name", card.transform, "サーバーの名前", FontSize, TextColor, TextAnchor.MiddleLeft);
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(460f, 70f), new Vector2(0f, 0.5f));
        Mark(name, TitlePartRole.ServerCardName);

        Text count = NewText("Players", card.transform, "1/8", FontSize, TextColor, TextAnchor.MiddleRight);
        Place(count.rectTransform, new Vector2(1f, 0.5f), new Vector2(-210f, 0f), new Vector2(100f, 70f), new Vector2(1f, 0.5f));
        Mark(count, TitlePartRole.ServerCardPlayers);

        TitleMenuButton join = NewButton(card.transform, "参加する", new Vector2(180f, 60f), FontSize, TitleButtonAction.JoinListedServer);
        Place((RectTransform)join.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(180f, 60f), new Vector2(1f, 0.5f));

        Text message = NewText("Message", listBox.transform, string.Empty, FontSize - 4, TextColor, TextAnchor.LowerCenter);
        Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(780f, 160f), new Vector2(0.5f, 0f));
        message.verticalOverflow = VerticalWrapMode.Overflow;
        Mark(message, TitlePartRole.JoinableListMessage);

        AddBackButton(page.transform);
        return page;
    }

    /// <summary>
    /// **参加の確認の窓**（2026/10/19）。公開・フレンドのサーバーを一覧で押したときに出る。
    /// パスワードなし：「○○に参加しますか？」「はい」「いいえ」。パスワードあり：「パスワードを入力してください」・入力欄・「参加する」「やめる」。
    /// 文字はコードが場合に合わせて書き換える。間違っていたら下の赤い文字に「パスワードが間違えています」が出る。
    /// </summary>
    private static GameObject BuildJoinDialog(Transform root)
    {
        RectTransform dialog = NewRect("JoinDialog", root);
        Stretch(dialog);
        Mark(dialog, TitlePartRole.JoinDialog);

        // 後ろの画面を押せないように、暗い幕で覆う
        Image shade = NewImage("Shade", dialog, new Color(0f, 0f, 0f, 0.6f));
        Stretch(shade.rectTransform);

        Image box = NewImage("Box", dialog, new Color(0.08f, 0.08f, 0.10f, 0.95f));
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 460f), new Vector2(0.5f, 0.5f));

        Text title = NewText("Title", box.transform, "「サーバーの名前」に参加しますか？", FontSize + 6, TitleColor, TextAnchor.MiddleCenter);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 110f), new Vector2(0.5f, 1f));
        Mark(title, TitlePartRole.JoinDialogTitle);

        Column password = new Column("PasswordRow", box.transform, 800f, 6f,
            new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(0.5f, 1f));
        AddInput(password, "パスワード", TitleInputKind.JoinPassword);
        password.Finish();
        Mark(password.Rect, TitlePartRole.JoinDialogPasswordRow);

        // 暗い箱の上でも入力欄が見えるように、少し明るくする
        foreach (TitleInput input in password.Rect.GetComponentsInChildren<TitleInput>(true))
        {
            input.GetComponent<Image>().color = new Color(0.20f, 0.21f, 0.25f, 0.95f);
        }

        Text message = NewText("Message", box.transform, string.Empty, FontSize, new Color(1f, 0.45f, 0.4f, 1f), TextAnchor.MiddleCenter);
        Place(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(900f, 60f), new Vector2(0.5f, 0f));
        Mark(message, TitlePartRole.JoinDialogMessage);

        TitleMenuButton yes = NewButton(box.transform, "はい", new Vector2(320f, 76f), ButtonFontSize - 2, TitleButtonAction.ConfirmJoinDialog);
        Place((RectTransform)yes.transform, new Vector2(0.5f, 0f), new Vector2(-180f, 40f), new Vector2(320f, 76f), new Vector2(0.5f, 0f));
        Mark(yes.Label, TitlePartRole.JoinDialogYesLabel);

        TitleMenuButton no = NewButton(box.transform, "いいえ", new Vector2(320f, 76f), ButtonFontSize - 2, TitleButtonAction.CloseJoinDialog);
        Place((RectTransform)no.transform, new Vector2(0.5f, 0f), new Vector2(180f, 40f), new Vector2(320f, 76f), new Vector2(0.5f, 0f));
        Mark(no.Label, TitlePartRole.JoinDialogNoLabel);

        return dialog.gameObject;
    }

    /// <summary>
    /// 版 8 → 9：参加の確認の窓を足し（つないでいる途中の表示より後ろ）、
    /// 「サーバーを作る」の「1人で練習する」（ソロプレイができたので要らない）と、マルチプレイの画面のパスワードの欄（窓に移した）を消す。
    /// </summary>
    private static void AddJoinDialog(GameObject root)
    {
        foreach (TitleMenuButton look in root.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.PracticeSolo)
            {
                Object.DestroyImmediate(look.gameObject);
                break;
            }
        }

        // 版 8 のマルチプレイの画面には、印の無い「Password」という名前の欄があった（名前で探すしかない）
        TitlePart multi = FindPart(root, TitlePartRole.PageMulti);
        Transform oldPassword = multi != null ? multi.transform.Find("Password") : null;
        if (oldPassword != null)
        {
            Object.DestroyImmediate(oldPassword.gameObject);
        }

        if (FindPart(root, TitlePartRole.JoinDialog) != null)
        {
            return;
        }

        GameObject dialog = BuildJoinDialog(root.transform);
        dialog.SetActive(false);
        PutBehindBusyOverlay(root, dialog);
    }

    /// <summary>「□ 名前」のチェック1行（押すとチェックが付いたり外れたりする）。</summary>
    private static void AddCheckRow(Column column, string label, TitleButtonAction action, TitlePartRole checkTextRole)
    {
        Row row = new Row(column, 60f, 12f, "Check " + label);
        TitleMenuButton check = NewButton(row.Rect, "■", new Vector2(64f, 56f), FontSize + 6, action);
        Mark(check.Label, checkTextRole);
        row.Add(check);

        Text text = NewText("Label", row.Rect, label, FontSize, TextColor, TextAnchor.MiddleLeft);
        text.rectTransform.sizeDelta = new Vector2(400f, 56f);
        row.Add(text.rectTransform);
    }

    /// <summary>
    /// 版 7 → 8：**メインをソロプレイ／マルチプレイに組み替え、マルチプレイの画面を足し、使わなくなった画面を消す。**（2026/10/19）
    ///
    /// ・メインの「クイックプレイ」→「ソロプレイ」、「サーバーを作る」→「マルチプレイ」。「サーバーを探す」は消して、下のボタンを詰める
    /// ・「サーバーを探す」のハブ・パブリックの一覧・LAN の参加・フレンドの一覧の画面を消す（マルチプレイの画面の一覧にまとめた）
    /// ・マルチプレイの画面を足す
    /// </summary>
    private static void RestructureForSoloMulti(GameObject root)
    {
        if (FindPart(root, TitlePartRole.PageMulti) != null)
        {
            return;
        }

        TitlePart mainPage = FindPart(root, TitlePartRole.PageMain);
        if (mainPage != null)
        {
            TitleMenuButton find = null;
            foreach (TitleMenuButton look in mainPage.GetComponentsInChildren<TitleMenuButton>(true))
            {
                switch (look.Action)
                {
                    case TitleButtonAction.QuickPlay:
                        RenameButton(look, TitleButtonAction.PlaySolo, "ソロプレイ");
                        break;
                    case TitleButtonAction.OpenCreate:
                        RenameButton(look, TitleButtonAction.OpenMulti, "マルチプレイ");
                        break;
                    case TitleButtonAction.OpenFind:
                        find = look;
                        break;
                }
            }

            // 「サーバーを探す」を消し、その下のボタンを1つぶん上へ詰める
            if (find != null)
            {
                RectTransform findRect = (RectTransform)find.transform;
                float step = findRect.sizeDelta.y + 22f; // ボタンの高さ＋ボタンどうしの間（22。メインの画面の Column と同じ）
                MoveUpSiblingsAfter(findRect.parent, findRect.GetSiblingIndex(), step);

                Object.DestroyImmediate(find.gameObject);
            }
        }

        // 使わなくなった画面を消す
        foreach (TitlePartRole role in new[]
                 {
                     TitlePartRole.PageFind, TitlePartRole.PageFindPublic,
                     TitlePartRole.PageFindLan, TitlePartRole.PageFindFriends,
                 })
        {
            TitlePart old = FindPart(root, role);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }

        // マルチプレイの画面を足す（つないでいる途中の表示より手前に出ないように、その前に並べる）
        GameObject multi = BuildMultiPage(root.transform);
        multi.SetActive(false);
        PutBehindBusyOverlay(root, multi);
    }

    /// <summary>ボタンの Action と文字を変える。</summary>
    private static void RenameButton(TitleMenuButton look, TitleButtonAction action, string label)
    {
        look.EditorSetup(action, look.GetComponent<Image>(), look.Label);
        look.gameObject.name = label;
        if (look.Label != null)
        {
            look.Label.text = label;
        }
    }

    /// <summary>
    /// 版 6 → 7：メインの「サーバーを作る」を複製して「クイックプレイ」にし、**その上**に置く。
    /// 並んでいるボタンの入れ物を上へ1つぶん広げ、今あるボタンの見た目の位置は変えない（手で動かした配置もそのまま）。
    /// </summary>
    private static void AddQuickPlayButton(GameObject root)
    {
        TitleMenuButton openCreate = null;
        foreach (TitleMenuButton look in root.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.QuickPlay)
            {
                return;
            }

            if (look.Action == TitleButtonAction.OpenCreate)
            {
                openCreate = look;
            }
        }

        if (openCreate == null)
        {
            return;
        }

        RectTransform createRect = (RectTransform)openCreate.transform;
        RectTransform column = createRect.parent as RectTransform;
        float step = createRect.sizeDelta.y + 22f;

        // 入れ物を上へ広げ（下端は動かさない）、今あるボタンはそのぶん下げて、見た目の位置を保つ
        if (column != null && Mathf.Approximately(column.pivot.y, 0f))
        {
            column.sizeDelta += new Vector2(0f, step);
            foreach (Transform child in column)
            {
                ((RectTransform)child).anchoredPosition -= new Vector2(0f, step);
            }
        }

        GameObject copy = Object.Instantiate(openCreate.gameObject, createRect.parent);
        copy.name = "クイックプレイ";
        copy.transform.SetSiblingIndex(createRect.GetSiblingIndex());

        RectTransform quickRect = (RectTransform)copy.transform;
        quickRect.anchoredPosition = createRect.anchoredPosition + new Vector2(0f, step);

        TitleMenuButton quick = copy.GetComponent<TitleMenuButton>();
        quick.EditorSetup(TitleButtonAction.QuickPlay, copy.GetComponent<Image>(), quick.Label);
        if (quick.Label != null)
        {
            quick.Label.text = "クイックプレイ";
        }
    }

    /// <summary>「サーバーを作る」の、部屋の公開のしかたの見出しと説明（2026/10/15）。</summary>
    private const string RoomKindLabel = "部屋の公開のしかた";
    private const string RoomKindNote =
        "パブリック：一覧に出る ／ プライベート：参加コードを知っている人だけ ／ LAN：同じネットワークのPCだけ（展示会場はこちら）";

    /// <summary>
    /// 版 5 → 6：「つなぎかた（インターネット／LAN）」のボタンの行を消し、□パブリック □プライベート の行に □LAN を足す。
    /// 消した行のぶん、下の部品を上へ詰める（手で動かした配置は、それ以外そのまま）。
    /// </summary>
    private static void ChangeConnectionChoiceToChecks(GameObject root)
    {
        TitleMenuButton useInternet = null;
        TitleMenuButton existingLan = null;
        foreach (TitleMenuButton look in root.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.UseInternet)
            {
                useInternet = look;
            }
            else if (look.Action == TitleButtonAction.SetLan)
            {
                existingLan = look;
            }
        }

        if (existingLan != null)
        {
            return;
        }

        // 1. 「つなぎかた」のボタンの行を消して、下の部品を上へ詰める。見出しの行は「部屋の公開のしかた」に書き換える
        if (useInternet != null)
        {
            RectTransform modeRow = (RectTransform)useInternet.transform.parent;
            Transform column = modeRow.parent;
            int index = modeRow.GetSiblingIndex();
            float removed = modeRow.sizeDelta.y + 10f; // 行の高さ＋行どうしの間（10。「サーバーを作る」の Column と同じ）

            // すぐ上の行が「つなぎかた」の見出し（AddLabel で作った行）
            if (index > 0)
            {
                Transform labelRow = column.GetChild(index - 1);
                foreach (Text text in labelRow.GetComponentsInChildren<Text>(true))
                {
                    if (text.name == "Label")
                    {
                        text.text = RoomKindLabel;
                        // 幅は AddLabel と同じ決め方（1文字 ≒ 文字の大きさぶん）
                        text.rectTransform.sizeDelta = new Vector2(Mathf.Max(200f, RoomKindLabel.Length * FontSize + 10f), text.rectTransform.sizeDelta.y);
                    }
                    else if (text.name == "Note")
                    {
                        text.text = RoomKindNote;
                    }
                }
            }

            MoveUpSiblingsAfter(column, index, removed);

            if (column is RectTransform columnRect)
            {
                columnRect.sizeDelta -= new Vector2(0f, removed);
            }

            Object.DestroyImmediate(modeRow.gameObject);
        }

        // 2. □パブリック □プライベート の行の説明を消し、その場所に □LAN を足す
        TitlePart privateRow = FindPart(root, TitlePartRole.PrivateRow);
        if (privateRow == null)
        {
            return;
        }

        // □LAN を置く横の位置：説明の文字（このあと消す）を除いた部品の、いちばん右の端＋間（12。Row の間と同じ）
        float x = 0f;
        foreach (Transform child in privateRow.transform)
        {
            if (child.name == "Note")
            {
                continue;
            }

            RectTransform rect = (RectTransform)child;
            x = Mathf.Max(x, rect.anchoredPosition.x + rect.sizeDelta.x + 12f);
        }

        Transform oldNote = privateRow.transform.Find("Note");
        if (oldNote != null)
        {
            Object.DestroyImmediate(oldNote.gameObject);
        }

        TitleMenuButton lanCheck = NewButton(privateRow.transform, "□", new Vector2(64f, 56f), FontSize + 6, TitleButtonAction.SetLan);
        Mark(lanCheck.Label, TitlePartRole.LanCheckText);
        Place((RectTransform)lanCheck.transform, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(64f, 56f), new Vector2(0f, 0.5f));

        // 「LAN」の文字は、チェック（幅 64）と間（12）のぶん右
        Text lanLabel = NewText("LanLabel", privateRow.transform, "LAN", FontSize, TextColor, TextAnchor.MiddleLeft);
        Place(lanLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(x + 64f + 12f, 0f), new Vector2(120f, 56f), new Vector2(0f, 0.5f));
    }

    /// <summary>
    /// コマンドから、足りない部品だけを足す（Unity を画面なしで動かすとき用：
    /// `Unity.exe -batchmode -quit -projectPath ... -executeMethod TitleScreenPrefabBuilder.UpgradeFromCommandLine`）。
    /// 画面なしで `-quit` を付けると、<see cref="AutoBuildIfMissing"/> の `delayCall` が呼ばれる前に終わってしまうため、こちらを直接呼ぶ。
    /// ※ <see cref="AutoBuildIfMissing"/> と違い、版 3 より古いプレハブでも作り直さずに <see cref="Upgrade"/> する（Upgrade は版 3 からの差分しか知らない）。
    /// </summary>
    public static void UpgradeFromCommandLine()
    {
        TitleScreen existing = AssetDatabase.LoadAssetAtPath<TitleScreen>(PrefabPath);
        if (existing != null && existing.PrefabVersion < PrefabVersion)
        {
            Upgrade(existing.PrefabVersion);
        }
    }

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

    /// <summary>プレハブを最初の状態で丸ごと作り（今あるものは上書き）、TitleScene に無ければ置く。</summary>
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

    /// <summary>
    /// 画面の一番上の入れ物（Canvas）と、その中の画面を全部作る。**いまの版の形**はここで決まる。
    /// 並べる順番がそのまま重なり順（後ろほど手前）：背景 → 各画面 → 参加の確認の窓 → つないでいる途中の表示。
    /// メイン以外の画面は隠しておく（どれを出すかは、再生したときに <see cref="TitleScreen"/> が決める）。
    /// </summary>
    private static GameObject BuildRoot(Scene stage)
    {
        GameObject root = new GameObject("TitleScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, stage);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // ポーズ画面（100）より後ろ、ほかのUIより手前

        // 1920×1080 を基準に、画面の大きさに合わせて全体を拡大・縮小する（横と縦の合わせ方は半々）
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<TitleScreen>().EditorSetPrefabVersion(PrefabVersion);

        BuildBackground(root.transform);
        BuildMainPage(root.transform);
        BuildCreatePage(root.transform).SetActive(false);
        BuildMultiPage(root.transform).SetActive(false);
        BuildFindPrivatePage(root.transform).SetActive(false);
        BuildSettingsPage(root.transform).SetActive(false);
        BuildJoinDialog(root.transform).SetActive(false);
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
        column.Add(NewButton(column.Rect, "ソロプレイ", MainButtonSize, ButtonFontSize, TitleButtonAction.PlaySolo));
        column.Add(NewButton(column.Rect, "マルチプレイ", MainButtonSize, ButtonFontSize, TitleButtonAction.OpenMulti));
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

        // 部屋の公開のしかた：□パブリック □プライベート □LAN（どれか1つだけにチェックが付く。2026/10/15）
        // （前は「つなぎかた（インターネット／LAN）」のボタンの行が別にあった）
        AddLabel(column, RoomKindLabel, RoomKindNote);
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
        TitleMenuButton lanCheck = NewButton(privateRow.Rect, "□", new Vector2(64f, 56f), FontSize + 6, TitleButtonAction.SetLan);
        Mark(lanCheck.Label, TitlePartRole.LanCheckText);
        privateRow.Add(lanCheck);
        Text lanLabel = NewText("LanLabel", privateRow.Rect, "LAN", FontSize, TextColor, TextAnchor.MiddleLeft);
        lanLabel.rectTransform.sizeDelta = new Vector2(120f, 56f);
        privateRow.Add(lanLabel.rectTransform);

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

    /// <summary>
    /// 「フレンドのサーバーを探す」（Steam。2026/10/6）。Steam のフレンドのうち、このゲームで部屋に入っている人を並べる。
    /// いまの <see cref="BuildRoot"/> では作らない。版 3 → 4 の <see cref="Upgrade"/> だけで使う（この画面は版 8 で消える）。
    /// </summary>
    private static GameObject BuildFindFriendsPage(Transform root)
    {
        GameObject page = NewPage("FindFriendsPage", root, TitlePartRole.PageFindFriends);

        // 左：名前・パスワード・更新
        Column left = new Column("Left", page.transform, 640f, 14f,
            new Vector2(0f, 1f), new Vector2(120f, -80f), new Vector2(0f, 1f));
        AddNameInput(left);
        AddLabel(left, "パスワード", "（パスワードあり）の部屋に入るとき");
        AddInput(left, "パスワードなし", TitleInputKind.JoinPassword);
        left.Add(NewButton(left.Rect, "フレンドのサーバーを更新", new Vector2(640f, 70f), FontSize + 2, TitleButtonAction.RefreshFriends));
        Text note = NewText("Note", left.Rect,
            "Steam のフレンドのうち、このゲームで部屋に入っている人が出ます。Steam を起動しておく必要があります。",
            FontSize - 8, NoteColor, TextAnchor.UpperLeft);
        note.rectTransform.sizeDelta = new Vector2(640f, 70f);
        left.Add(note.rectTransform);
        left.Finish();

        // 右：フレンドの部屋の一覧
        Text resultsTitle = NewText("ResultsTitle", page.transform, "フレンドのサーバー", FontSize + 6, TextColor, TextAnchor.LowerLeft);
        Place(resultsTitle.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -60f), new Vector2(800f, 50f), new Vector2(0f, 1f));

        Image listBox = NewImage("ListBox", page.transform, PanelColor);
        Place(listBox.rectTransform, new Vector2(1f, 1f), new Vector2(-920f, -120f), new Vector2(820f, 760f), new Vector2(0f, 1f));

        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, TitlePartRole.FriendListContent);

        // フレンドの部屋1つぶんのカードの見本（再生すると隠れ、見つかった数だけ複製される）
        Image card = NewImage("FriendCardTemplate", content, AccentColor);
        SetLayoutSize(card.gameObject, 780f, 150f);
        Mark(card, TitlePartRole.FriendCardTemplate);

        Text name = NewText("Name", card.transform, "フレンドの名前 の部屋", FontSize + 2, TextColor, TextAnchor.UpperLeft);
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(740f, 40f), new Vector2(0f, 1f));
        Mark(name, TitlePartRole.FriendCardName);

        Text info = NewText("Info", card.transform, "部屋の名前　1/4", FontSize - 4, NoteColor, TextAnchor.UpperLeft);
        Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -50f), new Vector2(740f, 30f), new Vector2(0f, 1f));
        Mark(info, TitlePartRole.FriendCardInfo);

        TitleMenuButton join = NewButton(card.transform, "参加する", new Vector2(740f, 56f), FontSize, TitleButtonAction.JoinFriendServer);
        Place((RectTransform)join.transform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(740f, 56f), new Vector2(0.5f, 0f));

        Text message = NewText("Message", listBox.transform, string.Empty, FontSize - 2, TextColor, TextAnchor.UpperCenter);
        Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(780f, 120f), new Vector2(0.5f, 1f));
        Mark(message, TitlePartRole.FriendListMessage);

        AddBackButton(page.transform);
        return page;
    }

    /// <summary>
    /// 「LANのサーバーに参加する」画面の下半分に、**見つかった LAN の部屋の一覧**を置く（2026/10/15）。
    /// 同じネットワークで「サーバーを作る → LAN」した部屋が、IPアドレスを打たなくても並ぶ（<see cref="LanRoomDiscovery"/>）。
    /// いまの <see cref="BuildRoot"/> では作らない。版 4 → 5 の <see cref="Upgrade"/> だけで使う（この画面は版 8 で消える）。
    /// </summary>
    private static void BuildLanRoomList(Transform page)
    {
        Text title = NewText("LanRoomsTitle", page, "見つかった LAN の部屋（押すと参加）", FontSize, TextColor, TextAnchor.LowerLeft);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(1000f, 40f), new Vector2(0.5f, 1f));

        Image listBox = NewImage("LanRoomsBox", page, PanelColor);
        Place(listBox.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -515f), new Vector2(1000f, 400f), new Vector2(0.5f, 1f));

        RectTransform content = NewScrollList(listBox.transform);
        Mark(content, TitlePartRole.LanListContent);

        // 部屋1つぶんのカードの見本（再生すると隠れ、見つかった部屋の数だけ複製される）
        Image card = NewImage("LanCardTemplate", content, AccentColor);
        SetLayoutSize(card.gameObject, 940f, 84f);
        Mark(card, TitlePartRole.LanCardTemplate);

        Text name = NewText("Name", card.transform, "部屋の名前（192.168.0.10）", FontSize, TextColor, TextAnchor.MiddleLeft);
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(560f, 70f), new Vector2(0f, 0.5f));
        Mark(name, TitlePartRole.ServerCardName);

        Text count = NewText("Players", card.transform, "1/8", FontSize, TextColor, TextAnchor.MiddleRight);
        Place(count.rectTransform, new Vector2(1f, 0.5f), new Vector2(-230f, 0f), new Vector2(120f, 70f), new Vector2(1f, 0.5f));
        Mark(count, TitlePartRole.ServerCardPlayers);

        TitleMenuButton join = NewButton(card.transform, "参加する", new Vector2(200f, 60f), FontSize, TitleButtonAction.JoinListedServer);
        Place((RectTransform)join.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(200f, 60f), new Vector2(1f, 0.5f));

        Text message = NewText("Message", listBox.transform, string.Empty, FontSize - 2, TextColor, TextAnchor.UpperCenter);
        Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(960f, 120f), new Vector2(0.5f, 1f));
        Mark(message, TitlePartRole.LanListMessage);
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

        /// <summary>部品と部品の間（縦）。</summary>
        private readonly float spacing;

        /// <summary>上の端から、次の部品を置く所までの長さ（置くたびに増える）。</summary>
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

        /// <summary>部品と部品の間（横）。</summary>
        private readonly float spacing;

        /// <summary>左の端から、次の部品を置く所までの長さ（置くたびに増える）。</summary>
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

        // 見出しの幅は、1文字 ≒ 文字の大きさ（FontSize）として文字数から決める（短くても 200）。説明はその右に並ぶ
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

    /// <summary>
    /// 入力欄を1つ。何を入れる欄かは <see cref="TitleInput"/> の Kind で決まる（打てる文字数や中身は、再生したときに <see cref="TitleScreen"/> が入れる）。
    /// 幅は入れ物の幅まで、ただし 1000 より広くはしない。
    /// </summary>
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

    /// <summary>ボタンを1つ。押したときにすることは Action で決まる（<see cref="TitleMenuButton"/>）。部品の名前は label と同じになる。</summary>
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

    /// <summary>
    /// 縦に流れる一覧（パブリックサーバーの検索結果）。中身を入れる入れ物を返す。ここだけは自動で並べる。
    /// いまは参加可能なサーバーの一覧（マルチプレイの画面）。古い版から上げるときは、LAN・フレンドの一覧にも使う。
    /// 入れ物には VerticalLayoutGroup が付く。中のカードは <see cref="SetLayoutSize"/> で大きさを決める。
    /// </summary>
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

    /// <summary>部品に印（<see cref="TitlePart"/> の Role）を付ける。<see cref="TitleScreen"/> はこの印で部品を見つける。</summary>
    private static void Mark(Component target, TitlePartRole role)
    {
        target.gameObject.AddComponent<TitlePart>().EditorSetRole(role);
    }

    /// <summary>自動で並べる一覧の中の部品の大きさを決める。自動で並べるときに見る大きさ（LayoutElement）にも同じ値を入れておく。</summary>
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
