using System.Collections.Generic;
using NUnit.Framework;
using Unity.Services.Multiplayer;

/// <summary>
/// 公開の部屋の一覧から、**ゲームの版が違う人の部屋を外す**処理（<see cref="InternetConnection.SameVersionOnly"/>）を確かめる。
/// 版が違う部屋に入ろうとすると、つないだ直後に相手から切られるので、一覧にもクイックマッチにも出さない（2026/10/28）。
/// </summary>
public class RoomVersionTests
{
    /// <summary>版 <paramref name="version"/> の部屋。null なら版が書かれていない（2026/10/28 より前の版のホスト）。</summary>
    private static FakeSessionInfo Room(string name, string version)
    {
        Dictionary<string, SessionProperty> properties = new Dictionary<string, SessionProperty>();
        if (version != null)
        {
            properties.Add("ver", new SessionProperty(version, VisibilityPropertyOptions.Public));
        }

        return new FakeSessionInfo { Name = name, Id = name, MaxPlayers = 8, AvailableSlots = 7, Properties = properties };
    }

    [Test]
    public void PublicRooms_OnlySameVersionAreKept()
    {
        List<ISessionInfo> rooms = new List<ISessionInfo>
        {
            Room("同じ版1", "aaaa"),
            Room("違う版", "bbbb"),
            Room("版なし", null),
            new FakeSessionInfo { Name = "情報なし", Id = "x", MaxPlayers = 8, AvailableSlots = 7 },
            Room("同じ版2", "aaaa"),
        };

        List<string> names = new List<string>();
        foreach (ISessionInfo room in InternetConnection.SameVersionOnly(rooms, "aaaa"))
        {
            names.Add(room.Name);
        }

        // 版の書かれていない部屋（古い版のホスト）も外す。残った部屋は一覧の順のまま
        Assert.That(names, Is.EqualTo(new[] { "同じ版1", "同じ版2" }));
    }
}
