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
        private RawInputMouse _reader;
        private int _seenRestoreCount;
        // onBeforeUpdate と Update の両方から呼ばれるので、1 フレームに 1 回だけ処理するための印
        private bool _processedThisFrame;

        /// <summary>Unity に登録を取られて取り戻した回数（動作確認用）。</summary>
        public int RegistrationRestoreCount => _reader?.RegistrationRestoreCount ?? 0;

        public static MultiMouseManager Instance { get; private set; }

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

        /// <summary>デバイスとポインタの割り当てをやり直す（次に動いたマウスが 1 本目になる）。</summary>
        public void ClearAssignments()
        {
            _deviceToPointer.Clear();
            for (int i = 0; i < _pointers.Length; i++)
            {
                _pointers[i].ResetAssignment();
                _pointers[i].ScreenPosition = DefaultPosition(i);
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

            // デバイスハンドルは作り直すと変わるので、割り当ても捨てる
            _deviceToPointer.Clear();
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

                var pointer = Resolve(frame.DeviceHandle);
                if (pointer == null) continue;   // ポインタ数を超えたデバイスは無視
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

        private MultiMousePointer Resolve(IntPtr device)
        {
            if (_deviceToPointer.TryGetValue(device, out int index))
                return _pointers[index];

            for (int i = 0; i < _pointers.Length; i++)
            {
                if (_pointers[i].IsAssigned) continue;

                _deviceToPointer.Add(device, i);
                _pointers[i].IsAssigned = true;
                _pointers[i].DeviceHandle = device;
                _pointers[i].DeviceName = _reader.GetDeviceName(device);
                Debug.Log($"[MultiMouse] ポインタ {i} にデバイスを割り当て: {_pointers[i].DeviceName}");
                return _pointers[i];
            }
            return null;
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
