using System.Runtime.InteropServices;
using ClickSimulator.Models;

namespace ClickSimulator.Services;

/// <summary>
/// 鼠标键盘操作录制器，安装低层钩子捕获输入事件并生成脚本
/// </summary>
public class InputRecorder : IDisposable
{
    #region Win32 API

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
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
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    // 录制控制键（不录制这些键）
    private static readonly HashSet<uint> _controlKeys = new() { 0x77, 0x79, 0x7B }; // F8, F10, F12

    #endregion

    private volatile bool _recording;
    private Thread? _hookThread;
    private uint _threadId;
    private readonly List<RecordEntry> _entries = new();
    private DateTime _lastEventTime;
    private POINT _lastMousePos;
    private bool _leftDown;
    private bool _rightDown;
    private DateTime _lastMouseMoveTime;
    private POINT _pendingMovePos;
    private bool _hasPendingMove;

    public bool IsRecording => _recording;

    private record struct RecordEntry(DateTime Time, string Command);

    /// <summary>
    /// 开始录制
    /// </summary>
    public void Start()
    {
        if (_recording) return;

        _entries.Clear();
        _recording = true;
        _lastEventTime = DateTime.UtcNow;
        _leftDown = false;
        _rightDown = false;
        _hasPendingMove = false;

        _hookThread = new Thread(HookLoop)
        {
            IsBackground = true,
            Name = "RecorderHookLoop"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    /// <summary>
    /// 停止录制并生成脚本命令列表
    /// </summary>
    public List<ScriptCommand> Stop()
    {
        _recording = false;

        // 发送消息退出消息循环
        try { if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); }
        catch { }

        _hookThread?.Join(3000);

        // 确保按键释放
        FlushPendingMove();
        if (_leftDown) _entries.Add(new RecordEntry(DateTime.UtcNow, "LeftUp"));
        if (_rightDown) _entries.Add(new RecordEntry(DateTime.UtcNow, "RightUp"));

        return BuildScript();
    }

    private void HookLoop()
    {
        _threadId = (uint)Environment.CurrentManagedThreadId;

        var mouseProc = new LowLevelHookProc(MouseHookCallback);
        var keyboardProc = new LowLevelHookProc(KeyboardHookCallback);
        using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        var hMod = GetModuleHandle(curModule?.ModuleName);

        var mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, hMod, 0);
        var keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, hMod, 0);

