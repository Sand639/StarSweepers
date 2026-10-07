using System;
using System.Collections.Generic;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
using System.Threading;
#endif

namespace ProjectEL4S.MultiMouse
{
    /// <summary>1フレーム分・1デバイス分の生入力。</summary>
    public struct RawMouseFrame
    {
        public IntPtr DeviceHandle;
        public int DeltaX;
        public int DeltaY;
        public int Wheel;
        /// <summary>押された瞬間のビット（bit0:左 bit1:右 bit2:中）。</summary>
        public int ButtonDown;
        /// <summary>離された瞬間のビット（bit0:左 bit1:右 bit2:中）。</summary>
        public int ButtonUp;
        /// <summary>絶対座標デバイス（ペンタブ・リモートデスクトップ等）かどうか。</summary>
        public bool HasAbsolute;
        /// <summary>絶対座標の 0..1 正規化値。HasAbsolute のときだけ有効。</summary>
        public float AbsoluteX;
        public float AbsoluteY;
    }

    /// <summary>
    /// Windows の Raw Input (WM_INPUT) をデバイス単位で読む。
    /// Unity の Input System は複数マウスを 1 つのカーソルに合成してしまうため、こちらで直接拾う。
    /// 専用スレッドでメッセージ専用ウィンドウを作り、そこで受信して溜め込む。
    ///
    /// エディタで使ううえでの注意（守らないと Editor がクラッシュする）:
    ///   - ウィンドウクラス名は Start ごとにユニークにする。ドメインリロード後に前回のクラスを
    ///     再利用すると、破棄済みアセンブリの関数ポインタへディスパッチされて即死する。
    ///   - 停止処理はドメインリロード前に必ず走らせる（MultiMouseManager 側でフックしている）。
    ///   - WndProc から managed 例外を native 側へ抜かせない。抜けるとプロセスが落ちる。
    /// </summary>
    public sealed class RawInputMouse : IDisposable
    {
        private sealed class Accum
        {
            public int DeltaX;
            public int DeltaY;
            public int Wheel;
            public int ButtonDown;
            public int ButtonUp;
            public bool HasAbsolute;
            public float AbsoluteX;
            public float AbsoluteY;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<IntPtr, Accum> _accum = new Dictionary<IntPtr, Accum>();
        private readonly List<IntPtr> _order = new List<IntPtr>();
        private int _eventCount;
        private int _restoreCount;
        private string _lastThief;

        public bool IsRunning { get; private set; }
        public string LastError { get; private set; }

        /// <summary>受信した WM_INPUT の総数。0 のままなら受信に失敗している。</summary>
        public int EventCount
        {
            get { lock (_lock) { return _eventCount; } }
        }

        /// <summary>
        /// 登録を他に取られていたので取り戻した回数。増えた瞬間は、離したボタンを取りこぼしている可能性がある。
        /// エディタでは Game ビューにフォーカスが戻るたびに Unity が登録し直すため、そのたびに 1 増える。
        /// </summary>
        public int RegistrationRestoreCount
        {
            get { lock (_lock) { return _restoreCount; } }
        }

        /// <summary>最後に登録を持っていった相手（ウィンドウのクラス名）。原因の切り分け用。</summary>
        public string LastRegistrationThief
        {
            get { lock (_lock) { return _lastThief; } }
        }

        private void Push(IntPtr device, int dx, int dy, int wheel, int down, int up, bool hasAbs, float ax, float ay)
        {
            lock (_lock)
            {
                if (!_accum.TryGetValue(device, out var a))
                {
                    a = new Accum();
                    _accum.Add(device, a);
                    _order.Add(device);
                }
                a.DeltaX += dx;
                a.DeltaY += dy;
                a.Wheel += wheel;
                a.ButtonDown |= down;
                a.ButtonUp |= up;
                if (hasAbs)
                {
                    a.HasAbsolute = true;
                    a.AbsoluteX = ax;
                    a.AbsoluteY = ay;
                }
                _eventCount++;
            }
        }

