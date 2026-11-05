using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **床に寝かせて描く板（リング・狙いの帯・光の輪）の共通の道具。**（2026/10/27）
///
/// ・<see cref="UnitQuad"/> … 床に寝かせた 1m 四方の板（上向き。UV の縦が前）
/// ・<see cref="FallbackMaterial"/> … マテリアルが入っていないときに、シェーダーの名前から作る
/// </summary>
public static class SweeperGroundMesh
{
    private static Mesh unitQuad;
    private static readonly Dictionary<string, Material> Fallbacks = new Dictionary<string, Material>();

    /// <summary>床に寝かせた 1m 四方の板。中心が原点、UV の (0.5, 1) が前（+Z）。</summary>
    public static Mesh UnitQuad
    {
        get
        {
            if (unitQuad != null)
            {
                return unitQuad;
            }

            unitQuad = new Mesh { name = "SweeperUnitQuad" };
            unitQuad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f)
            };
            unitQuad.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            unitQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            unitQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            unitQuad.RecalculateBounds();
            return unitQuad;
        }
    }

    /// <summary>
    /// シェーダーの名前からマテリアルを作る（同じ名前なら使い回す）。
    /// **ビルドではシェーダーが入らないことがある**ので、本番ではマテリアルのファイルをインスペクターに入れておくこと。
    /// </summary>
    public static Material FallbackMaterial(string shaderName)
    {
        if (Fallbacks.TryGetValue(shaderName, out Material cached) && cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"シェーダー {shaderName} が見つかりません。マテリアルをインスペクターに入れてください。");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material made = new Material(shader) { name = shaderName + " (自動)" };
        Fallbacks[shaderName] = made;
        return made;
    }
}
