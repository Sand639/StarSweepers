using UnityEngine;

/// <summary>
/// **宇宙船の素材の種類（5種類）。** 3〜4番目は 2026/10/6 に増やした（大槻さんの依頼。STAGE_02 用）。
///
/// **マップで使う種類は、そのマップのスポナーに入っているプレハブで決まる**（3種類のマップでは、4・5番目は出ない）。
/// 種類集めのルール（Collect）では、**そのマップで使う種類を全部**そろえたらラウンド勝利。
///
/// 種類を増やしたくなったら、ここに足して <see cref="SpaceJunkMaterials.Count"/> を見直す。
/// 名前・色・色の呼び名の並びも一緒に足すこと。
/// </summary>
public enum SpaceJunkMaterialKind
{
    /// <summary>装甲板</summary>
    Plate = 0,

    /// <summary>回路基板</summary>
    Circuit = 1,

    /// <summary>燃料タンク</summary>
    Fuel = 2,

    /// <summary>アンテナ（2026/10/6 追加）</summary>
    Antenna = 3,

    /// <summary>エンジン（2026/10/6 追加）</summary>
    Engine = 4
}

/// <summary>
/// 素材の種類ごとの、名前と色をまとめた場所。画面表示・素材の見た目・ゴールの表示に使う。
///
/// **色は、チームの色（青・赤・緑・黄）と見分けがつくものを選んである。**
/// 素材の色とチームの色が似ていると、「誰の物か」と「何の素材か」が混ざって読めなくなるため。
/// </summary>
public static class SpaceJunkMaterials
{
    /// <summary>素材の種類の数（全部のマップを合わせて）。マップで使う種類は <see cref="IsUsed"/> で調べる。</summary>
    public const int Count = 5;

    private static readonly string[] Names = { "装甲板", "回路基板", "燃料タンク", "アンテナ", "エンジン" };

    private static readonly Color[] Colors =
    {
        new Color(0.90f, 0.90f, 0.93f), // 装甲板 … 白
        new Color(0.62f, 0.35f, 0.88f), // 回路基板 … 紫
        new Color(1.00f, 0.62f, 0.10f), // 燃料タンク … 橙
        new Color(1.00f, 0.45f, 0.75f), // アンテナ … ピンク
        new Color(0.32f, 0.32f, 0.36f), // エンジン … 灰
    };

    /// <summary>種類の名前。</summary>
    public static string Name(SpaceJunkMaterialKind kind)
    {
        return Names[Mathf.Clamp((int)kind, 0, Names.Length - 1)];
    }

    private static readonly string[] ColorNames = { "白", "紫", "橙", "ピンク", "灰" };

    /// <summary>種類の色の呼び名（画面の表示に使う。例：「紫」）。</summary>
    public static string ColorName(SpaceJunkMaterialKind kind)
    {
        return ColorNames[Mathf.Clamp((int)kind, 0, ColorNames.Length - 1)];
    }

    /// <summary>種類の色。</summary>
    public static Color Color(SpaceJunkMaterialKind kind)
    {
        return Colors[Mathf.Clamp((int)kind, 0, Colors.Length - 1)];
    }

    /// <summary>番号から種類へ。スポナーがランダムに選ぶときに使う。</summary>
    public static SpaceJunkMaterialKind FromIndex(int index)
    {
        return (SpaceJunkMaterialKind)Mathf.Clamp(index, 0, Count - 1);
    }

    /// <summary>
    /// **いまのマップで出る種類か。** マップのスポナー（<see cref="SpaceJunkSpawner"/>）に入っているプレハブで決まる。
    /// スポナーが無ければ、全部の種類を「出る」とみなす。
    /// </summary>
    public static bool IsUsed(SpaceJunkMaterialKind kind)
    {
        SpaceJunkSpawner spawner = SpaceJunkSpawner.Current;
        return spawner == null || spawner.UsesKind(kind);
    }

    /// <summary>
    /// **いまのマップで出る種類から、ランダムに1つ選んだ番号。**
    /// イベント（お題・高価値の種類）が、出ない種類を選ばないようにするため。
    /// </summary>
    public static int RandomUsedIndex()
    {
        int usedCount = 0;
        for (int i = 0; i < Count; i++)
        {
            if (IsUsed(FromIndex(i)))
            {
                usedCount++;
            }
        }

        if (usedCount == 0)
        {
            return Random.Range(0, Count);
        }

        int pick = Random.Range(0, usedCount);
        for (int i = 0; i < Count; i++)
        {
            if (IsUsed(FromIndex(i)))
            {
                if (pick == 0)
                {
                    return i;
                }
                pick--;
            }
        }

        return 0;
    }
}

/// <summary>
/// **「この物は宇宙船の素材で、種類はこれ」という目印。**
///
/// フックで引っ掛けられるようにする目印（<c>HookableObject</c>）は**釣りと共用**しているので、
/// こちらは**種類だけ**を持つ。素材のプレハブは種類ごとに1つずつ作るので、
/// 種類は最初から決まっていて、通信で送る必要がない。
///
/// 使い方：素材のプレハブに <c>HookableObject</c> と一緒に付ける。
/// </summary>
public class SpaceJunkMaterial : MonoBehaviour
{
    [Header("この素材の種類")]
    [Tooltip("ゴールに入れたときに、どの種類として数えるか")]
    [SerializeField] private SpaceJunkMaterialKind kind = SpaceJunkMaterialKind.Plate;

    [Header("見た目")]
    [Tooltip("種類の色に塗る見た目。**空にしておけば色を変えない**（自分でマテリアルを作ったとき用）")]
    [SerializeField] private Renderer[] tintRenderers;

    /// <summary>この素材の種類。</summary>
    public SpaceJunkMaterialKind Kind => kind;

    /// <summary>画面に出す名前。</summary>
    public string KindName => SpaceJunkMaterials.Name(kind);

    private void Start()
    {
        ApplyColor();
    }

    /// <summary>種類の色を見た目に塗る。</summary>
    private void ApplyColor()
    {
        if (tintRenderers == null)
        {
            return;
        }

        Color color = SpaceJunkMaterials.Color(kind);

        foreach (Renderer renderer in tintRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }
}
