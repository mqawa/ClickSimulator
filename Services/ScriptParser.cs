using ClickSimulator.Models;

namespace ClickSimulator.Services;

/// <summary>
/// 脚本解析结果，包含命令列表和重复次数
/// </summary>
public record ScriptParseResult
{
    public List<ScriptCommand> Commands { get; set; } = new();
    /// <summary>
    /// 重复次数：-1 表示无限循环，>=1 表示执行次数
    /// </summary>
    public int RepeatCount { get; set; } = 1;
    /// <summary>
    /// 点击延迟下限(ms)：0 表示使用默认 30ms
    /// </summary>
    public int ClickDelayMin { get; set; }
    /// <summary>
    /// 点击延迟上限(ms)：>0 表示随机范围 [ClickDelayMin, ClickDelayMax]，0 表示固定使用 ClickDelayMin
    /// </summary>
    public int ClickDelayMax { get; set; }
}

public class ScriptParser
{
    public ScriptParseResult Parse(string filePath)
    {
        var result = new ScriptParseResult();
        var lines = File.ReadAllLines(filePath);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // 文件头部的 #repeat 指令
            if (line.StartsWith("#repeat", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("#loop", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var count))
                {
                    result.RepeatCount = count; // -1 表示无限
                }
                continue;
            }

            // 文件头部的 #clickdelay 指令: #clickdelay 150 或 #clickdelay 50, 200
            if (line.StartsWith("#clickdelay", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var range = parts[1].Split(',', StringSplitOptions.TrimEntries);
                    if (range.Length >= 2
                        && int.TryParse(range[0], out var min)
                        && int.TryParse(range[1], out var max)
                        && max > min)
                    {
                        result.ClickDelayMin = min;
                        result.ClickDelayMax = max;
                    }
                    else if (int.TryParse(range[0], out var fixedDelay))
                    {
                        result.ClickDelayMin = fixedDelay;
                        result.ClickDelayMax = 0;
                    }
                }
                continue;
            }

            // ' 开头的行视为注释
            if (line.StartsWith("'"))
                continue;

            var cmd = ParseLine(line);
            if (cmd != null)
                result.Commands.Add(cmd);
        }

        // 自动修正鼠标 Down/Up 配对
        result.Commands = FixMousePairs(result.Commands);

