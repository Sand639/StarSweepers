using System;
using UnityEngine;

namespace ProjectEL4S.MultiMouse
{
    public enum MultiMouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    /// <summary>物理マウス 1 台に対応する仮想ポインタ。</summary>
    public sealed class MultiMousePointer
    {
        private readonly bool[] _pressed = new bool[3];
        private readonly bool[] _down = new bool[3];
        private readonly bool[] _up = new bool[3];

        public int Index { get; }
        public Color Color { get; internal set; }

        /// <summary>物理デバイスが割り当てられているか。</summary>
        public bool IsAssigned { get; internal set; }
        public IntPtr DeviceHandle { get; internal set; }
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

        public bool LeftPressed => _pressed[0];
        public bool LeftDown => _down[0];
        public bool LeftUp => _up[0];
        public bool RightPressed => _pressed[1];
        public bool RightDown => _down[1];
        public bool RightUp => _up[1];

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
