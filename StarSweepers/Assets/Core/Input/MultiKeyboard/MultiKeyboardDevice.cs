using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary><see cref="MultiKeyboardDevice.ReadMove"/> でどのキーを移動に使うか。</summary>
    public enum MultiKeyboardMoveKeys
    {
        Wasd = 0,
        Arrows = 1,
        WasdAndArrows = 2,
    }

    /// <summary>
    /// 物理キーボード 1 台に対応する入力状態。使い方は Input System の Keyboard とほぼ同じ。
    /// 値は <see cref="MultiKeyboardManager"/> が毎フレーム入れる（このクラス自身は Windows を触らない）。
    /// </summary>
    public sealed class MultiKeyboardDevice
    {
        private static readonly int s_keyCount = ComputeKeyCount();

        // Key の番号をそのまま添字にした「押されているか」「このフレーム押したか」「このフレーム離したか」
        private readonly bool[] _pressed = new bool[s_keyCount];
        private readonly bool[] _down = new bool[s_keyCount];
        private readonly bool[] _up = new bool[s_keyCount];
        // 押されているキーの一覧（離すとき・表示用に全キーを舐めずに済ませる）
        private readonly List<Key> _pressedKeys = new List<Key>();

        /// <summary>このキーボードの番号（0 から）。<see cref="MultiKeyboardManager.GetKeyboard"/> に渡す番号と同じ。</summary>
        public int Index { get; }
        /// <summary>表示用の色（MultiKeyboardManager の Keyboard Colors から）。</summary>
        public Color Color { get; internal set; }

        /// <summary>物理デバイスが割り当てられているか。</summary>
        public bool IsAssigned { get; internal set; }
        /// <summary>Windows がこのキーボードに付けた番号（Raw Input の HANDLE）。抜き差しや再起動で変わるので保存には使わない。</summary>
        public IntPtr DeviceHandle { get; internal set; }
        /// <summary>Windows のデバイスパス（\\?\HID#VID_xxxx… の形）。同じ USB の口に挿している限り変わらないので、固定登録はこちらで覚える。</summary>
        public string DeviceName { get; internal set; } = string.Empty;

        /// <summary>今押されているキー（押した順）。</summary>
        public IReadOnlyList<Key> PressedKeys => _pressedKeys;

        /// <summary>このフレームに何かのキーが押されたか。</summary>
        public bool AnyKeyDown { get; private set; }

        public MultiKeyboardDevice(int index)
        {
            Index = index;
        }

        public bool IsPressed(Key key) => InRange(key) && _pressed[(int)key];
        public bool WasPressedThisFrame(Key key) => InRange(key) && _down[(int)key];
        public bool WasReleasedThisFrame(Key key) => InRange(key) && _up[(int)key];

        /// <summary>
        /// 移動入力を -1..1 で返す（上・右が正）。斜めは長さ 1 に揃える。
        /// 反対のキーを同時に押したときは打ち消し合って 0 になる。
        /// </summary>
        public Vector2 ReadMove(MultiKeyboardMoveKeys keys = MultiKeyboardMoveKeys.Wasd)
        {
            bool wasd = keys != MultiKeyboardMoveKeys.Arrows;
            bool arrows = keys != MultiKeyboardMoveKeys.Wasd;

            bool up = (wasd && IsPressed(Key.W)) || (arrows && IsPressed(Key.UpArrow));
            bool down = (wasd && IsPressed(Key.S)) || (arrows && IsPressed(Key.DownArrow));
            bool left = (wasd && IsPressed(Key.A)) || (arrows && IsPressed(Key.LeftArrow));
            bool right = (wasd && IsPressed(Key.D)) || (arrows && IsPressed(Key.RightArrow));

            var v = new Vector2((right ? 1f : 0f) - (left ? 1f : 0f), (up ? 1f : 0f) - (down ? 1f : 0f));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        /// <summary>フレームの頭で「このフレームに押した・離した」を消す（「押されている」は持ち越す）。</summary>
        internal void BeginFrame()
        {
            AnyKeyDown = false;
            Array.Clear(_down, 0, _down.Length);
            Array.Clear(_up, 0, _up.Length);
        }

        /// <summary>押しっぱなしのリピートで何度呼ばれても、Down が立つのは最初の 1 回だけ。</summary>
        internal void Press(Key key)
        {
            if (!InRange(key)) return;
            int i = (int)key;
            if (_pressed[i]) return;
            _pressed[i] = true;
            _down[i] = true;
            AnyKeyDown = true;
            _pressedKeys.Add(key);
        }

        internal void Release(Key key)
        {
            if (!InRange(key)) return;
            int i = (int)key;
            if (!_pressed[i]) return;
            _pressed[i] = false;
            _up[i] = true;
            _pressedKeys.Remove(key);
        }

        /// <summary>押されているキーをすべて離す（Up も立てる）。フォーカスが外れたとき用。</summary>
        internal void ReleaseAll()
        {
            for (int i = _pressedKeys.Count - 1; i >= 0; i--)
            {
                Release(_pressedKeys[i]);
            }
        }

        /// <summary>デバイスとの結び付きと、押しているキーを全部消して未割り当てに戻す。</summary>
        internal void ResetAssignment()
        {
            IsAssigned = false;
            DeviceHandle = IntPtr.Zero;
            DeviceName = string.Empty;
            AnyKeyDown = false;
            Array.Clear(_pressed, 0, _pressed.Length);
            Array.Clear(_down, 0, _down.Length);
            Array.Clear(_up, 0, _up.Length);
            _pressedKeys.Clear();
        }

        private static bool InRange(Key key) => key > Key.None && (int)key < s_keyCount;

        /// <summary>
        /// 配列の大きさ。Key の番号は飛び飛びなので、「値の個数」ではなく「いちばん大きい番号 + 1」にして番号をそのまま添字に使えるようにする。
        /// </summary>
        private static int ComputeKeyCount()
        {
            int max = 0;
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if ((int)k > max) max = (int)k;
            }
            return max + 1;
        }
    }
}
