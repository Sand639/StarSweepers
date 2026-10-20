using UnityEngine;

/// <summary>
/// **プレイヤーとフック（または物資）を結ぶ糸の見た目。**
///
/// いまは LineRenderer でまっすぐ1本引くだけ。
/// たるみを付けたり、別の表現に変えたくなったときに、ここだけ差し替えられるよう分けてある。
///
/// 使い方：LineRenderer の付いた空オブジェクトに付けて、HookController の Line に入れる。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class HookLine : MonoBehaviour
{
    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();

        // 手元とフックの先の2点をまっすぐ結ぶだけ。両端はワールド座標で渡される
        line.positionCount = 2;
        line.useWorldSpace = true;

        // 投げるまでは見せない（出し入れは HookController / FishingNetPlayer が毎フレーム決める）
        Show(false);
    }

    /// <summary>糸の両端を決める。a＝手元、b＝フックの先。</summary>
    public void SetEnds(Vector3 a, Vector3 b)
    {
        // インスペクターなどで点の数を変えられていても、2点に戻してから入れる
        if (line.positionCount != 2)
        {
            line.positionCount = 2;
        }
        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    /// <summary>糸を出す／隠す。</summary>
    public void Show(bool visible)
    {
        line.enabled = visible;
    }
}
