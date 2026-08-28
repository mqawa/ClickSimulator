using ClickSimulator.Models;
using ClickSimulator.Services;

namespace ClickSimulator;

public partial class MainForm : Form
{
    #region 服务实例

    private readonly ScriptExecutor _executor = new();
    private readonly GlobalHotkeyManager _hotkeyManager = new();
    private InputRecorder? _recorder;
    private CancellationTokenSource? _cts;
    private readonly ScriptParser _parser = new();
    private readonly object _lockObj = new();

    #endregion

    #region 状态

    private string _configPath = "";
    private List<string> _scriptFiles = new();
    private string? _chosenFile;
    private List<ScriptParseResult> _allResults = new();

    #endregion

    #region UI 控件

    private ToolStrip _toolStrip = null!;
    private ToolStripButton _btnExecute = null!;
    private ToolStripButton _btnRecord = null!;
    private ToolStripButton _btnStop = null!;
    private ToolStripButton _btnSelectScript = null!;
    private ToolStripButton _btnReload = null!;
    private ToolStripButton _btnClearLog = null!;
    private ToolStripLabel _lblRepeat = null!;
    private ToolStripTextBox _txtRepeat = null!;
    private ToolStripButton _btnApplyRepeat = null!;

    private SplitContainer _splitContainer = null!;
    private Panel _leftPanel = null!;
    private Label _lblScriptList = null!;
    private ListBox _lstScripts = null!;
    private CheckBox _chkSelectAll = null!;
    private RichTextBox _rtbLog = null!;

    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblStatus = null!;
    private ToolStripStatusLabel _lblScriptInfo = null!;
    private ToolStripProgressBar _progressBar = null!;

    private System.Windows.Forms.Timer _countdownTimer = null!;
    private int _countdownSeconds;

    // 执行统计
    private DateTime _execStartTime;
    private int _totalCommandsExecuted;
    private int _totalScriptsExecuted;
    private int _totalLoopsExecuted;

    // 进度条覆盖显示
    private int _progressMarkerPos = -1;
    private const int ProgressBarWidth = 40;

    #endregion

    public MainForm()
    {
        InitializeComponent();
        InitializeApp();
    }

