using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// タイトル画面のプレハブ（2026/10/13 からの作り）に、**コードが頼りにしている印の付いた部品**がそろっているかを確かめる。
/// 配置や見た目は自由に変えてよいが、印を消すとその部品は動かなくなるので、ここで気づけるようにする。
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

    [TestCase(TitlePartRole.PageStart)]
    [TestCase(TitlePartRole.PageMode)]
    [TestCase(TitlePartRole.SoloPreview)]
    [TestCase(TitlePartRole.MultiPreview)]
    [TestCase(TitlePartRole.RoomListPanel)]
    [TestCase(TitlePartRole.RoomListContent)]
    [TestCase(TitlePartRole.RoomCardTemplate)]
    [TestCase(TitlePartRole.RoomCardName)]
    [TestCase(TitlePartRole.RoomCardPlayers)]
    [TestCase(TitlePartRole.CreateRoomButton)]
    [TestCase(TitlePartRole.CreateWindow)]
    [TestCase(TitlePartRole.PasswordWindow)]
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
    public void TitlePrefab_HasEveryButtonOfTheNewFlow()
    {
        HashSet<TitleButtonAction> actions = new HashSet<TitleButtonAction>();
        foreach (TitleMenuButton button in LoadPrefab().GetComponentsInChildren<TitleMenuButton>(true))
        {
            actions.Add(button.Action);
        }

        TitleButtonAction[] required =
        {
            TitleButtonAction.Start, TitleButtonAction.PlaySolo, TitleButtonAction.ShowMulti,
            TitleButtonAction.OpenCreateRoom, TitleButtonAction.CreateRoomInternet, TitleButtonAction.CreateRoomLan,
            TitleButtonAction.CloseWindow, TitleButtonAction.JoinRoom, TitleButtonAction.ConfirmJoinPassword,
        };

        foreach (TitleButtonAction action in required)
        {
            Assert.That(actions.Contains(action), Is.True, $"Action が {action} のボタンが無い");
        }
    }

    [Test]
    public void TitlePrefab_HasNameAndPasswordInputs()
    {
        HashSet<TitleInputKind> kinds = new HashSet<TitleInputKind>();
        foreach (TitleInput input in LoadPrefab().GetComponentsInChildren<TitleInput>(true))
        {
            kinds.Add(input.Kind);
        }

        Assert.That(kinds, Does.Contain(TitleInputKind.PlayerName));
        Assert.That(kinds, Does.Contain(TitleInputKind.CreatePassword));
        Assert.That(kinds, Does.Contain(TitleInputKind.JoinPassword));
    }
}
