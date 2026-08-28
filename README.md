# ClickSimulator

一个基于 .NET 8 (WinForms) 的鼠标键盘自动化模拟器。支持录制鼠标键盘操作、编辑脚本，并通过全局热键执行/停止。

## 功能特性

- 🖱️ **录制操作**：自动录制鼠标移动、点击、滚轮和键盘按键
- ▶️ **脚本执行**：解析并执行自定义脚本，支持循环与随机延迟
- ⌨️ **全局热键**：无需切换窗口即可控制执行/停止/录制
- 📜 **多脚本批量执行**：支持同时选择多个脚本依次执行
- 📊 **进度显示**：状态栏实时显示执行进度与统计信息

## 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 管理员权限（程序启动时自动请求 UAC 提权）

## 快速开始

```powershell
# 构建
dotnet build

# 运行
dotnet run
```

发布单文件可执行程序：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 全局热键

| 热键 | 功能 |
| ---- | ---- |
| `F8`  | 开始 / 停止录制 |
| `F10` | 执行选中的脚本 |
| `F12` | 停止执行 |

## 脚本语法

脚本文件存放在 `scripts/` 目录下，为纯文本 `.txt` 格式。

### 文件头指令

| 指令 | 说明 | 示例 |
| ---- | ---- | ---- |
| `#repeat` / `#loop` | 执行次数，`-1` 表示无限循环 | `#repeat 10` |
| `#clickdelay` | 点击延迟(ms)，支持随机范围 | `#clickdelay 150` 或 `#clickdelay 50, 200` |
| `'` 开头的行 | 注释 | `' 这是一行注释` |

### 命令列表

| 命令 | 参数 | 说明 |
| ---- | ---- | ---- |
| `MoveTo` | `x, y` | 移动鼠标到绝对坐标 |
| `MoveRelative` / `MoveRel` | `dx, dy` | 按相对位移移动鼠标 |
| `Delay` | `ms` 或 `min, max` | 延迟，可指定随机范围 |
| `LeftClick` | `[次数]` | 左键点击，默认 1 次 |
| `RightClick` | `[次数]` | 右键点击，默认 1 次 |
| `LeftDown` / `LeftUp` | — | 左键按下 / 抬起（用于拖拽） |
| `RightDown` / `RightUp` | — | 右键按下 / 抬起 |
| `KeyPress` | `"按键"` | 按下并释放按键 |
| `KeyDown` / `KeyUp` | `"按键"` | 按下 / 释放按键 |
| `Scroll` | `delta` | 滚轮滚动，正数向上、负数向下 |

### 示例

```
#repeat 3
#clickdelay 80, 200

MoveTo 1066, 634
LeftClick 1
Delay 500
MoveTo 841, 275
RightClick 1
Scroll 120
KeyPress "Enter"
```

## 项目结构

```
click_ui/
├── MainForm.cs                  # 主窗体与 UI 逻辑
├── Program.cs                   # 程序入口（含 UAC 提权）
├── ClickSimulator.csproj        # 项目文件
├── Models/
│   └── ScriptCommand.cs         # 命令模型与枚举
├── Services/
│   ├── GlobalHotkeyManager.cs   # 全局热键（键盘钩子）
│   ├── InputRecorder.cs         # 输入录制
│   ├── InputSimulator.cs        # 输入模拟
│   ├── ScriptExecutor.cs        # 脚本执行器
│   └── ScriptParser.cs          # 脚本解析器
└── scripts/                     # 示例脚本
```

## 注意事项

- 程序需要**管理员权限**才能在部分目标窗口中正常注入模拟输入。
- 使用 `F12` 可随时停止执行或录制。
- 录制结果保存在 `config/` 目录下，可通过界面重新加载执行。