    private void InitializeComponent()
    {
        // ===== 窗体设置 =====
        Text = "ClickSimulator - 鼠标键盘模拟器";
        Size = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 450);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath!)!;

        // ===== ToolStrip =====
        _toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };

        _btnExecute = new ToolStripButton("  ▶  执行 (F10)")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "开始执行脚本 (快捷键: F10)"
        };
        _btnExecute.Click += (s, e) => TriggerExecute();

        _btnRecord = new ToolStripButton("  ⏺  录制 (F8)")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "开始/停止录制 (快捷键: F8)"
        };
        _btnRecord.Click += (s, e) => ToggleRecording();

        _btnStop = new ToolStripButton("  ⏹  停止 (F12)")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Enabled = false,
            ToolTipText = "停止执行 (快捷键: F12)"
        };
        _btnStop.Click += (s, e) => TriggerStop();

        _btnSelectScript = new ToolStripButton("  📂  浏览...")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "浏览并加载 scripts 文件夹外的脚本文件"
        };
        _btnSelectScript.Click += (s, e) => SelectAndLoadScripts();

        _btnReload = new ToolStripButton("  🔄  重新加载")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "重新加载当前脚本"
        };
        _btnReload.Click += (s, e) => ReloadCurrentScripts();

        _btnClearLog = new ToolStripButton("  🧹  清空日志")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "清空日志输出"
        };
        _btnClearLog.Click += (s, e) => ClearLog();

        var sep1 = new ToolStripSeparator();

        _lblRepeat = new ToolStripLabel("循环次数:");
        _txtRepeat = new ToolStripTextBox
        {
            Text = "",
            Size = new Size(50, 23),
            ToolTipText = "留空使用脚本默认值; 0=无限循环"
        };
        _btnApplyRepeat = new ToolStripButton("应用")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "应用循环次数覆盖"
        };
        _btnApplyRepeat.Click += (s, e) => ApplyRepeatOverride();

        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _btnExecute, _btnRecord, _btnStop,
            new ToolStripSeparator(),
            _btnSelectScript, _btnReload,
            new ToolStripSeparator(),
            _btnClearLog,
            sep1,
            _lblRepeat, _txtRepeat, _btnApplyRepeat
        });

        // ===== SplitContainer =====
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Panel1MinSize = 180,
            Panel2MinSize = 100
        };

        // ===== 左侧面板 - 脚本列表 =====
        _leftPanel = new Panel { Dock = DockStyle.Fill };

        _lblScriptList = new Label
        {
            Text = "脚本列表:",
            Location = new Point(8, 8),
            Size = new Size(200, 20),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };

        _lstScripts = new ListBox
        {
            Location = new Point(8, 32),
            Size = new Size(210, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            SelectionMode = SelectionMode.One
        };

        _chkSelectAll = new CheckBox
        {
            Text = "循环执行所有脚本",
            Location = new Point(8, 240),
            Size = new Size(200, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            AutoSize = true
        };
        _chkSelectAll.CheckedChanged += (s, e) =>
        {
            _lstScripts.Enabled = !_chkSelectAll.Checked;
            if (_chkSelectAll.Checked)
                _lstScripts.ClearSelected();
        };

        _leftPanel.Controls.Add(_lblScriptList);
        _leftPanel.Controls.Add(_lstScripts);
        _leftPanel.Controls.Add(_chkSelectAll);

        // ===== 右侧面板 - 日志 =====
        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Consolas", 9.75F),
            WordWrap = true,
            BorderStyle = BorderStyle.None
        };

        _splitContainer.Panel1.Controls.Add(_leftPanel);
        _splitContainer.Panel2.Controls.Add(_rtbLog);

        // ===== StatusStrip =====
        _statusStrip = new StatusStrip();

        _lblStatus = new ToolStripStatusLabel("就绪")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblScriptInfo = new ToolStripStatusLabel("未加载脚本")
        {
            Width = 250,
            TextAlign = ContentAlignment.MiddleRight
        };

        _progressBar = new ToolStripProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Visible = false,
            Width = 100
        };

        _statusStrip.Items.AddRange(new ToolStripItem[] { _lblStatus, _progressBar, _lblScriptInfo });

        // ===== 倒计时定时器 =====
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += CountdownTimer_Tick;

        // ===== 添加到窗体 =====
        Controls.Add(_splitContainer);
        Controls.Add(_toolStrip);
        Controls.Add(_statusStrip);
    }

    private void InitializeApp()
    {
        // 查找 scripts 文件夹
        _configPath = FindScriptsFolder();

        // 刷新脚本列表
        RefreshScriptFiles();
        RefreshScriptListUI();

        // 启动时自动加载第一个脚本（RefreshScriptListUI 内部设索引时事件未触发）
        if (_scriptFiles.Count > 0)
        {
            _chosenFile = _scriptFiles[0];
            ParseAndLoad();
        }

        // 初始化录制器
        _recorder = new InputRecorder();

        // 设置热键事件（Start 在 OnLoad 中调用）
        _hotkeyManager.LogMessage += msg => this.BeginInvoke(() => AppendLog(msg));
        _hotkeyManager.OnF8Pressed += () => this.BeginInvoke(ToggleRecording);
        _hotkeyManager.OnF10Pressed += () => this.BeginInvoke(TriggerExecute);
        // F12 停止：直接操作（线程安全），无需等待 UI 封送
        _hotkeyManager.OnF12Pressed += () =>
        {
            _executor.RequestStop();
            _cts?.Cancel();
            this.BeginInvoke(() =>
            {
                if (_countdownTimer.Enabled)
                {
                    _countdownTimer.Stop();
                    AppendLog("[取消] 倒计时被中断。", Color.Orange);
                    ResetButtonsAfterStop();
                }
                else
                {
                    AppendLog("\n[F12] 停止执行！", Color.Orange);
                }
            });
        };

        AppendLog("[✓] 程序就绪，请选择脚本后按 F10 或点击执行按钮。", Color.Lime);
        AppendLog("");

        UpdateStatus("就绪");
    }

    #region 核心逻辑

    private void TriggerExecute()
    {
        lock (_lockObj)
        {
            if (_executor.IsRunning)
            {
                AppendLog("[提示] 脚本已在运行中，请先停止。", Color.Yellow);
                return;
            }

            if (_recorder!.IsRecording)
            {
                AppendLog("[提示] 录制中，请先停止录制。", Color.Yellow);
                return;
            }

            if (_allResults.Count == 0)
            {
                AppendLog("[提示] 尚未加载脚本，请先选择。", Color.Yellow);
                SelectAndLoadScripts();
                if (_allResults.Count == 0) return;
            }

            // 应用循环次数覆盖
            ApplyRepeatFromTextBox();

            // 启动 10 秒倒计时
            _countdownSeconds = 10;
            _btnExecute.Enabled = false;
            _btnStop.Enabled = true;
            _btnRecord.Enabled = false;
            _btnSelectScript.Enabled = false;
            _btnReload.Enabled = false;
            _lstScripts.Enabled = false;
            _chkSelectAll.Enabled = false;
            _progressBar.Visible = true;
            _lblStatus.Text = $"将在 {_countdownSeconds} 秒后开始执行... (可按 F12 取消)";

            AppendLog($"\n[F10] 将在 {_countdownSeconds} 秒后开始执行脚本...", Color.Cyan);
            _countdownTimer.Start();
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        _countdownSeconds--;
        _lblStatus.Text = $"将在 {_countdownSeconds} 秒后开始执行... (可按 F12 取消)";

        if (_countdownSeconds <= 0)
        {
            _countdownTimer.Stop();
            StartExecution();
        }
    }

    private void StartExecution()
    {
        lock (_lockObj)
        {
            _lblStatus.Text = "执行中...";
            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _btnStop.Enabled = true;
            _btnExecute.Enabled = false;
            _lstScripts.Enabled = false;
            _chkSelectAll.Enabled = false;

            AppendLog("倒计时结束，开始执行!", Color.Lime);

            _progressMarkerPos = _rtbLog.TextLength;
            AppendLog("");
            AppendLog("");

            _execStartTime = DateTime.Now;
            _totalCommandsExecuted = 0;
            _totalScriptsExecuted = 0;
            _totalLoopsExecuted = 0;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                bool wasStopped = false;

                try
                {
                    int scriptIdx = 0;
                    int totalScripts = _allResults.Count;

                    while (!token.IsCancellationRequested)
                    {
                        for (int si = 0; si < _allResults.Count; si++)
                        {
                            if (token.IsCancellationRequested) break;
                            var result = _allResults[si];
                            int loops = result.RepeatCount == -1 ? 0 : result.RepeatCount;
                            var label = result.RepeatCount == -1 ? "无限" : result.RepeatCount.ToString();

                            this.BeginInvoke(() => AppendLog($"\n>>> 执行脚本 #{scriptIdx + 1} (重复 {label} 次) <<<", Color.Cyan));

                            this.BeginInvoke(() =>
                            {
                                _progressMarkerPos = _rtbLog.TextLength;
                                AppendLog("");
                                AppendLog("");
                            });

                            int cmdsPerCycle = result.Commands.Count;
                            int prevLoop = 0;

                            await _executor.ExecuteLoopAsync(result.Commands, loops,
                                result.ClickDelayMin, result.ClickDelayMax,
                                msg => this.BeginInvoke(() => AppendLog(msg)),
                                (cmdIdx, totalCmds, currentLoop) =>
                                {
                                    this.BeginInvoke(() =>
                                    {
                                        UpdateProgress(cmdIdx, totalCmds, currentLoop,
                                            scriptIdx, totalScripts, loops);
                                        if (currentLoop > prevLoop)
                                        {
                                            _totalLoopsExecuted += (currentLoop - prevLoop);
                                            _totalCommandsExecuted += (currentLoop - prevLoop) * cmdsPerCycle;
                                            prevLoop = currentLoop;
                                        }
                                    });
                                });

                            if (_executor.IsRunning) _totalScriptsExecuted++;
                            else { wasStopped = true; break; }
                            scriptIdx++;
                        }

                        if (token.IsCancellationRequested || !_executor.IsRunning) break;
                        await Task.Delay(200, token);
                    }
                }
                catch (TaskCanceledException) { wasStopped = true; }
                finally
                {
                    bool stopped = wasStopped;
                    this.BeginInvoke(() =>
                    {
                        PrintExecutionReport(stopped);
                        OnExecutionFinished();
                    });
                }
            }, token);
        }
    }

    private void PrintExecutionReport(bool wasStopped)
    {
        var elapsed = DateTime.Now - _execStartTime;
        string timeStr = elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1} 秒"
            : $"{elapsed.Minutes} 分 {elapsed.Seconds} 秒";

        var scriptNames = _allResults.Select(r =>
            _chosenFile == "__ALL__" ? "全部脚本" : Path.GetFileNameWithoutExtension(_chosenFile ?? "?")).Distinct();

        AppendLog("");
        AppendLog("═══════════════════════════════════", Color.FromArgb(120, 200, 255));
        AppendLog("  执行报告", Color.FromArgb(255, 220, 100));
        AppendLog("───────────────────────────────────", Color.FromArgb(120, 200, 255));
        AppendLog($"  耗时:       {timeStr}", Color.White);
        AppendLog($"  脚本:       {string.Join(", ", scriptNames)}", Color.White);
        AppendLog($"  执行脚本:   {_totalScriptsExecuted} 次", Color.White);
        AppendLog($"  循环轮数:   {_totalLoopsExecuted} 轮", Color.White);
        AppendLog($"  执行命令:   ~{_totalCommandsExecuted} 条", Color.White);
        AppendLog($"  状态:       {(wasStopped ? "已停止" : "完成")}", wasStopped ? Color.Orange : Color.Lime);
        AppendLog("═══════════════════════════════════", Color.FromArgb(120, 200, 255));
        AppendLog("");
    }

    /// <summary>
    /// 覆盖更新两行进度条（在 RichTextBox 中原地刷新）
    /// </summary>
    private void UpdateProgress(int cmdIdx, int totalCmds, int currentLoop,
        int scriptIdx, int totalScripts, int loopCount)
    {
        if (_progressMarkerPos < 0) return;

        // 行1: 当前脚本命令进度（本轮内）
        double scriptPct = totalCmds > 0 ? (double)cmdIdx / totalCmds : 1.0;
        string bar1 = MakeProgressBar(scriptPct, ProgressBarWidth);
        string line1 = $"{bar1} {scriptPct * 100,5:0}%  脚本命令 ({cmdIdx}/{totalCmds})";

        // 行2: 总体进度 = 已完成的脚本权重 + 当前脚本中 (已完成的轮次 + 当前轮内进度)
        double perScript = 1.0 / totalScripts;
        double overallFromFinished = (double)scriptIdx * perScript;

        double overallFromCurrentScript;
        int effectiveLoops = loopCount == 0 ? 1 : loopCount; // 无限循环时无法计算精确百分比
        double loopProgress = totalCmds > 0 ? (double)cmdIdx / totalCmds : 1.0;
        int completedLoops = currentLoop - 1;
        if (loopCount == 0)
        {
            // 无限循环：只显示命令进度，不计算精确百分比
            overallFromCurrentScript = loopProgress * perScript * 0.1; // 象征性
        }
        else
        {
            overallFromCurrentScript = (completedLoops + loopProgress) / effectiveLoops * perScript;
        }

        double overallPct = Math.Min(overallFromFinished + overallFromCurrentScript, 1.0);
        string loopLabel = loopCount == 0 ? "∞" : $"{currentLoop}/{loopCount}";
        string bar2 = MakeProgressBar(overallPct, ProgressBarWidth);
        string line2 = $"{bar2} {overallPct * 100,5:0}%  总体进度  (脚本 {scriptIdx + 1}/{totalScripts}, 第 {loopLabel} 轮)";

        string combined = line1 + "\n" + line2;

        // 原地覆盖
        int savedPos = _rtbLog.SelectionStart;
        int savedLen = _rtbLog.SelectionLength;

        _rtbLog.Select(_progressMarkerPos, _rtbLog.TextLength - _progressMarkerPos);
        _rtbLog.SelectionColor = Color.FromArgb(120, 200, 255);
        _rtbLog.SelectedText = combined;

        // 恢复选区
        _rtbLog.Select(savedPos, savedLen);
    }

    private static string MakeProgressBar(double pct, int width)
    {
        int filled = Math.Max(0, Math.Min(width, (int)(pct * width)));
        return "[" + new string('█', filled) + new string('░', width - filled) + "]";
    }

    private void ClearLog()
    {
        _rtbLog.Clear();
        _progressMarkerPos = -1;
    }

    private void OnExecutionFinished()
    {
        _progressMarkerPos = -1;
        ResetButtonsAfterStop();
    }

    private void TriggerStop()
    {
        if (_countdownTimer.Enabled)
        {
            _countdownTimer.Stop();
            AppendLog("[取消] 倒计时被中断。", Color.Orange);
            ResetButtonsAfterStop();
            return;
        }

        AppendLog("\n[F12] 停止执行！", Color.Orange);
        _executor.RequestStop();
        _cts?.Cancel();
    }

    private void ResetButtonsAfterStop()
    {
        _btnExecute.Enabled = true;
        _btnStop.Enabled = false;
        _btnRecord.Enabled = true;
        _btnSelectScript.Enabled = true;
        _btnReload.Enabled = true;
        _lstScripts.Enabled = true;
        _chkSelectAll.Enabled = true;
        _progressBar.Visible = false;
        UpdateStatus("就绪");
    }

    private void ToggleRecording()
    {
        lock (_lockObj)
        {
            if (_recorder!.IsRecording)
                StopRecording();
            else if (_executor.IsRunning)
                AppendLog("[提示] 脚本执行中，请先停止再录制。", Color.Yellow);
            else
                StartRecording();
        }
    }

    private void StartRecording()
    {
        AppendLog("\n[● REC] 开始录制... (按 F8 或点击录制按钮停止)", Color.OrangeRed);
        _recorder!.Start();
        _btnRecord.Text = "  ⏹  停止录制 (F8)";
        _btnExecute.Enabled = false;
        _btnSelectScript.Enabled = false;
        _btnReload.Enabled = false;
        UpdateStatus("录制中...");
    }

    private void StopRecording()
    {
        var commands = _recorder!.Stop();
        _btnRecord.Text = "  ⏺  录制 (F8)";
        _btnExecute.Enabled = true;
        _btnSelectScript.Enabled = true;
        _btnReload.Enabled = true;

        AppendLog($"[■] 录制停止，共 {commands.Count} 条命令。", Color.OrangeRed);

        if (commands.Count == 0)
        {
            AppendLog("[提示] 没有录制到任何操作。", Color.Yellow);
            UpdateStatus("就绪");
            return;
        }

        // 弹出保存对话框
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        using var sfd = new SaveFileDialog
        {
            Title = "保存录制的脚本",
            FileName = $"record_{timestamp}.txt",
            Filter = "脚本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = _configPath
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            SaveRecordedScript(sfd.FileName, commands);
        }
        else
        {
            // 用户取消保存，仍然用默认名保存
            string defaultPath = Path.Combine(_configPath, $"record_{timestamp}.txt");
            SaveRecordedScript(defaultPath, commands);
        }

        UpdateStatus("就绪");
    }

    private void SaveRecordedScript(string filePath, List<ScriptCommand> commands)
    {
        try
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("' 录制时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteLine($"' 命令数: {commands.Count}");
            writer.WriteLine("#repeat 1");
            writer.WriteLine();

            foreach (var cmd in commands)
            {
                writer.WriteLine(FormatCommand(cmd));
            }

            AppendLog($"[✓] 已保存到: {Path.GetFileName(filePath)}", Color.Lime);

            // 刷新脚本列表
            RefreshScriptFiles();
            RefreshScriptListUI();

            // 自动加载刚录制的脚本
            _chosenFile = filePath;
            _allResults.Clear();
            var result = _parser.Parse(filePath);
            _allResults.Add(result);
            _lblScriptInfo.Text = $"已加载: {Path.GetFileName(filePath)} ({result.Commands.Count} 条命令)";

            AppendLog("[提示] 已自动加载刚录制的脚本。", Color.Lime);
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 保存脚本失败: {ex.Message}", Color.Red);
        }
    }

    private bool SelectAndLoadScripts()
    {
        RefreshScriptFiles();
        RefreshScriptListUI();

        if (_scriptFiles.Count == 0)
        {
            AppendLog("[提示] scripts 文件夹中没有脚本文件。", Color.Yellow);
            AppendLog("[提示] 您可以先使用录制功能来创建脚本。", Color.Yellow);
            return false;
        }

        using var ofd = new OpenFileDialog
        {
            Title = "选择脚本文件",
            Filter = "脚本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = _configPath,
            Multiselect = false
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _chosenFile = ofd.FileName;
            _chkSelectAll.Checked = false;

            // 在列表中也选中对应项
            for (int i = 0; i < _scriptFiles.Count; i++)
            {
                if (string.Equals(_scriptFiles[i], _chosenFile, StringComparison.OrdinalIgnoreCase))
                {
                    _lstScripts.SelectedIndex = i;
                    break;
                }
            }

            return ParseAndLoad();
        }

        return false;
    }

    private bool ParseAndLoad()
    {
        if (_chkSelectAll.Checked)
        {
            _chosenFile = "__ALL__";
            _allResults.Clear();

            foreach (var file in _scriptFiles)
            {
                AppendLog($"解析脚本: {Path.GetFileName(file)}", Color.White);
                var result = _parser.Parse(file);
                var label = result.RepeatCount == -1 ? "无限循环" : $"{result.RepeatCount} 次";
                AppendLog($"  -> {result.Commands.Count} 条命令, 重复: {label}", Color.Gray);
                _allResults.Add(result);
            }

            _lblScriptInfo.Text = $"已加载: 全部脚本 ({_allResults.Sum(r => r.Commands.Count)} 条命令)";
        }
        else
        {
            if (string.IsNullOrEmpty(_chosenFile) || !File.Exists(_chosenFile))
            {
                AppendLog($"[错误] 脚本文件不存在: {_chosenFile}", Color.Red);
                return false;
            }

            _allResults.Clear();
            AppendLog($"解析脚本: {Path.GetFileName(_chosenFile)}", Color.White);
            var result = _parser.Parse(_chosenFile);
            var label = result.RepeatCount == -1 ? "无限循环" : $"{result.RepeatCount} 次";
            AppendLog($"  -> {result.Commands.Count} 条命令, 重复: {label}", Color.Gray);
            _allResults.Add(result);

            // 更新循环次数文本框
            _txtRepeat.Text = result.RepeatCount == -1 ? "0" : result.RepeatCount.ToString();

            _lblScriptInfo.Text = $"已加载: {Path.GetFileName(_chosenFile)} ({result.Commands.Count} 条命令)";
        }

        AppendLog("[✓] 脚本加载完成。", Color.Lime);
        AppendLog("");

        UpdateStatus("就绪");
        return true;
    }

    private void ReloadCurrentScripts()
    {
        if (string.IsNullOrEmpty(_chosenFile))
        {
            AppendLog("[提示] 尚未选择脚本，请先选择。", Color.Yellow);
            return;
        }

        AppendLog($"[reload] 重新加载: {(_chosenFile == "__ALL__" ? "全部脚本" : Path.GetFileName(_chosenFile))}", Color.White);
        ParseAndLoad();
    }

    private void ApplyRepeatOverride()
    {
        ApplyRepeatFromTextBox();
    }

    private void ApplyRepeatFromTextBox()
    {
        if (_allResults.Count == 0) return;

        var text = _txtRepeat.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (int.TryParse(text, out int newRepeat) && newRepeat >= 0)
        {
            int actual = newRepeat == 0 ? -1 : newRepeat;
            for (int i = 0; i < _allResults.Count; i++)
            {
                _allResults[i] = _allResults[i] with { RepeatCount = actual };
            }
            string label = newRepeat == 0 ? "无限循环" : $"{newRepeat} 次";
            AppendLog($"[✓] 循环次数已覆盖为: {label}", Color.Lime);
        }
    }

    #endregion

    #region UI 辅助

    private void RefreshScriptFiles()
    {
        if (Directory.Exists(_configPath))
        {
            _scriptFiles = Directory.GetFiles(_configPath, "*.*")
                .Where(f => !f.EndsWith(".md") && !f.EndsWith(".txt~"))
                .OrderBy(f => f)
                .ToList();
        }
        else
        {
            _scriptFiles = new List<string>();
        }
    }

    private void RefreshScriptListUI()
    {
        // 临时取消事件防止刷列表时触发解析
        _lstScripts.SelectedIndexChanged -= AutoLoadOnSelect;
        _lstScripts.Items.Clear();
        foreach (var file in _scriptFiles)
        {
            _lstScripts.Items.Add(Path.GetFileName(file));
        }

        if (_scriptFiles.Count > 0 && _lstScripts.SelectedIndex < 0)
        {
            _lstScripts.SelectedIndex = 0;
        }
        _lstScripts.SelectedIndexChanged += AutoLoadOnSelect;
    }

    private void AutoLoadOnSelect(object? sender, EventArgs e)
    {
        if (_lstScripts.SelectedIndex >= 0 && _lstScripts.SelectedIndex < _scriptFiles.Count)
        {
            _chosenFile = _scriptFiles[_lstScripts.SelectedIndex];
            _chkSelectAll.Checked = false;
            ParseAndLoad();
        }
    }

    private void UpdateStatus(string text)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(text));
            return;
        }

        _lblStatus.Text = text;
    }

    private void AppendLog(string text, Color? color = null)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(text, color));
            return;
        }

        Color c = color ?? Color.FromArgb(220, 220, 220);

        _rtbLog.SelectionStart = _rtbLog.TextLength;
        _rtbLog.SelectionLength = 0;
        _rtbLog.SelectionColor = c;
        _rtbLog.AppendText(text + Environment.NewLine);
        _rtbLog.SelectionColor = _rtbLog.ForeColor;

        // 自动滚动到底部
        _rtbLog.SelectionStart = _rtbLog.TextLength;
        _rtbLog.ScrollToCaret();
    }

    #endregion

    #region 工具方法

    private string FormatCommand(ScriptCommand cmd)
    {
        return cmd.Type switch
        {
            CommandType.MoveTo => $"MoveTo {cmd.X}, {cmd.Y}",
            CommandType.Delay => cmd.Value2 > 0
                ? $"Delay {cmd.Value}, {cmd.Value2}"
                : $"Delay {cmd.Value}",
            CommandType.LeftClick => "LeftClick 1",
            CommandType.RightClick => "RightClick 1",
            CommandType.LeftDown => "LeftDown",
            CommandType.LeftUp => "LeftUp",
            CommandType.RightDown => "RightDown",
            CommandType.RightUp => "RightUp",
            CommandType.KeyDown => $"KeyDown \"{cmd.Text}\"",
            CommandType.KeyUp => $"KeyUp \"{cmd.Text}\"",
            CommandType.Scroll => $"Scroll {cmd.Value}",
            _ => ""
        };
    }

    private string FindScriptsFolder()
    {
        var exeDir = AppContext.BaseDirectory;
        var scriptsPath = Path.Combine(exeDir, "scripts");
        if (Directory.Exists(scriptsPath))
            return scriptsPath;

        var projectDir = Directory.GetCurrentDirectory();
        scriptsPath = Path.Combine(projectDir, "scripts");
        if (Directory.Exists(scriptsPath))
            return scriptsPath;

        for (int i = 0; i < 3; i++)
        {
            projectDir = Directory.GetParent(projectDir)?.FullName!;
            scriptsPath = Path.Combine(projectDir, "scripts");
            if (Directory.Exists(scriptsPath))
                return scriptsPath;
        }

        return scriptsPath;
    }

    #endregion

    #region 窗体和清理

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _splitContainer.SplitterDistance = 250;
        _splitContainer.Panel2MinSize = 300;

        // 窗体加载完成后启动全局热键
        _hotkeyManager.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 立即信号停止，不 Join 等待（后台线程会随进程退出）
        _executor.RequestStop();
        _cts?.Cancel();
        _hotkeyManager.Stop();

        base.OnFormClosing(e);
    }

    #endregion
}
