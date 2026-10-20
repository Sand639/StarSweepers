using System;
using UnityEngine;

namespace ProjectEL4S.MultiMouse
{
    /// <summary>マウスのボタン。番号は <see cref="RawMouseFrame.ButtonDown"/> のビット位置と同じ（bit0:左 bit1:右 bit2:中）。</summary>
    public enum MultiMouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    /// <summary>
    /// 物理マウス 1 台に対応する仮想ポインタ。
    /// OS のカーソルとは別に、自前の位置とボタンの状態を持つ。値は <see cref="MultiMouseManager"/> が毎フレーム入れる。
    /// </summary>
    public sealed class MultiMousePointer
    {
        // 左・右・中の 3 ボタンぶん（添字は MultiMouseButton の番号）
        private readonly bool[] _pressed = new bool[3];
        private readonly bool[] _down = new bool[3];
        private readonly bool[] _up = new bool[3];

        /// <summary>このポインタの番号（0 から）。<see cref="MultiMouseManager.GetPointer"/> に渡す番号と同じ。</summary>
        public int Index { get; }
        /// <summary>表示用の色（MultiMouseManager の Pointer Colors から）。</summary>
        public Color Color { get; internal set; }

        /// <summary>物理デバイスが割り当てられているか。</summary>
        public bool IsAssigned { get; internal set; }
        /// <summary>Windows がこのマウスに付けた番号（Raw Input の HANDLE）。抜き差しや再起動で変わるので保存には使わない。</summary>
        public IntPtr DeviceHandle { get; internal set; }
        /// <summary>Windows のデバイスパス（\\?\HID#VID_xxxx… の形）。同じ USB の口に挿している限り変わらないので、固定登録はこちらで覚える。</summary>
        public string DeviceName { get; internal set; } = string.Empty;

        /// <summary>スクリーン座標（左下原点・ピクセル）。Unity の Screen 座標系と同じ。</summary>
        public Vector2 ScreenPosition { get; internal set; }
        /// <summary>このフレームの移動量（ピクセル・スクリーン座標系）。感度倍率を掛けたあとの値。</summary>
        public Vector2 Delta { get; internal set; }
        /// <summary>
        /// このフレームの移動量（デバイスの生カウント・感度倍率なし）。Y は Unity に合わせて上が正に反転済み。
        /// Windows のポインタ速度・加速度の影響を受けないので、速度を測る用途はこちらを使う。
        /// </summary>
        public Vector2 RawDelta { get; internal set; }
        /// <summary>このフレームのホイール量。</summary>
        public float Wheel { get; internal set; }

        public MultiMousePointer(int index)
        {
            Index = index;
        }

        public bool IsPressed(MultiMouseButton button) => _pressed[(int)button];
        public bool WasPressedThisFrame(MultiMouseButton button) => _down[(int)button];
        public bool WasReleasedThisFrame(MultiMouseButton button) => _up[(int)button];

        // よく使う左右のボタンの近道（IsPressed(MultiMouseButton.Left) などと同じ）
        public bool LeftPressed => _pressed[0];
        public bool LeftDown => _down[0];
        public bool LeftUp => _up[0];
        public bool RightPressed => _pressed[1];
        public bool RightDown => _down[1];
        public bool RightUp => _up[1];

        /// <summary>フレームの頭で、このフレームぶんの移動量・ホイール・「押した／離した」を消す（「押されている」と位置は持ち越す）。</summary>
        internal void BeginFrame()
        {
            Delta = Vector2.zero;
            RawDelta = Vector2.zero;
            Wheel = 0f;
            for (int i = 0; i < 3; i++)
            {
                _down[i] = false;
                _up[i] = false;
            }
        }

        /// <summary>
        /// ビットで届いた「押した」「離した」をボタンごとの状態にする。
        /// 同じフレームで押して離した（両方のビットが立つ）ときは、押した→離したの順に処理されるので、
        /// 「押されている」は false のまま、「押した」と「離した」の両方が立つ。
        /// </summary>
        internal void ApplyButtons(int downBits, int upBits)
        {
            for (int i = 0; i < 3; i++)
            {
                int mask = 1 << i;
                if ((downBits & mask) != 0)
                {
                    _pressed[i] = true;
                    _down[i] = true;
                }
                if ((upBits & mask) != 0)
                {
                    _pressed[i] = false;
                    _up[i] = true;
                }
            }
        }

        /// <summary>押されているボタンをすべて離す（Up も立てる）。離したイベントを取りこぼしたとき用。</summary>
        internal void ReleaseAll()
        {
            for (int i = 0; i < 3; i++)
            {
                if (!_pressed[i]) continue;
                _pressed[i] = false;
                _up[i] = true;
            }
        }

        /// <summary>デバイスとの結び付きと、ボタンの状態を全部消して未割り当てに戻す（位置はそのまま。置き直すのは管理役の仕事）。</summary>
        internal void ResetAssignment()
        {
            IsAssigned = false;
            DeviceHandle = IntPtr.Zero;
            DeviceName = string.Empty;
            for (int i = 0; i < 3; i++)
            {
                _pressed[i] = false;
                _down[i] = false;
                _up[i] = false;
            }
        }
    }
}
