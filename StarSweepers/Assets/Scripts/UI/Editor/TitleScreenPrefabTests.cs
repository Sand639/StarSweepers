using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// タイトル画面のプレハブに、**コードが頼りにしている印の付いた部品**がそろっているかを確かめる。
/// 配置や見た目は自由に変えてよいが、印を消すとその部品は動かなくなるので、ここで気づけるようにする。
/// （2026/10/19：メインを ソロプレイ／マルチプレイ／設定／ゲームを終了 にし、マルチプレイの画面にまとめた形に合わせた）
/// </summary>
public class TitleScreenPrefabTests
{
    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TitleScreenPrefabBuilder.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Assets/Resources/TitleScreen.prefab が無い");
        return prefab;
    }

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

    [Test]
    public void TitlePrefab_IsTheCurrentVersion()
    {
        Assert.That(LoadPrefab().GetComponent<TitleScreen>().PrefabVersion,
            Is.EqualTo(TitleScreenPrefabBuilder.PrefabVersion));
    }

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
        };

        foreach (TitleButtonAction action in required)
        {
            Assert.That(actions.Contains(action), Is.True, $"Action が {action} のボタンが無い");
        }
    }

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
}
