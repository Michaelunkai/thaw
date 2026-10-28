using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Thaw;

/// <summary>Win32 / Native API interop used by Thaw.</summary>
internal static class Native
{
    // ---------- user32 ----------

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const int WM_HOTKEY = 0x0312;
    internal const int VK_MENU = 0x12;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_F4 = 0x73;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    // Bounded broadcasts are used only as a transient desktop/UI refresh
    // nudge. SMTO_ABORTIFHUNG prevents one unresponsive window from holding
    // the recovery worker indefinitely.
    internal static readonly IntPtr HWND_BROADCAST = new(-1);
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_THEMECHANGED = 0x031A;

    // Shell notification refresh is deliberately used instead of stopping the
    // shell: it asks the existing Explorer process to refresh cached associations
    // and icons without dropping open windows.
    internal const uint SHCNE_ASSOCCHANGED = 0x08000000;
    internal const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    internal static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>
    /// Repaints the desktop and shell surfaces without going through a
    /// HWND_BROADCAST.
    ///
    /// The broadcast form was removed deliberately. SMTO_ABORTIFHUNG aborts the
    /// whole send as soon as *one* window in the session is not pumping messages,
    /// which is precisely the situation an unfreeze is called for, and without that
    /// flag a HWND_BROADCAST has to walk every top-level window in the session —
    /// hundreds of sends that reliably exceeded the action budget on a busy machine.
    /// Targeting the desktop window and the shell windows directly is both cheaper
    /// and more likely to actually repaint.
    /// </summary>
    internal static bool TryBroadcastDesktopRefresh(out int deliveredMessages)
    {
        deliveredMessages = 0;
        try
        {
            IntPtr desktop = GetDesktopWindow();
            if (desktop != IntPtr.Zero &&
                RedrawWindow(desktop, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW))
                deliveredMessages++;

            // The taskbar and the desktop host window are the two surfaces a user
            // actually notices when the shell stops repainting. Each send is bounded
            // and deliberately omits SMTO_ABORTIFHUNG for the same reason.
            foreach (string className in new[] { "Shell_TrayWnd", "Progman", "WorkerW" })
            {
                IntPtr shell = FindWindow(className, null);
                if (shell == IntPtr.Zero) continue;
                // A hung shell must not be able to stall recovery, so this send is
                // bounded and its result is treated as best effort.
                IntPtr result = SendMessageTimeout(
                    shell, WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ERRORONEXIT, 120, out _);
                if (result != IntPtr.Zero) deliveredMessages++;
            }

            return deliveredMessages > 0;
        }
        catch (Exception ex)
        {
            Log.Debug("Desktop refresh unavailable: " + ex.Message);
            return false;
        }
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(
        IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint dwFlags);

    internal const uint RDW_INVALIDATE = 0x0001;
    internal const uint RDW_ERASE = 0x0004;
    internal const uint RDW_ALLCHILDREN = 0x0080;
    internal const uint RDW_UPDATENOW = 0x0100;


    // ---------- Desktop Window Manager diagnostics ----------

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UNSIGNED_RATIO
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    // DWM_TIMING_INFO is intentionally kept in the native field order.  The
    // structure is versioned by cbSize; Windows only fills fields it knows.
    // All DWM_FRAME_COUNT and QPC_TIME values are ULONGLONG in dwmapi.h.
    // dwmapi.h wraps this definition in pshpack1.h / poppack.h.  Using the
    // CLR's default alignment makes the x64 structure 320 bytes instead of
    // the native 292 bytes and DWM returns MILERR_MISMATCHED_SIZE.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DWM_TIMING_INFO
    {
        internal uint cbSize;
        internal UNSIGNED_RATIO rateRefresh;
        internal ulong qpcRefreshPeriod;
        internal UNSIGNED_RATIO rateCompose;
        internal ulong qpcVBlank;
        internal ulong cRefresh;
        internal uint cDXRefresh;
        internal ulong qpcCompose;
        internal ulong cFrame;
        internal uint cDXPresent;
        internal ulong cRefreshFrame;
        internal ulong cFrameSubmitted;
        internal uint cDXPresentSubmitted;
        internal ulong cFrameConfirmed;
        internal uint cDXPresentConfirmed;
        internal ulong cRefreshConfirmed;
        internal uint cDXRefreshConfirmed;
        internal ulong cFramesLate;
        internal uint cFramesOutstanding;
        internal ulong cFrameDisplayed;
        internal ulong qpcFrameDisplayed;
        internal ulong cRefreshFrameDisplayed;
        internal ulong cFrameComplete;
        internal ulong qpcFrameComplete;
        internal ulong cFramePending;
        internal ulong qpcFramePending;
        internal ulong cFramesDisplayed;
        internal ulong cFramesComplete;
        internal ulong cFramesPending;
        internal ulong cFramesAvailable;
        internal ulong cFramesDropped;
        internal ulong cFramesMissed;
        internal ulong cRefreshNextDisplayed;
        internal ulong cRefreshNextPresented;
        internal ulong cRefreshesDisplayed;
        internal ulong cRefreshesPresented;
        internal ulong cRefreshStarted;
        internal ulong cPixelsReceived;
        internal ulong cPixelsDrawn;
        internal ulong cBuffersEmpty;
    }

    [DllImport("dwmapi.dll", SetLastError = false)]
    internal static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO timingInfo);

