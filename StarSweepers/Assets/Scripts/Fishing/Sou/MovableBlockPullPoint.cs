using UnityEngine;

/// <summary>MovableBlock 四周其中一個可被 Hook 勾住的拉點。</summary>
/// <remarks>
/// （日本語）**<see cref="MovableBlock"/> の4辺にある、フックを掛ける柱の1本。**
///
/// ブロックの子オブジェクト（Front／Back／Left／Right）に付ける。フックはこの柱に掛かり、
/// 引っ張りが完成すると（<see cref="CompletePull"/>）親のブロックに「この辺から引かれた」と伝える。
/// 実際に動くのはブロックのほう。
///
/// テスト（`MovableBlockTests.cs`）が `side` を名前で指定しているので、項目名は変えないこと。
/// </remarks>
[RequireComponent(typeof(BoxCollider))]
public class MovableBlockPullPoint : MonoBehaviour, IHookPullable
{
    /// <summary>動かすブロック。空なら親から自動で探す（<see cref="EnsureBlock"/>）。</summary>
    [SerializeField] private MovableBlock block;

    /// <summary>この柱がブロックのどの辺か。FourDirections モードでは、この辺の向きへ動く。</summary>
    [SerializeField] private MovableBlockPullSide side = MovableBlockPullSide.Front;

    private bool isHooked;

    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked => isHooked;

    /// <summary>誰も掛けておらず、ブロックが動いている最中でもないときだけ掛けられる（連打で二重に動かないように）。</summary>
    public bool CanBeHooked
    {
        get
        {
            EnsureBlock();
            return !isHooked && block != null && !block.IsMoving;
        }
    }

    private void Awake()
    {
        if (block == null)
        {
            EnsureBlock();
        }

        // 柱の当たり判定はすり抜け（トリガー）にして、プレイヤーや物資がぶつからないようにする。
        // フックはトリガーにも当たるので、引っ掛けることはできる
        Collider pullCollider = GetComponent<Collider>();
        pullCollider.isTrigger = true;
    }

    public void SetHooked(bool hooked)
    {
        isHooked = hooked;
    }

    /// <summary>引っ張りが完成した。ブロックを1回動かし、フックは自動で外れる。</summary>
    public void CompletePull(HookPullContext context)
    {
        EnsureBlock();
        if (block != null)
        {
            block.Pull(context.PlayerPosition, side);
        }

        isHooked = false;
    }

    /// <summary>インスペクターで入れ忘れていても、親の MovableBlock を自動で使う。</summary>
    private void EnsureBlock()
    {
        if (block == null)
        {
            block = GetComponentInParent<MovableBlock>();
        }
    }
}
