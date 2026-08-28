using System.Runtime.InteropServices;

namespace ClickSimulator.Services;

/// <summary>
/// 使用 Windows SendInput API 模拟鼠标和键盘输入
/// </summary>
public class InputSimulator
{
    #region Win32 API

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEKEYBDHARDWAREUNION mkhi;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEKEYBDHARDWAREUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    // Mouse events
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // Keyboard events
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    #endregion

    /// <summary>
    /// 平滑移动鼠标到绝对坐标（模拟人手移动轨迹）
    /// </summary>
    public void MoveTo(int targetX, int targetY, int speedMs = 120)
    {
        GetCursorPos(out var start);
        int startX = start.x;
        int startY = start.y;

        int dx = targetX - startX;
        int dy = targetY - startY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        // 距离太短直接瞬移
        if (distance < 5)
        {
            SetCursorPos(targetX, targetY);
            return;
        }

        // 步数：根据距离动态调整，每步约 3-5 像素
        int steps = Math.Max(5, (int)(distance / 4));
        double stepDelay = (double)speedMs / steps;

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            // 使用 ease-out 曲线让移动更自然：先快后慢
            double eased = 1.0 - Math.Pow(1.0 - t, 2.5);

            int curX = startX + (int)(dx * eased);
            int curY = startY + (int)(dy * eased);

            SetCursorPos(curX, curY);
            Thread.Sleep(Math.Max(1, (int)stepDelay));
        }

        // 确保精确到达
        SetCursorPos(targetX, targetY);
    }

    /// <summary>
    /// 将焦点设置到当前鼠标位置所在的窗口
    /// </summary>
    public void FocusWindowAtCursor()
    {
        GetCursorPos(out var pt);
        IntPtr hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero) return;

        // 使用 AttachThreadInput 技巧确保 SetForegroundWindow 生效
        uint currentThreadId = GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

        bool attached = false;
        if (currentThreadId != targetThreadId)
        {
            attached = AttachThreadInput(currentThreadId, targetThreadId, true);
        }

        SetForegroundWindow(hWnd);

        if (attached)
        {
            AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// 相对移动鼠标
    /// </summary>
    public void MoveRelative(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void LeftDown()
    {
        SendMouseButton(MOUSEEVENTF_LEFTDOWN);
    }

    public void LeftUp()
    {
        SendMouseButton(MOUSEEVENTF_LEFTUP);
    }

    public void LeftClick()
    {
        LeftDown();
        Thread.Sleep(30);
        LeftUp();
    }

    public void RightDown()
    {
        SendMouseButton(MOUSEEVENTF_RIGHTDOWN);
    }

    public void RightUp()
    {
        SendMouseButton(MOUSEEVENTF_RIGHTUP);
    }

    public void RightClick()
    {
        RightDown();
        Thread.Sleep(30);
        RightUp();
    }

    public void Scroll(int amount)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)amount,
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void KeyDown(Keys key)
    {
        SendKey(key, KEYEVENTF_SCANCODE); // 0x0008 = scan code mode (key down)
    }

    public void KeyUp(Keys key)
    {
        SendKey(key, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP);
    }

    public void KeyPress(Keys key)
    {
        KeyDown(key);
        Thread.Sleep(30);
        KeyUp(key);
    }

    public void KeyPress(string keyName)
    {
        if (Enum.TryParse<Keys>(keyName, true, out var key))
        {
            KeyPress(key);
        }
    }

    public void KeyDown(string keyName)
    {
        if (Enum.TryParse<Keys>(keyName, true, out var key))
        {
            KeyDown(key);
        }
    }

    public void KeyUp(string keyName)
    {
        if (Enum.TryParse<Keys>(keyName, true, out var key))
        {
            KeyUp(key);
        }
    }

    /// <summary>
    /// 检查 F10 或 F12 是否刚被按下（用于紧急停止）
    /// </summary>
    public static bool IsKeyPressed(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private void SendMouseButton(uint flag)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mkhi = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flag,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SendKey(Keys key, uint flag)
    {
        ushort vk = (ushort)key;
        uint scanCode = MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC = 0

        // 扩展键标记（Right Alt/Ctrl, Insert, Delete, Home, End, PgUp, PgDn, Arrows, NumLock 等）
        bool isExtended = key is Keys.Insert or Keys.Delete or Keys.Home or Keys.End
            or Keys.PageUp or Keys.PageDown or Keys.Left or Keys.Up or Keys.Right or Keys.Down
            or Keys.NumLock or Keys.PrintScreen or Keys.Divide
            or Keys.RControlKey or Keys.RMenu or Keys.RWin;

        uint dwFlags = flag | KEYEVENTF_SCANCODE;
        if (isExtended) dwFlags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            mkhi = new MOUSEKEYBDHARDWAREUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)scanCode,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
