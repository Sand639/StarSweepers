using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **つかまって移動できるロープ。**
/// （VALORANT のマップにある、上下に移動できる紐と同じ役割）
///
/// この部品を付けたゲームオブジェクトの位置が**ロープの根元**になる。
/// そこから `height` の分だけ**この物の上向き**へ伸びているものとして扱う。
///
/// つまり、**置いたときの傾きで種類が決まる。**
///
/// - **そのまま置けば「縦のロープ」**… 上り下りする。体は横にぶら下がる
/// - **倒して置けば「横のロープ」**… 左右に伝っていく。**体は下にぶら下がる**（うんてい）
/// - 斜めに置けば、その中間の動きになる
///
/// 縦と横の切り替えは<see cref="IsHorizontal"/>で**自動で決まる**ので、設定は要らない。
///
/// ## 当たり判定を持たない
///
/// ロープは**当たり判定（Collider）を使っていない。**
/// 見つけ方は「近くにある物を物理で探す」のではなく、
/// **自分自身を <see cref="All"/> に登録しておいて、そこから探す**方式にしてある。
///
/// 理由は2つ。
///
/// - ロープに体がぶつかって、つかまる前に押し返されるのを防ぐため
/// - ステージに置くロープはせいぜい数本なので、探すのが軽いため
///
/// 上り下りする側の処理は <see cref="RobotRopeClimber"/> にある。
/// </summary>
public class RobotRope : MonoBehaviour
{
    [Header("長さ")]
    [Tooltip("根元から先端までの長さ（メートル）。この物の位置が根元になる")]
    [Range(1f, 50f)]
    [SerializeField] private float height = 6f;

    [Header("つかまり方")]
    [Tooltip("**縦のロープのとき**、ロープからどれだけ離れてぶら下がるか（メートル）。" +
             "体がロープにめり込んで見えないようにする")]
    [Range(0f, 2f)]
    [SerializeField] private float hangDistance = 0.55f;

    [Tooltip("**横のロープのとき**、手からどれだけ下にぶら下がるか（メートル）。" +
             "0なら手がロープにぴったり付く")]
    [Range(0f, 2f)]
    [SerializeField] private float hangBelow = 0.15f;

    [Tooltip("根元から、ここまでは進めない余白（メートル）。0なら端ぴったりまで行ける")]
    [Range(0f, 5f)]
    [SerializeField] private float bottomMargin = 0f;

    [Tooltip("先端から、ここまでは進めない余白（メートル）。頭が天井や壁に刺さるのを防ぐ")]
    [Range(0f, 5f)]
    [SerializeField] private float topMargin = 0.2f;

    [Header("見た目")]
    [Tooltip("長さに合わせて自動で伸び縮みさせる筒。Unityの「Cylinder」を入れる。空でもよい")]
    [SerializeField] private Transform visual;

    /// <summary>
    /// いまシーンに出ているロープの一覧。
    /// **探すときはここを見る。**（当たり判定を使わないため）
    /// </summary>
    public static readonly List<RobotRope> All = new List<RobotRope>();

    /// <summary>ロープが伸びていく向き。この物の上向き。</summary>
    public Vector3 Direction => transform.up;

    /// <summary>ロープの下端。この物の位置そのもの。</summary>
    public Vector3 Bottom => transform.position;

    /// <summary>ロープの上端。</summary>
    public Vector3 Top => Bottom + Direction * height;

    /// <summary>ロープからどれだけ離れてぶら下がるか（縦のロープのとき）。</summary>
    public float HangDistance => hangDistance;

    /// <summary>手からどれだけ下にぶら下がるか（横のロープのとき）。</summary>
    public float HangBelow => hangBelow;

    /// <summary>
    /// **横向きのロープか**（うんていのように伝っていくタイプか）。
    ///
    /// 置いたときの傾きだけで決めている。**45度より寝ていれば横向き。**
    /// 設定項目にしていないのは、**見た目と動きが食い違う事故を防ぐため。**
    /// 横に倒して置いたのに上下移動になる、ということが起きない。
    /// </summary>
    public bool IsHorizontal => Mathf.Abs(Vector3.Dot(Direction, Vector3.up)) < Cos45Degrees;

    /// <summary>
    /// cos 45度（約0.707）。ロープの向きと真上との内積がこれより小さい＝**真上から45度より倒れている。**
    /// 逆さまに置いたときも同じに扱えるよう、内積は絶対値で比べている。
    /// </summary>
    private const float Cos45Degrees = 0.707f;

    /// <summary>つかまれる一番下の高さ（下端からの距離）。</summary>
    public float MinHold => Mathf.Min(bottomMargin, height);

    /// <summary>つかまれる一番上の高さ（下端からの距離）。</summary>
    public float MaxHold => Mathf.Max(MinHold, height - topMargin);

    private void OnEnable()
    {
        // 出ている間だけ一覧に載せる。止めたロープは、つかまる側から見つからなくなる
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// 長さを変えたときに、見た目も合わせて伸ばす。
    ///
    /// Unityの筒（Cylinder）は**高さ2・中心が原点**なので、
    /// 大きさは長さの半分にし、位置も半分だけ持ち上げている。
    /// </summary>
    private void OnValidate()
    {
        if (visual == null)
        {
            return;
        }

        Vector3 scale = visual.localScale;

        visual.localPosition = new Vector3(0f, height * 0.5f, 0f);
        visual.localScale = new Vector3(scale.x, height * 0.5f, scale.z);
    }

    /// <summary>下端から `distance` メートル上がった所の座標を返す。</summary>
    public Vector3 PointAt(float distance)
    {
        return Bottom + Direction * distance;
    }

    /// <summary>
    /// ある座標が、ロープの**どのくらいの高さ**にあたるかを返す（下端からの距離）。
    /// ロープの外側にある場合は、はみ出した値がそのまま返る。
    /// </summary>
    public float HeightOf(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - Bottom, Direction);
    }

    /// <summary>ある座標から一番近い、ロープ上の点を返す。</summary>
    public Vector3 ClosestPoint(Vector3 worldPosition)
    {
        return PointAt(Mathf.Clamp(HeightOf(worldPosition), 0f, height));
    }

    /// <summary>シーンビューにロープの線を描く。置いたときに長さが分かるように。</summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.35f);
        Gizmos.DrawLine(Bottom, Top);

        // つかまれる範囲を、少しだけ太い線で重ねて描く
        // （実際には、つかまれる一番下と一番上に、半径 0.08メートルの半透明の球を置いて示している）
        Gizmos.color = new Color(1f, 0.85f, 0.35f, 0.5f);
        Gizmos.DrawSphere(PointAt(MinHold), 0.08f);
        Gizmos.DrawSphere(PointAt(MaxHold), 0.08f);
    }
}
