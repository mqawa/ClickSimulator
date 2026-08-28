using System.Diagnostics;
using System.Security.Principal;

namespace ClickSimulator;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 如果不是管理员权限，自动请求提权
        if (!IsRunningAsAdmin())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas" // 触发 UAC 提权对话框
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 用户拒绝了 UAC 提权
                MessageBox.Show(
                    "需要管理员权限才能确保模拟输入在目标窗口中正常生效。\n\n请重新启动程序并允许管理员权限。",
                    "ClickSimulator - 需要管理员权限",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return; // 退出当前非管理员进程
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
