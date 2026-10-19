using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace CyberPipe.ServiceControl;

/// <summary>
/// Реализация <see cref="IScmManager"/> для операционной системы Windows на базе <see cref="System.ServiceProcess.ServiceController"/>.
/// </summary>
public sealed class WindowsScmManager : IScmManager
{
    public bool IsServiceInstalled(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return CheckInstalledWindows(serviceName);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckInstalledWindows(string serviceName)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(serviceName);
            _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
        {
            return false;
        }
    }

    public string GetServiceStatus(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Stopped";
        }

        return GetStatusWindows(serviceName);
    }

    [SupportedOSPlatform("windows")]
    private static string GetStatusWindows(string serviceName)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(serviceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => "Running",
                ServiceControllerStatus.Stopped => "Stopped",
                ServiceControllerStatus.StartPending => "Starting",
                ServiceControllerStatus.StopPending => "Stopping",
                ServiceControllerStatus.Paused => "Stopped",
                _ => sc.Status.ToString()
            };
        }
        catch (Exception)
        {
            return "Stopped";
        }
    }

    public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Service Control Manager is only supported on Windows.");
        }

        return Task.Run(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                StartWindows(serviceName);
            }
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static void StartWindows(string serviceName)
    {
        using var sc = new System.ServiceProcess.ServiceController(serviceName);
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Service Control Manager is only supported on Windows.");
        }

        return Task.Run(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                StopWindows(serviceName);
            }
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static void StopWindows(string serviceName)
    {
        using var sc = new System.ServiceProcess.ServiceController(serviceName);
        if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    }
}
