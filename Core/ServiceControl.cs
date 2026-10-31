using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace PrivacyGuard
{
    public static class ServiceHelper
    {
        public static bool Exists()
        {
            try
            {
                return ServiceController.GetServices().Any(s =>
                    string.Equals(s.ServiceName, ProductInfo.ServiceName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public static string GetStateText()
        {
            try
            {
                using (ServiceController controller = new ServiceController(ProductInfo.ServiceName))
                {
                    switch (controller.Status)
                    {
                        case ServiceControllerStatus.Running: return "运行中";
                        case ServiceControllerStatus.Stopped: return "已停止";
                        case ServiceControllerStatus.StartPending: return "正在启动";
                        case ServiceControllerStatus.StopPending: return "正在停止";
                        case ServiceControllerStatus.Paused: return "已暂停";
                        default: return controller.Status.ToString();
                    }
                }
            }
            catch
            {
                return "未安装";
            }
        }

        public static bool IsRunning()
        {
            try
            {
                using (ServiceController controller = new ServiceController(ProductInfo.ServiceName))
                {
                    return controller.Status == ServiceControllerStatus.Running;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Start(out string message)
        {
            message = "";
            try
            {
                using (ServiceController controller = new ServiceController(ProductInfo.ServiceName))
                {
                    if (controller.Status == ServiceControllerStatus.Running) return true;
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool Stop(out string message)
        {
            message = "";
            try
            {
                using (ServiceController controller = new ServiceController(ProductInfo.ServiceName))
                {
                    if (controller.Status == ServiceControllerStatus.Stopped) return true;
                    if (!controller.CanStop)
                    {
                        message = "服务不支持停止";
                        return false;
                    }
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static void RunSc(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = System.IO.Path.Combine(Environment.SystemDirectory, "sc.exe");
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    if (process != null) process.WaitForExit(10000);
                }
            }
            catch
            {
            }
        }
    }
}