    [DllImport("dwmapi.dll", SetLastError = false)]
    internal static extern int DwmEnableMMCSS([MarshalAs(UnmanagedType.Bool)] bool enable);

    // ---------- bounded window and memory probes ----------

    internal const uint WM_NULL = 0x0000;
    internal const uint WM_CANCELMODE = 0x001F;
    internal const uint WM_DISPLAYCHANGE = 0x007E;

    internal static readonly IntPtr HWND_MESSAGE = new(-3);

    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    // ---------- display-change observation ----------

    /// <summary>
    /// A message-only window that records WM_DISPLAYCHANGE, which Windows broadcasts
    /// when the display mode is re-applied. That broadcast is the only user-mode
    /// evidence that a graphics-driver reset actually took effect: SendInput's return
    /// count proves the keystrokes were queued, not that the driver reacted.
    ///
    /// It is armed before the chord is injected and polled after, on a dedicated
    /// thread, so a recovery run can distinguish "requested" from "confirmed"
    /// instead of reporting an unproven reset as a success.
    /// </summary>
    internal sealed class DisplayChangeProbe : IDisposable
    {
        private const string ClassName = "ThawDisplayChangeProbe";
        private readonly WindowProc _proc;
        private readonly ManualResetEventSlim _signal = new(false);
        private Thread? _thread;
        private IntPtr _window;
        private int _observed;
        private int _failed;

        private delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        internal DisplayChangeProbe()
        {
            // Held in a field so the GC never collects the delegate while the native
            // window class still points at its thunk.
            _proc = OnWindowMessage;
        }

