using UnityEngine;

/// <summary>
/// Hook する Player ごとに、拉扯を許可するか判断したい対象が実装する追加契約。
/// </summary>
public interface IPlayerAwareHookPullable
{
    bool CanBeHookedBy(Transform playerRoot);
}
