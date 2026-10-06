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
        private RawInputKeyboard _reader;
        private int _seenRestoreCount;
        // onBeforeUpdate と Update の両方から呼ばれるので、1 フレームに 1 回だけ処理するための印
        private bool _processedThisFrame;

        public static MultiKeyboardManager Instance { get; private set; }

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

        /// <summary>デバイスと番号の割り当てをやり直す（次にキーを押したキーボードが 0 番になる）。</summary>
        public void ClearAssignments()
        {
            _deviceToKeyboard.Clear();
            for (int i = 0; i < _keyboards.Length; i++)
            {
                _keyboards[i].ResetAssignment();
            }
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

            // デバイスハンドルは作り直すと変わるので、割り当ても捨てる
            _deviceToKeyboard.Clear();
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

            for (int i = 0; i < _keyboards.Length; i++)
            {
                if (_keyboards[i].IsAssigned) continue;

                _deviceToKeyboard.Add(device, i);
                _keyboards[i].IsAssigned = true;
                _keyboards[i].DeviceHandle = device;
                _keyboards[i].DeviceName = _reader.GetDeviceName(device);
                Debug.Log($"[MultiKeyboard] キーボード {i} にデバイスを割り当て: {_keyboards[i].DeviceName}");
                return _keyboards[i];
            }
            return null;
        }
    }
}
