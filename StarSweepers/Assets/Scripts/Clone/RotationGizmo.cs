using UnityEngine;

/// <summary>
/// 「いまどの向きに回るか」を示す輪。
///
/// 回転キーを押している間だけ、その軸の輪が出る。
/// 色は3D作業で一般的な決まりに合わせている（X＝赤 / Y＝緑 / Z＝青）。
///
/// ゲーム画面に映る必要があるため、Unityの Gizmo 機能ではなく
/// 実際の線（LineRenderer）で描いている。
/// </summary>
public class RotationGizmo : MonoBehaviour
{
    /// <summary>輪1つを作る点の数。多いほど滑らかな円になる。</summary>
    private const int SegmentCount = 48;

    private LineRenderer ringX;
    private LineRenderer ringY;
    private LineRenderer ringZ;

    /// <summary>回転を示す輪を作って返す。</summary>
    public static RotationGizmo Create(Transform parent)
    {
        GameObject root = new GameObject("RotationGizmo");
        root.transform.SetParent(parent, false);

        RotationGizmo gizmo = root.AddComponent<RotationGizmo>();
        gizmo.Build();
        gizmo.SetVisible(false, false, false);

        return gizmo;
    }

    /// <summary>3つの輪を作る。色は X＝赤 / Y＝緑 / Z＝青。</summary>
    private void Build()
    {
        // X軸で回る＝YZ平面の輪、という関係になる
        ringX = CreateRing("RingX", Vector3.right, new Color(1f, 0.35f, 0.35f));
        ringY = CreateRing("RingY", Vector3.up, new Color(0.4f, 1f, 0.5f));
        ringZ = CreateRing("RingZ", Vector3.forward, new Color(0.45f, 0.7f, 1f));
    }

    /// <summary>
    /// 指定した軸のまわりを一周する輪を作る。
    /// axis が回転の軸で、その軸に垂直な面に円を描く。
    /// </summary>
    private LineRenderer CreateRing(string name, Vector3 axis, Color color)
    {
        GameObject ringObject = new GameObject(name);
        ringObject.transform.SetParent(transform, false);

        LineRenderer line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = CreateLineMaterial(color);

        // 軸に垂直な2方向を求めて、その2つを組み合わせて円を描く
        Vector3 first = Vector3.Cross(axis, Vector3.up);
        if (first.sqrMagnitude < 0.001f)
        {
            first = Vector3.Cross(axis, Vector3.forward);
        }

        first.Normalize();
        Vector3 second = Vector3.Cross(axis, first).normalized;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i / (float)SegmentCount * Mathf.PI * 2f;
            Vector3 point = first * Mathf.Cos(angle) + second * Mathf.Sin(angle);
            line.SetPosition(i, point);
        }

        return line;
    }

    /// <summary>光の影響を受けない、色だけの線のマテリアル。URP のシェーダーが無ければ Sprites/Default に逃がす。</summary>
    private static Material CreateLineMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        return new Material(shader) { color = color };
    }

    /// <summary>どの輪を出すかを決める。</summary>
    public void SetVisible(bool showX, bool showY, bool showZ)
    {
        if (ringX != null)
        {
            ringX.gameObject.SetActive(showX);
        }

        if (ringY != null)
        {
            ringY.gameObject.SetActive(showY);
        }

        if (ringZ != null)
        {
            ringZ.gameObject.SetActive(showZ);
        }
    }

    /// <summary>輪の位置・向き・大きさを合わせる。</summary>
    public void Place(Vector3 position, Quaternion rotation, float radius)
    {
        transform.SetPositionAndRotation(position, rotation);

        // 大きさを変えると輪の半径も変わる
        transform.localScale = Vector3.one * radius;

        // 遠くでも近くでも見やすいよう、太さを半径に合わせる（半径の 3.5%。最低 1cm）
        float width = Mathf.Max(0.01f, radius * 0.035f);
        SetWidth(ringX, width);
        SetWidth(ringY, width);
        SetWidth(ringZ, width);
    }

    /// <summary>線の太さを、世界の単位（メートル）で指定する。</summary>
    private static void SetWidth(LineRenderer line, float width)
    {
        if (line != null)
        {
            // 親を拡大しているぶん、太さは割り戻す
            float scale = line.transform.lossyScale.x;
            line.widthMultiplier = scale > 0.0001f ? width / scale : width;
        }
    }
}
