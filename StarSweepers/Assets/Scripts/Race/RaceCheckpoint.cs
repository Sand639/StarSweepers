using UnityEngine;

/// <summary>
/// **コースの途中に置く「通過点」。**
///
/// 番号順に通らないと1周と認められない。**近道の防止**になる。
/// **0番がスタート／ゴールの線**で、ここを通ると1周ぶん進む。
///
/// ## 当たり判定を使っていない理由
///
/// 当たり判定（トリガー）は、**速いと通り抜ける。**
/// 1フレームで何メートルも進むため、薄い板だと素通りしてしまう。
///
/// ここでは**「前のフレームの位置」から「今の位置」までの線が、この面を横切ったか**で調べている。
/// **どれだけ速くても取りこぼさない。**
/// </summary>
public class RaceCheckpoint : MonoBehaviour
{
    [Tooltip("**通る順番。** 0番がスタート／ゴールの線")]
    [SerializeField] private int order;

    [Tooltip("線の幅（メートル）。コースより少し広くしておく")]
    [SerializeField] private float width = 20f;

    [Tooltip("線の高さ（メートル）。飛び越えても取れる高さにしておく")]
    [SerializeField] private float height = 8f;

    [Tooltip("Gizmo（エディタ上の目印）の色")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.5f);

    /// <summary>通る順番。0番がスタート／ゴールの線。</summary>
    public int Order => order;

    /// <summary>この線がスタート／ゴールの線か。</summary>
    public bool IsStartLine => order == 0;

    /// <summary>この線が向いている方向（この向きに走り抜けると通過になる）。</summary>
    public Vector3 Forward => transform.forward;

    /// <summary>番号を入れ直す。コースを自動で作るときに使う。</summary>
    public void SetOrder(int value)
    {
        order = value;
    }

    /// <summary>線の大きさを入れ直す。コースを自動で作るときに使う。</summary>
    public void SetSize(float lineWidth, float lineHeight)
    {
        width = lineWidth;
        height = lineHeight;
    }

    /// <summary>
    /// **<paramref name="from"/> から <paramref name="to"/> へ動く間に、この線を横切ったか。**
    ///
    /// **正しい向き（手前から奥へ）に抜けたときだけ true。**
    /// 逆走して戻ってきても通過にならない。
    /// </summary>
    public bool WasCrossed(Vector3 from, Vector3 to)
    {
        Vector3 center = transform.position;
        Vector3 normal = transform.forward;

        float before = Vector3.Dot(from - center, normal);
        float after = Vector3.Dot(to - center, normal);

        // 手前（マイナス側）から奥（プラス側）へ抜けたときだけ通す
        if (before > 0f || after <= 0f)
        {
            return false;
        }

        // 面を横切った瞬間の位置を求める（t は from→to の道のりのうち、面に達した割合 0〜1）
        float t = before / (before - after);
        Vector3 hit = Vector3.Lerp(from, to, t) - center;

        if (Mathf.Abs(Vector3.Dot(hit, transform.right)) > width * 0.5f)
        {
            return false;
        }

        float up = Vector3.Dot(hit, transform.up);

        return up >= -height * 0.5f && up <= height * 0.5f;
    }

    /// <summary>エディタで線の位置と向きが分かるよう、半透明の板と、走る向きの矢印を描く。</summary>
    private void OnDrawGizmos()
    {
        // この線の位置・向きを基準にして描く（以降の座標は、線から見たもの）
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        // 板の厚み 0.2m は見た目だけ。通過の判定は厚みの無い面で行う（WasCrossed）
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(Vector3.zero, new Vector3(width, height, 0.2f));

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, 0.2f));

        // 走る向きを矢印で示す（長さ 3m）
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 3f);
        Gizmos.DrawLine(Vector3.forward * 3f, Vector3.forward * 2f + Vector3.right * 0.8f);
        Gizmos.DrawLine(Vector3.forward * 3f, Vector3.forward * 2f - Vector3.right * 0.8f);

        Gizmos.matrix = Matrix4x4.identity;
    }
}
