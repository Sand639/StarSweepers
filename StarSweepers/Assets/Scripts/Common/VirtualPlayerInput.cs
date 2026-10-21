using UnityEngine;

/// <summary>
/// **ボット（NPC）が「押す」ための、目に見えないコントローラー。** （2026/10/21・大槻さんの依頼）
///
/// プレイヤーの体にこれを付けると、その体の操作スクリプト（移動・狙い・フック・投げる）は
/// **本物のキーボード・マウス・コントローラーを読まず、ここに入っている値だけを読む**（<see cref="PlayerInputSource"/> を通して）。
/// ボットの頭（<see cref="SpaceJunkBot"/>）は、ここに「スティックをこう倒す」「カーソルをここへ」「RT を押す／離す」を書き込むだけ。
/// **つまりボットは、プレイヤーと同じ操作しかできない。**
///
/// できる操作は、操作タイプA・投げ方「左右で使い分け」のときのプレイヤーと同じ。
///
/// | 書き込むもの | プレイヤーで言うと |
/// | --- | --- |
/// | <see cref="Move"/> | 左スティック（WASD） |
/// | <see cref="SetPointer"/> | 右スティックやマウスで動かすカーソル（画面の位置） |
/// | <see cref="SetPull"/> | 左クリック（LT）＝引っ張る |
/// | <see cref="SetThrow"/> | 右クリック（RT）＝投げる |
///
/// 十字キー（狙い方の切り替え）や R（戻る）などのボタンは、**押せない**（いつも「押していない」）。
/// </summary>
[DisallowMultipleComponent]
public class VirtualPlayerInput : MonoBehaviour
{
    /// <summary>ボタン1つ分。押しっぱなしかどうかと、押した／離したフレームを覚える。</summary>
    private class Button
    {
        public bool Held;
        private int pressedFrame = -1;
        private int releasedFrame = -1;

        public bool PressedThisFrame => pressedFrame == Time.frameCount;
        public bool ReleasedThisFrame => releasedFrame == Time.frameCount;

        public void Set(bool down)
        {
            if (down == Held)
            {
                return;
            }

            Held = down;
            if (down)
            {
                pressedFrame = Time.frameCount;
            }
            else
            {
                releasedFrame = Time.frameCount;
            }
        }
    }

    private readonly Button pull = new Button();
    private readonly Button throwButton = new Button();

    /// <summary>左スティックの倒し方（-1..1）。**画面の上下左右**で入れる（プレイヤーと同じく、カメラの向きで回される）。</summary>
    public Vector2 Move { get; set; }

    /// <summary>カーソルが出ているか。false のときは狙いが決まらない（マウスがつながっていないのと同じ）。</summary>
    public bool HasPointer { get; private set; }

    /// <summary>カーソルの画面の位置（Screen 座標）。</summary>
    public Vector2 Pointer { get; private set; }

    public bool PullHeld => pull.Held;
    public bool PullPressed => pull.PressedThisFrame;
    public bool PullReleased => pull.ReleasedThisFrame;

    public bool ThrowHeld => throwButton.Held;
    public bool ThrowPressed => throwButton.PressedThisFrame;
    public bool ThrowReleased => throwButton.ReleasedThisFrame;

    private void Awake()
    {
        // この体の入力の読み口を、こちらに切り替える
        PlayerInputSource.Get(this).Virtual = this;
    }

    private void OnDisable()
    {
        // 止められたら、手を離してスティックも戻す（押しっぱなしのまま残さない）
        Move = Vector2.zero;
        pull.Set(false);
        throwButton.Set(false);
    }

    /// <summary>カーソルを画面のこの位置に置く。</summary>
    public void SetPointer(Vector2 screenPoint)
    {
        Pointer = screenPoint;
        HasPointer = true;
    }

    /// <summary>左クリック（LT）＝引っ張る、を押す（true）／離す（false）。</summary>
    public void SetPull(bool down)
    {
        pull.Set(down);
    }

    /// <summary>右クリック（RT）＝投げる、を押す（true）／離す（false）。</summary>
    public void SetThrow(bool down)
    {
        throwButton.Set(down);
    }
}
