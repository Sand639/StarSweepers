using UnityEngine;

/// <summary>Hook が大型物件の拉扯を完成した時に渡す情報。</summary>
public readonly struct HookPullContext
{
    public HookPullContext(Vector3 playerPosition, float strength, Transform playerRoot = null)
    {
        PlayerPosition = playerPosition;
        Strength = Mathf.Clamp01(strength);
        PlayerRoot = playerRoot;
    }

    public Vector3 PlayerPosition { get; }
    public float Strength { get; }
    public Transform PlayerRoot { get; }
}

/// <summary>
/// 投げられる物資とは別に、Hook の左拉扯で動作する大型物件が実装する介面。
/// 障害物や浮島ごとに、拉扯完成後の動作を差し替えられる。
/// </summary>
public interface IHookPullable
{
    Component HookComponent { get; }
    Vector3 HookAnchorPoint { get; }
    bool IsHooked { get; }
    bool CanBeHooked { get; }

    void SetHooked(bool hooked);
    void CompletePull(HookPullContext context);
}
