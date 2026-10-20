using System;
using UnityEngine;

/// <summary>
/// **いまの重力の強さを、どこからでも見られるようにしたもの。**
///
/// 変えるのは <c>GravityShifter</c>（`Assets/Scripts/Stage/GravityShifter.cs`）。
/// ここは**今いくつか**を持っているだけ。
///
/// ## なぜ Physics.gravity だけでは足りないか
///
/// Unityには最初から `Physics.gravity`（初期値 -9.81）がある。
/// **ただし、これが効くのは物理演算（Rigidbody）で動く物だけ。**
///
/// このプロジェクトの**人・ロボット・カートは `CharacterController`** で動いていて、
/// 落ちる速さは**それぞれのスクリプトが自分で計算している。**
/// `Physics.gravity` を変えても、**人は同じ速さで落ちてくる。**
///
/// そこでこの入れ物を1つ置き、
/// **落ちる計算をしている場所が、みんなここを見る**形にした。
/// `Physics.gravity` のほうも一緒に変えているので、**転がる物も同じように軽くなる。**
/// </summary>
public static class WorldGravity
{
    /// <summary>地球の重力（下向き）。**この値のときが「ふつう」。**</summary>
    public const float Standard = -9.81f;

    /// <summary>いまの重力（下向きなので**マイナスの値**）。</summary>
    public static float Value { get; private set; } = Standard;

    /// <summary>
    /// **ふつうを1としたときの、いまの強さ。**
    ///
    /// 月（-1.62）なら約0.17、地球なら1、2倍の重さなら2。
    /// 落ちる計算をしている場所は、**自分の重力にこれを掛ける**だけでよい。
    /// </summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>**誰かが重力をいじっているか。** 画面に出すかどうかの判断に使う。</summary>
    public static bool IsControlled { get; private set; }

    /// <summary>重力が変わったときに呼ばれる。渡ってくるのは新しい値。</summary>
    public static event Action<float> Changed;

    /// <summary>
    /// 重力を入れ替える。
    /// <paramref name="value"/> は**下向きなのでマイナス**（地球は -9.81）。
    /// </summary>
    public static void Set(float value)
    {
        IsControlled = true;

        if (Mathf.Approximately(Value, value))
        {
            return;
        }

        Value = value;
        Scale = value / Standard;

        // 転がる物・落ちる物（Rigidbody）にも効かせる
        Physics.gravity = new Vector3(0f, value, 0f);

        Changed?.Invoke(value);
    }

    /// <summary>ふつうの重力に戻す。**シーンを抜けるときは必ず呼ぶこと。**</summary>
    public static void Release()
    {
        IsControlled = false;
        Set(Standard);

        // Set() の中で「いじっている」印が付くので、戻し終わったあとにもう一度外す
        IsControlled = false;
    }

    /// <summary>
    /// 再生を始めるたびに、ふつうの重力から始める。
    ///
    /// **この値はゲームを通してひとつしかない**ので、
    /// 月の重力のまま再生を止めると、次に始めたときも月のままになりかねない。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnStart()
    {
        IsControlled = false;
        Value = Standard;
        Scale = 1f;
        Physics.gravity = new Vector3(0f, Standard, 0f);
    }
}
