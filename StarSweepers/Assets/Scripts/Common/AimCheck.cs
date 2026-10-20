using UnityEngine;

/// <summary>
/// **「それを狙えているか」を判断する共通の道具。**
///
/// 物を持つ・ロープにつかまる・扉を開ける、のどれも同じ判断をするので、ここにまとめてある。
///
/// **判断のしかたは2通り。** 呼ぶ側がどちらかを選ぶ。
///
/// | 方法 | 中心 | 向いている相手 |
/// | <see cref="IsAimedFromCamera"/> | **カメラ** | **細長い物**（ロープなど）。画面のどこに映っているかとほぼ同じ |
/// | <see cref="IsAimed"/> | **プレイヤーの頭** | **手を伸ばして触る物**（箱・扉など） |
///
/// カメラ中心のほうは**見えているとおり**に決まるので、
/// **自分の体の裏にある物でも、画面の中央に映っていれば狙える。**
///
/// ## 角度で判断するときのしかた
///
/// **プレイヤーの頭を中心にして、そこから2本の向きへ角度を広げる。**
/// **どちらかの範囲に入っていれば「狙えている」**とみなす。
///
/// | 広げる向き | これがあると何ができるか |
/// | **カメラの向き**（上下の傾きを含む） | **見上げた先・見下ろした先**の物を狙える |
/// | **その水平の向き**（傾きを外したもの） | **足元や、同じ高さの物**を、いちいち見下ろさずに狙える |
///
/// ## なぜ「頭が中心」なのか
///
/// **カメラを中心にすると、三人称のときに感覚が合わない。**
/// カメラは体の数メートル後ろにあるので、
/// **体のすぐ横にある物が「正面にある」と判断されてしまう。**
///
/// **体の向きを中心にするのも合わない。**
/// 体の向きは常に水平なので、**見上げた先の物にどうしても届かない**
/// （角度をいくら広げても、真上は90度を超えてしまう）。
///
/// **頭を中心にして、カメラの向きで広げる**のが、見た目と一番合う。
/// </summary>
public static class AimCheck
{
    /// <summary>
    /// 「ほぼ同じ位置」とみなす距離の2乗（0.01m = 1cm の2乗）。
    /// これより近いと向きが決まらないので、角度の計算をしない。
    /// </summary>
    private const float SameSpotSqrDistance = 0.0001f;

    /// <summary>一度見つけたカメラを覚えておく（毎フレーム `Camera.main` で探すと重いため）。</summary>
    private static Camera cachedCamera;

