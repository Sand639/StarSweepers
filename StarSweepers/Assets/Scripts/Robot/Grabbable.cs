using UnityEngine;

/// <summary>
/// **ロボットが持てる物**に付ける印。
///
/// これが付いている物だけが、持ち上げの対象になる。
/// シーンに置いた箱などに付けて使う。
///
/// 持ち上げの処理そのものは <see cref="RobotGrabber"/> にある。
/// こちらは「持てる物かどうか」と、持ったときの見た目の調整だけを持つ。
///
/// **Rigidbody が必ず要る。** 持っている間は物理を止め（手に付けて運ぶ）、
/// 離したときに元の状態へ戻す、という作りのため。
///
/// 使っている物：ロボット分離の検証シーンの持てる箱、鍵（<see cref="DoorKey"/>）など。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Grabbable : MonoBehaviour
{
    [Header("持ったときの見え方")]
    [Tooltip("手の位置から、どれだけずらして持つか")]
    [SerializeField] private Vector3 holdOffset = Vector3.zero;

    [Tooltip("ONにすると、持ったときに向きをまっすぐ揃える")]
    [SerializeField] private bool straightenWhenHeld = true;

    [Header("持てるかどうか")]
    [Tooltip("OFFにすると、印は付いているが持てなくなる（重すぎる物を表現したいとき）")]
    [SerializeField] private bool canBeCarried = true;

    /// <summary>手の位置からのずれ。</summary>
    public Vector3 HoldOffset => holdOffset;

    /// <summary>持ったときに向きをまっすぐにするか。</summary>
    public bool StraightenWhenHeld => straightenWhenHeld;

    /// <summary>持ち上げられるか。</summary>
    public bool CanBeCarried => canBeCarried;

    /// <summary>いま誰かに持たれているか。**二重に持たれるのを防ぐ**ために使う。</summary>
    public bool IsHeld { get; private set; }

    /// <summary>持たれた／離された、を記録する。<see cref="RobotGrabber"/> から呼ばれる。</summary>
    public void SetHeld(bool held)
    {
        IsHeld = held;
    }
}
