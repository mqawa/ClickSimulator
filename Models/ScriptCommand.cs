namespace ClickSimulator.Models;

public enum CommandType
{
    MoveTo,
    Delay,
    LeftClick,
    RightClick,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    KeyPress,
    KeyDown,
    KeyUp,
    Scroll,
    MoveRelative
}

public class ScriptCommand
{
    public CommandType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Value { get; set; }       // Delay(ms), 随机下限, repeat count
    public int Value2 { get; set; }      // Delay 随机上限, >0 表示启用随机
    public string? Text { get; set; }    // For KeyPress/KeyDown/KeyUp
}
