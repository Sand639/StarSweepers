using UnityEngine;

/// <summary>Hook が大型物件の拉扯を完成した時に渡す情報。</summary>
/// <remarks>
/// 「拉扯（ラーチャ）」＝引っ張ること。<see cref="ThrowController"/> が、もう一度左入力が来て
/// 引っ張りが完成したときに作り、<see cref="IHookPullable.CompletePull"/> に渡す。
/// </remarks>
public readonly struct HookPullContext
{
    /// <summary>
    /// <paramref name="strength"/> はゲージの強さ。0〜1 に丸めて保存する。
    /// <paramref name="playerRoot"/> は引っ張った人の体の根元。テストなど、人がいないときは省略できる。
    /// </summary>
    public HookPullContext(Vector3 playerPosition, float strength, Transform playerRoot = null)
    {
        PlayerPosition = playerPosition;
        Strength = Mathf.Clamp01(strength);
        PlayerRoot = playerRoot;
    }

    /// <summary>引っ張った人の位置（ワールド座標）。「プレイヤーの方向へ動く」モードの向きの計算に使う。</summary>
    public Vector3 PlayerPosition { get; }

    /// <summary>ゲージの強さ（0〜1）。いまの <see cref="MovableBlock"/> は使っていない（動く距離は Move Distance で固定）。</summary>
    public float Strength { get; }

    /// <summary>引っ張った人の体の根元。無ければ null。移動床が「床の上の人」を見分けるのに使う。</summary>
    public Transform PlayerRoot { get; }
}

/// <summary>
/// 投げられる物資とは別に、Hook の左拉扯で動作する大型物件が実装する介面。
/// 障害物や浮島ごとに、拉扯完成後の動作を差し替えられる。
/// </summary>
/// <remarks>
/// **フックがこれを見つける仕組み**：当たった Collider の親をたどって、この介面を持つ部品を探す
/// （<see cref="HookController.FindPullable"/>）。だから、柱など子オブジェクトの当たり判定に当たっても
/// 親の部品が見つかる。
///
/// 実装例：<see cref="MovableBlockPullPoint"/>（4辺から引けるブロック）、
/// `SpaceJunk/PullAblePlatePullPoint.cs`（8方向から引ける移動床）。
/// 詳しくは `Documents/機能ドキュメント/フックで動かせる大型物件.md`。
/// </remarks>
public interface IHookPullable
{
    /// <summary>この物件の部品そのもの。消えたか（Unity 上で null になったか）を HookController が確かめるのに使う。</summary>
    Component HookComponent { get; }

    /// <summary>フックと糸を貼り付ける位置（ワールド座標）。</summary>
    Vector3 HookAnchorPoint { get; }

    /// <summary>いま誰かのフックが掛かっているか。</summary>
    bool IsHooked { get; }

    /// <summary>いま引っ掛けてよいか（動いている最中・すでに掛かっている などは false）。</summary>
    bool CanBeHooked { get; }

    /// <summary>フックが掛かった／外れた、を記録する。HookController と ThrowController から呼ばれる。</summary>
    void SetHooked(bool hooked);

    /// <summary>もう一度左入力が来て引っ張りが完成したときに呼ばれる。ここで実際に動かす。</summary>
    void CompletePull(HookPullContext context);
}
