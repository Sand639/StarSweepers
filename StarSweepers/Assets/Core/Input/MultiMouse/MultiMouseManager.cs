using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectEL4S.MultiMouse
{
    /// <summary>
    /// 物理マウスを Raw Input でデバイス別に読み、マウスごとに独立した仮想ポインタを動かす。
    /// OS のカーソルは 2 台分が合成されて 1 つしか動かないので、こちらで座標を自前に持つ。
    /// 割り当ては「最初に動かした順」。<see cref="SetFixedDevices"/> で番号ごとのデバイスを決めておけば、
    /// 動かした順に関係なくその番号になる（固定登録）。
    ///
    /// Raw Input を登録すると Unity の Mouse.current.delta が止まるので、受け取った移動量を
    /// <see cref="UnityMouseBridge"/> で Unity へ流し直す（既存のスクリプトがそのまま動く）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MultiMouseManager : MonoBehaviour
    {
        [Header("ポインタ")]
        [SerializeField] private int _maxPointers = 2;
        [Tooltip("生の移動量に掛ける倍率（1 でだいたい 1 カウント = 1 ピクセル）")]
        [SerializeField] private float _sensitivity = 1f;
        [SerializeField] private Color[] _pointerColors =
        {
            new Color(0.2f, 0.8f, 1f),
            new Color(1f, 0.45f, 0.2f),
            new Color(0.5f, 1f, 0.4f),
            new Color(1f, 0.9f, 0.3f),
        };

        [Tooltip("クリックしたマウスだけ割り当てる（動かしただけでは割り当てない）。登録の画面などで、触れただけのマウスを拾わないために使う")]
        [SerializeField] private bool _assignOnClickOnly = false;

        [Header("OS カーソル")]
        [Tooltip("OS の矢印カーソルを隠す（2 台のマウスで動く合成カーソルが邪魔になるため）")]
        [SerializeField] private bool _hideSystemCursor = true;
        [Tooltip("OS カーソルをウィンドウ内に閉じ込める。ビルドで他アプリを誤クリックしたくない場合に ON")]
        [SerializeField] private bool _confineSystemCursor = false;

        [Header("フォーカス")]
        [Tooltip("ゲームの画面が選ばれていない間の入力を無視する。Raw Input は他のウィンドウでの操作も拾うため")]
        [SerializeField] private bool _ignoreWhileUnfocused = true;

        [Header("Unity の入力へ流し直す")]
        [Tooltip("受け取った移動量を Unity の Mouse.current.delta へ流し直す。切ると、このコンポーネントが動いている間は" +
                 " Mouse.current.delta（カメラを回す操作など）が 0 のままになる。クリック・位置・ホイールは切っても動く")]
        [SerializeField] private bool _forwardToUnityInput = true;

        [Header("フォールバック")]
        [Tooltip("Raw Input が使えない環境では 1 本目のポインタを通常のマウスに追従させる")]
        [SerializeField] private bool _fallbackToSystemMouse = true;

        private readonly List<RawMouseFrame> _frames = new List<RawMouseFrame>();
        private readonly Dictionary<IntPtr, int> _deviceToPointer = new Dictionary<IntPtr, int>();
        private readonly UnityMouseBridge _bridge = new UnityMouseBridge();
        private MultiMousePointer[] _pointers;
        // 固定登録：番号ごとに使うデバイスの名前（Windows のデバイスパス）。空の番号は今までどおり動かした順
        private string[] _fixedDevices = Array.Empty<string>();
        // 名前を取るのは Windows への問い合わせになるので、ハンドルごとに覚えておく
        private readonly Dictionary<IntPtr, string> _deviceNames = new Dictionary<IntPtr, string>();
        private readonly List<IntPtr> _connected = new List<IntPtr>();
        private RawInputMouse _reader;
        private int _seenRestoreCount;
        // onBeforeUpdate と Update の両方から呼ばれるので、1 フレームに 1 回だけ処理するための印
        private bool _processedThisFrame;

        /// <summary>Unity に登録を取られて取り戻した回数（動作確認用）。</summary>
        public int RegistrationRestoreCount => _reader?.RegistrationRestoreCount ?? 0;

        public static MultiMouseManager Instance { get; private set; }

        /// <summary>台数を決める（Awake の前、GameObject を非アクティブにしている間だけ効く）。<see cref="InputControl.InputSeatManager.CreatePersistent"/> が使う。</summary>
        internal int MaxPointersSetting { set => _maxPointers = value; }

        /// <summary>画面のマウスカーソルを隠すか（有効にする前に決める）。</summary>
        internal bool HideSystemCursorSetting { set => _hideSystemCursor = value; }

        /// <summary>Raw Input が動いているか。false のときは複数マウスを分離できていない。</summary>
        public bool RawInputAvailable => _reader != null && _reader.IsRunning;
        /// <summary>受信した WM_INPUT の総数（動作確認用）。</summary>
        public int RawEventCount => _reader?.EventCount ?? 0;
        /// <summary>初期化に失敗した理由。成功なら null。</summary>
        public string StatusMessage { get; private set; }

        /// <summary>デバイスが割り当てられているポインタの数。</summary>
        public int AssignedPointerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pointers.Length; i++)
                {
                    if (_pointers[i].IsAssigned) n++;
                }
                return n;
            }
        }

        public IReadOnlyList<MultiMousePointer> Pointers => _pointers;

        /// <summary>範囲外なら null。</summary>
        public MultiMousePointer GetPointer(int index)
        {
            if (_pointers == null || index < 0 || index >= _pointers.Length) return null;
            return _pointers[index];
        }

        /// <summary>クリックしたマウスだけ割り当てるか（動かしただけでは割り当てない）。</summary>
        public bool AssignOnClickOnly
        {
            get => _assignOnClickOnly;
            set => _assignOnClickOnly = value;
        }

        /// <summary>
        /// デバイスとポインタの割り当てをやり直す（次に動いたマウスが 1 本目になる）。
        /// 固定登録がある番号は、つながっていればすぐ登録したマウスに戻る。
        /// </summary>
        public void ClearAssignments()
        {
            _deviceToPointer.Clear();
            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].ResetAssignment();
                _pointers[i].ScreenPosition = DefaultPosition(i);
            }
            BindFixedDevices();
        }

        /// <summary>
        /// 番号ごとに使うマウスを決める（固定登録）。deviceNames[i] が i 番のデバイスの名前で、
        /// <see cref="GetAssignedDeviceNames"/> で取ったものをそのまま渡せばよい。null・空の番号は動かした順のまま。
        /// 呼ぶと割り当てをやり直し、今つながっている登録済みのマウスは動かす前から割り当てる。
        /// </summary>
        public void SetFixedDevices(IReadOnlyList<string> deviceNames)
        {
            _fixedDevices = new string[_pointers.Length];
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
            var names = new string[_pointers.Length];
            for (int i = 0; i < _pointers.Length; i++)
            {
                var p = _pointers[i];
                // ハンドル 0（リモートデスクトップなど、どの機器か分からない入力）は登録しても次に見分けられない
                names[i] = p.IsAssigned && p.DeviceHandle != IntPtr.Zero ? p.DeviceName : string.Empty;
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

            _maxPointers = Mathf.Max(1, _maxPointers);
            _pointers = new MultiMousePointer[_maxPointers];
            for (int i = 0; i < _maxPointers; i++)
            {
                _pointers[i] = new MultiMousePointer(i)
                {
                    Color = _pointerColors != null && _pointerColors.Length > 0
                        ? _pointerColors[i % _pointerColors.Length]
                        : Color.white,
                    ScreenPosition = DefaultPosition(i),
                };
            }
            _fixedDevices = new string[_maxPointers];
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

            if (_hideSystemCursor) Cursor.visible = false;
            if (_confineSystemCursor) Cursor.lockState = CursorLockMode.Confined;
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopReader;
#endif
            Application.quitting -= StopReader;
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;

            StopReader();

            if (_hideSystemCursor) Cursor.visible = true;
            if (_confineSystemCursor) Cursor.lockState = CursorLockMode.None;
        }

        private void OnDestroy()
        {
            StopReader();
            if (Instance == this) Instance = null;
        }

        private void EnsureReader()
        {
            if (_reader != null) return;

            _reader = new RawInputMouse();
            _seenRestoreCount = 0;
            if (_reader.Start())
            {
                StatusMessage = null;
                BindFixedDevices();
            }
            else
            {
                StatusMessage = _reader.LastError ?? "Raw Input を開始できませんでした";
                Debug.LogWarning("[MultiMouse] " + StatusMessage);
            }
        }

        private void StopReader()
        {
            if (_reader == null) return;

            var reader = _reader;
            _reader = null;
            reader.Dispose();
            _bridge.Clear();

            // デバイスハンドルは作り直すと変わるので、割り当ても捨てる（固定登録の名前は残す）
            _deviceToPointer.Clear();
            _deviceNames.Clear();
            if (_pointers == null) return;
            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].ResetAssignment();
            }
        }

        /// <summary>
        /// Input System が入力を処理する直前。ここで流し直すと、同じフレームのうちに Mouse.current.delta に反映される。
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

            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].BeginFrame();
            }

            _reader.Poll(_frames);
            ReleaseIfRegistrationWasLost();

            if (_ignoreWhileUnfocused && !Application.isFocused)
            {
                // 他のウィンドウでの操作は捨てる。押しっぱなしのまま戻ってこないよう、全部離しておく
                for (int i = 0; i < _pointers.Length; i++)
                {
                    _pointers[i].ReleaseAll();
                }
                _bridge.Clear();
                return;
            }

            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];

                // Unity へは、割り当てに関係なく全部のマウスの移動量を流す（ふつうのマウスと同じ動きにする）
                if (!frame.HasAbsolute) _bridge.Add(frame.DeltaX, frame.DeltaY);

                var pointer = Resolve(frame.DeviceHandle, assignIfNew: !_assignOnClickOnly || frame.ButtonDown != 0);
                if (pointer == null) continue;   // ポインタ数を超えたデバイス・まだ割り当てないデバイスは無視
                Apply(pointer, frame);
            }

            if (_forwardToUnityInput) _bridge.Flush();
            else _bridge.Clear();
        }

        private void ProcessFallback()
        {
            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].BeginFrame();
            }

            if (_fallbackToSystemMouse && Mouse.current != null)
            {
                var pointer = _pointers[0];
                pointer.IsAssigned = true;
                pointer.DeviceName = "(fallback) " + Mouse.current.displayName;

                Vector2 pos = Mouse.current.position.ReadValue();
                pointer.Delta = pos - pointer.ScreenPosition;
                pointer.RawDelta = pointer.Delta;
                pointer.ScreenPosition = Clamp(pos);
                pointer.Wheel = Mouse.current.scroll.ReadValue().y;

                int down = 0, up = 0;
                if (Mouse.current.leftButton.wasPressedThisFrame) down |= 1 << 0;
                if (Mouse.current.leftButton.wasReleasedThisFrame) up |= 1 << 0;
                if (Mouse.current.rightButton.wasPressedThisFrame) down |= 1 << 1;
                if (Mouse.current.rightButton.wasReleasedThisFrame) up |= 1 << 1;
                if (Mouse.current.middleButton.wasPressedThisFrame) down |= 1 << 2;
                if (Mouse.current.middleButton.wasReleasedThisFrame) up |= 1 << 2;
                pointer.ApplyButtons(down, up);
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

            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].ReleaseAll();
            }
            Debug.Log($"[MultiMouse] Raw Input の登録を {_reader.LastRegistrationThief} に取られていたので取り戻しました（{restored} 回目）");
        }

        private MultiMousePointer Resolve(IntPtr device, bool assignIfNew)
        {
            if (_deviceToPointer.TryGetValue(device, out int index))
                return _pointers[index];
            if (!assignIfNew) return null;

            string name = GetCachedDeviceName(device);
            int slot = FindFixedSlot(name);
            bool isFixed = slot >= 0;
            if (!isFixed) slot = FindFreeSlot();
            if (slot < 0) return null;

            Assign(slot, device, name, isFixed);
            return _pointers[slot];
        }

        /// <summary>今つながっている、固定登録したマウスを、動かす前から割り当てる。</summary>
        private void BindFixedDevices()
        {
            if (!RawInputAvailable || _pointers == null) return;
            bool any = false;
            for (int i = 0; i < _fixedDevices.Length; i++) any |= HasFixedDevice(i);
            if (!any) return;

            _reader.GetConnectedDevices(_connected);
            for (int i = 0; i < _connected.Count; i++)
            {
                IntPtr device = _connected[i];
                if (_deviceToPointer.ContainsKey(device)) continue;
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
                if (_pointers[i].IsAssigned || !HasFixedDevice(i)) continue;
                if (string.Equals(_fixedDevices[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 登録していないマウスに渡す番号。固定登録の無い番号を先に使い、
        /// 足りなければ「登録したマウスがつながっていない番号」を使う（抜けた分を別のマウスで代わりに遊べるように）。
        /// </summary>
        private int FindFreeSlot()
        {
            for (int i = 0; i < _pointers.Length; i++)
            {
                if (!_pointers[i].IsAssigned && !HasFixedDevice(i)) return i;
            }
            for (int i = 0; i < _pointers.Length; i++)
            {
                if (!_pointers[i].IsAssigned) return i;
            }
            return -1;
        }

        private void Assign(int slot, IntPtr device, string name, bool isFixed)
        {
            _deviceToPointer.Add(device, slot);
            _pointers[slot].IsAssigned = true;
            _pointers[slot].DeviceHandle = device;
            _pointers[slot].DeviceName = name;
            Debug.Log(isFixed
                ? $"[MultiMouse] ポインタ {slot} に登録済みのデバイスを割り当て: {name}"
                : $"[MultiMouse] ポインタ {slot} にデバイスを割り当て: {name}");
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

        private void Apply(MultiMousePointer pointer, in RawMouseFrame frame)
        {
            Vector2 position = pointer.ScreenPosition;

            if (frame.HasAbsolute)
            {
                // 絶対座標デバイス（ペンタブ・リモートデスクトップ等）はそのまま座標を置き換える
                position = new Vector2(frame.AbsoluteX * Screen.width, (1f - frame.AbsoluteY) * Screen.height);
                pointer.Delta = position - pointer.ScreenPosition;
            }
            else
            {
                // Raw Input の Y は下方向が正。Unity のスクリーン座標は上方向が正なので反転する
                var rawDelta = new Vector2(frame.DeltaX, -frame.DeltaY);
                var delta = rawDelta * _sensitivity;
                position += delta;
                pointer.Delta += delta;
                pointer.RawDelta += rawDelta;
            }

            pointer.ScreenPosition = Clamp(position);
            pointer.Wheel += frame.Wheel / 120f;
            pointer.ApplyButtons(frame.ButtonDown, frame.ButtonUp);
        }

        private static Vector2 Clamp(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, 0f, Screen.width),
                Mathf.Clamp(position.y, 0f, Screen.height));
        }

        private static Vector2 DefaultPosition(int index)
        {
            float step = Screen.width * 0.15f;
            float x = Screen.width * 0.5f + (index - 0.5f) * step;
            return new Vector2(Mathf.Clamp(x, 0f, Screen.width), Screen.height * 0.5f);
        }
    }
}
