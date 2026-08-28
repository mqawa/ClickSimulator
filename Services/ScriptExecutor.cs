using ClickSimulator.Models;

namespace ClickSimulator.Services;

public class ScriptExecutor
{
    private readonly InputSimulator _input = new();
    private volatile bool _stopRequested;
    private int _clickDelayMin;
    private int _clickDelayMax;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// 请求停止当前执行
    /// </summary>
    public void RequestStop()
    {
        _stopRequested = true;
    }

    private void FocusWindowForInput()
    {
        _input.FocusWindowAtCursor();
    }

    /// <summary>
    /// 执行单个命令
    /// </summary>
    private void ExecuteCommand(ScriptCommand cmd)
    {
        if (_stopRequested) return;

        switch (cmd.Type)
        {
            case CommandType.MoveTo:
                _input.MoveTo(cmd.X, cmd.Y);
                FocusWindowForInput();
                break;

            case CommandType.MoveRelative:
                _input.MoveRelative(cmd.X, cmd.Y);
                break;

            case CommandType.Delay:
                // Value2 > 0 时启用随机: Value 为下限, Value2 为上限
                int delayMs = cmd.Value2 > 0
                    ? Random.Shared.Next(cmd.Value, cmd.Value2 + 1)
                    : cmd.Value;
                DelayWithCheck(delayMs);
                break;

            case CommandType.LeftClick:
                for (int i = 0; i < cmd.Value && !_stopRequested; i++)
                {
                    _input.LeftDown();
                    DelayWithCheck(GetClickDelay());
                    _input.LeftUp();
                    if (i < cmd.Value - 1) DelayWithCheck(30);
                }
                break;

            case CommandType.RightClick:
                for (int i = 0; i < cmd.Value && !_stopRequested; i++)
                {
                    _input.RightDown();
                    DelayWithCheck(GetClickDelay());
                    _input.RightUp();
                    if (i < cmd.Value - 1) DelayWithCheck(30);
                }
                break;

            case CommandType.LeftDown:
                _input.LeftDown();
                break;

            case CommandType.LeftUp:
                _input.LeftUp();
                break;

            case CommandType.RightDown:
                _input.RightDown();
                break;

            case CommandType.RightUp:
                _input.RightUp();
                break;

            case CommandType.KeyPress:
                FocusWindowForInput();
                _input.KeyPress(cmd.Text!);
                break;

            case CommandType.KeyDown:
                FocusWindowForInput();
                for (int i = 0; i < cmd.Value && !_stopRequested; i++)
                    _input.KeyDown(cmd.Text!);
                break;

            case CommandType.KeyUp:
                FocusWindowForInput();
                for (int i = 0; i < cmd.Value && !_stopRequested; i++)
                    _input.KeyUp(cmd.Text!);
                break;

            case CommandType.Scroll:
                _input.Scroll(cmd.Value);
                break;
        }
    }

    /// <summary>
    /// 执行脚本命令列表，支持多次循环
    /// </summary>
    public async Task ExecuteLoopAsync(List<ScriptCommand> commands, int loopCount,
        int clickDelayMin, int clickDelayMax, Action<string> log,
        Action<int, int, int>? onCommandProgress = null)
    {
        _clickDelayMin = clickDelayMin;
        _clickDelayMax = clickDelayMax;
        IsRunning = true;
        _stopRequested = false;

        int totalCommands = commands.Count;

        try
        {
            int currentLoop = 0;
            while (!_stopRequested && (loopCount == 0 || currentLoop < loopCount))
            {
                currentLoop++; // 1-based
                if (loopCount == 0)
                    log($"--- 第 {currentLoop} 轮 (无限循环) ---");
                else
                    log($"--- 第 {currentLoop}/{loopCount} 轮 ---");

                int cmdIdx = 0;
                foreach (var cmd in commands)
                {
                    if (_stopRequested) break;

                    // 报告进度 (cmdIdx, totalCmds, currentLoop)
                    onCommandProgress?.Invoke(cmdIdx, totalCommands, currentLoop);

                    // 执行每个命令前检查 F12 是否被按下（通过 GetAsyncKeyState）
                    if (InputSimulator.IsKeyPressed(Keys.F12))
                    {
                        _stopRequested = true;
                        log("检测到 F12，停止执行。");
                        break;
                    }

                    ExecuteCommand(cmd);
                    cmdIdx++;
                }

                // 本轮完成
                onCommandProgress?.Invoke(totalCommands, totalCommands, currentLoop);

                if (!_stopRequested && loopCount == 0)
                {
                    // 无限循环时轮次间短暂延迟，防止 CPU 空转
                    await Task.Delay(100);
                }
            }

            log(_stopRequested ? "执行已停止。" : "执行完成。");
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// 根据 #clickdelay 配置计算点击延迟
    /// </summary>
    private int GetClickDelay()
    {
        if (_clickDelayMin <= 0)
            return 30; // 默认 30ms

        if (_clickDelayMax > _clickDelayMin)
            return Random.Shared.Next(_clickDelayMin, _clickDelayMax + 1);

        return _clickDelayMin;
    }

    /// <summary>
    /// 可中断的延迟
    /// </summary>
    private void DelayWithCheck(int milliseconds)
    {
        int elapsed = 0;
        int step = 50; // 每 50ms 检查一次

        while (elapsed < milliseconds && !_stopRequested)
        {
            int wait = Math.Min(step, milliseconds - elapsed);
            Thread.Sleep(wait);
            elapsed += wait;

            // 检查 F12
            if (InputSimulator.IsKeyPressed(Keys.F12))
            {
                _stopRequested = true;
            }
        }
    }
}
