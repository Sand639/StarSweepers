using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary>
    /// Raw Input で受け取ったキー入力を、Unity の <see cref="Keyboard.current"/> へ流し直す橋渡し。
    ///
    /// こちらがキーボードの Raw Input を登録している間は、Unity の Keyboard.current のキーが一切動かなくなる
    /// （2026/10/6 に検証）。ポーズ画面やプレイヤー操作など、既存のスクリプトを動かし続けるために、
    /// 全部のキーボードを合わせた「どのキーが押されているか」を Unity のキーボードへ書き込む。
    ///
    /// 文字入力（onTextInput）と IMGUI（OnGUI）のキーは、Windows の別の経路で Unity に届き続けるので、
    /// ここでは扱わない（扱うと二重になる）。
    /// </summary>
    internal sealed class UnityKeyboardBridge
    {
        private static readonly int s_keyCount = ComputeKeyCount();

        // デバイスごとに「押しているキー」を持ち、キーごとに「何台で押しているか」を数える。
        // 2 台で同じキーを押して片方だけ離しても、押したままと扱うため
        private readonly Dictionary<IntPtr, bool[]> _pressedByDevice = new Dictionary<IntPtr, bool[]>();
        private readonly int[] _pressCount = new int[s_keyCount];
        private bool _dirty;

        public void OnKey(IntPtr device, Key key, bool isUp)
        {
            int k = (int)key;
            if (k <= 0 || k >= s_keyCount) return;

            if (!_pressedByDevice.TryGetValue(device, out var pressed))
            {
                pressed = new bool[s_keyCount];
                _pressedByDevice.Add(device, pressed);
            }

            if (isUp)
            {
                if (!pressed[k]) return;
                pressed[k] = false;
                _pressCount[k]--;
            }
            else
            {
                if (pressed[k]) return;   // 押しっぱなしのリピート
                pressed[k] = true;
                _pressCount[k]++;
            }
            _dirty = true;
        }

        /// <summary>全部のキーを離した扱いにする（フォーカスが外れた・登録を取られていた ときなど）。</summary>
        public void ReleaseAll()
        {
            _pressedByDevice.Clear();
            Array.Clear(_pressCount, 0, _pressCount.Length);
            _dirty = true;
        }

        /// <summary>
        /// 変化があれば Unity のキーボードへ書き込む。
        /// Input System が入力を処理する直前（onBeforeUpdate）に呼ぶと、同じフレームのうちに反映される。
        /// </summary>
        public void Flush()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.enabled) return;   // フォーカスが外れている間などは Unity が止めている

            // 変化が無くても、Unity 側の状態とずれていたら書き直す。
            // エディタで Unity に登録を取られていた一瞬に、Unity 側だけキーが押されたままになることがあるため
            if (!_dirty && IsInSync(keyboard)) return;
            _dirty = false;

            var state = new KeyboardState();
            for (int k = 1; k < s_keyCount; k++)
            {
                if (_pressCount[k] > 0) state.Set((Key)k, true);
            }
            InputSystem.QueueStateEvent(keyboard, state);
        }

        private bool IsInSync(Keyboard keyboard)
        {
            var keys = keyboard.allKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                KeyControl control = keys[i];
                int k = (int)control.keyCode;
                bool ours = k > 0 && k < s_keyCount && _pressCount[k] > 0;
                if (control.isPressed != ours) return false;
            }
            return true;
        }

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