        // 消息循环
        while (_recording)
        {
            var result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
            if (result == IntPtr.Zero || result == new IntPtr(-1))
                break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _recording)
        {
            uint msg = (uint)wParam;
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var now = DateTime.UtcNow;

            switch (msg)
            {
                case WM_MOUSEMOVE:
                    _pendingMovePos = ms.pt;
                    _hasPendingMove = true;
                    _lastMouseMoveTime = now;
                    break;

                case WM_LBUTTONDOWN:
                    FlushPendingMove();
                    RecordDelay(now);
                    _leftDown = true;
                    _entries.Add(new RecordEntry(now, $"LeftDown"));
                    _lastEventTime = now;
                    break;

                case WM_LBUTTONUP:
                    FlushPendingMove();
                    RecordDelay(now);
                    _leftDown = false;
                    _entries.Add(new RecordEntry(now, $"LeftUp"));
                    _lastEventTime = now;
                    break;

                case WM_RBUTTONDOWN:
                    FlushPendingMove();
                    RecordDelay(now);
                    _rightDown = true;
                    _entries.Add(new RecordEntry(now, $"RightDown"));
                    _lastEventTime = now;
                    break;

                case WM_RBUTTONUP:
                    FlushPendingMove();
                    RecordDelay(now);
                    _rightDown = false;
                    _entries.Add(new RecordEntry(now, $"RightUp"));
                    _lastEventTime = now;
                    break;

                case WM_MOUSEWHEEL:
                    FlushPendingMove();
                    RecordDelay(now);
                    int delta = (short)((ms.mouseData >> 16) & 0xFFFF);
                    _entries.Add(new RecordEntry(now, $"Scroll {delta}"));
                    _lastEventTime = now;
                    break;
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _recording)
        {
            uint msg = (uint)wParam;
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // 跳过控制键
            if (_controlKeys.Contains(kb.vkCode))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                FlushPendingMove();
                var now = DateTime.UtcNow;
                RecordDelay(now);
                var keyName = ((Keys)kb.vkCode).ToString();
                _entries.Add(new RecordEntry(now, $"KeyDown \"{keyName}\""));
                _lastEventTime = now;
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                var now = DateTime.UtcNow;
                RecordDelay(now);
                var keyName = ((Keys)kb.vkCode).ToString();
                _entries.Add(new RecordEntry(now, $"KeyUp \"{keyName}\""));
                _lastEventTime = now;
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void FlushPendingMove()
    {
        if (!_hasPendingMove) return;
        _hasPendingMove = false;

        // 只有移动超过 3 像素才记录
        int dx = Math.Abs(_pendingMovePos.x - _lastMousePos.x);
        int dy = Math.Abs(_pendingMovePos.y - _lastMousePos.y);
        if (dx < 3 && dy < 3) return;

        _lastMousePos = _pendingMovePos;
        var now = DateTime.UtcNow;

        // 鼠标移动延迟 > 50ms 才记录（合并微小移动）
        double msSinceLast = (now - _lastEventTime).TotalMilliseconds;
        if (msSinceLast >= 50)
        {
            RecordDelay(now);
            _entries.Add(new RecordEntry(now, $"MoveTo {_pendingMovePos.x}, {_pendingMovePos.y}"));
            _lastEventTime = now;
        }
    }

    private void RecordDelay(DateTime now)
    {
        int ms = (int)(now - _lastEventTime).TotalMilliseconds;
        if (ms >= 15)
        {
            _entries.Add(new RecordEntry(_lastEventTime, $"Delay {ms}"));
        }
    }

    /// <summary>
    /// 生成最终的脚本命令列表（合并连续 Down+Up 为 Click）
    /// </summary>
    private List<ScriptCommand> BuildScript()
    {
        var parser = new ScriptParser();
        var rawLines = new List<string>();

        // 第一步：合并短间隔的 Down+Up 为 Click
        var merged = new List<string>();
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            // 检测: Down → (短Delay) → Up，合并为 Click
            if (IsMouseDown(entry.Command) && i + 2 < _entries.Count)
            {
                var delayEntry = _entries[i + 1];
                var upEntry = _entries[i + 2];

                if (IsMatchingUp(entry.Command, upEntry.Command)
                    && delayEntry.Command.StartsWith("Delay ")
                    && int.TryParse(delayEntry.Command[6..], out int d) && d <= 300)
                {
                    string clickType = entry.Command.StartsWith("Left") ? "LeftClick" : "RightClick";
                    merged.Add($"{clickType} 1");
                    i += 2; // 跳过 Delay 和 Up
                    continue;
                }
            }

            merged.Add(entry.Command);
        }

        // 第二步：合并连续的 Delay（累加）
        var final = new List<string>();
        int accumulatedDelay = 0;
        foreach (var cmd in merged)
        {
            if (cmd.StartsWith("Delay ") && int.TryParse(cmd[6..], out int d))
            {
                accumulatedDelay += d;
            }
            else
            {
                if (accumulatedDelay >= 15)
                {
                    final.Add($"Delay {accumulatedDelay}");
                }
                accumulatedDelay = 0;
                final.Add(cmd);
            }
        }
        if (accumulatedDelay >= 15)
            final.Add($"Delay {accumulatedDelay}");

        // 解析为 ScriptCommand（使用 parser 的 ParseLine 能力）
        var commands = new List<ScriptCommand>();
        foreach (var line in final)
        {
            var cmd = ParseLine(line);
            if (cmd != null) commands.Add(cmd);
        }

        return commands;
    }

    private static bool IsMouseDown(string cmd) =>
        cmd == "LeftDown" || cmd == "RightDown";

    private static bool IsMatchingUp(string down, string up) =>
        (down == "LeftDown" && up == "LeftUp") || (down == "RightDown" && up == "RightUp");

    private static ScriptCommand? ParseLine(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var cmd = parts[0];
        var args = parts.Length > 1 ? parts[1] : "";

        return cmd switch
        {
            "MoveTo" => ParseMoveTo(args),
            "Delay" => new ScriptCommand { Type = CommandType.Delay, Value = int.TryParse(args, out var ms) ? ms : 100 },
            "LeftClick" => new ScriptCommand { Type = CommandType.LeftClick, Value = 1 },
            "RightClick" => new ScriptCommand { Type = CommandType.RightClick, Value = 1 },
            "LeftDown" => new ScriptCommand { Type = CommandType.LeftDown },
            "LeftUp" => new ScriptCommand { Type = CommandType.LeftUp },
            "RightDown" => new ScriptCommand { Type = CommandType.RightDown },
            "RightUp" => new ScriptCommand { Type = CommandType.RightUp },
            "KeyDown" => ParseKey(args, CommandType.KeyDown),
            "KeyUp" => ParseKey(args, CommandType.KeyUp),
            "Scroll" => new ScriptCommand { Type = CommandType.Scroll, Value = int.TryParse(args, out var v) ? v : 120 },
            _ => null
        };
    }

    private static ScriptCommand ParseMoveTo(string args)
    {
        var coords = args.Split(',', 2, StringSplitOptions.TrimEntries);
        return new ScriptCommand
        {
            Type = CommandType.MoveTo,
            X = coords.Length > 0 && int.TryParse(coords[0], out var x) ? x : 0,
            Y = coords.Length > 1 && int.TryParse(coords[1], out var y) ? y : 0
        };
    }

    private static ScriptCommand ParseKey(string args, CommandType type)
    {
        var text = args.Trim();
        if (text.StartsWith('"') && text.EndsWith('"'))
            text = text[1..^1];
        return new ScriptCommand { Type = type, Text = text, Value = 1 };
    }

    public void Dispose()
    {
        if (_recording) Stop();
    }
}
