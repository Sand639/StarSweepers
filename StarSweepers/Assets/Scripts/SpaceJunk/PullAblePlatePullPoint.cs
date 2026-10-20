using UnityEngine;

/// <summary>
/// **移動床（<see cref="PullAblePlate"/>）の8本の柱のひとつ。フックの引っ掛け先。**
/// プレハブでは床の子 Front〜FrontLeft に1つずつ付いていて、引っ張り終わると床をその向きへ動かす。
///
/// フック側（<c>HookController</c>）は、大型の引っ張り対象の共通の口（<c>IHookPullable</c>）として扱う。
/// さらに <c>IPlayerAwareHookPullable</c> も持たせて、**「誰が撃ったか」で引っ掛けられるかを変える**：
/// 自分が乗っている床の柱は引っ掛からない（自分で自分を引っ張れてしまうのを防ぐ）。
///
/// 柱を使わない向きは、床の側がこの GameObject ごと非表示にする（<see cref="PullAblePlate.SetPullPointActive"/>）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class PullAblePlatePullPoint : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    [Tooltip("この柱を引っ張ると床が動く向き。プレハブの子の名前（Front など）と合わせる")]
    [SerializeField] private PullAblePlateDirection direction;

    [Tooltip("この柱が付いている床。空なら親から自動で探す")]
    [SerializeField] private PullAblePlate plate;

    /// <summary>この柱の向き。</summary>
    public PullAblePlateDirection Direction => direction;

    public Component HookComponent => this;

    /// <summary>フックの線がつながる場所（柱の位置）。</summary>
    public Vector3 HookAnchorPoint => transform.position;

    /// <summary>いま誰かのフックが掛かっているか。掛かっている間は、ほかの人は引っ掛けられない。</summary>
    public bool IsHooked { get; private set; }

    /// <summary>
    /// いま引っ掛けられるか。柱が有効で、床が動いている最中でなく、まだ誰も掛けていないとき。
    /// （誰が撃ったかまで見るのは <see cref="CanBeHookedBy"/>）
    /// </summary>
    public bool CanBeHooked => isActiveAndEnabled
        && gameObject.activeInHierarchy
        && plate != null
        && plate.IsDirectionActive(direction)
        && !plate.IsMoving
        && !IsHooked;

    private void Awake()
    {
        ResolvePlate();
    }

    private void OnValidate()
    {
        ResolvePlate();
        // 柱はフックが「触れた」ことだけ分かればよく、物をさえぎらない（トリガー）
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    private void OnDisable()
    {
        // 掛かったまま無効にされても、次に有効にしたときにまた掛けられるようにする
        IsHooked = false;
    }

    /// <summary>そのプレイヤーが引っ掛けられるか。**自分が乗っている床の柱は引っ掛けられない。**</summary>
    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanBeHooked && !plate.IsPlayerOnPlate(playerRoot);
    }

    /// <summary>フックが触れた／外れたときにフック側が呼ぶ。掛けられる状態のときだけ「掛かった」になる。</summary>
    public void SetHooked(bool hooked)
    {
        IsHooked = hooked && CanBeHooked;
    }

    /// <summary>
    /// 引っ張りのゲージがたまり切ったときにフック側が呼ぶ。床をこの柱の向きへ1回ぶん動かす。
    /// 動かせたかどうかにかかわらず、フックは外れる。
    /// </summary>
    public void CompletePull(HookPullContext context)
    {
        if (IsHooked && plate != null)
        {
            plate.Pull(direction);
        }

        IsHooked = false;
    }

    /// <summary>床の参照が空なら、親から探して入れる（プレハブでは床の子に置いているため）。</summary>
    private void ResolvePlate()
    {
        if (plate == null)
        {
            plate = GetComponentInParent<PullAblePlate>();
        }
    }
}
