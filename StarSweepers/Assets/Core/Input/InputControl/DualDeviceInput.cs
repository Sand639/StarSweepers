using ProjectEL4S.MultiMouse;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.InputControl
{
    /// <summary>左手側 / 右手側 のどちらか。</summary>
    public enum DualInputSide
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>今フレーム実際に使われている操作デバイス。</summary>
    public enum DualInputScheme
    {
        None = 0,
        DualMouse = 1,
        Gamepad = 2,
    }

    /// <summary>ゲームパッドのどちらのスティックを割り当てるか。</summary>
    public enum GamepadStick
    {
        LeftStick = 0,
        RightStick = 1,
    }

    /// <summary>
    /// マウス2台 / XBOXコントローラ のどちらでも同じ API で操作できるようにする入力レイヤー。
    /// プレイヤーの GameObject に貼って使う。
    ///
    /// 既定の対応:
    ///   左マウス(0番) = 左スティック / 左クリック = 左トリガー / 右クリック = LB
    ///   右マウス(1番) = 右スティック / 左クリック = RB       / 右クリック = 右トリガー
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class DualDeviceInput : MonoBehaviour
    {
        /// <summary>片側（左手側 or 右手側）の入力状態。毎フレーム更新される。</summary>
        public sealed class SideState
        {
            public DualInputSide Side { get; internal set; }

            /// <summary>-1..1 に正規化した方向入力。ゲームパッド=スティック、マウス=移動量を正規化したもの。</summary>
            public Vector2 Stick { get; internal set; }

            /// <summary>このフレームの移動量。マウス=ピクセル、ゲームパッド=スティック値×カーソル速度×dt。</summary>
            public Vector2 Delta { get; internal set; }

            /// <summary>スクリーン座標のカーソル位置（左下原点）。ゲームパッド時はスティックで積算した仮想カーソル。</summary>
            public Vector2 CursorPosition { get; internal set; }

            /// <summary>トリガー入力 0..1。左側=左クリック/LT、右側=右クリック/RT。マウス時は 0 か 1。</summary>
            public float Trigger { get; internal set; }

            /// <summary>トリガーが閾値以上か。</summary>
            public bool TriggerPressed { get; internal set; }
            public bool TriggerDown { get; internal set; }
            public bool TriggerUp { get; internal set; }

            /// <summary>バンパー入力。左側=右クリック/LB、右側=左クリック/RB。</summary>
            public bool BumperPressed { get; internal set; }
            public bool BumperDown { get; internal set; }
            public bool BumperUp { get; internal set; }

            /// <summary>この側のデバイスが使える状態か（マウス未割り当て / パッド未接続なら false）。</summary>
            public bool IsActive { get; internal set; }

            internal bool PrevTriggerPressed;
            internal bool PrevBumperPressed;
        }

        [Header("マウス割り当て（変更可）")]
        [Tooltip("左手側に使うマウスのポインタ番号")]
        [SerializeField] private int _leftMousePointerIndex = 0;

        [Tooltip("右手側に使うマウスのポインタ番号")]
        [SerializeField] private int _rightMousePointerIndex = 1;

        [Header("ゲームパッド割り当て（変更可）")]
        [SerializeField] private GamepadStick _leftSideStick = GamepadStick.LeftStick;
        [SerializeField] private GamepadStick _rightSideStick = GamepadStick.RightStick;

        [Header("感度・閾値")]
        [Tooltip("マウスの生カウントがこの値のとき Stick が 1.0 になる")]
        [SerializeField] private float _mouseStickFullScale = 20f;

        [Tooltip("スティックのデッドゾーン")]
        [SerializeField] private float _stickDeadzone = 0.15f;

        [Tooltip("トリガーをこの値以上で「押した」とみなす")]
        [SerializeField] private float _triggerThreshold = 0.35f;

        [Tooltip("ゲームパッド時の仮想カーソル速度（px/秒）")]
        [SerializeField] private float _gamepadCursorSpeed = 900f;

        [Header("デバイス切り替え")]
        [Tooltip("入力があったデバイスへ自動で切り替える")]
        [SerializeField] private bool _autoSwitchScheme = true;

        [Tooltip("自動切り替えを切ったときに使うデバイス")]
        [SerializeField] private DualInputScheme _fixedScheme = DualInputScheme.DualMouse;

        private readonly SideState[] _sides =
        {
            new SideState { Side = DualInputSide.Left },
            new SideState { Side = DualInputSide.Right },
        };

        private MultiMouseManager _mice;
        private DualInputScheme _activeScheme = DualInputScheme.None;

        /// <summary>今フレーム有効な操作デバイス。</summary>
        public DualInputScheme ActiveScheme => _activeScheme;

        /// <summary>左手側の入力状態。</summary>
        public SideState Left => _sides[0];

        /// <summary>右手側の入力状態。</summary>
        public SideState Right => _sides[1];

        /// <summary>左右をまとめて扱いたいとき。</summary>
        public SideState Get(DualInputSide side) => _sides[(int)side];

        /// <summary>マウスのポインタ割り当てを実行中に変える。</summary>
        public void SetMousePointerIndices(int leftIndex, int rightIndex)
        {
            _leftMousePointerIndex = leftIndex;
            _rightMousePointerIndex = rightIndex;
        }

        /// <summary>ゲームパッドのスティック割り当てを実行中に変える。</summary>
        public void SetStickAssignment(GamepadStick leftSide, GamepadStick rightSide)
        {
            _leftSideStick = leftSide;
            _rightSideStick = rightSide;
        }

        private void Start()
        {
            // Update 内では探さない（Input仕様書の規約）
            _mice = MultiMouseManager.Instance;
            if (_mice == null)
            {
                _mice = FindFirstObjectByType<MultiMouseManager>();
            }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _sides[0].CursorPosition = center;
            _sides[1].CursorPosition = center;
        }

        private void Update()
        {
            var gamepad = Gamepad.current;
            _activeScheme = ResolveScheme(gamepad);

            for (int i = 0; i < _sides.Length; i++)
            {
                var side = _sides[i];
                side.PrevTriggerPressed = side.TriggerPressed;
                side.PrevBumperPressed = side.BumperPressed;
            }

            if (_activeScheme == DualInputScheme.Gamepad && gamepad != null)
            {
                UpdateFromGamepad(gamepad);
            }
            else if (_activeScheme == DualInputScheme.DualMouse)
            {
                UpdateFromMouse();
            }
            else
            {
                ClearAll();
            }

            for (int i = 0; i < _sides.Length; i++)
            {
                var side = _sides[i];
                side.TriggerDown = side.TriggerPressed && !side.PrevTriggerPressed;
                side.TriggerUp = !side.TriggerPressed && side.PrevTriggerPressed;
                side.BumperDown = side.BumperPressed && !side.PrevBumperPressed;
                side.BumperUp = !side.BumperPressed && side.PrevBumperPressed;
            }
        }

        private DualInputScheme ResolveScheme(Gamepad gamepad)
        {
            if (!_autoSwitchScheme)
            {
                if (_fixedScheme == DualInputScheme.Gamepad && gamepad == null) return DualInputScheme.None;
                return _fixedScheme;
            }

            if (gamepad != null && HasGamepadActivity(gamepad)) return DualInputScheme.Gamepad;
            if (HasMouseActivity()) return DualInputScheme.DualMouse;

            // どちらも今フレーム動いていないなら直前のデバイスを維持する
            if (_activeScheme != DualInputScheme.None) return _activeScheme;
            if (_mice != null) return DualInputScheme.DualMouse;
            return gamepad != null ? DualInputScheme.Gamepad : DualInputScheme.None;
        }

        private bool HasGamepadActivity(Gamepad gamepad)
        {
            float dz = _stickDeadzone * _stickDeadzone;
            if (gamepad.leftStick.ReadValue().sqrMagnitude > dz) return true;
            if (gamepad.rightStick.ReadValue().sqrMagnitude > dz) return true;
            if (gamepad.leftTrigger.ReadValue() > _triggerThreshold) return true;
            if (gamepad.rightTrigger.ReadValue() > _triggerThreshold) return true;
            return gamepad.leftShoulder.isPressed || gamepad.rightShoulder.isPressed;
        }

        private bool HasMouseActivity()
        {
            if (_mice == null) return false;
            return HasPointerActivity(_leftMousePointerIndex) || HasPointerActivity(_rightMousePointerIndex);
        }

        private bool HasPointerActivity(int index)
        {
            var p = _mice.GetPointer(index);
            if (p == null || !p.IsAssigned) return false;
            if (p.RawDelta.sqrMagnitude > 0f) return true;
            return p.LeftPressed || p.RightPressed;
        }

        private void UpdateFromGamepad(Gamepad gamepad)
        {
            // 左手側: 左スティック / 左トリガー / LB
            ApplyGamepadSide(_sides[0],
                ReadStick(gamepad, _leftSideStick),
                gamepad.leftTrigger.ReadValue(),
                gamepad.leftShoulder.isPressed);

            // 右手側: 右スティック / 右トリガー / RB
            ApplyGamepadSide(_sides[1],
                ReadStick(gamepad, _rightSideStick),
                gamepad.rightTrigger.ReadValue(),
                gamepad.rightShoulder.isPressed);
        }

        private static Vector2 ReadStick(Gamepad gamepad, GamepadStick stick)
        {
            return stick == GamepadStick.LeftStick
                ? gamepad.leftStick.ReadValue()
                : gamepad.rightStick.ReadValue();
        }

        private void ApplyGamepadSide(SideState side, Vector2 rawStick, float trigger, bool bumper)
        {
            Vector2 stick = ApplyDeadzone(rawStick);
            Vector2 delta = stick * (_gamepadCursorSpeed * Time.deltaTime);

            side.IsActive = true;
            side.Stick = stick;
            side.Delta = delta;
            side.CursorPosition = ClampToScreen(side.CursorPosition + delta);
            side.Trigger = trigger;
            side.TriggerPressed = trigger >= _triggerThreshold;
            side.BumperPressed = bumper;
        }

        private void UpdateFromMouse()
        {
            // 左手側: 左クリック = 左トリガー / 右クリック = LB
            ApplyMouseSide(_sides[0], _leftMousePointerIndex, leftClickIsTrigger: true);
            // 右手側: 右クリック = 右トリガー / 左クリック = RB
            ApplyMouseSide(_sides[1], _rightMousePointerIndex, leftClickIsTrigger: false);
        }

        private void ApplyMouseSide(SideState side, int pointerIndex, bool leftClickIsTrigger)
        {
            var pointer = _mice != null ? _mice.GetPointer(pointerIndex) : null;
            if (pointer == null || !pointer.IsAssigned)
            {
                ClearSide(side);
                return;
            }

            Vector2 stick = _mouseStickFullScale > 0f
                ? pointer.RawDelta / _mouseStickFullScale
                : pointer.RawDelta;
            stick = Vector2.ClampMagnitude(stick, 1f);

            bool trigger = leftClickIsTrigger ? pointer.LeftPressed : pointer.RightPressed;
            bool bumper = leftClickIsTrigger ? pointer.RightPressed : pointer.LeftPressed;

            side.IsActive = true;
            side.Stick = stick;
            side.Delta = pointer.Delta;
            side.CursorPosition = pointer.ScreenPosition;
            side.Trigger = trigger ? 1f : 0f;
            side.TriggerPressed = trigger;
            side.BumperPressed = bumper;
        }

        private Vector2 ApplyDeadzone(Vector2 v)
        {
            float mag = v.magnitude;
            if (mag <= _stickDeadzone) return Vector2.zero;
            float scaled = Mathf.InverseLerp(_stickDeadzone, 1f, mag);
            return v / mag * Mathf.Min(scaled, 1f);
        }

        private static Vector2 ClampToScreen(Vector2 pos)
        {
            return new Vector2(
                Mathf.Clamp(pos.x, 0f, Screen.width),
                Mathf.Clamp(pos.y, 0f, Screen.height));
        }

        private void ClearAll()
        {
            ClearSide(_sides[0]);
            ClearSide(_sides[1]);
        }

        private static void ClearSide(SideState side)
        {
            side.IsActive = false;
            side.Stick = Vector2.zero;
            side.Delta = Vector2.zero;
            side.Trigger = 0f;
            side.TriggerPressed = false;
            side.BumperPressed = false;
        }
    }
}