        /// <summary>溜まった入力を取り出して空にする。並び順は「最初に入力が来た順」で安定。</summary>
        public void Poll(List<RawMouseFrame> results)
        {
            results.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _order.Count; i++)
                {
                    var handle = _order[i];
                    var a = _accum[handle];
                    results.Add(new RawMouseFrame
                    {
                        DeviceHandle = handle,
                        DeltaX = a.DeltaX,
                        DeltaY = a.DeltaY,
                        Wheel = a.Wheel,
                        ButtonDown = a.ButtonDown,
                        ButtonUp = a.ButtonUp,
                        HasAbsolute = a.HasAbsolute,
                        AbsoluteX = a.AbsoluteX,
                        AbsoluteY = a.AbsoluteY,
                    });
                    a.DeltaX = 0;
                    a.DeltaY = 0;
                    a.Wheel = 0;
                    a.ButtonDown = 0;
                    a.ButtonUp = 0;
                    a.HasAbsolute = false;
                }
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        private const int WM_CLOSE = 0x0010;
        private const int WM_QUIT = 0x0012;
        private const int WM_INPUT = 0x00FF;
        private const uint PM_REMOVE = 0x0001;
        private const uint QS_ALLINPUT = 0x04FF;
        private const uint MWMO_INPUTAVAILABLE = 0x0004;

        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIM_TYPEMOUSE = 0;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

        private const int RI_LEFT_DOWN = 0x0001;
        private const int RI_LEFT_UP = 0x0002;
        private const int RI_RIGHT_DOWN = 0x0004;
        private const int RI_RIGHT_UP = 0x0008;
        private const int RI_MIDDLE_DOWN = 0x0010;
        private const int RI_MIDDLE_UP = 0x0020;
        private const int RI_WHEEL = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int width, int height, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRegisteredRawInputDevices([Out] RAWINPUTDEVICE[] pRawInputDevices, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        // 登録が残っているかを確かめる間隔。Unity が取り返すのはフォーカスが戻ったときなので、これで十分
        private const int RegistrationCheckIntervalMs = 200;
        private readonly RAWINPUTDEVICE[] _registeredBuf = new RAWINPUTDEVICE[32];
        private int _nextRegistrationCheck;
        // こちらが登録する前にマウスの登録を持っていたウィンドウ（Unity）。止めるときにここへ返す
        private IntPtr _previousOwner;
        private uint _previousFlags;

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        // static フィールドで保持することで、デリゲートと thunk がドメインの寿命いっぱい生存する
        private static readonly WndProcDelegate s_wndProc = StaticWndProc;
        // WndProc は必ずウィンドウを作ったスレッドで呼ばれるので、スレッド単位で持てば取り違えない
        [ThreadStatic] private static RawInputMouse t_owner;

        // RAWINPUTHEADER: dwType(4) + dwSize(4) + hDevice(ptr) + wParam(ptr)
        private static readonly int HeaderSize = 8 + 2 * IntPtr.Size;
        // RAWMOUSE 内のオフセット（ヘッダ末尾からの相対）
        private const int MouseFlagsOffset = 0;   // USHORT usFlags
        private const int MouseButtonsOffset = 4; // ULONG  ulButtons
        private const int MouseLastXOffset = 12;  // LONG   lLastX
        private const int MouseLastYOffset = 16;  // LONG   lLastY

        private Thread _thread;
        private IntPtr _hwnd;
        private IntPtr _buffer;
        private uint _bufferSize;
        private string _className;
        private IntPtr _hInstance;
        private volatile bool _stopRequested;
        private volatile bool _disposed;

        public bool Start()
        {
            if (IsRunning) return true;
            if (_disposed) return false;

            _stopRequested = false;
            LastError = null;
            // ドメインリロードをまたいで前回のクラスを再利用しないよう、毎回ユニークな名前にする
            _className = "ProjectEL4S_RawInputSink_" + Guid.NewGuid().ToString("N");

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => ThreadMain(ready))
            {
                IsBackground = true,
                Name = "RawInputMouse",
            };
            _thread.Start();
            ready.Wait(3000);
            ready.Dispose();

            IsRunning = _hwnd != IntPtr.Zero && LastError == null;
            if (!IsRunning && LastError == null)
            {
                LastError = "Raw Input スレッドの初期化がタイムアウトしました";
            }
            return IsRunning;
        }

