using UnityEngine;

/// <summary>
/// **電撃の「形」を作る計算**をまとめたもの。体のまわりの電撃（<see cref="HelixLightning"/>）と
/// ビーム（<see cref="HelixBeam"/>）の両方で使う。
///
/// 1. 目印の点のあいだを**エルミート曲線**でなめらかにつなぐ（<see cref="SampleHermite"/>）
/// 2. できた線を、進む向きと直角の方向へランダムにずらして**ギザギザ**にする（<see cref="Jag"/>）
/// </summary>
public static class LightningShape
{
    /// <summary>目印の点が <paramref name="controlCount"/> 個のとき、補間後の点がいくつになるか。</summary>
    public static int PointCount(int controlCount, int samplesPerSegment)
    {
        return (controlCount - 1) * Mathf.Max(1, samplesPerSegment) + 1;
    }

    /// <summary>
    /// 目印の点（先頭から <paramref name="controlCount"/> 個）をエルミート曲線でつなぎ、<paramref name="output"/> に書く。
    /// 書いた点の数を返す。
    /// </summary>
    public static int SampleHermite(Vector3[] controlPoints, int controlCount, int samplesPerSegment, Vector3[] output)
    {
        int segments = controlCount - 1;
        int samples = Mathf.Max(1, samplesPerSegment);
        int index = 0;

        // 各区間は始点を含み、終点は含めない（次の区間の始点と同じ点になるため）
        for (int s = 0; s < segments; s++)
        {
            Vector3 p0 = controlPoints[s];
            Vector3 p1 = controlPoints[s + 1];
            Vector3 m0 = Tangent(controlPoints, controlCount, s);
            Vector3 m1 = Tangent(controlPoints, controlCount, s + 1);

            // 最後の区間だけ終点も含める
            int count = s == segments - 1 ? samples + 1 : samples;

            for (int k = 0; k < count; k++)
            {
                output[index++] = Hermite(p0, p1, m0, m1, k / (float)samples);
            }
        }

        return index;
    }

    /// <summary>
    /// 線の各点を、進む向きと直角の方向へランダムにずらしてギザギザにする。
    /// 端はずらさず、真ん中ほど大きくずらす（両端が細く消えていくように見える）。
    /// </summary>
    public static void Jag(Vector3[] points, int count, float jitter)
    {
        for (int i = 1; i < count - 1; i++)
        {
            // envelope は両端で 0、真ん中で 1 の山形。sin だけだと端の近くが細すぎるので、平方根で山をなだらかにしている
            float t = i / (float)(count - 1);
            float envelope = Mathf.Sqrt(Mathf.Sin(t * Mathf.PI));

            // 前後の点を結んだ向きを「進む向き」とし、それと直角な面の中でランダムにずらす
            // （進む向きにずらしても線の形はほとんど変わらないため）
            Vector3 forward = (points[i + 1] - points[i - 1]).normalized;
            Vector3 offset = Vector3.ProjectOnPlane(Random.insideUnitSphere, forward);

            // insideUnitSphere の長さは 0〜1 なので、ずれは最大で jitter の 2 倍、平均するとおよそ jitter
            points[i] += offset * (jitter * 2f * envelope);
        }
    }

    /// <summary>
    /// エルミート曲線。p0 から p1 へ、それぞれの点で向き m0・m1 を持つなめらかな曲線上の点を返す（s は 0〜1）。
    /// </summary>
    public static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float s)
    {
        float s2 = s * s;
        float s3 = s2 * s;

        return (2f * s3 - 3f * s2 + 1f) * p0
             + (s3 - 2f * s2 + s) * m0
             + (-2f * s3 + 3f * s2) * p1
             + (s3 - s2) * m1;
    }

    /// <summary>
    /// 目印の点での曲線の向き（接線）。前後の点から求める（Catmull-Rom と同じ求め方）。
    /// 両端は、となりの点との差をそのまま使う。
    /// </summary>
    private static Vector3 Tangent(Vector3[] points, int count, int i)
    {
        if (i == 0)
        {
            return points[1] - points[0];
        }

        if (i == count - 1)
        {
            return points[i] - points[i - 1];
        }

        return (points[i + 1] - points[i - 1]) * 0.5f;
    }
}
