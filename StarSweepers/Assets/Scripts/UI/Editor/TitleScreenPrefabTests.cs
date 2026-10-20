using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// タイトル画面のプレハブに、**コードが頼りにしている印の付いた部品**がそろっているかを確かめる。
/// 配置や見た目は自由に変えてよいが、印を消すとその部品は動かなくなるので、ここで気づけるようにする。
/// （2026/10/19：メインを ソロプレイ／マルチプレイ／設定／ゲームを終了 にし、マルチプレイの画面にまとめた形に合わせた）
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
    [TestCase(TitlePartRole.PageCreate)]
    [TestCase(TitlePartRole.PageFindPrivate)]
    [TestCase(TitlePartRole.PageSettings)]
    [TestCase(TitlePartRole.JoinableListContent)]
    [TestCase(TitlePartRole.JoinableListMessage)]
    [TestCase(TitlePartRole.JoinableCardTemplate)]
    [TestCase(TitlePartRole.SearchPublicCheckText)]
    [TestCase(TitlePartRole.SearchFriendsCheckText)]
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

    /// <summary>マルチプレイの画面にまとめたので、前の「サーバーを探す」の画面は残っていない（2026/10/19）。</summary>
    [TestCase(TitlePartRole.PageFind)]
    [TestCase(TitlePartRole.PageFindPublic)]
    [TestCase(TitlePartRole.PageFindLan)]
    [TestCase(TitlePartRole.PageFindFriends)]
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
            TitleButtonAction.QuickPlay, TitleButtonAction.OpenFindPrivate, TitleButtonAction.OpenCreate,
            TitleButtonAction.ToggleSearchPublic, TitleButtonAction.ToggleSearchFriends,
            // サーバーを作る・プライベートサーバーを探す
            TitleButtonAction.CreateServer, TitleButtonAction.MaxPlayersDown, TitleButtonAction.MaxPlayersUp,
            TitleButtonAction.SetPublic, TitleButtonAction.SetPrivate, TitleButtonAction.SetLan,
            TitleButtonAction.JoinPrivate,
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

    /// <summary>参加可能なサーバーの一覧は、マルチプレイの画面の中にある。</summary>
    [Test]
    public void TitlePrefab_JoinableListIsOnTheMultiPage()
    {
        Transform page = FindPart(TitlePartRole.PageMulti);
        Transform list = FindPart(TitlePartRole.JoinableListContent);

        Assert.That(page, Is.Not.Null);
        Assert.That(list, Is.Not.Null);
        Assert.That(list.IsChildOf(page), Is.True, "参加可能なサーバーの一覧が、マルチプレイの画面の外にある");
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