        private IntPtr OnWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DISPLAYCHANGE)
            {
                Volatile.Write(ref _observed, 1);
                try { _signal.Set(); } catch (ObjectDisposedException) { }
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>True when a WM_DISPLAYCHANGE arrived since the probe was armed.</summary>
        internal bool Observed => Volatile.Read(ref _observed) != 0;

        /// <summary>True when the listener could not be created at all.</summary>
        internal bool Unavailable => Volatile.Read(ref _failed) != 0;

        /// <summary>
        /// Starts the listener. Returns false when it could not be started, which the
        /// caller must treat as "no proof available" rather than "no reset".
        /// </summary>
        internal bool Arm()
        {
            Volatile.Write(ref _observed, 0);
            try
            {
                _signal.Reset();
                _thread = new Thread(Listen)
                {
                    IsBackground = true,
                    Name = "Thaw.DisplayChangeProbe",
                };
                _thread.Start();
                for (int i = 0; i < 100 && _window == IntPtr.Zero && Volatile.Read(ref _failed) == 0; i++)
                    Thread.Sleep(10);
                return _window != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Debug("Display-change probe unavailable: " + ex.Message);
                return false;
            }
        }

        private void Listen()
        {
            IntPtr module = GetModuleHandle(null);
            var wc = new WNDCLASS
            {
                lpfnWndProc = _proc,
                hInstance = module,
                lpszClassName = ClassName,
            };
            ushort atom = RegisterClass(ref wc);
            if (atom == 0)
            {
                Volatile.Write(ref _failed, 1);
                return;
            }

            _window = CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0,
                HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);
            if (_window == IntPtr.Zero)
            {
                Volatile.Write(ref _failed, 1);
                return;
            }

            while (_window != IntPtr.Zero &&
                   GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_window != IntPtr.Zero) DestroyWindow(_window);
                _window = IntPtr.Zero;
                _thread?.Join(250);
            }
            catch { }
            _signal.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASS
        {
            public uint style;
            public WindowProc lpfnWndProc;
            public int cbClassExtra;
            public int cbWindowExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
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
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WNDCLASS lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int cx, int cy, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    internal const uint SMTO_BLOCK = 0x0001;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal const uint SMTO_ERRORONEXIT = 0x0020;
    internal const int ERROR_TIMEOUT = 1460;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    internal enum MEMORY_RESOURCE_NOTIFICATION_TYPE
    {
        LowMemoryResourceNotification = 0,
        HighMemoryResourceNotification = 1,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateMemoryResourceNotification(MEMORY_RESOURCE_NOTIFICATION_TYPE notificationType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryMemoryResourceNotification(
        IntPtr resourceNotificationHandle,
        [MarshalAs(UnmanagedType.Bool)] out bool resourceState);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetEvent(IntPtr handle);

    // ---------- process accounting and quality of service ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessIoCounters(IntPtr processHandle, out IO_COUNTERS counters);

    internal enum PROCESS_INFORMATION_CLASS
    {
        ProcessMemoryPriority = 0,
        ProcessPowerThrottling = 9,
    }

    internal const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x00000001;
    internal const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x00000004;


    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_POWER_THROTTLING_STATE
    {
        internal uint Version;
        internal uint ControlMask;
        internal uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessInformation(
        IntPtr processHandle,
        PROCESS_INFORMATION_CLASS processInformationClass,
        IntPtr processInformation,
        uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessInformation(
        IntPtr hProcess,
        PROCESS_INFORMATION_CLASS processInformationClass,
        IntPtr processInformation,
        uint processInformationSize);


    // ---------- CPU Sets (Windows 10+, best effort) ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessDefaultCpuSets(
        IntPtr processHandle,
        IntPtr cpuSetIds,
        uint cpuSetIdCount,
        out uint requiredIdCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDefaultCpuSets(
        IntPtr processHandle,
        IntPtr cpuSetIds,
        uint cpuSetIdCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetThreadSelectedCpuSets(
        IntPtr threadHandle,
        IntPtr cpuSetIds,
        uint cpuSetIdCount,
        out uint requiredIdCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadSelectedCpuSets(
        IntPtr threadHandle,
        IntPtr cpuSetIds,
        uint cpuSetIdCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemCpuSetInformation(
        IntPtr cpuSetInformation,
        uint bufferLength,
        out uint returnedLength,
        IntPtr processHandle,
        uint flags);

    // ---------- MMCSS worker registration ----------

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, EntryPoint = "AvSetMmThreadCharacteristicsW", SetLastError = true)]
    internal static extern IntPtr AvSetMmThreadCharacteristics(string taskName, out uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, int priority);

    // ---------- Wait Chain Traversal (diagnostic only) ----------

    internal const uint WCT_SYNC_OPEN_SESSION = 0x00000000;
    internal const uint WCT_OUT_OF_PROC_FLAG = 0x00000001;
    internal const uint WCT_OUT_OF_PROC_COM_FLAG = 0x00000002;
    internal const uint WCT_OUT_OF_PROC_CS_FLAG = 0x00000004;
    internal const uint WCT_MAX_NODE_COUNT = 16;
    internal const int ERROR_IO_PENDING = 997;
    internal const int ERROR_MORE_DATA = 234;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const int ERROR_NOT_SUPPORTED = 50;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_OBJECT_NOT_FOUND = 441;

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThreadWaitChainSession(uint flags, IntPtr callback);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern void CloseThreadWaitChainSession(IntPtr wctSessionHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetThreadWaitChain(
        IntPtr wctSessionHandle,
        IntPtr context,
        uint flags,
        uint threadId,
        ref uint nodeCount,
        IntPtr nodeInfoArray,
        [MarshalAs(UnmanagedType.Bool)] out bool isCycle);

    // The largest member of WAITCHAIN_NODE_INFO is LockObject.  Keeping the
    // native size here lets the managed wrapper inspect the fixed-size array
    // without introducing unsafe code or a third-party interop package.
    internal const int WCT_NODE_INFO_SIZE = 280;

    // ---------- application restart/recovery ----------

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate uint ApplicationRecoveryCallback(IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int RegisterApplicationRestart(string? commandLine, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int UnregisterApplicationRestart();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int RegisterApplicationRecoveryCallback(
        ApplicationRecoveryCallback callback,
        IntPtr parameter,
        uint pingInterval,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int UnregisterApplicationRecoveryCallback();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ApplicationRecoveryInProgress();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ApplicationRecoveryFinished([MarshalAs(UnmanagedType.Bool)] bool success);

    // ---------- SetupAPI / Configuration Manager (read-only enumeration) ----------

    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_ALLCLASSES = 0x00000004;
    internal const uint SPDRP_DEVICEDESC = 0x00000000;
    internal const uint SPDRP_HARDWAREID = 0x00000001;
    internal const uint SPDRP_MFG = 0x0000000B;
    internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    internal const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
    internal const uint SPDRP_DRIVER = 0x00000009;
    internal const uint DN_HAS_PROBLEM = 0x00000400;
    internal static readonly Guid GUID_DEVCLASS_DISPLAY = new("4d36e968-e325-11ce-bfc1-08002be10318");

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        internal uint cbSize;
        internal Guid ClassGuid;
        internal uint DevInst;
        internal IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsAll(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        IntPtr propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        IntPtr deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", SetLastError = false)]
    internal static extern int CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        uint flags);

    // ---------- kernel32 ----------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_SET_QUOTA = 0x0100;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint HIGH_PRIORITY_CLASS = 0x00000080;
    internal const uint REALTIME_PRIORITY_CLASS = 0x00000100;
    internal const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    internal const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThread(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

    internal const uint THREAD_QUERY_INFORMATION = 0x0040;
    internal const uint THREAD_SET_INFORMATION = 0x0020;
    internal const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;
    internal const uint THREAD_SET_LIMITED_INFORMATION = 0x0400;
    internal const uint THREAD_SUSPEND_RESUME = 0x0002;

    // Priority boost doubles the scheduler quantum of a thread or every thread in
    // a process. It is the least invasive "make this app run now" primitive and
    // has no kernel counterpart, so an exact restore is always possible.
    //
    // The absolute-priority pair (Get/SetThreadPriority) is intentionally absent.
    // GetThreadPriority fails for every thread on current Windows 11 builds and
    // NtQueryInformationThread(ThreadBasePriority) is refused, so the prior value
    // cannot be captured and a raise could not be rolled back exactly. Thaw does
    // not make a mutation it cannot undo.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessPriorityBoost(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] bool pfDisable);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadPriorityBoost(IntPtr hThread, [MarshalAs(UnmanagedType.Bool)] bool pfDisable);

    // Per-thread affinity is the only supported way to move a busy process off
    // specific logical processors on current Windows. SetThreadAffinityMask
    // returns the thread's previous mask, which is exactly what a bounded
    // rollback needs.
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    internal const uint ES_CONTINUOUS = 0x80000000;
    internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
    internal const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetDriveType(string lpRootPathName);

    internal const uint DRIVE_UNKNOWN = 0;
    internal const uint DRIVE_FIXED = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushFileBuffers(IntPtr hFile);

    internal static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>
    /// Builds the Win32 device path for a volume ("\\.\C:"). A volume root must be
    /// opened through the device namespace, or with FILE_FLAG_BACKUP_SEMANTICS on
    /// the drive-letter path; a plain "C:\" open with neither fails with
    /// ERROR_PATH_NOT_FOUND, which would silently skip the flush.
    /// </summary>
    internal static string? GetVolumeDevicePath(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2) return null;
        char drive = char.ToUpperInvariant(root[0]);
        if (drive is < 'A' or > 'Z' || root[1] != ':') return null;
        return @"\\.\" + drive + ":";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemFileCacheSize(IntPtr MinimumFileCacheSize, IntPtr MaximumFileCacheSize, uint Flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    internal static MEMORYSTATUSEX GetMemoryStatus()
    {
        var ms = new MEMORYSTATUSEX();
        ms.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        if (!GlobalMemoryStatusEx(ref ms)) ms.dwMemoryLoad = 0;
        return ms;
    }

    // ---------- psapi ----------

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr hProcess);

    // ---------- ntdll ----------

    internal const int SystemMemoryListInformation = 0x50;
    internal const int MemoryPurgeStandbyList = 1;

    [DllImport("ntdll.dll")]
    internal static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    // ---------- user32 input injection ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT's native union is sized by its largest member. On 64-bit Windows,
    // MOUSEINPUT is 32 bytes while KEYBDINPUT is only 24. Omitting the mouse
    // member shrinks managed INPUT to 32 bytes instead of the required 40 and
    // makes SendInput reject the entire request with ERROR_INVALID_PARAMETER.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type; // 1 = keyboard
        public INPUTUNION U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // Resolver-cache flush leaves existing sockets and authentication sessions
    // intact.  It is only invoked for an explicit, user-triggered recovery.
    [DllImport("dnsapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DnsFlushResolverCache();

    // ---------- terminal services (launch explorer as the interactive user) ----------

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    // ---------- advapi32 ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    internal const int TOKEN_QUERY = 0x00000008;
    internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    internal const int TokenPrivileges = 3;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    // ---------- registry hive write-back ----------

    internal static readonly IntPtr HKEY_LOCAL_MACHINE = new(unchecked((int)0x80000002));

    internal const int KEY_READ = 0x20019;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegFlushKey(IntPtr hKey, uint reserved);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern int RegCloseKey(IntPtr hKey);

    /// <summary>
    /// Writes a registry key's dirty log out to its backing hive. A frozen hive
    /// is a real cause of multi-second stalls, because every process that reads
    /// configuration blocks behind the same hive lock. This is a flush, not a
    /// value mutation, so there is nothing to roll back.
    /// </summary>
    internal static bool TryFlushRegistryHive(IntPtr root, string keyPath)
    {
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegOpenKeyEx(root, keyPath, 0, KEY_READ, out key) != 0 || key == IntPtr.Zero)
                return false;
            return RegFlushKey(key, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("Registry hive flush unavailable for " + keyPath + ": " + ex.Message);
            return false;
        }
        finally
        {
            if (key != IntPtr.Zero)
            {
                try { RegCloseKey(key); } catch { }
            }
        }
    }

    // ---------- relief-tier entry points ----------

    /// <summary>
    /// Every Win32 entry point the Alt+F4 relief tier depends on, named exactly as
    /// the CLR resolves them. Anything declared with CharSet.Unicode must appear
    /// with its <c>W</c> suffix here, because the CLR appends the suffix before
    /// calling GetProcAddress. Probing the export table proves the API surface
    /// exists on this build before a recovery run tries to use it; the probe itself
    /// only reads exports.
    /// </summary>
    internal static IReadOnlyList<string> ReliefEntryPoints { get; } = new[]
    {
        "SetProcessPriorityBoost", "SetThreadPriorityBoost", "SetThreadAffinityMask",
        "OpenThread", "GetCurrentThread", "SetThreadExecutionState", "GetDriveTypeW",
        "CreateFileW", "FlushFileBuffers",
    };

    internal static IReadOnlyList<string> AdvapiReliefEntryPoints { get; } = new[]
    {
        "RegOpenKeyExW", "RegFlushKey", "RegCloseKey",
    };

    /// <summary>
    /// Returns the relief-tier exports that cannot be resolved. An empty list means
    /// the tier's API surface is present; it is not a claim that any action worked.
    /// </summary>
    internal static IReadOnlyList<string> FindMissingReliefEntryPoints()
    {
        var missing = new List<string>(4);
        AddMissing(missing, "kernel32.dll", ReliefEntryPoints);
        AddMissing(missing, "advapi32.dll", AdvapiReliefEntryPoints);
        return missing;
    }

    private static void AddMissing(List<string> missing, string module, IReadOnlyList<string> names)
    {
        IntPtr handle;
        try { handle = LoadLibraryEx(module, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE); }
        catch (DllNotFoundException) { handle = IntPtr.Zero; }
        catch (EntryPointNotFoundException) { handle = IntPtr.Zero; }

        if (handle == IntPtr.Zero)
        {
            missing.Add(module);
            return;
        }

        try
        {
            foreach (string name in names)
            {
                if (GetProcAddress(handle, name) == IntPtr.Zero) missing.Add(module + "!" + name);
            }
        }
        finally
        {
            FreeLibrary(handle);
        }
    }

    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hLibModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    /// <summary>True when the current process runs with an elevated (administrator) token.</summary>
    internal static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Log.Debug("Elevation probe unavailable: " + ex.Message);
            return false;
        }
    }

    /// <summary>Enables a privilege (e.g. SeIncreaseQuotaPrivilege) on the current process token.</summary>
    internal static bool EnablePrivilege(string privilege) =>
        EnablePrivilege(privilege, out _);

    /// <summary>
    /// Enables a privilege and, on failure, reports which of the three steps
    /// failed. A bare <c>false</c> is not diagnosable: OpenProcessToken,
    /// LookupPrivilegeValue and AdjustTokenPrivileges fail for entirely different
    /// reasons, and a recovery action that only knows "unavailable" cannot be
    /// acted on.
    /// </summary>
    internal static bool EnablePrivilege(string privilege, out string failure)
    {
        failure = "";
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out token) ||
                token == IntPtr.Zero)
            {
                failure = "OpenProcessToken:" + Marshal.GetLastWin32Error();
                return false;
            }

            if (!LookupPrivilegeValue(null, privilege, out long luid))
            {
                failure = "LookupPrivilegeValue:" + Marshal.GetLastWin32Error();
                return false;
            }

            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };

            // AdjustTokenPrivileges can return TRUE while leaving ERROR_NOT_ALL_ASSIGNED
            // (1300) behind when the token lacks the privilege, so the last error has
            // to be cleared first: it is otherwise whatever an earlier, unrelated
            // call on this worker thread happened to leave behind.
            SetLastError(0);
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                failure = "AdjustTokenPrivileges:" + Marshal.GetLastWin32Error();
                return false;
            }

            int last = Marshal.GetLastWin32Error();
            if (last != 0)
            {
                failure = last == 1300
                    ? "not-assigned-to-token(1300)"
                    : "AdjustTokenPrivileges-lastError:" + last;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = "exception:" + ex.GetType().Name + ":" + ex.Message;
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void SetLastError(uint dwErrCode);


    // ---------- helpers ----------

    internal static uint GetForegroundPid()
    {
        uint pid = 0;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero) GetWindowThreadProcessId(hwnd, out pid);
        return pid;
    }
}