        private void ThreadMain(ManualResetEventSlim ready)
        {
            t_owner = this;
            bool initialized = false;
            try
            {
                try
                {
                    _hInstance = GetModuleHandleW(null);
                    var wc = new WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                        hInstance = _hInstance,
                        lpszClassName = _className,
                    };
                    if (RegisterClassExW(ref wc) == 0)
                    {
                        LastError = "RegisterClassEx 失敗 (Win32 error " + Marshal.GetLastWin32Error() + ")";
                        return;
                    }

                    // HWND_MESSAGE(-3) を親にしたメッセージ専用ウィンドウ
                    _hwnd = CreateWindowExW(0, _className, _className, 0, 0, 0, 0, 0,
                        new IntPtr(-3), IntPtr.Zero, _hInstance, IntPtr.Zero);
                    if (_hwnd == IntPtr.Zero)
                    {
                        LastError = "CreateWindowEx 失敗 (Win32 error " + Marshal.GetLastWin32Error() + ")";
                        return;
                    }

                    _bufferSize = 64;   // RAWINPUT(マウス) は x64 で 48 バイト。余裕を持たせる
                    _buffer = Marshal.AllocHGlobal((int)_bufferSize);

                    // 登録すると Unity の登録を上書きしてしまうので、止めるときに返せるよう元の持ち主を覚えておく
                    if (FindOwner(out IntPtr prevOwner, out uint prevFlags) && prevOwner != IntPtr.Zero)
                    {
                        _previousOwner = prevOwner;
                        _previousFlags = prevFlags;
                    }

                    if (!RegisterRawInputDevices(BuildDevices(RIDEV_INPUTSINK, _hwnd), 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                    {
                        LastError = "RegisterRawInputDevices 失敗 (Win32 error " + Marshal.GetLastWin32Error() + ")";
                        return;
                    }
                    initialized = true;
                }
                finally
                {
                    ready.Set();
                }

                MessageLoop();
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
            finally
            {
                Teardown(initialized);
                t_owner = null;
            }
        }

        private static RAWINPUTDEVICE[] BuildDevices(uint flags, IntPtr target)
        {
            return new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,   // Generic Desktop Controls
                    usUsage = 0x02,       // Mouse
                    dwFlags = flags,
                    hwndTarget = target,
                },
            };
        }

        private void MessageLoop()
        {
            // GetMessage で無限に待つと、停止通知を取りこぼしたときにスレッドが残留する。
            // 100ms ごとに起きて停止フラグを見る形にして、確実に終了できるようにする。
            while (!_stopRequested)
            {
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT)
                    {
                        _stopRequested = true;
                        break;
                    }
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
                if (_stopRequested) break;
                EnsureRegistration();
                MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 100, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            }
        }

