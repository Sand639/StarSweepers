using System;
using System.Collections.Generic;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
using System.Threading;
#endif

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary>1 キー分の押した / 離したイベント（デバイス別）。</summary>
    public struct RawKeyEvent
    {
        public IntPtr DeviceHandle;
        /// <summary>スキャンコード（キーの物理的な位置。キー配列の設定に左右されない）。</summary>
        public ushort MakeCode;
        /// <summary>拡張キーか（E0 付き。右 Ctrl・矢印キーなどをテンキー側と区別する）。</summary>
        public bool Extended;
        /// <summary>Windows の仮想キーコード。Pause など、スキャンコードだけでは決まらないキー用。</summary>
        public ushort VirtualKey;
        /// <summary>true = 離した / false = 押した（押しっぱなしのリピートも false で届く）。</summary>
        public bool IsUp;
    }

    /// <summary>
    /// Windows の Raw Input (WM_INPUT) でキーボードをデバイス単位で読む。
    /// Unity の Input System は複数キーボードを 1 台に合成してしまうため、こちらで直接拾う。
    /// 仕組みは RawInputMouse と同じ（専用スレッドのメッセージ専用ウィンドウで受信して溜め込む）。
    ///
    /// マウスと違い「押した→離した」が 1 フレーム内に収まることがあるので、
    /// 合計値ではなくイベントの列として溜める。
    ///
    /// エディタで使ううえでの注意（RawInputMouse と同じ。守らないと Editor がクラッシュする）:
    ///   - ウィンドウクラス名は Start ごとにユニークにする。
    ///   - 停止処理はドメインリロード前に必ず走らせる（MultiKeyboardManager 側でフックしている）。
    ///   - WndProc から managed 例外を native 側へ抜かせない。
    /// </summary>
    public sealed class RawInputKeyboard : IDisposable
    {
        // Poll されない間（エディタの一時停止など）に溜まり続けないための上限。
        // 超えたら押したイベントだけ捨てる（離したイベントを捨てるとキーが押されっぱなしになるため）
        private const int MaxQueuedEvents = 4096;

        private readonly object _lock = new object();
        private readonly List<RawKeyEvent> _queue = new List<RawKeyEvent>();
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
        /// 登録を他に取られていたので取り戻した回数。増えた瞬間は、離したキーを取りこぼしている可能性がある。
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

        /// <summary>受信スレッドから呼ばれ、イベントを列に足す。Poll はメインスレッドから来るので lock で守る。</summary>
        private void Push(in RawKeyEvent e)
        {
            lock (_lock)
            {
                _eventCount++;
                if (_queue.Count >= MaxQueuedEvents && !e.IsUp) return;
                _queue.Add(e);
            }
        }

        /// <summary>溜まったイベントを届いた順に取り出して空にする。</summary>
        public void Poll(List<RawKeyEvent> results)
        {
            results.Clear();
            lock (_lock)
            {
                results.AddRange(_queue);
                _queue.Clear();
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // ---- Windows が決めている番号（値は Win32 の定義どおり。名前も Windows のものに合わせてある）
        // ウィンドウに届くメッセージの種類
        private const int WM_CLOSE = 0x0010;             // 閉じる（Dispose から送って受信スレッドを止める）
        private const int WM_QUIT = 0x0012;              // メッセージループを抜ける合図
        private const int WM_INPUT = 0x00FF;             // Raw Input が 1 件届いた
        private const uint PM_REMOVE = 0x0001;           // PeekMessage：読んだメッセージは列から取り除く
        private const uint QS_ALLINPUT = 0x04FF;         // MsgWaitForMultipleObjectsEx：どの種類のメッセージが来ても起きる
        private const uint MWMO_INPUTAVAILABLE = 0x0004; // MsgWaitForMultipleObjectsEx：待ち始める前から溜まっているメッセージでも起きる

        // Raw Input の登録・問い合わせに使う値
        private const uint RIDEV_REMOVE = 0x00000001;    // 登録を消す
        private const uint RIDEV_INPUTSINK = 0x00000100; // 前面でないウィンドウにも届ける（見えない窓で受けるので必須）
        private const uint RID_INPUT = 0x10000003;       // GetRawInputData：入力の中身をもらう
        private const uint RIDI_DEVICENAME = 0x20000007; // GetRawInputDeviceInfo：デバイスの名前をもらう
        private const uint RIM_TYPEKEYBOARD = 1;         // RAWINPUTHEADER.dwType がキーボード

        // RAWKEYBOARD の中身の読み方
        private const ushort RI_KEY_BREAK = 0x01;        // Flags：離した（立っていなければ押した）
        private const ushort RI_KEY_E0 = 0x02;           // Flags：拡張キー（E0 付き）
        private const ushort KEYBOARD_OVERRUN_MAKE_CODE = 0xFF; // MakeCode：キーボードのバッファあふれ（入力ではない）
        private const ushort VK_FAKE = 0xFF;             // VKey：Windows が Shift の補正などで混ぜる偽のキー
        private const uint MAPVK_VK_TO_VSC_EX = 4;       // MapVirtualKey：仮想キー → スキャンコード（E0 付きは上位バイトに 0xE0 が入る）

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

        // ウィンドウにメッセージが届くたびに Windows から呼ばれる関数の形（StaticWndProc がこれ）
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // ---- Windows の関数（user32.dll / kernel32.dll）。引数の名前と型は Win32 の定義どおり
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRegisteredRawInputDevices([Out] RAWINPUTDEVICE[] pRawInputDevices, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        // 登録が残っているかを確かめる間隔。Unity が取り返すのはフォーカスが戻ったときなので、これで十分
        private const int RegistrationCheckIntervalMs = 200;
        // GetRegisteredRawInputDevices の受け皿。このプロセスで登録されている「デバイスの種類」の数ぶん要る（32 もあれば足りる）
        private readonly RAWINPUTDEVICE[] _registeredBuf = new RAWINPUTDEVICE[32];
        // 次に登録を確かめる時刻（Environment.TickCount のミリ秒）
        private int _nextRegistrationCheck;
        // こちらが登録する前にキーボードの登録を持っていたウィンドウ（Unity）。止めるときにここへ返す
        private IntPtr _previousOwner;
        private uint _previousFlags;

        // static フィールドで保持することで、デリゲートと thunk がドメインの寿命いっぱい生存する
        private static readonly WndProcDelegate s_wndProc = StaticWndProc;
        // WndProc は必ずウィンドウを作ったスレッドで呼ばれるので、スレッド単位で持てば取り違えない
        [ThreadStatic] private static RawInputKeyboard t_owner;

        // RAWINPUTHEADER: dwType(4) + dwSize(4) + hDevice(ptr) + wParam(ptr)
        private static readonly int HeaderSize = 8 + 2 * IntPtr.Size;
        // RAWKEYBOARD 内のオフセット（ヘッダ末尾からの相対）
        private const int KeyMakeCodeOffset = 0;  // USHORT MakeCode
        private const int KeyFlagsOffset = 2;     // USHORT Flags
        private const int KeyVKeyOffset = 6;      // USHORT VKey

        private Thread _thread;        // 受信スレッド（メッセージ専用ウィンドウを持ち、WM_INPUT を待ち続ける）
        private IntPtr _hwnd;          // 受信用の見えないウィンドウ
        private IntPtr _buffer;        // GetRawInputData の受け皿（1 件ぶん。毎回確保しないよう使い回す）
        private uint _bufferSize;
        private string _className;     // ウィンドウクラスの名前（Start ごとにユニーク）
        private IntPtr _hInstance;
        private volatile bool _stopRequested;   // 受信スレッドに「止まれ」を伝える印（別スレッドから書くので volatile）
        private volatile bool _disposed;

        /// <summary>
        /// 受信スレッドを立ち上げ、ウィンドウの用意と Raw Input の登録が済むまで待つ（最長 3 秒）。
        /// 成功すれば true。失敗の理由は <see cref="LastError"/>。
        /// </summary>
        public bool Start()
        {
            if (IsRunning) return true;
            if (_disposed) return false;

            _stopRequested = false;
            LastError = null;
            // ドメインリロードをまたいで前回のクラスを再利用しないよう、毎回ユニークな名前にする
            _className = "ProjectEL4S_RawInputKeyboardSink_" + Guid.NewGuid().ToString("N");

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => ThreadMain(ready))
            {
                IsBackground = true,
                Name = "RawInputKeyboard",
            };
            _thread.Start();
            ready.Wait(3000);   // 受信スレッドが ready.Set() するまで待つ（ミリ秒）。普段は一瞬で返る
            ready.Dispose();

            IsRunning = _hwnd != IntPtr.Zero && LastError == null;
            if (!IsRunning && LastError == null)
            {
                LastError = "Raw Input スレッドの初期化がタイムアウトしました";
            }
            return IsRunning;
        }

        /// <summary>
        /// 受信スレッドの本体。流れは「ウィンドウクラスを登録 → 見えないウィンドウを作る → 受け皿を確保 →
        /// 今の登録の持ち主（Unity）を覚える → Raw Input を登録 → メッセージループ（止まるまで） → 片付け」。
        /// 内側の try/finally は、途中で失敗しても必ず ready.Set() して <see cref="Start"/> を待たせっぱなしにしないため。
        /// </summary>
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

                    _bufferSize = 64;   // RAWINPUT(キーボード) は x64 で 40 バイト。余裕を持たせる
                    _buffer = Marshal.AllocHGlobal((int)_bufferSize);

                    // 登録すると Unity の登録を上書きしてしまうので、止めるときに返せるよう元の持ち主を覚えておく
                    if (FindOwner(out IntPtr prevOwner, out uint prevFlags) && prevOwner != IntPtr.Zero)
                    {
                        _previousOwner = prevOwner;
                        _previousFlags = prevFlags;
                    }

                    // RIDEV_NOLEGACY は付けない。付けると Unity 側に WM_KEYDOWN / WM_CHAR が届かなくなる
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

        /// <summary>RegisterRawInputDevices に渡す「キーボードを target のウィンドウへ」という登録 1 件ぶん。</summary>
        private static RAWINPUTDEVICE[] BuildDevices(uint flags, IntPtr target)
        {
            return new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,   // Generic Desktop Controls
                    usUsage = 0x06,       // Keyboard
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
        /// Unity もキーボードを登録している。エディタでは Game ビューにフォーカスが戻るたびに
        /// Unity が自分のウィンドウへ登録し直すので、放っておくとこちらには何も届かなくなる
        /// （2026/10/6 に検証。ビルドではフォーカスを移しても取り返されなかった）。
        /// 定期的に確かめて、取られていたら取り戻す。
        /// </summary>
        private void EnsureRegistration()
        {
            // TickCount は約 25 日で一周して負になるが、「引き算して符号を見る」形なら一周をまたいでも正しく比べられる
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

        /// <summary>今キーボードの登録を持っているウィンドウ。読めなかったら false。登録が無ければ owner は Zero。</summary>
        private bool FindOwner(out IntPtr owner, out uint flags)
        {
            owner = IntPtr.Zero;
            flags = 0;
            uint count = (uint)_registeredBuf.Length;
            uint got = GetRegisteredRawInputDevices(_registeredBuf, ref count, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (got == uint.MaxValue) return false;   // 登録が多すぎて読めない。判定できないので何もしない

            for (int i = 0; i < (int)got; i++)
            {
                if (_registeredBuf[i].usUsagePage == 0x01 && _registeredBuf[i].usUsage == 0x06)
                {
                    owner = _registeredBuf[i].hwndTarget;
                    flags = _registeredBuf[i].dwFlags;
                    break;
                }
            }
            return true;
        }

        /// <summary>ウィンドウのクラス名（ログで「誰に取られたか」を見分けるため）。取れなければハンドルの 16 進。</summary>
        private static string ClassNameOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(登録そのものが消えていた)";
            var sb = new System.Text.StringBuilder(256);
            return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "0x" + hwnd.ToInt64().ToString("X");
        }

        /// <summary>
        /// 受信スレッドの片付け（スレッドの最後に必ず通る）。
        /// unregisterRawInput は「登録まで済んでいたか」。済んでいなければ登録には触らない（他人の登録を消さないため）。
        /// </summary>
        private void Teardown(bool unregisterRawInput)
        {
            // 先に受信を止める。
            // 登録を消すだけだと、Unity の Keyboard.current は（ビルドでは）二度と動かなくなる。
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

        /// <summary>
        /// 受信用ウィンドウにメッセージが届くたびに Windows から呼ばれる（受信スレッド上）。
        /// static なのは、関数ポインタとして Windows に渡すため。自分のインスタンスは t_owner から引く。
        /// </summary>
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

        /// <summary>WM_INPUT 1 件を読み、キーのイベントにして列へ足す。読めない・要らない入力は黙って捨てる。</summary>
        private void HandleRawInput(IntPtr hRawInput)
        {
            if (_buffer == IntPtr.Zero) return;

            uint size = _bufferSize;
            uint copied = GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, (uint)HeaderSize);
            if (copied == uint.MaxValue || copied < HeaderSize) return;   // uint.MaxValue = 失敗（(UINT)-1）

            // PtrToStructure を使うと 1 イベントごとに boxing が発生するので、直接読む
            // （先頭 0 = dwType、8 = hDevice。HeaderSize のコメントの並びどおり）
            if ((uint)Marshal.ReadInt32(_buffer, 0) != RIM_TYPEKEYBOARD) return;
            IntPtr device = Marshal.ReadIntPtr(_buffer, 8);

            int kb = HeaderSize;
            ushort makeCode = (ushort)Marshal.ReadInt16(_buffer, kb + KeyMakeCodeOffset);
            ushort flags = (ushort)Marshal.ReadInt16(_buffer, kb + KeyFlagsOffset);
            ushort vkey = (ushort)Marshal.ReadInt16(_buffer, kb + KeyVKeyOffset);

            // 0xFF は Shift 補正などで Windows が混ぜる偽のキー。オーバーランも捨てる
            if (vkey == VK_FAKE || makeCode == KEYBOARD_OVERRUN_MAKE_CODE) return;

            bool extended = (flags & RI_KEY_E0) != 0;

            // ソフトウェアから送られた入力などはスキャンコードが 0 のことがある。仮想キーから引き直す
            if (makeCode == 0 && vkey != 0)
            {
                uint sc = MapVirtualKeyW(vkey, MAPVK_VK_TO_VSC_EX);
                makeCode = (ushort)(sc & 0xFF);
                extended |= (sc & 0xFF00) == 0xE000;
            }

            Push(new RawKeyEvent
            {
                DeviceHandle = device,
                MakeCode = makeCode,
                Extended = extended,
                VirtualKey = vkey,
                IsUp = (flags & RI_KEY_BREAK) != 0,
            });
        }

        /// <summary>デバイスの識別名（\\?\HID#VID_xxxx... 形式）。どのキーボードかの判別用。</summary>
        public string GetDeviceName(IntPtr device)
        {
            // 1 回目は受け皿を渡さずに「何文字か」だけ聞き、2 回目でその大きさの受け皿に書いてもらう（Win32 でよくある形）
            uint size = 0;
            if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, IntPtr.Zero, ref size) != 0 || size == 0)
                return device.ToString();

            // size は文字数。Unicode は 1 文字 2 バイトで、終端の 1 文字ぶんも足しておく
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
        /// 今つながっているキーボードのハンドルを results に入れる（まだ一度も押されていない物も含む）。
        /// 固定登録で、起動した瞬間に番号を決めるために使う。RawInputMouse にも同じものがある。
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
                if (list[i].dwType == RIM_TYPEKEYBOARD) results.Add(list[i].hDevice);
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
            // （2000 = スレッドが終わるのを待つ上限のミリ秒。これを超えたら諦めてメインスレッドを止めない）
            if (!thread.Join(2000))
            {
                LastError = "Raw Input スレッドの停止がタイムアウトしました";
            }
        }

#else   // Windows 以外

        public bool Start()
        {
            LastError = "Raw Input は Windows 専用です（このプラットフォームでは複数キーボードを分離できません）";
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
