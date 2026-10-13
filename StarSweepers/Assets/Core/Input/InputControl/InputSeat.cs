using System;
using ProjectEL4S.MultiKeyboard;
using ProjectEL4S.MultiMouse;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectEL4S.InputControl
{
    /// <summary>
    /// 席で使うボタン（ゲームの操作の名前）。どのキー・マウスのボタン・コントローラのボタンにするかは
    /// <see cref="InputSeatManager"/> のインスペクターで決める。名前は InputSystem_Actions の Player に合わせてある。
    /// </summary>
    public enum InputSeatButton
    {
        Attack = 0,
        Aim = 1,
        Jump = 2,
        Interact = 3,
        Sprint = 4,
        Crouch = 5,
        Previous = 6,
        Next = 7,
        Pause = 8,
    }

    /// <summary>その席が最後に触った操作デバイス。ボタンの案内の出し分けなどに使う。</summary>
    public enum InputSeatScheme
    {
        None = 0,
        KeyboardMouse = 1,
        Gamepad = 2,
    }

    /// <summary>
    /// 1 つのボタンに、どのキー・マウスのボタン・コントローラのボタンを割り当てるか。
    /// どれか 1 つでも押されていれば「押されている」になる。空の欄は使わない。
    /// </summary>
    [Serializable]
    public sealed class InputSeatButtonBinding
    {
        public InputSeatButton button;
        public Key[] keys = Array.Empty<Key>();
        public MultiMouseButton[] mouseButtons = Array.Empty<MultiMouseButton>();
        public GamepadButton[] gamepadButtons = Array.Empty<GamepadButton>();

        public InputSeatButtonBinding() { }

        public InputSeatButtonBinding(InputSeatButton button, Key[] keys, MultiMouseButton[] mouseButtons, GamepadButton[] gamepadButtons)
        {
            this.button = button;
            this.keys = keys ?? Array.Empty<Key>();
            this.mouseButtons = mouseButtons ?? Array.Empty<MultiMouseButton>();
            this.gamepadButtons = gamepadButtons ?? Array.Empty<GamepadButton>();
        }

        /// <summary>
        /// 初期の割り当て。マウスの左右は、釣りの操作（左クリック＝LT・右クリック＝RT）に合わせてある。
        /// </summary>
        public static InputSeatButtonBinding[] CreateDefaults()
        {
            var none = Array.Empty<MultiMouseButton>();
            return new[]
            {
                new InputSeatButtonBinding(InputSeatButton.Attack, null, new[] { MultiMouseButton.Left }, new[] { GamepadButton.LeftTrigger }),
                new InputSeatButtonBinding(InputSeatButton.Aim, null, new[] { MultiMouseButton.Right }, new[] { GamepadButton.RightTrigger }),
                new InputSeatButtonBinding(InputSeatButton.Jump, new[] { Key.Space }, none, new[] { GamepadButton.South }),
                new InputSeatButtonBinding(InputSeatButton.Interact, new[] { Key.E }, none, new[] { GamepadButton.West }),
                new InputSeatButtonBinding(InputSeatButton.Sprint, new[] { Key.LeftShift }, none, new[] { GamepadButton.LeftStick }),
                new InputSeatButtonBinding(InputSeatButton.Crouch, new[] { Key.C }, none, new[] { GamepadButton.East }),
                new InputSeatButtonBinding(InputSeatButton.Previous, new[] { Key.Digit1 }, none, new[] { GamepadButton.LeftShoulder }),
                new InputSeatButtonBinding(InputSeatButton.Next, new[] { Key.Digit2 }, none, new[] { GamepadButton.RightShoulder }),
                new InputSeatButtonBinding(InputSeatButton.Pause, new[] { Key.Escape }, none, new[] { GamepadButton.Start }),
            };
        }
    }

    /// <summary>
    /// 遊ぶ人 1 人ぶん（＝ 1 席）の入力。キーボード＋マウスとコントローラのどちらで操作しても、同じ書き方で読める。
    /// 両方つながっている席では、両方の入力を合わせる（どちらで操作してもよい）。
    /// 値は <see cref="InputSeatManager"/> が毎フレーム更新する。席は <c>InputSeatManager.Instance.GetSeat(0)</c> で取る。
    /// </summary>
    public sealed class InputSeat
    {
        private static readonly int s_buttonCount = Enum.GetValues(typeof(InputSeatButton)).Length;

        private readonly bool[] _pressed = new bool[s_buttonCount];
        private readonly bool[] _down = new bool[s_buttonCount];
        private readonly bool[] _up = new bool[s_buttonCount];

        /// <summary>席の番号（0 から）。キーボード・マウスは同じ番号のものがこの席に入る。</summary>
        public int Index { get; }
        public Color Color { get; internal set; }

        /// <summary>この席のキーボード。管理役が無い・台数が足りないときは null。つながっているかは <see cref="HasKeyboard"/>。</summary>
        public MultiKeyboardDevice Keyboard { get; internal set; }
        /// <summary>この席のマウス。管理役が無い・台数が足りないときは null。つながっているかは <see cref="HasMouse"/>。</summary>
        public MultiMousePointer Mouse { get; internal set; }
        /// <summary>この席のコントローラ。入っていなければ null。</summary>
        public Gamepad Gamepad { get; internal set; }

        public bool HasKeyboard => Keyboard != null && Keyboard.IsAssigned;
        public bool HasMouse => Mouse != null && Mouse.IsAssigned;
        public bool HasGamepad => Gamepad != null && Gamepad.added;
        /// <summary>何か 1 つでも操作デバイスが入っているか。false の席には誰も座っていない。</summary>
        public bool HasAnyDevice => HasKeyboard || HasMouse || HasGamepad;

        /// <summary>最後に触ったのがキーボード＋マウスかコントローラか。</summary>
        public InputSeatScheme LastUsedScheme { get; internal set; }

        /// <summary>移動。-1..1（上・右が正）。キーボード＝WASD／矢印、コントローラ＝左スティック。</summary>
        public Vector2 Move { get; internal set; }

        /// <summary>照準の向き。-1..1。コントローラの右スティック（マウスでは 0。マウスは <see cref="Cursor"/> を使う）。</summary>
        public Vector2 AimStick { get; internal set; }

        /// <summary>このフレームの視点・カーソルの移動量（ピクセル）。マウス＝移動量、コントローラ＝右スティック×速さ×時間。</summary>
        public Vector2 LookDelta { get; internal set; }

        /// <summary>
        /// 画面上のカーソル位置（左下原点・ピクセル。Unity の Screen 座標と同じ）。
        /// マウス＝その席のマウスの位置、コントローラ＝右スティックで動かす仮想カーソル。両方使っても 1 つにつながる。
        /// </summary>
        public Vector2 Cursor { get; internal set; }

        internal bool CursorInitialized;
        // コントローラが席に入った瞬間のボタンを「押した」と数えないよう、一度全部離されるまで待つ
        internal bool GamepadArmed;

        public InputSeat(int index)
        {
            Index = index;
        }

        public bool IsPressed(InputSeatButton button) => _pressed[(int)button];
        public bool WasPressedThisFrame(InputSeatButton button) => _down[(int)button];
        public bool WasReleasedThisFrame(InputSeatButton button) => _up[(int)button];

        /// <summary>
        /// ボタンの今フレームの状態を入れる。anyDownThisFrame は「どれかのデバイスでこのフレームに押された」。
        /// 押して離すのが 1 フレームに収まった場合（キーボードの素早い連打）も、押した・離したの両方が立つ。
        /// </summary>
        internal void SetButton(int index, bool pressed, bool anyDownThisFrame)
        {
            bool prev = _pressed[index];
            bool down = !prev && (pressed || anyDownThisFrame);
            _down[index] = down;
            _up[index] = !pressed && (prev || down);
            _pressed[index] = pressed;
        }

        internal void ClearInput()
        {
            Move = Vector2.zero;
            AimStick = Vector2.zero;
            LookDelta = Vector2.zero;
            for (int i = 0; i < s_buttonCount; i++)
            {
                // 押していたボタンは「離した」を 1 回出してから 0 にする
                _up[i] = _pressed[i];
                _down[i] = false;
                _pressed[i] = false;
            }
        }
    }
}
