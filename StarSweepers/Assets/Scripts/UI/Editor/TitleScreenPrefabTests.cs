using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// タイトル画面のプレハブに、**コードが頼りにしている印の付いた部品**がそろっているかを確かめる。
/// 配置や見た目は自由に変えてよいが、印を消すとその部品は動かなくなるので、ここで気づけるようにする。
/// （2026/10/15：前の画面（サーバーを作る・探す・設定）に戻したので、その部品を確かめる内容にした）
/// </summary>
public class TitleScreenPrefabTests
{
    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TitleScreenPrefabBuilder.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Assets/Resources/TitleScreen.prefab が無い");
        return prefab;
    }

    [Test]
    public void TitlePrefab_IsTheCurrentVersion()
    {
        Assert.That(LoadPrefab().GetComponent<TitleScreen>().PrefabVersion,
            Is.EqualTo(TitleScreenPrefabBuilder.PrefabVersion));
    }

    [TestCase(TitlePartRole.PageMain)]
    [TestCase(TitlePartRole.PageCreate)]
    [TestCase(TitlePartRole.PageFind)]
    [TestCase(TitlePartRole.PageFindPrivate)]
    [TestCase(TitlePartRole.PageFindPublic)]
    [TestCase(TitlePartRole.PageFindLan)]
    [TestCase(TitlePartRole.PageSettings)]
    [TestCase(TitlePartRole.PageFindFriends)]
    [TestCase(TitlePartRole.PublicListContent)]
    [TestCase(TitlePartRole.ServerCardTemplate)]
    [TestCase(TitlePartRole.LanListContent)]
    [TestCase(TitlePartRole.LanListMessage)]
    [TestCase(TitlePartRole.LanCardTemplate)]
    [TestCase(TitlePartRole.PublicCheckText)]
    [TestCase(TitlePartRole.PrivateCheckText)]
    [TestCase(TitlePartRole.LanCheckText)]
    [TestCase(TitlePartRole.BusyOverlay)]
    public void TitlePrefab_HasPart(TitlePartRole role)
    {
        bool found = false;
        foreach (TitlePart part in LoadPrefab().GetComponentsInChildren<TitlePart>(true))
        {
            found |= part.Role == role;
        }
        Assert.That(found, Is.True, $"印 {role} の付いた部品が無い");
    }

    [Test]
    public void TitlePrefab_HasMainButtons()
    {
        HashSet<TitleButtonAction> actions = new HashSet<TitleButtonAction>();
        foreach (TitleMenuButton button in LoadPrefab().GetComponentsInChildren<TitleMenuButton>(true))
        {
            actions.Add(button.Action);
        }

        TitleButtonAction[] required =
        {
            TitleButtonAction.QuickPlay, TitleButtonAction.OpenCreate, TitleButtonAction.OpenFind, TitleButtonAction.OpenSettings, TitleButtonAction.Quit,
            TitleButtonAction.CreateServer, TitleButtonAction.MaxPlayersDown, TitleButtonAction.MaxPlayersUp,
            TitleButtonAction.SetPublic, TitleButtonAction.SetPrivate, TitleButtonAction.SetLan,
            TitleButtonAction.JoinPrivate, TitleButtonAction.JoinLan, TitleButtonAction.SearchPublic,
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
        Assert.That(kinds.Contains(TitleInputKind.LanAddress), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.CreatePassword), Is.True);
        Assert.That(kinds.Contains(TitleInputKind.JoinPassword), Is.True);
    }

    /// <summary>LAN の部屋の一覧は「LANのサーバーに参加する」画面の中にある。</summary>
    [Test]
    public void TitlePrefab_LanRoomListIsOnTheLanPage()
    {
        Transform lanPage = null;
        Transform lanList = null;
        foreach (TitlePart part in LoadPrefab().GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == TitlePartRole.PageFindLan)
            {
                lanPage = part.transform;
            }
            else if (part.Role == TitlePartRole.LanListContent)
            {
                lanList = part.transform;
            }
        }

        Assert.That(lanPage, Is.Not.Null);
        Assert.That(lanList, Is.Not.Null);
        Assert.That(lanList.IsChildOf(lanPage), Is.True, "LAN の部屋の一覧が「LANのサーバーに参加する」画面の外にある");
    }
}
