using System;
using System.Collections.Generic;
using ProjectEL4S.MultiKeyboard;
using ProjectEL4S.MultiMouse;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectEL4S.InputControl
{
    /// <summary>登録の今の段階。</summary>
    public enum InputSeatRegistrationStep
    {
        None = 0,
        Keyboards = 1,
        Mice = 2,
    }

    /// <summary>
    /// 遊ぶ人ごとの「席」をまとめる管理役。席ごとに、キーボード＋マウスとコントローラを同じ書き方で読めるようにする。
    ///
    ///   var seat = InputSeatManager.Instance.GetSeat(0);   // P1
    ///   Vector2 move = seat.Move;                           // WASD／矢印 か 左スティック
    ///   if (seat.WasPressedThisFrame(InputSeatButton.Jump)) { ... }   // Space か A
    ///
    /// 席 i には、キーボード i 番・マウス i 番（MultiKeyboardManager / MultiMouseManager の番号）が入る。
    /// コントローラは、ボタンを押した順に空いている席へ入る。
    ///
    /// 固定登録：<see cref="BeginRegistration"/> で「P1 のキーボード → P2 のキーボード → P1 のマウス → …」の順に
    /// 押してもらうと、どのキーボード・マウスが何番かをファイルに保存し、次からは起動しただけでその番号になる。
    /// 保存先は <see cref="InputSeatStore.FilePath"/>。
    /// </summary>
    // キーボード・マウスの管理役（-100）が先に動いてから席の値をそろえ、ふつうのスクリプト（0）はそのあとで読む、という順番
    [DefaultExecutionOrder(-90)]
    public sealed class InputSeatManager : MonoBehaviour
    {
        // コントローラを席に入れる合図として見るボタン
        private static readonly GamepadButton[] s_joinButtons =
        {
            GamepadButton.South, GamepadButton.East, GamepadButton.West, GamepadButton.North,
            GamepadButton.Start, GamepadButton.Select,
            GamepadButton.LeftShoulder, GamepadButton.RightShoulder,
            GamepadButton.LeftTrigger, GamepadButton.RightTrigger,
        };

        [Header("席")]
        [Tooltip("遊ぶ人数。MultiKeyboardManager の Max Keyboards・MultiMouseManager の Max Pointers もこの数以上にすること")]
        [SerializeField] private int _seatCount = 2;
        [Tooltip("席の色（P1 から順。席のほうが多ければ繰り返す）。画面で P1・P2 を描き分けるときに使う")]
        [SerializeField] private Color[] _seatColors =
        {
            new Color(0.2f, 0.8f, 1f),
            new Color(1f, 0.45f, 0.2f),
            new Color(0.5f, 1f, 0.4f),
            new Color(1f, 0.9f, 0.3f),
        };

        [Header("つなぐ相手（空なら、シーンにある物を使う。無ければこの GameObject に作る）")]
        [Tooltip("キーボードを台ごとに読む管理役")]
        [SerializeField] private MultiKeyboardManager _keyboards;
        [Tooltip("マウスを台ごとに読む管理役")]
        [SerializeField] private MultiMouseManager _mice;

        [Header("固定登録")]
        [Tooltip("起動したときに、保存してあるキーボード・マウスの登録を読み込む。切ると毎回「押した順」になる")]
        [SerializeField] private bool _loadSavedRegistration = true;

        [Header("コントローラ")]
        [Tooltip("ボタンを押したコントローラを、空いている席に自動で入れる（抜けた席・誰もいない席・コントローラの無い席の順）")]
        [SerializeField] private bool _joinGamepadsByPress = true;

        [Header("操作")]
        [Tooltip("キーボードでの移動に使うキー（WASD／矢印／両方）")]
        [SerializeField] private MultiKeyboardMoveKeys _moveKeys = MultiKeyboardMoveKeys.WasdAndArrows;
        [Tooltip("スティックのデッドゾーン")]
        [SerializeField] private float _stickDeadzone = 0.15f;
        [Tooltip("右スティックで動かす仮想カーソルの速さ（px/秒）。Look Delta にも使う")]
        [SerializeField] private float _gamepadCursorSpeed = 900f;
        [Tooltip("ボタンごとの割り当て。同じボタンが2つあるときは上のほうを使う")]
        [SerializeField] private InputSeatButtonBinding[] _buttons = InputSeatButtonBinding.CreateDefaults();

        private InputSeat[] _seats = Array.Empty<InputSeat>();
        // _buttons を「ボタンの番号 → 割り当て」の形に並べ直したもの（毎フレーム探さずに済むように）。無いボタンは null
        private InputSeatButtonBinding[] _bindingByButton;
        // コントローラが抜けた席。次に入ってきたコントローラはここへ戻す
        private readonly List<int> _seatsWaitingForGamepad = new List<int>();

        // ---- 固定登録の途中経過（登録していないときは使わない）
        private InputSeatRegistrationStep _step;
        // 前のフレームまでに割り当て済みだったキーボードの台数。「登録済みのキーボードで Enter」を見分けるのに使う
        private int _seenKeyboardCount;
        // キーボードの番で決まったデバイスの名前。マウスの番が終わったら、まとめて保存する
        private string[] _registeredKeyboards;
        // 登録を始める前の「クリックしたマウスだけ割り当てる」の設定。終わったら元に戻す
        private bool _miceClickOnlyBefore;

        public static InputSeatManager Instance { get; private set; }

        // CreatePersistent で作るときの席の数（Awake で読んで 0 に戻す）
        private static int s_nextSeatCount;

        /// <summary>
        /// **シーンをまたいで残る管理役を作る**（1台で複数人のオンライン対戦用。<c>LocalMultiplayer</c> が呼ぶ）。
        /// キーボード・マウスの台数も席の数にそろえる。画面のマウスカーソルは隠さない（ロビーの画面を押せるように）。
        /// </summary>
        public static InputSeatManager CreatePersistent(int seatCount)
        {
            seatCount = Mathf.Max(1, seatCount);

            var go = new GameObject("InputSeats（1台で複数人）");
            go.SetActive(false);
            DontDestroyOnLoad(go);

            var keyboards = go.AddComponent<MultiKeyboardManager>();
            keyboards.MaxKeyboardsSetting = seatCount;
            var mice = go.AddComponent<MultiMouseManager>();
            mice.MaxPointersSetting = seatCount;
            mice.HideSystemCursorSetting = false;

            go.SetActive(true);   // ここでキーボード・マウスの管理役が起動する

            s_nextSeatCount = seatCount;
            // 起動した時点で、上のキーボード・マウスの管理役（Instance）につながる
            return go.AddComponent<InputSeatManager>();
        }

        public int SeatCount => _seats.Length;
        public IReadOnlyList<InputSeat> Seats => _seats;
        public MultiKeyboardManager KeyboardManager => _keyboards;
        public MultiMouseManager MouseManager => _mice;

        /// <summary>読み込んだ（または今保存した）固定登録。無ければ null。</summary>
        public InputSeatSaveData SavedRegistration { get; private set; }

        public bool IsRegistering => _step != InputSeatRegistrationStep.None;
        public InputSeatRegistrationStep RegistrationStep => _step;

        /// <summary>登録中、次に押してもらう席の番号（0 = P1）。</summary>
        public int RegistrationSeatIndex
        {
            get
            {
                // 登録は押した順に 0 番から埋まっていくので、「割り当て済みの台数」がそのまま次の席の番号になる
                switch (_step)
                {
                    case InputSeatRegistrationStep.Keyboards: return _keyboards.AssignedKeyboardCount;
                    case InputSeatRegistrationStep.Mice: return _mice.AssignedPointerCount;
                    default: return -1;
                }
            }
        }

        /// <summary>登録中に画面へ出す案内。登録していなければ空文字。</summary>
        public string RegistrationPrompt
        {
            get
            {
                int seat = RegistrationSeatIndex + 1;
                switch (_step)
                {
                    case InputSeatRegistrationStep.Keyboards:
                        return $"キーボードの登録（{seat} / {KeyboardTarget}）\n" +
                               $"P{seat} の人は、自分のキーボードのどれかのキーを押してください。\n" +
                               "（ここで終わるときは、登録済みのキーボードで Enter）";
                    case InputSeatRegistrationStep.Mice:
                        return $"マウスの登録（{seat} / {MouseTarget}）\n" +
                               $"P{seat} の人は、自分のマウスをクリックしてください。\n" +
                               "（ここで終わるときは、キーボードで Enter）";
                    default:
                        return string.Empty;
                }
            }
        }

        // 登録で押してもらう台数。席の数と管理役の台数の少ないほう（それ以上は入れる場所が無い）
        private int KeyboardTarget => Mathf.Min(_seats.Length, _keyboards.Keyboards.Count);
        private int MouseTarget => Mathf.Min(_seats.Length, _mice.Pointers.Count);

        /// <summary>範囲外なら null。0 = P1。</summary>
        public InputSeat GetSeat(int index)
        {
            if (index < 0 || index >= _seats.Length) return null;
            return _seats[index];
        }

        /// <summary>そのコントローラが入っている席。どこにも入っていなければ null。</summary>
        public InputSeat FindSeat(Gamepad gamepad)
        {
            if (gamepad == null) return null;
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i].Gamepad == gamepad) return _seats[i];
            }
            return null;
        }

        /// <summary>コントローラを席に入れる。ほかの席に入っていたら、そちらからは外す。</summary>
        public bool AssignGamepad(int seatIndex, Gamepad gamepad)
        {
            var seat = GetSeat(seatIndex);
            if (seat == null || gamepad == null) return false;

            var other = FindSeat(gamepad);
            if (other != null) other.Gamepad = null;

            seat.Gamepad = gamepad;
            seat.GamepadArmed = false;
            _seatsWaitingForGamepad.Remove(seatIndex);
            Debug.Log($"[InputSeat] P{seatIndex + 1} にコントローラを割り当て: {gamepad.displayName}");
            return true;
        }

        public void RemoveGamepad(int seatIndex)
        {
            var seat = GetSeat(seatIndex);
            if (seat != null) seat.Gamepad = null;
        }

        /// <summary>
        /// 割り当てをやり直す。固定登録したキーボード・マウスは登録どおりに戻り、それ以外は押した順に、
        /// コントローラはボタンを押した順にまた入る。
        /// </summary>
        public void ResetAssignments()
        {
            _keyboards.ClearAssignments();
            _mice.ClearAssignments();
            _seatsWaitingForGamepad.Clear();
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i].Gamepad = null;
                _seats[i].CursorInitialized = false;
            }
        }

        /// <summary>
        /// 固定登録を始める。キーボード（P1 → P2 …）、マウス（P1 → P2 …）の順に押してもらい、終わったら保存する。
        /// 登録中は、どの席の入力も 0 になる（登録の操作でゲームが動かないように）。
        /// </summary>
        public void BeginRegistration()
        {
            if (IsRegistering) return;

            _miceClickOnlyBefore = _mice.AssignOnClickOnly;
            _registeredKeyboards = null;
            _seenKeyboardCount = 0;

            // 固定を外して割り当ても消す。ここからは押した順＝席の順になる
            _keyboards.SetFixedDevices(null);
            _mice.SetFixedDevices(null);
            // 登録の途中で、手が触れただけのマウスを拾わないようにする
            _mice.AssignOnClickOnly = true;

            _step = InputSeatRegistrationStep.Keyboards;
            Debug.Log("[InputSeat] キーボード・マウスの登録を始めました");

            // 分けて読めない環境では登録しても意味が無いので飛ばす
            if (!_keyboards.RawInputAvailable) EnterMiceStep();
        }

        /// <summary>登録をやめて、前の登録（無ければ押した順）に戻す。</summary>
        public void CancelRegistration()
        {
            if (!IsRegistering) return;
            EndRegistrationMode();
            ApplyRegistration(SavedRegistration);
            Debug.Log("[InputSeat] 登録をやめました（前の登録に戻しました）");
        }

        /// <summary>保存してある登録を消し、押した順に戻す。</summary>
        public void ForgetRegistration()
        {
            if (IsRegistering) EndRegistrationMode();
            InputSeatStore.Delete();
            SavedRegistration = null;
            ApplyRegistration(null);
            Debug.Log("[InputSeat] 登録を消しました（押した順で割り当てます）");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[InputSeat] InputSeatManager がシーンに2つあります。後から来たほうは使いません", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (s_nextSeatCount > 0)
            {
                _seatCount = s_nextSeatCount;
                s_nextSeatCount = 0;
            }

            // 管理役は DefaultExecutionOrder(-100) なので、ここより先に Awake が済んでいる
            if (_keyboards == null) _keyboards = MultiKeyboardManager.Instance;
            if (_keyboards == null) _keyboards = gameObject.AddComponent<MultiKeyboardManager>();
            if (_mice == null) _mice = MultiMouseManager.Instance;
            if (_mice == null) _mice = gameObject.AddComponent<MultiMouseManager>();

            _seatCount = Mathf.Max(1, _seatCount);
            _seats = new InputSeat[_seatCount];
            for (int i = 0; i < _seatCount; i++)
            {
                _seats[i] = new InputSeat(i)
                {
                    Color = _seatColors != null && _seatColors.Length > 0 ? _seatColors[i % _seatColors.Length] : Color.white,
                    Keyboard = _keyboards.GetKeyboard(i),
                    Mouse = _mice.GetPointer(i),
                };
            }
            if (_keyboards.Keyboards.Count < _seatCount)
                Debug.LogWarning($"[InputSeat] キーボードの台数（{_keyboards.Keyboards.Count}）が席の数（{_seatCount}）より少ないので、足りない席はキーボードを使えません", this);
            if (_mice.Pointers.Count < _seatCount)
                Debug.LogWarning($"[InputSeat] マウスの台数（{_mice.Pointers.Count}）が席の数（{_seatCount}）より少ないので、足りない席はマウスを使えません", this);

            BuildBindings();

            if (_loadSavedRegistration)
            {
                SavedRegistration = InputSeatStore.Load();
                if (SavedRegistration != null) ApplyRegistration(SavedRegistration);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (IsRegistering) EndRegistrationMode();
            Instance = null;
        }

        private void OnValidate()
        {
            // 実行中にインスペクターで割り当てを変えたら、すぐ効くようにする
            if (Application.isPlaying && _seats.Length > 0) BuildBindings();
        }

        /// <summary>インスペクターの割り当て一覧を、ボタンの番号で引ける形に並べ直す。同じボタンが 2 つあれば上のほうだけ使う。</summary>
        private void BuildBindings()
        {
            _bindingByButton = new InputSeatButtonBinding[Enum.GetValues(typeof(InputSeatButton)).Length];
            if (_buttons == null) return;
            for (int i = 0; i < _buttons.Length; i++)
            {
                var b = _buttons[i];
                if (b == null) continue;
                int index = (int)b.button;
                if (index < 0 || index >= _bindingByButton.Length || _bindingByButton[index] != null) continue;
                _bindingByButton[index] = b;
            }
        }

        /// <summary>固定登録を管理役に渡す。null なら固定を外して「押した順」に戻す。</summary>
        private void ApplyRegistration(InputSeatSaveData data)
        {
            _keyboards.SetFixedDevices(data?.keyboards);
            _mice.SetFixedDevices(data?.mice);
        }

        private void Update()
        {
            DropDisconnectedGamepads();
            UpdateRegistration();
            if (_joinGamepadsByPress && !IsRegistering) JoinPressedGamepads();

            for (int i = 0; i < _seats.Length; i++)
            {
                if (IsRegistering) _seats[i].ClearInput();
                else UpdateSeat(_seats[i]);
            }
        }

        // ---------------------------------------------------------------- 登録

        private void UpdateRegistration()
        {
            switch (_step)
            {
                case InputSeatRegistrationStep.Keyboards:
                {
                    int count = _keyboards.AssignedKeyboardCount;
                    // Enter は「前のフレームまでに登録済みのキーボード」で押したときだけ数える（新しいキーボードの Enter は登録）
                    if (count >= KeyboardTarget || EnterPressedOnKeyboards(_seenKeyboardCount))
                    {
                        EnterMiceStep();
                        break;
                    }
                    _seenKeyboardCount = count;
                    break;
                }
                case InputSeatRegistrationStep.Mice:
                {
                    bool enter = EnterPressedOnKeyboards(_keyboards.Keyboards.Count) ||
                                 (Keyboard.current != null &&
                                  (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame));
                    if (_mice.AssignedPointerCount >= MouseTarget || enter) FinishRegistration();
                    break;
                }
            }
        }

        /// <summary>先頭から count 台のうち、割り当て済みのキーボードのどれかで Enter が押されたか。</summary>
        private bool EnterPressedOnKeyboards(int count)
        {
            for (int i = 0; i < count && i < _keyboards.Keyboards.Count; i++)
            {
                var kb = _keyboards.Keyboards[i];
                if (!kb.IsAssigned) continue;
                if (kb.WasPressedThisFrame(Key.Enter) || kb.WasPressedThisFrame(Key.NumpadEnter)) return true;
            }
            return false;
        }

        /// <summary>キーボードの番を終えて、マウスの番に進む。</summary>
        private void EnterMiceStep()
        {
            _registeredKeyboards = _keyboards.RawInputAvailable ? _keyboards.GetAssignedDeviceNames() : Array.Empty<string>();
            // キーボードの番に触ったマウスは数えない
            _mice.ClearAssignments();
            _step = InputSeatRegistrationStep.Mice;

            if (!_mice.RawInputAvailable) FinishRegistration();
        }

        /// <summary>マウスの番も終わったので、キーボード・マウスの名前をファイルに保存し、その登録で割り当て直す。</summary>
        private void FinishRegistration()
        {
            var data = new InputSeatSaveData
            {
                keyboards = _registeredKeyboards ?? Array.Empty<string>(),
                mice = _mice.RawInputAvailable ? _mice.GetAssignedDeviceNames() : Array.Empty<string>(),
                savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm"),
            };
            EndRegistrationMode();
            InputSeatStore.Save(data);
            SavedRegistration = data;
            ApplyRegistration(data);
        }

        /// <summary>登録の状態を抜ける（保存するかどうかは呼ぶ側が決める）。登録中だけ変えていた設定も戻す。</summary>
        private void EndRegistrationMode()
        {
            _mice.AssignOnClickOnly = _miceClickOnlyBefore;
            _step = InputSeatRegistrationStep.None;
        }

        // ---------------------------------------------------------------- コントローラ

        /// <summary>USB が抜けるなどして消えたコントローラを席から外し、その席を「戻り待ち」にする。</summary>
        private void DropDisconnectedGamepads()
        {
            for (int i = 0; i < _seats.Length; i++)
            {
                var pad = _seats[i].Gamepad;
                if (pad == null || pad.added) continue;

                Debug.Log($"[InputSeat] P{i + 1} のコントローラが外れました（次につながったコントローラをこの席に戻します）");
                _seats[i].Gamepad = null;
                if (!_seatsWaitingForGamepad.Contains(i)) _seatsWaitingForGamepad.Add(i);
            }
        }

        /// <summary>まだどの席にも入っていないコントローラで、合図のボタンが押されたら空いている席に入れる。</summary>
        private void JoinPressedGamepads()
        {
            var pads = Gamepad.all;
            for (int p = 0; p < pads.Count; p++)
            {
                var pad = pads[p];
                if (FindSeat(pad) != null || !AnyJoinButton(pad, thisFrame: true)) continue;

                int seat = PickSeatForGamepad();
                if (seat < 0) return;   // 全部の席にコントローラが入っている
                AssignGamepad(seat, pad);
            }
        }

        /// <summary>
        /// 新しく入ってきたコントローラを入れる席。全部埋まっていれば -1。
        /// 優先順：①コントローラが抜けた席（挿し直したら元の席へ戻るように）→ ②誰も座っていない席 → ③コントローラだけ無い席
        /// </summary>
        private int PickSeatForGamepad()
        {
            for (int k = 0; k < _seatsWaitingForGamepad.Count; k++)
            {
                int i = _seatsWaitingForGamepad[k];
                if (i < _seats.Length && !_seats[i].HasGamepad) return i;
            }
            for (int i = 0; i < _seats.Length; i++)
            {
                if (!_seats[i].HasAnyDevice) return i;
            }
            for (int i = 0; i < _seats.Length; i++)
            {
                if (!_seats[i].HasGamepad) return i;
            }
            return -1;
        }

        /// <summary>席に入る合図のボタン（s_joinButtons）のどれかが、押された瞬間か（thisFrame）／押されているか。</summary>
        private static bool AnyJoinButton(Gamepad pad, bool thisFrame)
        {
            for (int i = 0; i < s_joinButtons.Length; i++)
            {
                var button = pad[s_joinButtons[i]];
                if (thisFrame ? button.wasPressedThisFrame : button.isPressed) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 席の入力

        /// <summary>
        /// 1 席ぶんの値を今フレームの入力で更新する。
        /// キーボード＋マウスとコントローラの両方がつながっていれば、両方を見て合わせる（移動は大きいほう、ボタンはどちらでも）。
        /// </summary>
        private void UpdateSeat(InputSeat seat)
        {
            var kb = seat.HasKeyboard ? seat.Keyboard : null;
            var mouse = seat.HasMouse ? seat.Mouse : null;
            var pad = seat.HasGamepad ? seat.Gamepad : null;

            // 席に入った合図のボタンを、そのまま「ジャンプ」などとして数えない
            if (pad != null && !seat.GamepadArmed)
            {
                if (AnyJoinButton(pad, thisFrame: false)) pad = null;
                else seat.GamepadArmed = true;
            }

            Vector2 kbMove = kb != null ? kb.ReadMove(_moveKeys) : Vector2.zero;
            Vector2 padMove = pad != null ? ApplyDeadzone(pad.leftStick.ReadValue()) : Vector2.zero;
            // 足すと 2 つ同時に倒したとき速くなりすぎるので、大きいほうだけ使う
            seat.Move = kbMove.sqrMagnitude >= padMove.sqrMagnitude ? kbMove : padMove;

            Vector2 aim = pad != null ? ApplyDeadzone(pad.rightStick.ReadValue()) : Vector2.zero;
            seat.AimStick = aim;

            // ポーズ中（timeScale = 0）でもカーソルは動かしたいので、実時間で進める
            Vector2 padDelta = aim * (_gamepadCursorSpeed * Time.unscaledDeltaTime);
            Vector2 mouseDelta = mouse != null ? mouse.Delta : Vector2.zero;
            seat.LookDelta = mouseDelta + padDelta;
            UpdateCursor(seat, mouse, mouseDelta, padDelta);

            // ボタンごとに、割り当てられたキー・マウスのボタン・コントローラのボタンのどれかが押されていれば「押されている」
            bool kbmUsed = false;
            int buttonCount = _bindingByButton.Length;
            for (int b = 0; b < buttonCount; b++)
            {
                var binding = _bindingByButton[b];
                bool pressed = false, down = false;
                if (binding != null)
                {
                    if (kb != null && binding.keys != null)
                    {
                        foreach (var key in binding.keys)
                        {
                            pressed |= kb.IsPressed(key);
                            down |= kb.WasPressedThisFrame(key);
                        }
                    }
                    if (mouse != null && binding.mouseButtons != null)
                    {
                        foreach (var mb in binding.mouseButtons)
                        {
                            pressed |= mouse.IsPressed(mb);
                            down |= mouse.WasPressedThisFrame(mb);
                            kbmUsed |= mouse.WasPressedThisFrame(mb);
                        }
                    }
                    if (pad != null && binding.gamepadButtons != null)
                    {
                        foreach (var gb in binding.gamepadButtons)
                        {
                            var control = pad[gb];
                            pressed |= control.isPressed;
                            down |= control.wasPressedThisFrame;
                        }
                    }
                }
                seat.SetButton(b, pressed, down);
            }

            // 「最後に触った機器」の判定。どちらも触っていないフレームは前の値のまま
            kbmUsed |= kb != null && (kb.AnyKeyDown || kbMove != Vector2.zero);
            kbmUsed |= mouseDelta != Vector2.zero;
            bool padUsed = pad != null && (padMove != Vector2.zero || aim != Vector2.zero || AnyJoinButton(pad, thisFrame: true));

            if (padUsed) seat.LastUsedScheme = InputSeatScheme.Gamepad;
            else if (kbmUsed) seat.LastUsedScheme = InputSeatScheme.KeyboardMouse;
            else if (!seat.HasAnyDevice) seat.LastUsedScheme = InputSeatScheme.None;
        }

        /// <summary>
        /// 席のカーソル位置を更新する。マウスが動いたフレームはマウスの位置、右スティックだけのフレームは積算した仮想カーソル。
        /// 両方持っている席でも 1 つのカーソルとしてつながるようにしてある。
        /// </summary>
        private static void UpdateCursor(InputSeat seat, MultiMousePointer mouse, Vector2 mouseDelta, Vector2 padDelta)
        {
            if (!seat.CursorInitialized)
            {
                seat.Cursor = mouse != null ? mouse.ScreenPosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                seat.CursorInitialized = true;
            }

            if (mouse != null && mouseDelta != Vector2.zero)
            {
                seat.Cursor = mouse.ScreenPosition;
            }
            else if (padDelta != Vector2.zero)
            {
                seat.Cursor = new Vector2(
                    Mathf.Clamp(seat.Cursor.x + padDelta.x, 0f, Screen.width),
                    Mathf.Clamp(seat.Cursor.y + padDelta.y, 0f, Screen.height));
                // マウスに持ち替えたときにカーソルが飛ばないよう、マウスの位置もそろえておく
                if (mouse != null) mouse.ScreenPosition = seat.Cursor;
            }
        }

        /// <summary>
        /// スティックのデッドゾーン処理。
        /// 単に「デッドゾーン以下なら 0」にするだけだと、縁を越えた瞬間に値が 0 → 0.15 に飛ぶので、
        /// 縁で 0・倒し切りで 1 になるよう残りの範囲を引き延ばす（向きは変えず、長さだけ直す）。
        /// </summary>
        private Vector2 ApplyDeadzone(Vector2 v)
        {
            float mag = v.magnitude;
            if (mag <= _stickDeadzone) return Vector2.zero;
            float scaled = Mathf.InverseLerp(_stickDeadzone, 1f, mag);
            return v / mag * Mathf.Min(scaled, 1f);
        }
    }
}
