using System.Collections.Generic;
using UnityEngine;

/// <summary>ロビーのチームエリア。プレイヤーが1秒留まると、そのプレイヤーを指定チームへ移す。</summary>
[RequireComponent(typeof(Collider))]
public sealed class LobbyTeamZone : MonoBehaviour
{
    [Tooltip("赤＝0、青＝1。SpaceJunkTeams のチーム番号")]
    [SerializeField, Min(0)] private int team;
    [Tooltip("チーム変更までエリア内に留まる秒数")]
    [SerializeField, Min(0f)] private float staySeconds = 1f;

    private readonly Dictionary<ulong, PlayerStay> players = new Dictionary<ulong, PlayerStay>();
    private readonly Dictionary<ulong, HashSet<Collider>> colliders = new Dictionary<ulong, HashSet<Collider>>();
    private readonly HashSet<ulong> changedPlayers = new HashSet<ulong>();

    private sealed class PlayerStay
    {
        public float Since;
    }

    private void Reset()
    {
        Collider zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        FishingNetPlayer player = other.GetComponentInParent<FishingNetPlayer>();
        if (player == null || !player.IsOwner || !player.IsSpawned)
        {
            return;
        }

        ulong key = player.PlayerKey;
        if (!colliders.TryGetValue(key, out HashSet<Collider> set))
        {
            set = new HashSet<Collider>();
            colliders.Add(key, set);
        }

        set.Add(other);
        if (players.ContainsKey(key))
        {
            return;
        }

        players.Add(key, new PlayerStay { Since = Time.unscaledTime });
        changedPlayers.Remove(key);
        SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, true);
    }

    private void OnTriggerExit(Collider other)
    {
        FishingNetPlayer player = other.GetComponentInParent<FishingNetPlayer>();
        if (player == null || !player.IsOwner)
        {
            return;
        }

        ulong key = player.PlayerKey;
        if (!colliders.TryGetValue(key, out HashSet<Collider> set))
        {
            return;
        }

        set.Remove(other);
        if (set.Count > 0)
        {
            return;
        }

        colliders.Remove(key);
        players.Remove(key);
        changedPlayers.Remove(key);
        SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, false);
    }

    private void Update()
    {
        foreach (KeyValuePair<ulong, PlayerStay> pair in players)
        {
            if (!changedPlayers.Contains(pair.Key) && Time.unscaledTime - pair.Value.Since >= staySeconds)
            {
                changedPlayers.Add(pair.Key);
                SpaceJunkSession.Current?.RequestLobbyZoneTeam(pair.Key, team);
            }
        }
    }

    private void OnDisable()
    {
        foreach (ulong key in players.Keys)
        {
            SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, false);
        }

        players.Clear();
        colliders.Clear();
        changedPlayers.Clear();
    }
}
