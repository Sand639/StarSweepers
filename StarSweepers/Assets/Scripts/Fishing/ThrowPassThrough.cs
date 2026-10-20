using UnityEngine;

/// <summary>
/// **投げた物資が、投げた本人の体に当たらないようにする**（2026/9/30・大槻さん）。
///
/// 宇宙ごみ式の「投げる」は、物資を**頭上（足元から 2.6m）で止めてから**投げる。
/// プレイヤーの体（CharacterController）は高さ 2m ほどあるので、縦に長い物資だと頭に重なっていて、
/// 手を離した瞬間に**頭にぶつかって跳ねる・頭の上に乗る**といった変な動きになっていた。
///
/// そこで、投げた直後は**その物資と投げた本人の体だけ**当たらないようにし、
/// **離れたら（または一定時間たったら）元に戻す**。ほかのプレイヤーや壁には、これまでどおり当たる。
///
/// **持っている間も同じ。** 持ったまま歩くと、頭上の物資に体がぶつかって変な動きになっていたので、
/// 引っ掛けた瞬間から当たらないようにする（<see cref="Hold"/>）。
///
/// 使い方：引っ掛けた瞬間に <see cref="Hold"/>、投げた瞬間に <see cref="Apply"/> を呼ぶ。
/// 物資に一時的に付いて、終わったら自分で外れる。
/// オンラインでは、物資を動かしているPC（投げた本人と、そのあと引き取るホスト）の両方で呼ぶ。
/// </summary>
public class ThrowPassThrough : MonoBehaviour
{
    /// <summary>少なくともこの秒数は当たらないままにする（離れた判定がすぐ付いてしまわないように）。</summary>
    private const float MinSeconds = 0.15f;

    /// <summary>離れなくても、この秒数たったら元に戻す（頭の上に乗ったままにならないように）。</summary>
    private const float MaxSeconds = 1.5f;

    /// <summary>「離れた」とみなす余白（メートル）。</summary>
    private const float SeparationMargin = 0.2f;

    /// <summary>この物資の当たり判定（子も含む）。投げた本人の体との当たりを一時的に切る相手。</summary>
    private Collider[] itemColliders;

    /// <summary>投げた（または持っている）本人の体の当たり判定。null なら何も無視していない。</summary>
    private Collider thrower;

    /// <summary>手を離してからの経過秒数。持っている間は数えない。</summary>
    private float elapsed;

    /// <summary>
    /// 引っ掛けて持っている最中か。**持っている間は時間で戻さない**（2026/9/30：持ったまま歩くと、
    /// 頭上の物資に体がぶつかって変な動きになっていた）。引っ掛けが外れたら、自動で「離れたら戻す」に切り替わる。
    /// </summary>
    private bool holding;

    private HookableObject hookable;

    /// <summary>
    /// **引っ掛けた物資と、持っている本人の体を、持っている間ずっと当たらないようにする。**
    /// 手を離したら（引っ掛けが外れたら）、体から離れたところで自動で元に戻る。
    /// </summary>
    public static void Hold(GameObject item, Collider holderCollider)
    {
        ThrowPassThrough passThrough = Attach(item, holderCollider);
        if (passThrough != null)
        {
            passThrough.holding = true;
        }
    }

    /// <summary>
    /// <paramref name="item"/> と、投げた本人の体 <paramref name="throwerCollider"/> を、しばらく当たらないようにする。
    /// すでに付いていれば、相手と時間を付け直す。
    /// </summary>
    public static void Apply(GameObject item, Collider throwerCollider)
    {
        ThrowPassThrough passThrough = Attach(item, throwerCollider);
        if (passThrough != null)
        {
            passThrough.holding = false;
        }
    }

    /// <summary>物資にこの部品を付け（すでに付いていればそれを使い）、相手と時間を付け直す。</summary>
    private static ThrowPassThrough Attach(GameObject item, Collider collider)
    {
        if (item == null || collider == null)
        {
            return null;
        }

        if (!item.TryGetComponent(out ThrowPassThrough passThrough))
        {
            passThrough = item.AddComponent<ThrowPassThrough>();
        }

        passThrough.Begin(collider);
        return passThrough;
    }

    /// <summary>この本人の体と当たらないようにし始める。何度呼んでもよい。</summary>
    private void Begin(Collider throwerCollider)
    {
        hookable = GetComponent<HookableObject>();

        // 前の相手が違えば、先に元へ戻す
        if (thrower != null && thrower != throwerCollider)
        {
            SetIgnored(false);
        }

        thrower = throwerCollider;
        itemColliders = GetComponentsInChildren<Collider>();
        elapsed = 0f;
        enabled = true;

        SetIgnored(true);
    }

    private void Update()
    {
        // 本人の体がいなくなった（退出した・消えた）ら、もう無視し続ける意味が無い
        if (thrower == null)
        {
            End();
            return;
        }

        if (holding)
        {
            // 持っている間は数えない。**引っ掛けが外れたら**（投げた・離した・爆風・ほかの人に外された）ここから数え始める
            if (hookable != null && hookable.IsHooked && !hookable.IsVanished)
            {
                return;
            }

            holding = false;
            elapsed = 0f;
        }

        elapsed += Time.deltaTime;

        if (elapsed < MinSeconds)
        {
            return;
        }

        if (elapsed >= MaxSeconds || IsSeparated())
        {
            End();
        }
    }

    /// <summary>物資の当たり判定が、投げた本人の体から離れたか。</summary>
    private bool IsSeparated()
    {
        // 体の箱を余白ぶん広げてから重なりを見る。
        // Expand は箱の全体の幅を増やす（片側ずつではない）ので、両側に余白を付けるには 2 倍にする
        Bounds body = thrower.bounds;
        body.Expand(SeparationMargin * 2f);

        // すり抜け判定（トリガー）はそもそも当たらないので、実体のある当たり判定だけ見る
        foreach (Collider itemCollider in itemColliders)
        {
            if (itemCollider != null && !itemCollider.isTrigger && itemCollider.bounds.Intersects(body))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>当たりを元に戻し、この部品を休ませる（物資からは外さず、次に投げたときにまた使う）。</summary>
    private void End()
    {
        SetIgnored(false);
        thrower = null;
        holding = false;
        enabled = false;
    }

    /// <summary>物資が消えるとき（ポケットに入った・爆発した）に、無視の設定を残さない。</summary>
    private void OnDestroy()
    {
        SetIgnored(false);
    }

    /// <summary>物資の実体のある当たり判定すべてと、本人の体との当たりを切る／戻す。</summary>
    private void SetIgnored(bool ignore)
    {
        if (thrower == null || itemColliders == null)
        {
            return;
        }

        foreach (Collider itemCollider in itemColliders)
        {
            if (itemCollider != null && !itemCollider.isTrigger)
            {
                Physics.IgnoreCollision(itemCollider, thrower, ignore);
            }
        }
    }
}
