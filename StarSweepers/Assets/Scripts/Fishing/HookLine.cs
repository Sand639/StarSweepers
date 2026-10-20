using UnityEngine;

/// <summary>
/// **プレイヤーとフック（または物資）を結ぶ糸の見た目。**
///
/// **2026/10/20 から、糸は「液体のビーム」（<see cref="LiquidBeam"/>）で描く**（Use Liquid がオンのとき）。
/// 手元から水のような流れがのびて、フックの先まで届く。フックの先の球は隠し、流れの先端のふくらみをフックに見せる。
///
/// 色はチームの色で、**何もつかんでいないときは薄い色、つかんだら濃い鮮やかな色**になる
/// （つかんだ瞬間に、先端から根元へ染まっていく）。
/// チームの色は <see cref="SetTeamColor"/>、つかんでいるかは <see cref="SetGrabbing"/> で外から教えてもらう。
///
/// **つかんだ瞬間から約 0.5 秒で、物資のまわりを液体の枠で囲う**（<see cref="LiquidWrap"/>。Use Wrap がオンのとき）。
/// はなすと枠がはじけて消える。
///
/// Use Liquid をオフにすると、前と同じ LineRenderer のまっすぐな1本に戻る。
///
/// 使い方：LineRenderer の付いた空オブジェクトに付けて、HookController の Line に入れる。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class HookLine : MonoBehaviour
{
    [Header("液体の糸")]
    [Tooltip("オンなら、糸を液体のビームで描く。オフなら前と同じまっすぐな線")]
    [SerializeField] private bool useLiquid = true;

    [Tooltip("液体のビームのプレハブ（Assets/Prefabs/Effect/LiquidBeam）。空でも動くが、ビルドでは色が出ないことがあるので入れておく")]
    [SerializeField] private LiquidBeam liquidPrefab;

    [Tooltip("チームの色が決まるまでの色（1人用の検証シーンなど）")]
    [SerializeField] private Color teamColor = new Color(0.30f, 0.60f, 0.95f);

    [Tooltip("何もつかんでいないときの色の濃さ（0 で白、1 でチームの色そのもの）")]
    [Range(0f, 1f)]
    [SerializeField] private float paleSaturation = 0.55f;

    [Tooltip("何もつかんでいないときの色の明るさ（液体は少し光るので、1 だと白っぽく飛ぶ）")]
    [Range(0f, 1f)]
    [SerializeField] private float paleValue = 0.85f;

    [Tooltip("つかんだあとの色の濃さ（1 でいちばん鮮やか）")]
    [Range(0f, 1f)]
    [SerializeField] private float vividSaturation = 1f;

    [Header("つかんだ物資を囲う枠")]
    [Tooltip("オンなら、物資をつかんだときに液体の枠で囲う（Use Liquid がオンのときだけ）")]
    [SerializeField] private bool useWrap = true;

    [Tooltip("つかんだ物資を探す範囲（フックの先からの距離。メートル）")]
    [SerializeField] private float wrapSearchRadius = 0.8f;

    [Tooltip("これより大きな物（大きな板など）は囲わない（メートル）")]
    [SerializeField] private float wrapMaxSize = 5f;

    private LineRenderer line;
    private LiquidBeam liquid;
    private LiquidWrap wrap;
    private bool grabbing;

    /// <summary>糸の先（フックの先）の、最後に教えてもらった位置。</summary>
    private Vector3 tipPosition;
    private bool hasTip;

    /// <summary>糸を液体で描いているか。</summary>
    public bool UsesLiquid => useLiquid;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;

        if (useLiquid)
        {
            liquid = CreateLiquid();
            liquid.SetColor(CurrentColor());

            if (useWrap)
            {
                wrap = LiquidWrap.Create(transform, liquid.LineMaterial);
            }
        }

        Show(false);
    }

    /// <summary>液体のビームを、このオブジェクトの子に作る。</summary>
    private LiquidBeam CreateLiquid()
    {
        if (liquidPrefab != null)
        {
            LiquidBeam made = Instantiate(liquidPrefab, transform);
            made.name = "LiquidBeam";
            return made;
        }

        // プレハブが入っていないとき（検証シーンなど）。マテリアルは LiquidBeam が自分で作る
        var child = new GameObject("LiquidBeam");
        child.transform.SetParent(transform, false);
        return child.AddComponent<LiquidBeam>();
    }

    /// <summary>糸の両端を決める。a＝手元、b＝フックの先。</summary>
    public void SetEnds(Vector3 a, Vector3 b)
    {
        tipPosition = b;
        hasTip = true;

        if (wrap != null)
        {
            wrap.SetContactPoint(b);
        }

        if (liquid != null)
        {
            liquid.Follow(a, b);
            return;
        }

        if (line.positionCount != 2)
        {
            line.positionCount = 2;
        }
        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    /// <summary>糸を出す／隠す。液体のときは、隠すと根元から切れて消えていく。</summary>
    public void Show(bool visible)
    {
        if (liquid != null)
        {
            line.enabled = false;

            // 出すほうは SetEnds() で始まる
            if (!visible)
            {
                liquid.Stop();
                hasTip = false;

                if (wrap != null)
                {
                    wrap.Release();
                }
            }
            return;
        }

        line.enabled = visible;
    }

    /// <summary>
    /// **チームの色を決める。** 塗る色は、つかんでいるかどうかで薄く／濃くなる。
    /// チームの色を塗るところ（<c>FishingNetPlayer</c> / <c>SpaceJunkPlayerSetup</c>）から呼ぶ。
    /// </summary>
    public void SetTeamColor(Color color)
    {
        teamColor = color;

        if (liquid != null)
        {
            liquid.SetColor(CurrentColor());
        }

        if (wrap != null && grabbing)
        {
            wrap.SetColor(CurrentColor());
        }
    }

    /// <summary>
    /// **物資をつかんでいるか**を教える。毎フレーム呼んでよい（変わったときだけ色を変える）。
    /// つかんだ瞬間は、先端から根元へ濃い色に染まっていく。
    /// </summary>
    public void SetGrabbing(bool value)
    {
        if (grabbing == value)
        {
            return;
        }

        grabbing = value;

        if (liquid != null)
        {
            liquid.ChangeColorFromTip(CurrentColor());
        }

        if (wrap != null)
        {
            if (!grabbing)
            {
                wrap.Release();
            }
            else if (hasTip)
            {
                Transform grabbed = FindGrabbedObject(tipPosition);

                if (grabbed != null)
                {
                    wrap.Play(grabbed, tipPosition, CurrentColor());
                }
            }
        }
    }

    /// <summary>
    /// **フックの先にくっついている物を探す**（囲う枠のため）。
    /// 他の人のフックでは「何をつかんだか」が届かないので、自分のときも同じく、フックの先の近くから探す。
    /// 物資（<see cref="HookableObject"/>）を優先し、無ければ近くの当たり判定から探す。
    /// </summary>
    private Transform FindGrabbedObject(Vector3 tip)
    {
        Transform best = null;
        float bestDistance = wrapSearchRadius;

        foreach (HookableObject hookable in HookableObject.All)
        {
            if (hookable == null)
            {
                continue;
            }

            Collider collider = hookable.GetComponent<Collider>();
            float distance = collider != null
                ? Vector3.Distance(collider.bounds.ClosestPoint(tip), tip)
                : Vector3.Distance(hookable.transform.position, tip);

            if (distance < bestDistance)
            {
                best = hookable.transform;
                bestDistance = distance;
            }
        }

        if (best != null)
        {
            return best;
        }

        // 物資でない物（引っぱれる大きな物など）。自分の体とフックの先は除く
        foreach (Collider collider in Physics.OverlapSphere(tip, wrapSearchRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(transform.root) || collider.GetComponentInParent<HookProjectile>() != null)
            {
                continue;
            }

            if (collider.bounds.size.magnitude > wrapMaxSize)
            {
                continue;
            }

            float distance = Vector3.Distance(collider.bounds.ClosestPoint(tip), tip);

            if (distance < bestDistance)
            {
                best = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// **フックの先の見た目（球）を隠す。** 液体のときは、流れの先端がフックの代わりになるため。
    /// 当たり判定はそのまま残す。液体でないときは何もしない。
    /// </summary>
    public void HideTip(GameObject tip)
    {
        if (!useLiquid || tip == null)
        {
            return;
        }

        foreach (Renderer renderer in tip.GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }
    }

    /// <summary>いま塗る色。つかんでいれば濃く、いなければ薄く。</summary>
    private Color CurrentColor()
    {
        Color.RGBToHSV(teamColor, out float hue, out float saturation, out float value);

        return grabbing
            ? Color.HSVToRGB(hue, vividSaturation, 1f)
            : Color.HSVToRGB(hue, saturation * paleSaturation, paleValue);
    }
}
