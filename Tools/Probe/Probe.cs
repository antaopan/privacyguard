using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

internal static class Probe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort GetUserDefaultUILanguage();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort GetSystemDefaultUILanguage();

    private static int Main(string[] args)
    {
        try
        {
            if (Has(args, "--dump"))
            {
                string file = Value(args, "--dump");
                Dump(file, args);
                return 0;
            }
            if (Has(args, "--connect"))
            {
                string host = Value(args, "--connect");
                int port = int.Parse(Value(args, "--port"));
                int timeout = Has(args, "--timeout") ? int.Parse(Value(args, "--timeout")) : 3000;
                return Connect(host, port, timeout);
            }
            if (Has(args, "--listen"))
            {
                int port = int.Parse(Value(args, "--listen"));
                int seconds = Has(args, "--seconds") ? int.Parse(Value(args, "--seconds")) : 30;
                Listen(port, seconds);
                return 0;
            }
            if (Has(args, "--hold"))
            {
                Thread.Sleep(int.Parse(Value(args, "--hold")) * 1000);
                return 0;
            }
            Console.WriteLine("Probe: --dump <file> | --connect <host> --port <n> | --listen <port> --seconds <n> | --hold <n>");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PROBE_ERROR " + ex);
            return 2;
        }
    }

    private static void Dump(string file, string[] args)
    {
        List<string> lines = new List<string>();
        lines.Add("timestamp=" + DateTime.UtcNow.ToString("o"));
        lines.Add("pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);
        lines.Add("user=" + WindowsIdentity.GetCurrent().Name);
        lines.Add("elevated=" + new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
        lines.Add("machine=" + Environment.MachineName);
        lines.Add("env.COMPUTERNAME=" + Environment.GetEnvironmentVariable("COMPUTERNAME"));
        lines.Add("env.TZ=" + Environment.GetEnvironmentVariable("TZ"));
        lines.Add("env.LANG=" + Environment.GetEnvironmentVariable("LANG"));
        lines.Add("env.LC_ALL=" + Environment.GetEnvironmentVariable("LC_ALL"));
        lines.Add("env.LANGUAGE=" + Environment.GetEnvironmentVariable("LANGUAGE"));
        lines.Add("env.PRIVACYGUARD_SHIELD=" + Environment.GetEnvironmentVariable("PRIVACYGUARD_SHIELD"));
        lines.Add("env.OPENAI_API_KEY.present=" + (Environment.GetEnvironmentVariable("OPENAI_API_KEY") != null));
        lines.Add("env.ANTHROPIC_API_KEY.present=" + (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") != null));
        lines.Add("culture=" + CultureInfo.CurrentCulture.Name);
        lines.Add("uiCulture=" + CultureInfo.CurrentUICulture.Name);
        lines.Add("timezone=" + TimeZoneInfo.Local.Id);
        lines.Add("userUiLanguage=" + GetUserDefaultUILanguage());
        lines.Add("systemUiLanguage=" + GetSystemDefaultUILanguage());

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
            lines.Add("ipv4=" + string.Join(",", addresses.Select(a => a.ToString()).ToArray()));
        }
        catch (Exception ex)
        {
            lines.Add("ipv4.error=" + ex.Message);
        }

        lines.Add("args=" + string.Join(" ", args));
        File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
    }

    private static int Connect(string host, int port, int timeout)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeout))
                {
                    Console.WriteLine("CONNECT_TIMEOUT " + host + ":" + port);
                    return 3;
                }
                client.EndConnect(result);
                Console.WriteLine("CONNECT_OK " + host + ":" + port);
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("CONNECT_FAIL " + host + ":" + port + " " + ex.Message);
            return 4;
        }
    }

    private static void Listen(int port, int seconds)
    {
        TcpListener listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine("LISTENING " + port);
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            try
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }
                using (TcpClient client = listener.AcceptTcpClient())
                {
                    Console.WriteLine("ACCEPT " + client.Client.RemoteEndPoint);
                    NetworkStream stream = client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes("OK");
                    stream.Write(data, 0, data.Length);
                }
            }
            catch
            {
            }
        }
        listener.Stop();
    }

    private static bool Has(string[] args, string name)
    {
        return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        throw new ArgumentException("Missing value for " + name);
    }
}
