using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 隕石の落下範囲（<see cref="MeteorArea"/>）が、**攻撃円が範囲からはみ出さない落下点**を返すかを確かめるテスト。
///
/// `TryGetPoint(半径, 0～1のX, 0～1のZ)` は、範囲の縁から半径ぶん内側に寄せた中で点を選ぶ。
/// ここでは、拡大・回転した範囲／円が入りきらない範囲／0～1の外の値、の3つの場合を見る。
/// </summary>
public class MeteorAreaTests
{
    /// <summary>テストごとに作る、範囲を付けた物。終わったら <see cref="TearDown"/> で消す。</summary>
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// X だけ2倍に拡大し、30度回した範囲で、半径2m・(0, 1) を指定したら →
    /// 半径は「ワールドの長さ」として扱われ、X はローカルで 1、Z は 2 だけ内側に寄った点になるはず。
    /// </summary>
    [Test]
    public void TryGetPoint_InsetsWorldRadiusFromScaledRotatedBoxEdges()
    {
        // 大きさ 10×8、中心 (1, 0, -2)、X だけ2倍・Y軸で30度回した範囲
        MeteorArea area = CreateArea(
            new Vector3(10f, 0.2f, 8f),
            new Vector3(1f, 0f, -2f),
            new Vector3(2f, 1f, 1f),
            Quaternion.Euler(0f, 30f, 0f));

        bool result = area.TryGetPoint(2f, 0f, 1f, out Vector3 worldPoint);

        Assert.That(result, Is.True);
        // 回転と拡大を外して、範囲の中の座標で比べる。
        // X：半幅 5 − (半径2m ÷ 拡大率2) = 4 → 左端 1 − 4 = -3。
        // Y：上面の高さ（0.2 の半分）= 0.1。Z：半幅 4 − 2 = 2 → 奥端 -2 + 2 = 0
        Vector3 localPoint = root.transform.InverseTransformPoint(worldPoint);
        Assert.That(localPoint.x, Is.EqualTo(-3f).Within(0.0001f));
        Assert.That(localPoint.y, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(localPoint.z, Is.EqualTo(0f).Within(0.0001f));
    }

    /// <summary>
    /// 幅4m の範囲に半径2m の円を落とそうとしたら → 内側に寄せる余地が 0 になるので、false が返るはず
    /// （ちょうど同じ大きさでも、丸め誤差で外へ出やすいため使わない決まり）。
    /// </summary>
    [Test]
    public void TryGetPoint_ReturnsFalseWhenImpactCircleCannotFit()
    {
        MeteorArea area = CreateArea(
            new Vector3(4f, 0.2f, 3f),
            Vector3.zero,
            Vector3.one,
            Quaternion.identity);

        bool result = area.TryGetPoint(2f, 0.5f, 0.5f, out _);

        Assert.That(result, Is.False);
    }

    /// <summary>
    /// 0～1 の外の値（-5 と 8）を渡したら → 0 と 1 に切り詰められ、
    /// 円がはみ出さない範囲の端（10m 四方・半径2m なら ±3m）に落ちるはず。
    /// </summary>
    [Test]
    public void TryGetPoint_ClampsNormalizedCoordinatesInsideSafeRange()
    {
        MeteorArea area = CreateArea(
            new Vector3(10f, 0.2f, 10f),
            Vector3.zero,
            Vector3.one,
            Quaternion.identity);

        Assert.That(area.TryGetPoint(2f, -5f, 8f, out Vector3 point), Is.True);

        Assert.That(point.x, Is.EqualTo(-3f).Within(0.0001f));
        Assert.That(point.z, Is.EqualTo(3f).Within(0.0001f));
    }

    /// <summary>
    /// トリガーの BoxCollider（範囲の形）と <see cref="MeteorArea"/> を付けた物を作る。
    /// 作った物は <see cref="root"/> に覚えておき、テストの後で消す。
    /// </summary>
    private MeteorArea CreateArea(Vector3 size, Vector3 center, Vector3 scale, Quaternion rotation)
    {
        root = new GameObject("MeteorAreaTest");
        root.transform.localScale = scale;
        root.transform.rotation = rotation;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = size;
        box.center = center;
        box.isTrigger = true;

        return root.AddComponent<MeteorArea>();
    }
}