        /// <summary>
        /// Raw Input の登録は「1 プロセスにつき、1 種類のデバイスにつき 1 ウィンドウ」まで。
        /// Unity もマウスを登録している。エディタでは Game ビューにフォーカスが戻るたびに
        /// Unity が自分のウィンドウへ登録し直すので、放っておくとこちらには何も届かなくなる
        /// （2026/10/6 に検証。ビルドではフォーカスを移しても取り返されなかった）。
        /// 定期的に確かめて、取られていたら取り戻す。
        /// </summary>
        private void EnsureRegistration()
        {
            int now = Environment.TickCount;
            if (now - _nextRegistrationCheck < 0) return;
            _nextRegistrationCheck = now + RegistrationCheckIntervalMs;

            if (!FindOwner(out IntPtr owner, out uint ownerFlags)) return;
            if (owner == _hwnd) return;

            // 取り返した相手（Unity のそのときのウィンドウ）を、止めるときの返し先にする
            if (owner != IntPtr.Zero)
            {
                _previousOwner = owner;
                _previousFlags = ownerFlags;
            }

            if (RegisterRawInputDevices(BuildDevices(RIDEV_INPUTSINK, _hwnd), 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                string thief = ClassNameOf(owner);
                lock (_lock)
                {
                    _restoreCount++;
                    _lastThief = thief;
                }
            }
        }

        /// <summary>今マウスの登録を持っているウィンドウ。読めなかったら false。登録が無ければ owner は Zero。</summary>
        private bool FindOwner(out IntPtr owner, out uint flags)
        {
            owner = IntPtr.Zero;
            flags = 0;
            uint count = (uint)_registeredBuf.Length;
            uint got = GetRegisteredRawInputDevices(_registeredBuf, ref count, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (got == uint.MaxValue) return false;   // 登録が多すぎて読めない。判定できないので何もしない

            for (int i = 0; i < (int)got; i++)
            {
                if (_registeredBuf[i].usUsagePage == 0x01 && _registeredBuf[i].usUsage == 0x02)
                {
                    owner = _registeredBuf[i].hwndTarget;
                    flags = _registeredBuf[i].dwFlags;
                    break;
                }
            }
            return true;
        }

        private static string ClassNameOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(登録そのものが消えていた)";
            var sb = new System.Text.StringBuilder(256);
            return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "0x" + hwnd.ToInt64().ToString("X");
        }

        private void Teardown(bool unregisterRawInput)
        {
            // 先に受信を止める。
            // 登録を消すだけだと、Unity の Mouse.current は（ビルドでは）二度と動かなくなる。
            // Unity はビルドでは登録し直さないため。元の持ち主（Unity）がいれば、その登録に戻す
            if (unregisterRawInput)
            {
                try
                {
                    bool restored = _previousOwner != IntPtr.Zero && IsWindow(_previousOwner)
                        && RegisterRawInputDevices(BuildDevices(_previousFlags, _previousOwner), 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                    // RIDEV_REMOVE では hwndTarget は NULL でなければならない
                    if (!restored)
                        RegisterRawInputDevices(BuildDevices(RIDEV_REMOVE, IntPtr.Zero), 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                }
                catch (Exception) { /* 終了処理なので握る */ }
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (!string.IsNullOrEmpty(_className))
            {
                UnregisterClassW(_className, _hInstance);
                _className = null;
            }
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }

        private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // ここから managed 例外を native 側へ抜かすとプロセスが落ちるので、全部受け止める
            try
            {
                if (msg == WM_INPUT)
                {
                    t_owner?.HandleRawInput(lParam);
                }
                else if (msg == WM_CLOSE)
                {
                    if (t_owner != null) t_owner._stopRequested = true;
                    return IntPtr.Zero;
                }
            }
            catch (Exception)
            {
                // 握りつぶす（入力 1 件を捨てるだけ）
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void HandleRawInput(IntPtr hRawInput)
        {
            if (_buffer == IntPtr.Zero) return;

            uint size = _bufferSize;
            uint copied = GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, (uint)HeaderSize);
            if (copied == uint.MaxValue || copied < HeaderSize) return;

            // PtrToStructure を使うと 1 イベントごとに boxing が発生するので、直接読む
            if ((uint)Marshal.ReadInt32(_buffer, 0) != RIM_TYPEMOUSE) return;
            IntPtr device = Marshal.ReadIntPtr(_buffer, 8);

            int mouse = HeaderSize;
            ushort usFlags = (ushort)Marshal.ReadInt16(_buffer, mouse + MouseFlagsOffset);
            int buttons = Marshal.ReadInt32(_buffer, mouse + MouseButtonsOffset);
            int flags = buttons & 0xFFFF;
            short buttonData = (short)(buttons >> 16);
            int lastX = Marshal.ReadInt32(_buffer, mouse + MouseLastXOffset);
            int lastY = Marshal.ReadInt32(_buffer, mouse + MouseLastYOffset);

            int down = 0;
            int up = 0;
            if ((flags & RI_LEFT_DOWN) != 0) down |= 1 << 0;
            if ((flags & RI_RIGHT_DOWN) != 0) down |= 1 << 1;
            if ((flags & RI_MIDDLE_DOWN) != 0) down |= 1 << 2;
            if ((flags & RI_LEFT_UP) != 0) up |= 1 << 0;
            if ((flags & RI_RIGHT_UP) != 0) up |= 1 << 1;
            if ((flags & RI_MIDDLE_UP) != 0) up |= 1 << 2;

            int wheel = (flags & RI_WHEEL) != 0 ? buttonData : 0;

            if ((usFlags & MOUSE_MOVE_ABSOLUTE) != 0)
            {
                // 絶対座標デバイスは 0..65535 の正規化値で届く
                Push(device, 0, 0, wheel, down, up, true, lastX / 65535f, lastY / 65535f);
            }
            else
            {
                Push(device, lastX, lastY, wheel, down, up, false, 0f, 0f);
            }
        }

        /// <summary>デバイスの識別名（\\?\HID#VID_xxxx... 形式）。どのマウスかの判別用。</summary>
        public string GetDeviceName(IntPtr device)
        {
            uint size = 0;
            if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, IntPtr.Zero, ref size) != 0 || size == 0)
                return device.ToString();

            IntPtr buf = Marshal.AllocHGlobal((int)((size + 1) * 2));
            try
            {
                if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, buf, ref size) == uint.MaxValue)
                    return device.ToString();
                return Marshal.PtrToStringUni(buf) ?? device.ToString();
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        /// <summary>
        /// 今つながっているマウスのハンドルを results に入れる（まだ一度も動かされていない物も含む）。
        /// 固定登録で、起動した瞬間に番号を決めるために使う。RawInputKeyboard にも同じものがある。
        /// </summary>
        public void GetConnectedDevices(List<IntPtr> results)
        {
            results.Clear();
            uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0) return;

            // 数えてから取るまでの間に機器が増えることがあるので、少し多めに用意する
            count += 8;
            var list = new RAWINPUTDEVICELIST[count];
            uint got = GetRawInputDeviceList(list, ref count, size);
            if (got == uint.MaxValue) return;

            for (int i = 0; i < got; i++)
            {
                if (list[i].dwType == RIM_TYPEMOUSE) results.Add(list[i].hDevice);
            }
        }

        /// <summary>何度呼んでも安全。ドメインリロード前・Play 終了時に必ず呼ぶこと。</summary>
        public void Dispose()
        {
            _disposed = true;
            _stopRequested = true;
            IsRunning = false;

            var thread = _thread;
            _thread = null;
            if (thread == null) return;

            if (_hwnd != IntPtr.Zero)
            {
                PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            // PostMessage を取りこぼしても MessageLoop が 100ms ごとにフラグを見るので必ず抜ける
            if (!thread.Join(2000))
            {
                LastError = "Raw Input スレッドの停止がタイムアウトしました";
            }
        }

#else   // Windows 以外

        public bool Start()
        {
            LastError = "Raw Input は Windows 専用です（このプラットフォームでは複数マウスを分離できません）";
            return false;
        }

        public string GetDeviceName(IntPtr device) => device.ToString();

        public void GetConnectedDevices(List<IntPtr> results) => results.Clear();

        public void Dispose()
        {
            IsRunning = false;
        }

#endif
    }
}