    /// <summary>
    /// 狙いに使うカメラ。**画面の中央＝レティクルの位置**を表す。
    /// 見つからないときは null。
    /// </summary>
    public static Camera ActiveCamera
    {
        get
        {
            // 破棄されたら（シーンを切り替えたときなど）探し直す
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
            }

            return cachedCamera;
        }
    }

    /// <summary>
    /// **その場所を狙えているか。**
    ///
    /// `origin` は**プレイヤーの頭**、`target` は狙っている場所。
    /// `fallbackDirection` はカメラが見つからないときに使う向き（体の正面など）。
    /// </summary>
    public static bool IsAimed(Vector3 origin, Vector3 target, float maxAngle, Vector3 fallbackDirection)
    {
        Camera camera = ActiveCamera;
        Vector3 look = camera != null ? camera.transform.forward : fallbackDirection;

        return IsWithinAngle(origin, target, maxAngle, look, true);
    }

    /// <summary>
    /// **画面に映っている位置が、レティクルから何ピクセル離れているか**で判断する。
    ///
    /// **計算に頭やプレイヤーの位置は使わない。**
    /// **画面に見えているとおり**の距離をそのまま測るので、
    /// 「レティクルに重なって見えていれば届く」という、見た目どおりの判定になる。
    ///
    /// カメラが見つからないときは false（呼んだ側で角度の判定に切り替えること）。
    /// </summary>
    public static bool IsAimedFromCamera(
        Vector3 target, float maxAngle, Vector3 fallbackOrigin, Vector3 fallbackDirection)
    {
        Camera camera = ActiveCamera;

        if (camera == null)
        {
            return IsWithinAngle(fallbackOrigin, target, maxAngle, fallbackDirection, false);
        }

        return AngleFromCamera(target) <= maxAngle;
    }

    /// <summary>
    /// **その場所が、画面の中央から何度ずれた向きにあるか。**
    /// カメラから見た角度なので、**画面のどのあたりに映っているか**とほぼ同じ意味になる。
    ///
    /// カメラが見つからないときは、とても大きな数を返す。
    /// </summary>
    public static float AngleFromCamera(Vector3 target)
    {
        Camera camera = ActiveCamera;

        if (camera == null)
        {
            return float.MaxValue;
        }

        Vector3 toTarget = target - camera.transform.position;

        // カメラとほぼ同じ位置にある物は「真正面」扱い
        if (toTarget.sqrMagnitude < SameSpotSqrDistance)
        {
            return 0f;
        }

        return Vector3.Angle(camera.transform.forward, toTarget);
    }

    /// <summary>
    /// **間に壁が無いか。** 手を伸ばして触る物（箱・扉）で使う。
    ///
    /// これまでは角度と距離しか見ていなかったので、**壁の向こうの物を持てて、扉も開けられた**
    /// （`リスクリスト.md` 2026/9/7 登録）。
    ///
    /// - **自分の体**（<paramref name="ignoreRoot"/> の子）は数えない
    /// - **狙っている相手そのもの**（<paramref name="targetRoot"/> の子）も数えない。
    ///   相手の表面に当たって「遮られている」と判断されてしまうため
    /// - **ガラスのように透けて見える物**（<see cref="SeeThrough"/>）は数えない
    /// - すり抜け用の当たり判定（トリガー）も数えない
    /// </summary>
    public static bool HasClearPath(Vector3 origin, Vector3 target, Transform ignoreRoot, Transform targetRoot)
    {
        Vector3 toTarget = target - origin;
        float distance = toTarget.magnitude;

        // ほとんど同じ位置なら、遮る余地がない
        if (distance < 0.01f)
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(
            origin, toTarget / distance, pathHits, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = pathHits[i].collider;

            if (hit == null)
            {
                continue;
            }

            if (ignoreRoot != null && hit.transform.IsChildOf(ignoreRoot))
            {
                continue;
            }

            if (targetRoot != null && hit.transform.IsChildOf(targetRoot))
            {
                continue;
            }

            if (SeeThrough.Is(hit))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// 間に何があるかを調べるときの入れ物。毎回作り直さないよう使い回している。
    /// 一度に数えるのは16個まで（それ以上の物が一直線に重なることはまず無い）。
    /// </summary>
    private static readonly RaycastHit[] pathHits = new RaycastHit[16];

    /// <summary>
    /// 指定した向きで調べる。
    /// `includeFlat` が true なら、**その水平の向き**でも調べ、どちらかに入っていれば true。
    /// </summary>
    private static bool IsWithinAngle(
        Vector3 origin, Vector3 target, float maxAngle, Vector3 look, bool includeFlat)
    {
        Vector3 toTarget = target - origin;

        // 頭とほぼ同じ位置にあるものは、向きが決まらないので狙えている扱いにする
        if (toTarget.sqrMagnitude < SameSpotSqrDistance)
        {
            return true;
        }

        // 向きそのものが無い（長さ0）なら、判断のしようがない
        if (look.sqrMagnitude < SameSpotSqrDistance)
        {
            return false;
        }

        if (Vector3.Angle(look, toTarget) <= maxAngle)
        {
            return true;
        }

        if (!includeFlat)
        {
            return false;
        }

        // 上下の傾きを外した「水平の向き」でも調べる
        Vector3 flat = look;
        flat.y = 0f;

        // 真上・真下を向いているときは水平の向きが無いので、こちらでは判断しない
        if (flat.sqrMagnitude < SameSpotSqrDistance)
        {
            return false;
        }

        return Vector3.Angle(flat, toTarget) <= maxAngle;
    }
}