        return result;
    }

    /// <summary>
    /// 自动修正鼠标按下/抬起配对：
    /// - LeftDown 后必须跟着 LeftUp，不能直接 LeftClick/RightClick/RightDown/LeftDown
    /// - RightDown 同理
    /// - 脚本末尾自动补上缺失的 Up
    /// </summary>
    private List<ScriptCommand> FixMousePairs(List<ScriptCommand> commands)
    {
        var fixedList = new List<ScriptCommand>();
        bool leftDown = false;
        bool rightDown = false;

        foreach (var cmd in commands)
        {
            switch (cmd.Type)
            {
                case CommandType.LeftDown:
                    if (leftDown)
                        fixedList.Add(new ScriptCommand { Type = CommandType.LeftUp });
                    if (rightDown)
                    {
                        fixedList.Add(new ScriptCommand { Type = CommandType.RightUp });
                        rightDown = false;
                    }
                    leftDown = true;
                    fixedList.Add(cmd);
                    break;

                case CommandType.RightDown:
                    if (rightDown)
                        fixedList.Add(new ScriptCommand { Type = CommandType.RightUp });
                    if (leftDown)
                    {
                        fixedList.Add(new ScriptCommand { Type = CommandType.LeftUp });
                        leftDown = false;
                    }
                    rightDown = true;
                    fixedList.Add(cmd);
                    break;

                case CommandType.LeftClick:
                    if (rightDown)
                    {
                        fixedList.Add(new ScriptCommand { Type = CommandType.RightUp });
                        rightDown = false;
                    }
                    if (leftDown)
                    {
                        // LeftClick 自带 LeftDown+LeftUp，先补一个 LeftUp
                        fixedList.Add(new ScriptCommand { Type = CommandType.LeftUp });
                        leftDown = false;
                    }
                    fixedList.Add(cmd);
                    break;

                case CommandType.RightClick:
                    if (leftDown)
                    {
                        fixedList.Add(new ScriptCommand { Type = CommandType.LeftUp });
                        leftDown = false;
                    }
                    if (rightDown)
                    {
                        fixedList.Add(new ScriptCommand { Type = CommandType.RightUp });
                        rightDown = false;
                    }
                    fixedList.Add(cmd);
                    break;

                case CommandType.LeftUp:
                    leftDown = false;
                    fixedList.Add(cmd);
                    break;

                case CommandType.RightUp:
                    rightDown = false;
                    fixedList.Add(cmd);
                    break;

                default:
                    fixedList.Add(cmd);
                    break;
            }
        }

        // 末尾补上缺失的 Up
        if (leftDown)
            fixedList.Add(new ScriptCommand { Type = CommandType.LeftUp });
        if (rightDown)
            fixedList.Add(new ScriptCommand { Type = CommandType.RightUp });

        return fixedList;
    }

    private ScriptCommand? ParseLine(string line)
    {
        // Split by space, but handle quoted strings
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var commandName = parts[0];
        var args = parts.Length > 1 ? parts[1] : "";

        return commandName switch
        {
            "MoveTo" => ParseMoveTo(args),
            "MoveRelative" or "MoveRel" => ParseMoveRelative(args),
            "Delay" => ParseDelay(args),
            "LeftClick" => new ScriptCommand { Type = CommandType.LeftClick, Value = ParseOptionalInt(args) },
            "RightClick" => new ScriptCommand { Type = CommandType.RightClick, Value = ParseOptionalInt(args) },
            "LeftDown" => new ScriptCommand { Type = CommandType.LeftDown },
            "LeftUp" => new ScriptCommand { Type = CommandType.LeftUp },
            "RightDown" => new ScriptCommand { Type = CommandType.RightDown },
            "RightUp" => new ScriptCommand { Type = CommandType.RightUp },
            "KeyPress" => ParseKeyPress(args),
            "KeyDown" => ParseKey(args, CommandType.KeyDown),
            "KeyUp" => ParseKey(args, CommandType.KeyUp),
            "Scroll" => ParseScroll(args),
            _ => null // Unknown command, silently skip
        };
    }

    private ScriptCommand ParseMoveTo(string args)
    {
        var coords = args.Split(',', 2, StringSplitOptions.TrimEntries);
        return new ScriptCommand
        {
            Type = CommandType.MoveTo,
            X = coords.Length > 0 && int.TryParse(coords[0], out var x) ? x : 0,
            Y = coords.Length > 1 && int.TryParse(coords[1], out var y) ? y : 0
        };
    }

    private ScriptCommand ParseMoveRelative(string args)
    {
        var coords = args.Split(',', 2, StringSplitOptions.TrimEntries);
        return new ScriptCommand
        {
            Type = CommandType.MoveRelative,
            X = coords.Length > 0 && int.TryParse(coords[0], out var x) ? x : 0,
            Y = coords.Length > 1 && int.TryParse(coords[1], out var y) ? y : 0
        };
    }

    private ScriptCommand ParseDelay(string args)
    {
        // 支持 Delay 500 (固定) 或者 Delay 100, 1000 (随机范围)
        var parts = args.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var min)
            && int.TryParse(parts[1], out var max)
            && max > min)
        {
            return new ScriptCommand
            {
                Type = CommandType.Delay,
                Value = min,
                Value2 = max
            };
        }

        return new ScriptCommand
        {
            Type = CommandType.Delay,
            Value = int.TryParse(args, out var ms) ? ms : 1000
        };
    }

    private ScriptCommand ParseKey(string args, CommandType type)
    {
        var text = args.Trim();
        int repeat = 1;

        // 处理带引号的格式: KeyDown "Space", 1 或 KeyDown "1", 3
        if (text.StartsWith('"'))
        {
            var closeQuote = text.IndexOf('"', 1);
            if (closeQuote > 0)
            {
                var keyName = text[1..closeQuote];
                var rest = text[(closeQuote + 1)..].Trim();
                if (rest.StartsWith(','))
                    int.TryParse(rest[1..].Trim(), out repeat);
                return new ScriptCommand { Type = type, Text = keyName, Value = repeat };
            }
        }

        // 不带引号: KeyDown Space 或 KeyDown A
        return new ScriptCommand { Type = type, Text = text, Value = repeat };
    }

    private ScriptCommand ParseKeyPress(string args)
    {
        var text = args.Trim();

        // 处理带引号的格式: KeyPress "Space"
        if (text.StartsWith('"'))
        {
            var closeQuote = text.IndexOf('"', 1);
            if (closeQuote > 0)
                return new ScriptCommand { Type = CommandType.KeyPress, Text = text[1..closeQuote] };
        }

        return new ScriptCommand { Type = CommandType.KeyPress, Text = text };
    }

    private ScriptCommand ParseScroll(string args)
    {
        return new ScriptCommand
        {
            Type = CommandType.Scroll,
            Value = int.TryParse(args, out var amount) ? amount : 120
        };
    }

    private int ParseOptionalInt(string args)
    {
        return int.TryParse(args, out var v) ? v : 1;
    }
}
