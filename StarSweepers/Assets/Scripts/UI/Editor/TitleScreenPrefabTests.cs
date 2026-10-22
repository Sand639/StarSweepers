using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// タイトル画面のプレハブに、**コードが頼りにしている印の付いた部品**がそろっているかを確かめる。
/// 配置や見た目は自由に変えてよいが、印を消すとその部品は動かなくなるので、ここで気づけるようにする。
/// （2026/10/22：メインを マルチプレイ／プラクティス／設定／ゲームを終了 にし、マルチプレイを クイックマッチ／部屋を探す／部屋を作る に分けた形に合わせた）
///
/// 確かめるのは、**保存されているプレハブ**（`Assets/Resources/TitleScreen.prefab`）そのもの。作るツール（<see cref="TitleScreenPrefabBuilder"/>）は動かさない。
/// だから、ツールの版を上げてまだ Unity で開いていない（部品が足されていない）ときも、ここで落ちて気づける。
/// </summary>
public class TitleScreenPrefabTests
{
    /// <summary>プレハブを読み込む（開かずに読むだけ。書き換えはしない）。無ければ、そこでテストを失敗にする。</summary>
    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TitleScreenPrefabBuilder.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Assets/Resources/TitleScreen.prefab が無い");
        return prefab;
    }

    /// <summary>その印（Role）の付いた部品（隠れている部品も含める）。無ければ null。</summary>
    private static Transform FindPart(TitlePartRole role)
    {
        foreach (TitlePart part in LoadPrefab().GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == role)
            {
                return part.transform;
            }
        }
        return null;
    }

    /// <summary>
    /// プレハブの版が、ツールの版（<see cref="TitleScreenPrefabBuilder.PrefabVersion"/>）と同じか。
    /// 落ちたら、Unity のエディタを開き直す（読み込んだあとに足りない部品が足される）か、
    /// <see cref="TitleScreenPrefabBuilder.UpgradeFromCommandLine"/> を動かす。
    /// </summary>
    [Test]
    public void TitlePrefab_IsTheCurrentVersion()
    {
        Assert.That(LoadPrefab().GetComponent<TitleScreen>().PrefabVersion,
            Is.EqualTo(TitleScreenPrefabBuilder.PrefabVersion));
    }

    /// <summary>コードが Role で探す部品（画面・一覧・チェックの印・参加の確認の窓・つないでいる途中の表示）があるか。</summary>
    [TestCase(TitlePartRole.PageMain)]
    [TestCase(TitlePartRole.PageMulti)]
    [TestCase(TitlePartRole.PageMultiMenu)]
    [TestCase(TitlePartRole.PageCreate)]
    [TestCase(TitlePartRole.PageSettings)]
    [TestCase(TitlePartRole.JoinableListContent)]
    [TestCase(TitlePartRole.JoinableListMessage)]
    [TestCase(TitlePartRole.JoinableCardTemplate)]
    [TestCase(TitlePartRole.SearchPublicCheckText)]
    [TestCase(TitlePartRole.SearchFriendsCheckText)]
    [TestCase(TitlePartRole.SearchNoPasswordCheckText)]
    [TestCase(TitlePartRole.ServerCardName)]
    [TestCase(TitlePartRole.ServerCardCode)]
    [TestCase(TitlePartRole.ServerCardLock)]
    [TestCase(TitlePartRole.ServerCardPlayers)]
    [TestCase(TitlePartRole.PublicCheckText)]
    [TestCase(TitlePartRole.PrivateCheckText)]
    [TestCase(TitlePartRole.LanCheckText)]
    [TestCase(TitlePartRole.JoinDialog)]
    [TestCase(TitlePartRole.JoinDialogTitle)]
    [TestCase(TitlePartRole.JoinDialogMessage)]
    [TestCase(TitlePartRole.JoinDialogPasswordRow)]
    [TestCase(TitlePartRole.JoinDialogYesLabel)]
    [TestCase(TitlePartRole.JoinDialogNoLabel)]
    [TestCase(TitlePartRole.BusyOverlay)]
    public void TitlePrefab_HasPart(TitlePartRole role)
    {
        Assert.That(FindPart(role), Is.Not.Null, $"印 {role} の付いた部品が無い");
    }

    /// <summary>
    /// 一覧の画面にまとめたので、前の「サーバーを探す」の画面は残っていない（2026/10/19）。
    /// 「プライベートサーバーを探す」も「部屋を探す」にまとめた（2026/10/22）。
    /// </summary>
    [TestCase(TitlePartRole.PageFind)]
    [TestCase(TitlePartRole.PageFindPublic)]
    [TestCase(TitlePartRole.PageFindLan)]
    [TestCase(TitlePartRole.PageFindFriends)]
    [TestCase(TitlePartRole.PageFindPrivate)]
    public void TitlePrefab_HasNoOldFindPage(TitlePartRole role)
    {
        Assert.That(FindPart(role), Is.Null, $"使わなくなった画面 {role} が残っている");
    }

    /// <summary>
    /// 押すと何かが起きるボタン（Action）が、画面ごとにそろっているか。
    /// 「戻る」や設定のタブなど、ここに無い Action は確かめていない。
    /// </summary>
    [Test]
    public void TitlePrefab_HasEveryButton()
    {
        HashSet<TitleButtonAction> actions = new HashSet<TitleButtonAction>();
        foreach (TitleMenuButton button in LoadPrefab().GetComponentsInChildren<TitleMenuButton>(true))
        {
            actions.Add(button.Action);
        }

        TitleButtonAction[] required =
        {
            // メイン
            TitleButtonAction.PlaySolo, TitleButtonAction.OpenMulti, TitleButtonAction.OpenSettings, TitleButtonAction.Quit,
            // マルチプレイ
            TitleButtonAction.QuickPlay, TitleButtonAction.OpenFindRoom, TitleButtonAction.OpenCreate,
            // 部屋を探す
            TitleButtonAction.RefreshRoomList, TitleButtonAction.ToggleSearchPublic, TitleButtonAction.ToggleSearchFriends,
            TitleButtonAction.ToggleSearchNoPassword, TitleButtonAction.JoinListedServer,
            // 部屋を作る
            TitleButtonAction.CreateServer, TitleButtonAction.MaxPlayersDown, TitleButtonAction.MaxPlayersUp,
            TitleButtonAction.SetPublic, TitleButtonAction.SetPrivate, TitleButtonAction.SetLan,
            // 参加の確認の窓
            TitleButtonAction.ConfirmJoinDialog, TitleButtonAction.CloseJoinDialog,
        };

        foreach (TitleButtonAction action in required)
        {
            Assert.That(actions.Contains(action), Is.True, $"Action が {action} のボタンが無い");
        }
    }

    /// <summary>入力欄（<see cref="TitleInput"/> の Kind）がそろっていて、使わなくなった欄が残っていないか。</summary>
    [Test]
    public void TitlePrefab_HasInputs()
    {
        HashSet<TitleInputKind> kinds = new HashSet<TitleInputKind>();
        foreach (TitleInput input in LoadPrefab().GetComponentsInChildren<TitleInput>(true))
        {
            kinds.Add(input.Kind);
        }

        Assert.That(kinds.Contains(TitleInputKind.PlayerName), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.ServerName), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.PrivateCode), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.CreatePassword), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.JoinPassword), Is.True);

        // LAN は一覧から入るだけ。IP アドレスを打つ欄は無い（2026/10/19・大槻さん）
        Assert.That(kinds.Contains(TitleInputKind.LanAddress), Is.False, "IP アドレスを打つ欄が残っている");
    }

    /// <summary>
    /// 部屋の一覧と、部屋番号の検索欄（PrivateCode）は、「部屋を探す」の画面（PageMulti）の中にある。
    /// パスワードは部屋を押したときの窓で聞くので、この画面にパスワードの欄は無い（2026/10/22）。
    /// </summary>
    [Test]
    public void TitlePrefab_JoinableListIsOnTheFindRoomPage()
    {
        Transform page = FindPart(TitlePartRole.PageMulti);
        Transform list = FindPart(TitlePartRole.JoinableListContent);

        Assert.That(page, Is.Not.Null);
        Assert.That(list, Is.Not.Null);
        Assert.That(list.IsChildOf(page), Is.True, "参加可能なサーバーの一覧が、部屋を探す画面の外にある");
        Assert.That(InputKindsUnder(page).Contains(TitleInputKind.PrivateCode), Is.True, "部屋を探す画面に部屋番号の検索欄が無い");
        Assert.That(InputKindsUnder(page).Contains(TitleInputKind.JoinPassword), Is.False, "部屋を探す画面にパスワードの欄が残っている");
    }

    /// <summary>「部屋を探す」の右下の操作の案内（A 確認　B 戻る）は消した（2026/10/22・大槻さん）。</summary>
    [Test]
    public void TitlePrefab_FindRoomPageHasNoButtonHint()
    {
        Transform page = FindPart(TitlePartRole.PageMulti);
        Assert.That(page, Is.Not.Null);
        foreach (UnityEngine.UI.Text text in page.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            Assert.That(text.text.Contains("確認"), Is.False, $"操作の案内「{text.text}」が残っている");
        }
    }

    /// <summary>一覧の行の見本は、行そのものが押せるボタンで、鍵・部屋番号・人数を中に持つ（写真の形。2026/10/22）。</summary>
    [Test]
    public void TitlePrefab_RoomCardIsAButtonWithLockAndCode()
    {
        Transform card = FindPart(TitlePartRole.JoinableCardTemplate);
        Assert.That(card, Is.Not.Null);
        Assert.That(card.GetComponent<TitleMenuButton>(), Is.Not.Null, "行そのものがボタンになっていない");
        Assert.That(FindPart(TitlePartRole.ServerCardLock).IsChildOf(card), Is.True);
        Assert.That(FindPart(TitlePartRole.ServerCardCode).IsChildOf(card), Is.True);
        Assert.That(FindPart(TitlePartRole.ServerCardPlayers).IsChildOf(card), Is.True);
    }

    /// <summary>マルチプレイの画面に、クイックマッチ／部屋を探す／部屋を作る がこの順で並んでいる（2026/10/22）。</summary>
    [Test]
    public void TitlePrefab_MultiMenuHasThreeButtonsInOrder()
    {
        Transform page = FindPart(TitlePartRole.PageMultiMenu);
        Assert.That(page, Is.Not.Null);

        AssertTopToBottom(page, TitleButtonAction.QuickPlay, TitleButtonAction.OpenFindRoom, TitleButtonAction.OpenCreate);
    }

    /// <summary>メインは マルチプレイ／プラクティス／設定／ゲームを終了 の順（2026/10/22）。コントローラーで最初に選ばれるのはマルチプレイ。</summary>
    [Test]
    public void TitlePrefab_MainButtonsAreInOrder()
    {
        Transform page = FindPart(TitlePartRole.PageMain);
        Assert.That(page, Is.Not.Null);

        AssertTopToBottom(page, TitleButtonAction.OpenMulti, TitleButtonAction.PlaySolo,
            TitleButtonAction.OpenSettings, TitleButtonAction.Quit);
        Assert.That(page.GetComponentInChildren<TitleMenuButton>(true).Action, Is.EqualTo(TitleButtonAction.OpenMulti),
            "コントローラーで最初に選ばれるボタン（画面の中で一番前）がマルチプレイではない");
    }

    /// <summary>「部屋を作る」に名前の欄は無い（マルチプレイの画面で入れる。2026/10/22）。</summary>
    [Test]
    public void TitlePrefab_CreatePageHasNoNameInput()
    {
        Transform page = FindPart(TitlePartRole.PageCreate);
        Assert.That(page, Is.Not.Null);
        Assert.That(InputKindsUnder(page).Contains(TitleInputKind.PlayerName), Is.False, "部屋を作る画面に名前の欄が残っている");
    }

    private static HashSet<TitleInputKind> InputKindsUnder(Transform page)
    {
        HashSet<TitleInputKind> kinds = new HashSet<TitleInputKind>();
        foreach (TitleInput input in page.GetComponentsInChildren<TitleInput>(true))
        {
            kinds.Add(input.Kind);
        }
        return kinds;
    }

    /// <summary>その画面の中で、actions のボタンが画面の上から順に並んでいるか（画面の上での高さで比べる）。</summary>
    private static void AssertTopToBottom(Transform page, params TitleButtonAction[] actions)
    {
        float previousY = float.MaxValue;
        foreach (TitleButtonAction action in actions)
        {
            TitleMenuButton found = null;
            foreach (TitleMenuButton button in page.GetComponentsInChildren<TitleMenuButton>(true))
            {
                if (button.Action == action)
                {
                    found = button;
                    break;
                }
            }

            Assert.That(found, Is.Not.Null, $"{page.name} に Action が {action} のボタンが無い");

            // 同じ列の中にあるので、列の中の位置（中心の高さ）で比べられる
            RectTransform rect = (RectTransform)found.transform;
            float y = rect.anchoredPosition.y + (0.5f - rect.pivot.y) * rect.sizeDelta.y;
            Assert.That(y, Is.LessThan(previousY), $"{action} が1つ上のボタンより上にある");
            previousY = y;
        }
    }

    /// <summary>ソロプレイができたので、「サーバーを作る」の「1人で練習する」は無い（2026/10/19・大槻さん）。</summary>
    [Test]
    public void TitlePrefab_HasNoPracticeSoloButton()
    {
        foreach (TitleMenuButton button in LoadPrefab().GetComponentsInChildren<TitleMenuButton>(true))
        {
            Assert.That(button.Action, Is.Not.EqualTo(TitleButtonAction.PracticeSolo), "「1人で練習する」のボタンが残っている");
        }
    }
}
