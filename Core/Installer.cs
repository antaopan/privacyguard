using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace PrivacyGuard
{
    public class InstallResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Warning { get; set; }

        public InstallResult()
        {
            Message = "";
            Warning = "";
        }
    }

    public static class Installer
    {
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static InstallResult Install(bool startService)
        {
            InstallResult result = new InstallResult();
            if (!IsAdministrator())
            {
                result.Message = "需要管理员权限";
                return result;
            }

            string exe = AppPaths.ExePath;
            if (!File.Exists(exe))
            {
                result.Message = "找不到主程序文件";
                return result;
            }

            try
            {
                AppPaths.EnsureDataDir();
                SecureDataDir();
                ConfigStore.CreateDefaultIfMissing();

                StopAndDeleteService();

                string error;
                if (!CreateService(exe, out error))
                {
                    result.Message = "创建后台服务失败：" + error;
                    Logger.Error("installer", "CREATE_SERVICE_FAILED", result.Message);
                    return result;
                }

                ServiceHelper.RunSc("failure " + ProductInfo.ServiceName +
                    " reset= 86400 actions= restart/5000/restart/15000/restart/60000");
                ServiceHelper.RunSc("config " + ProductInfo.ServiceName + " start= auto");

                try
                {
                    RegisterLogonTask(exe);
                }
                catch (Exception ex)
                {
                    result.Warning = "已创建后台服务，但登录自启动任务创建失败：" + ex.Message;
                    Logger.Warn("installer", "TASK_FAILED", result.Warning);
                }

                try
                {
                    CreateShortcut(exe);
                }
                catch (Exception ex)
                {
                    result.Warning = Append(result.Warning, "开始菜单快捷方式创建失败：" + ex.Message);
                }

                try
                {
                    WriteUninstallEntry(exe);
                }
                catch (Exception ex)
                {
                    result.Warning = Append(result.Warning, "卸载登记写入失败：" + ex.Message);
                }

                if (startService)
                {
                    string startError;
                    if (!ServiceHelper.Start(out startError))
                    {
                        result.Message = "服务已安装，但启动失败：" + startError;
                        Logger.Error("installer", "START_SERVICE_FAILED", result.Message);
                        return result;
                    }

                    for (int i = 0; i < 30; i++)
                    {
                        if (StatusStore.IsFresh) break;
                        Thread.Sleep(300);
                    }
                }

                result.Success = true;
                result.Message = "后台服务与登录自启动已就绪";
                Logger.Info("installer", "INSTALLED", exe);
                return result;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                Logger.Error("installer", "INSTALL_EXCEPTION", ex.ToString());
                return result;
            }
        }

        public static InstallResult Uninstall()
        {
            InstallResult result = new InstallResult();
            if (!IsAdministrator())
            {
                result.Message = "需要管理员权限";
                return result;
            }

            try
            {
                string stopError;
                ServiceHelper.Stop(out stopError);
                StopAndDeleteService();
                DeleteLogonTask();
                FirewallManager.RemoveAll();

                string policyError;
                PolicyManager.RestoreAll(out policyError);

                DeleteShortcut();
                DeleteUninstallEntry();

                result.Success = true;
                result.Message = "后台服务、登录自启动、防火墙规则与系统加固已移除。";
                if (!string.IsNullOrEmpty(policyError)) result.Warning = "部分系统设置恢复失败：" + policyError;
                Logger.Info("installer", "UNINSTALLED", result.Message);
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                Logger.Error("installer", "UNINSTALL_EXCEPTION", ex.ToString());
            }
            return result;
        }

        public static InstallResult Repair()
        {
            return Install(true);
        }

        private static string Append(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return a + " " + b;
        }

        private static bool CreateService(string exe, out string error)
        {
            error = "";
            IntPtr scm = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;
            try
            {
                scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
                if (scm == IntPtr.Zero)
                {
                    error = NativeMethods.LastError();
                    return false;
                }

                string binary = "\"" + exe + "\" --service";
                service = NativeMethods.CreateService(
                    scm,
                    ProductInfo.ServiceName,
                    "PrivacyGuard 隐私卫士",
                    NativeMethods.SERVICE_ALL_ACCESS,
                    NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                    NativeMethods.SERVICE_AUTO_START,
                    NativeMethods.SERVICE_ERROR_NORMAL,
                    binary,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null);

                if (service == IntPtr.Zero)
                {
                    error = NativeMethods.LastError();
                    return false;
                }

                NativeMethods.SERVICE_DESCRIPTION desc = new NativeMethods.SERVICE_DESCRIPTION();
                desc.lpDescription = "PrivacyGuard 隐私卫士后台防护服务：应用网络隔离、AI 隐私加固策略与黑名单进程监控。";
                NativeMethods.ChangeServiceConfig2(service, 1, ref desc);
                return true;
            }
            finally
            {
                if (service != IntPtr.Zero) NativeMethods.CloseServiceHandle(service);
                if (scm != IntPtr.Zero) NativeMethods.CloseServiceHandle(scm);
            }
        }

        private static void StopAndDeleteService()
        {
            string message;
            ServiceHelper.Stop(out message);
            Thread.Sleep(300);

            IntPtr scm = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;
            try
            {
                scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
                if (scm == IntPtr.Zero) return;
                service = NativeMethods.OpenService(scm, ProductInfo.ServiceName, NativeMethods.SERVICE_ALL_ACCESS);
                if (service != IntPtr.Zero)
                {
                    NativeMethods.DeleteService(service);
                }
            }
            finally
            {
                if (service != IntPtr.Zero) NativeMethods.CloseServiceHandle(service);
                if (scm != IntPtr.Zero) NativeMethods.CloseServiceHandle(scm);
            }

            for (int i = 0; i < 20; i++)
            {
                if (!ServiceHelper.Exists()) return;
                Thread.Sleep(250);
            }
            ServiceHelper.RunSc("delete " + ProductInfo.ServiceName);
        }

        private static void SecureDataDir()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(Environment.SystemDirectory, "icacls.exe");
                psi.Arguments = "\"" + AppPaths.DataDir + "\" /inheritance:r /grant:r " +
                    "*S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("installer", "ACL_FAILED", ex.Message);
            }
        }

        private static void RegisterLogonTask(string exe)
        {
            string userId = WindowsIdentity.GetCurrent().Name;
            Type schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) throw new InvalidOperationException("任务计划程序不可用");
            dynamic scheduler = Activator.CreateInstance(schedulerType);
            scheduler.Connect();
            dynamic root = scheduler.GetFolder("\\");
            dynamic task = scheduler.NewTask(0);

            task.RegistrationInfo.Description = "PrivacyGuard 隐私卫士登录自启动";
            task.RegistrationInfo.Author = "PrivacyGuard";
            task.Principal.UserId = userId;
            task.Principal.LogonType = 3;
            task.Principal.RunLevel = 1;

            dynamic trigger = task.Triggers.Create(9);
            trigger.UserId = userId;
            trigger.Delay = "PT15S";

            dynamic action = task.Actions.Create(0);
            action.Path = exe;
            action.Arguments = "--minimized";
            action.WorkingDirectory = Path.GetDirectoryName(exe);

            task.Settings.Enabled = true;
            task.Settings.MultipleInstances = 2;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT0S";
            task.Settings.StartWhenAvailable = true;
            task.Settings.RestartCount = 2;
            task.Settings.RestartInterval = "PT1M";
            task.Settings.Hidden = false;

            root.RegisterTaskDefinition(ProductInfo.TaskName, task, 6, userId, null, 3);
        }

        private static void DeleteLogonTask()
        {
            try
            {
                Type schedulerType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedulerType == null) return;
                dynamic scheduler = Activator.CreateInstance(schedulerType);
                scheduler.Connect();
                dynamic root = scheduler.GetFolder("\\");
                root.DeleteTask(ProductInfo.TaskName, 0);
            }
            catch
            {
            }
        }

        private static void CreateShortcut(string exe)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "PrivacyGuard 隐私卫士.lnk");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "PrivacyGuard 隐私卫士";
            shortcut.Save();
        }

        private static void DeleteShortcut()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs");
                string path = Path.Combine(folder, "PrivacyGuard 隐私卫士.lnk");
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static void WriteUninstallEntry(string exe)
        {
            string uninstallScript = Path.Combine(Path.GetDirectoryName(exe), "uninstall.ps1");
            string uninstallCommand = File.Exists(uninstallScript)
                ? "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\""
                : "\"" + exe + "\" --uninstall";

            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = baseKey.CreateSubKey(ProductInfo.UninstallKey))
            {
                if (key == null) return;
                key.SetValue("DisplayName", "PrivacyGuard 隐私卫士", RegistryValueKind.String);
                key.SetValue("DisplayVersion", ProductInfo.Version, RegistryValueKind.String);
                key.SetValue("Publisher", "PrivacyGuard", RegistryValueKind.String);
                key.SetValue("InstallLocation", Path.GetDirectoryName(exe), RegistryValueKind.String);
                key.SetValue("DisplayIcon", exe + ",0", RegistryValueKind.String);
                key.SetValue("UninstallString", uninstallCommand, RegistryValueKind.String);
                key.SetValue("QuietUninstallString", uninstallCommand, RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024) + 2048, RegistryValueKind.DWord);
                }
                catch
                {
                }
            }
        }

        private static void DeleteUninstallEntry()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    baseKey.DeleteSubKeyTree(ProductInfo.UninstallKey, false);
                }
            }
            catch
            {
            }
        }

        public static bool IsInstalledFromProgramFiles()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return AppPaths.InstallDir.StartsWith(AppPaths.CleanPath(programFiles), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
