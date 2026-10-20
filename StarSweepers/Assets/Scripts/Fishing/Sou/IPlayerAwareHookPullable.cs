using UnityEngine;

/// <summary>
/// Hook する Player ごとに、拉扯を許可するか判断したい対象が実装する追加契約。
/// </summary>
/// <remarks>
/// <see cref="IHookPullable"/> に**加えて**実装する（これ単体では使わない）。
/// <see cref="HookController.CanHookPullable"/> が、<see cref="IHookPullable.CanBeHooked"/> を見たあとに
/// この契約を持っていればこちらも見る。持っていなければ誰からでも引っ掛けられる。
///
/// 使い道の例：移動床（`SpaceJunk/PullAblePlate.cs`）は、**その床の上に立っている人**からは
/// 引っ掛けられないようにする（自分の乗っている床を自分で引くと動きがおかしくなるため）。
/// </remarks>
public interface IPlayerAwareHookPullable
{
    /// <summary>この人（体の根元）から引っ掛けてよいか。</summary>
    bool CanBeHookedBy(Transform playerRoot);
}
