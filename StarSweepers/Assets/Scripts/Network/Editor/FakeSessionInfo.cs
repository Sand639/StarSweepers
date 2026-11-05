using System;
using System.Collections.Generic;
using Unity.Services.Multiplayer;

/// <summary>
/// テスト用の、公開の部屋の一覧の1件（中継サーバーにはつながない）。
/// <see cref="QuickPlayOrderTests"/>・<see cref="RoomVersionTests"/> で使う。
/// </summary>
internal class FakeSessionInfo : ISessionInfo
{
    public string Name { get; set; }
    public string Id { get; set; }
    public string Upid => string.Empty;
    public string HostId => string.Empty;
    public int AvailableSlots { get; set; }
    public int MaxPlayers { get; set; }
    public bool IsLocked { get; set; }
    public bool HasPassword { get; set; }
    public DateTime LastUpdated => DateTime.MinValue;
    public DateTime Created => DateTime.MinValue;
    public IReadOnlyDictionary<string, SessionProperty> Properties { get; set; }
}
