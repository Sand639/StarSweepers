using System.Collections.Generic;
using NUnit.Framework;
using Unity.Services.Multiplayer;

/// <summary>
/// クイックプレイで**入る部屋を試す順番**（<see cref="TitleScreen.OpenSessionsFullestFirst"/>）を確かめる。
/// 入れなかったら次の部屋を試すので、並び順がそのまま試す順になる（2026/10/28）。
/// </summary>
public class QuickPlayOrderTests
{
    /// <summary>定員8人で、<paramref name="players"/> 人が入っている部屋。</summary>
    private static FakeSessionInfo Room(string name, int players, bool locked = false, bool password = false)
    {
        return new FakeSessionInfo
        {
            Name = name,
            Id = name,
            MaxPlayers = 8,
            AvailableSlots = 8 - players,
            IsLocked = locked,
            HasPassword = password,
        };
    }

    private static List<string> NamesOf(List<ISessionInfo> rooms)
    {
        List<string> names = new List<string>();
        foreach (ISessionInfo room in rooms)
        {
            names.Add(room.Name);
        }
        return names;
    }

    [Test]
    public void QuickPlay_TriesOpenRoomsFullestFirst()
    {
        List<ISessionInfo> rooms = new List<ISessionInfo>
        {
            Room("A", 1),
            Room("満員", 8),
            Room("B", 3),
            Room("鍵", 2, locked: true),
            Room("パスワード", 5, password: true),
            Room("C", 3),
        };

        // 満員・鍵・パスワードの部屋は試さない。人数が同じ B と C は、一覧の順のまま
        Assert.That(NamesOf(TitleScreen.OpenSessionsFullestFirst(rooms)), Is.EqualTo(new[] { "B", "C", "A" }));
    }

    [Test]
    public void QuickPlay_NoOpenRoom_ReturnsEmpty()
    {
        List<ISessionInfo> rooms = new List<ISessionInfo>
        {
            Room("満員", 8),
            Room("鍵", 1, locked: true),
        };

        Assert.That(TitleScreen.OpenSessionsFullestFirst(rooms), Is.Empty);
    }
}
