using System.Runtime.InteropServices;

namespace ClickSimulator.Services;

/// <summary>
/// 全局热键管理器，使用 SetWindowsHookEx 低层键盘钩子
/// 不会与其他程序的 RegisterHotKey 冲突
/// </summary>
public class GlobalHotkeyManager : IDisposable
{
    #region Win32 API

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012;

    // F8 = 0x77 (119), F10 = 0x79 (121), F12 = 0x7B (123)
    private const uint VK_F8 = 0x77;
    private const uint VK_F10 = 0x79;
    private const uint VK_F12 = 0x7B;

    #endregion

    private IntPtr _hookId = IntPtr.Zero;
    private Thread? _messageLoopThread;
    private volatile bool _running;
    private uint _messageLoopThreadId;
    private LowLevelKeyboardProc? _proc; // 必须持有委托引用防止 GC 回收

    // 防止按键重复触发（按住不放只触发一次）
    private bool _f8WasDown;
    private bool _f10WasDown;
    private bool _f12WasDown;

    /// <summary>
    /// 日志消息事件（用于非控制台环境）
    /// </summary>
    public event Action<string>? LogMessage;

    public event Action? OnF8Pressed;
    public event Action? OnF10Pressed;
    public event Action? OnF12Pressed;

    public void TriggerF8() => OnF8Pressed?.Invoke();

    /// <summary>
    /// 外部触发 F10（控制台命令备用）
    /// </summary>
    public void TriggerF10() => OnF10Pressed?.Invoke();

    /// <summary>
    /// 外部触发 F12（控制台命令备用）
    /// </summary>
    public void TriggerF12() => OnF12Pressed?.Invoke();

    public void Start()
    {
        _running = true;

        _messageLoopThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "HotkeyMessageLoop"
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();
    }

    private void MessageLoop()
    {
        _messageLoopThreadId = (uint)Environment.CurrentManagedThreadId;

        // 安装低层键盘钩子
        _proc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            LogMessage?.Invoke($"[警告] 键盘钩子安装失败 (错误码: {error})，请尝试以管理员权限运行。");
            LogMessage?.Invoke("[提示] 全局热键不可用，请使用界面按钮控制。");
        }
        else
        {
            LogMessage?.Invoke("[✓] 全局热键已就绪: F10=执行  F12=停止  F8=录制");
        }

        // 消息循环：低层钩子需要消息循环才能工作
        while (_running)
        {
            IntPtr result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
            if (result == IntPtr.Zero || result == new IntPtr(-1))
                break;

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // 卸载钩子
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam;
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (kb.vkCode == VK_F8 && !_f8WasDown)
                {
                    _f8WasDown = true;
                    Task.Run(() => OnF8Pressed?.Invoke());
                }
                else if (kb.vkCode == VK_F10 && !_f10WasDown)
                {
                    _f10WasDown = true;
                    Task.Run(() => OnF10Pressed?.Invoke());
                }
                else if (kb.vkCode == VK_F12 && !_f12WasDown)
                {
                    _f12WasDown = true;
                    Task.Run(() => OnF12Pressed?.Invoke());
                }
            }
            else if (msg == 0x0101 || msg == 0x0105) // WM_KEYUP / WM_SYSKEYUP
            {
                if (kb.vkCode == VK_F8) _f8WasDown = false;
                if (kb.vkCode == VK_F10) _f10WasDown = false;
                if (kb.vkCode == VK_F12) _f12WasDown = false;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Stop()
    {
        _running = false;

        // 发送 WM_QUIT 让 GetMessage 退出
        try
        {
            if (_messageLoopThreadId != 0)
                PostThreadMessage(_messageLoopThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _messageLoopThread?.Join(2000);
    }
}

