using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary>
    /// 物理キーボードを Raw Input でデバイス別に読み、キーボードごとに独立した入力状態を持つ。
    /// OS 側では 2 台分のキー入力が合成されて 1 台に見えるので、こちらで台ごとに分ける。
    /// 割り当ては MultiMouseManager と同じく「最初にキーを押した順」。
    /// <see cref="SetFixedDevices"/> で番号ごとのデバイスを決めておけば、押した順に関係なくその番号になる（固定登録）。
    ///
    /// Raw Input を登録すると Unity の Keyboard.current が止まるので、受け取った入力を
    /// <see cref="UnityKeyboardBridge"/> で Unity へ流し直す（既存のスクリプトがそのまま動く）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MultiKeyboardManager : MonoBehaviour
    {
        [Header("キーボード")]
        [SerializeField] private int _maxKeyboards = 2;
        [SerializeField] private Color[] _keyboardColors =
        {
            new Color(0.2f, 0.8f, 1f),
            new Color(1f, 0.45f, 0.2f),
            new Color(0.5f, 1f, 0.4f),
            new Color(1f, 0.9f, 0.3f),
        };

        [Header("フォーカス")]
        [Tooltip("ゲームの画面が選ばれていない間の入力を無視する。Raw Input は他のアプリで打った文字も拾うため")]
        [SerializeField] private bool _ignoreWhileUnfocused = true;

        [Header("Unity の入力へ流し直す")]
        [Tooltip("受け取ったキー入力を Unity の Keyboard.current へ流し直す。切ると、このコンポーネントが動いている間は" +
                 " Keyboard.current・Input Actions のキーボード操作が一切効かなくなる")]
        [SerializeField] private bool _forwardToUnityInput = true;

        [Header("フォールバック")]
        [Tooltip("Raw Input が使えない環境では 1 台目を通常のキーボードに追従させる")]
        [SerializeField] private bool _fallbackToSystemKeyboard = true;

        private readonly List<RawKeyEvent> _events = new List<RawKeyEvent>();
        private readonly Dictionary<IntPtr, int> _deviceToKeyboard = new Dictionary<IntPtr, int>();
        private readonly UnityKeyboardBridge _bridge = new UnityKeyboardBridge();
        private MultiKeyboardDevice[] _keyboards;
        // 固定登録：番号ごとに使うデバイスの名前（Windows のデバイスパス）。空の番号は今までどおり押した順
        private string[] _fixedDevices = Array.Empty<string>();
        // 名前を取るのは Windows への問い合わせになるので、ハンドルごとに覚えておく
        private readonly Dictionary<IntPtr, string> _deviceNames = new Dictionary<IntPtr, string>();
        private readonly List<IntPtr> _connected = new List<IntPtr>();
        private RawInputKeyboard _reader;
        private int _seenRestoreCount;
        // onBeforeUpdate と Update の両方から呼ばれるので、1 フレームに 1 回だけ処理するための印
        private bool _processedThisFrame;

        public static MultiKeyboardManager Instance { get; private set; }

        /// <summary>台数を決める（Awake の前、GameObject を非アクティブにしている間だけ効く）。<see cref="InputControl.InputSeatManager.CreatePersistent"/> が使う。</summary>
        internal int MaxKeyboardsSetting { set => _maxKeyboards = value; }

        /// <summary>Unity に登録を取られて取り戻した回数（動作確認用）。</summary>
        public int RegistrationRestoreCount => _reader?.RegistrationRestoreCount ?? 0;

        /// <summary>Raw Input が動いているか。false のときは複数キーボードを分離できていない。</summary>
        public bool RawInputAvailable => _reader != null && _reader.IsRunning;
        /// <summary>受信した WM_INPUT の総数（動作確認用）。</summary>
        public int RawEventCount => _reader?.EventCount ?? 0;
        /// <summary>初期化に失敗した理由。成功なら null。</summary>
        public string StatusMessage { get; private set; }

        /// <summary>デバイスが割り当てられているキーボードの数。</summary>
        public int AssignedKeyboardCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _keyboards.Length; i++)
                {
                    if (_keyboards[i].IsAssigned) n++;
                }
                return n;
            }
        }

        public IReadOnlyList<MultiKeyboardDevice> Keyboards => _keyboards;

        /// <summary>範囲外なら null。</summary>
        public MultiKeyboardDevice GetKeyboard(int index)
        {
            if (_keyboards == null || index < 0 || index >= _keyboards.Length) return null;
            return _keyboards[index];
        }

        /// <summary>
        /// デバイスと番号の割り当てをやり直す（次にキーを押したキーボードが 0 番になる）。
        /// 固定登録がある番号は、つながっていればすぐ登録したキーボードに戻る。
        /// </summary>
        public void ClearAssignments()
        {
            _deviceToKeyboard.Clear();
            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].ResetAssignment();
            }
            BindFixedDevices();
        }

        /// <summary>
        /// 番号ごとに使うキーボードを決める（固定登録）。deviceNames[i] が i 番のデバイスの名前で、
        /// <see cref="GetAssignedDeviceNames"/> で取ったものをそのまま渡せばよい。null・空の番号は押した順のまま。
        /// 呼ぶと割り当てをやり直し、今つながっている登録済みのキーボードはキーを押す前から割り当てる。
        /// </summary>
        public void SetFixedDevices(IReadOnlyList<string> deviceNames)
        {
            _fixedDevices = new string[_keyboards.Length];
            if (deviceNames != null)
            {
                for (int i = 0; i < _fixedDevices.Length && i < deviceNames.Count; i++)
                {
                    _fixedDevices[i] = deviceNames[i];
                }
            }
            ClearAssignments();
        }

        /// <summary>番号ごとの、いま割り当たっているデバイスの名前（未割り当ては空文字）。固定登録の保存用。</summary>
        public string[] GetAssignedDeviceNames()
        {
            var names = new string[_keyboards.Length];
            for (int i = 0; i < _keyboards.Length; i++)
            {
                var kb = _keyboards[i];
                // ハンドル 0（リモートデスクトップなど、どの機器か分からない入力）は登録しても次に見分けられない
                names[i] = kb.IsAssigned && kb.DeviceHandle != IntPtr.Zero ? kb.DeviceName : string.Empty;
            }
            return names;
        }

        /// <summary>その番号に固定登録があるか。</summary>
        public bool HasFixedDevice(int index)
        {
            return index >= 0 && index < _fixedDevices.Length && !string.IsNullOrEmpty(_fixedDevices[index]);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            _maxKeyboards = Mathf.Max(1, _maxKeyboards);
            _keyboards = new MultiKeyboardDevice[_maxKeyboards];
            for (int i = 0; i < _maxKeyboards; i++)
            {
                _keyboards[i] = new MultiKeyboardDevice(i)
                {
                    Color = _keyboardColors != null && _keyboardColors.Length > 0
                        ? _keyboardColors[i % _keyboardColors.Length]
                        : Color.white,
                };
            }
            _fixedDevices = new string[_maxKeyboards];
        }

        private void OnEnable()
        {
            if (Instance != this) return;   // 重複インスタンス（Awake で Destroy 済み）では何もしない

            // ドメインリロード（再生中のスクリプト再コンパイル）の前に必ず止める。
            // 止めずにアセンブリが破棄されると、残留したスレッドが無効な関数ポインタを叩いて Editor が落ちる。
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopReader;
#endif
            Application.quitting += StopReader;
            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;

            EnsureReader();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopReader;
#endif
            Application.quitting -= StopReader;
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;

            StopReader();
        }

        private void OnDestroy()
        {
            StopReader();
            if (Instance == this) Instance = null;
        }

        private void EnsureReader()
        {
            if (_reader != null) return;

            _reader = new RawInputKeyboard();
            _seenRestoreCount = 0;
            if (_reader.Start())
            {
                StatusMessage = null;
                BindFixedDevices();
            }
            else
            {
                StatusMessage = _reader.LastError ?? "Raw Input を開始できませんでした";
                Debug.LogWarning("[MultiKeyboard] " + StatusMessage);
            }
        }

        private void StopReader()
        {
            if (_reader == null) return;

            var reader = _reader;
            _reader = null;
            reader.Dispose();

            // 流し直していたキーが Unity 側で押されたまま残らないよう、全部離して書き込んでおく
            _bridge.ReleaseAll();
            if (_forwardToUnityInput) _bridge.Flush();

            // デバイスハンドルは作り直すと変わるので、割り当ても捨てる（固定登録の名前は残す）
            _deviceToKeyboard.Clear();
            _deviceNames.Clear();
            if (_keyboards == null) return;
            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].ResetAssignment();
            }
        }

        /// <summary>
        /// Input System が入力を処理する直前。ここで流し直すと、同じフレームのうちに Keyboard.current に反映される。
        /// （Update で流すと、Unity に届くのが次のフレームになる）
        /// </summary>
        private void OnBeforeInputUpdate()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (_processedThisFrame || !RawInputAvailable) return;
            ProcessRawInput();
        }

        private void Update()
        {
            // 再コンパイルで一度止めた場合はここで作り直す
            EnsureReader();

            if (RawInputAvailable)
            {
                // Input System の更新の仕方を変えたなどで onBeforeUpdate が来なかったときの保険
                if (!_processedThisFrame) ProcessRawInput();
            }
            else
            {
                ProcessFallback();
            }
        }

        private void LateUpdate()
        {
            _processedThisFrame = false;
        }

        private void ProcessRawInput()
        {
            _processedThisFrame = true;

            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].BeginFrame();
            }

            _reader.Poll(_events);
            ReleaseIfRegistrationWasLost();

            if (_ignoreWhileUnfocused && !Application.isFocused)
            {
                // 他のアプリで打った分は捨てる。押しっぱなしのまま戻ってこないよう、全部離しておく
                for (int i = 0; i < _keyboards.Length; i++)
                {
                    _keyboards[i].ReleaseAll();
                }
                _bridge.ReleaseAll();
            }
            else
            {
                for (int i = 0; i < _events.Count; i++)
                {
                    var e = _events[i];
                    Key key = RawKeyMap.ToKey(e);
                    if (key == Key.None) continue;

                    // Unity へは、割り当てに関係なく全部のキーボードの入力を流す（ふつうのキーボードと同じ動きにする）
                    _bridge.OnKey(e.DeviceHandle, key, e.IsUp);

                    // 離したイベントでは割り当てない（割り当て直後に押しっぱなし扱いになるのを防ぐ）
                    var keyboard = Resolve(e.DeviceHandle, assignIfNew: !e.IsUp);
                    if (keyboard == null) continue;   // 台数を超えたデバイスは無視

                    if (e.IsUp) keyboard.Release(key);
                    else keyboard.Press(key);
                }
            }

            if (_forwardToUnityInput) _bridge.Flush();
        }

        private void ProcessFallback()
        {
            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].BeginFrame();
            }
            if (!_fallbackToSystemKeyboard || Keyboard.current == null) return;

            // Raw Input が使えないときは Unity が普通にキーボードを読めているので、それを 0 番に映す
            var keyboard = _keyboards[0];
            keyboard.IsAssigned = true;
            keyboard.DeviceName = "(fallback) " + Keyboard.current.displayName;

            var keys = Keyboard.current.allKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                KeyControl k = keys[i];
                if (k.wasPressedThisFrame) keyboard.Press(k.keyCode);
                if (k.wasReleasedThisFrame) keyboard.Release(k.keyCode);
            }
        }

        /// <summary>
        /// 登録を Unity に取られていた間の「離した」は届いていないので、押しっぱなしにならないよう全部離す。
        /// </summary>
        private void ReleaseIfRegistrationWasLost()
        {
            int restored = _reader.RegistrationRestoreCount;
            if (restored == _seenRestoreCount) return;
            _seenRestoreCount = restored;

            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].ReleaseAll();
            }
            _bridge.ReleaseAll();
            Debug.Log($"[MultiKeyboard] Raw Input の登録を {_reader.LastRegistrationThief} に取られていたので取り戻しました（{restored} 回目）");
        }

        private MultiKeyboardDevice Resolve(IntPtr device, bool assignIfNew)
        {
            if (_deviceToKeyboard.TryGetValue(device, out int index))
                return _keyboards[index];
            if (!assignIfNew) return null;

            string name = GetCachedDeviceName(device);
            int slot = FindFixedSlot(name);
            bool isFixed = slot >= 0;
            if (!isFixed) slot = FindFreeSlot();
            if (slot < 0) return null;

            Assign(slot, device, name, isFixed);
            return _keyboards[slot];
        }

        /// <summary>今つながっている、固定登録したキーボードを、キーを押す前から割り当てる。</summary>
        private void BindFixedDevices()
        {
            if (!RawInputAvailable || _keyboards == null) return;
            bool any = false;
            for (int i = 0; i < _fixedDevices.Length; i++) any |= HasFixedDevice(i);
            if (!any) return;

            _reader.GetConnectedDevices(_connected);
            for (int i = 0; i < _connected.Count; i++)
            {
                IntPtr device = _connected[i];
                if (_deviceToKeyboard.ContainsKey(device)) continue;
                string name = GetCachedDeviceName(device);
                int slot = FindFixedSlot(name);
                if (slot >= 0) Assign(slot, device, name, true);
            }
        }

        /// <summary>まだ埋まっていない、その名前で固定登録した番号。無ければ -1。</summary>
        private int FindFixedSlot(string name)
        {
            for (int i = 0; i < _fixedDevices.Length; i++)
            {
                if (_keyboards[i].IsAssigned || !HasFixedDevice(i)) continue;
                if (string.Equals(_fixedDevices[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 登録していないキーボードに渡す番号。固定登録の無い番号を先に使い、
        /// 足りなければ「登録したキーボードがつながっていない番号」を使う（抜けた分を別のキーボードで代わりに遊べるように）。
        /// </summary>
        private int FindFreeSlot()
        {
            for (int i = 0; i < _keyboards.Length; i++)
            {
                if (!_keyboards[i].IsAssigned && !HasFixedDevice(i)) return i;
            }
            for (int i = 0; i < _keyboards.Length; i++)
            {
                if (!_keyboards[i].IsAssigned) return i;
            }
            return -1;
        }

        private void Assign(int slot, IntPtr device, string name, bool isFixed)
        {
            _deviceToKeyboard.Add(device, slot);
            _keyboards[slot].IsAssigned = true;
            _keyboards[slot].DeviceHandle = device;
            _keyboards[slot].DeviceName = name;
            Debug.Log(isFixed
                ? $"[MultiKeyboard] キーボード {slot} に登録済みのデバイスを割り当て: {name}"
                : $"[MultiKeyboard] キーボード {slot} にデバイスを割り当て: {name}");
        }

        private string GetCachedDeviceName(IntPtr device)
        {
            if (!_deviceNames.TryGetValue(device, out string name))
            {
                name = _reader.GetDeviceName(device);
                _deviceNames.Add(device, name);
            }
            return name;
        }
    }
}
