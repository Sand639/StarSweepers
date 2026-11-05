using System.Collections.Generic;
using UnityEngine;

/// <summary>ロビー内のプレイヤー出現位置。番号は0から指定する。</summary>
public sealed class SpaceJunkLobbyPoint : MonoBehaviour
{
    public static readonly List<SpaceJunkLobbyPoint> All = new List<SpaceJunkLobbyPoint>();

    [SerializeField, Min(0)] private int spawnIndex;

    public int SpawnIndex => Mathf.Max(0, spawnIndex);

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }
}
