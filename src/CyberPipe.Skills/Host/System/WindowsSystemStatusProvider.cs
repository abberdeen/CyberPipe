using System.Runtime.InteropServices;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.System;

/// <summary>
/// Провайдер системного статуса хоста для Windows (TASK-050).
/// Считывает загрузку CPU, оперативную память, диски, заряд батареи и аптайм.
/// При сбое отдельного счётчика поле устанавливается в null с добавлением пояснения в Warnings.
/// </summary>
public sealed class WindowsSystemStatusProvider : ISystemStatusProvider
{
    private static ulong _lastIdleTime;
    private static ulong _lastKernelTime;
    private static ulong _lastUserTime;
    private static readonly object SyncLock = new();

    /// <inheritdoc />
    public async Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        double? cpuUsage = null;
        long? totalMemory = null;
        long? availableMemory = null;
        double? memoryUsagePercent = null;
        int? batteryPercent = null;
        bool? isBatteryCharging = null;
        TimeSpan? uptime = null;

        if (OperatingSystem.IsWindows())
        {
            // 1. CPU
            try
            {
                cpuUsage = await MeasureCpuUsageAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                warnings.Add($"CPU usage counter unavailable: {ex.Message}");
            }

            // 2. RAM
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    totalMemory = (long)memStatus.ullTotalPhys;
                    availableMemory = (long)memStatus.ullAvailPhys;
                    if (memStatus.ullTotalPhys > 0)
                    {
                        memoryUsagePercent = Math.Round((1.0 - ((double)memStatus.ullAvailPhys / memStatus.ullTotalPhys)) * 100.0, 2);
                    }
                }
                else
                {
                    warnings.Add("GlobalMemoryStatusEx returned false.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Memory counter unavailable: {ex.Message}");
            }

            // 3. Battery
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS powerStatus))
                {
                    if (powerStatus.BatteryLifePercent != 255)
                    {
                        batteryPercent = powerStatus.BatteryLifePercent;
                    }

                    if (powerStatus.ACLineStatus == 1)
                    {
                        isBatteryCharging = true;
                    }
                    else if (powerStatus.ACLineStatus == 0)
                    {
                        isBatteryCharging = false;
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Battery status unavailable: {ex.Message}");
            }
        }
        else
        {
            warnings.Add("Operating system is not Windows: native memory, battery, and CPU counters are unavailable.");
        }

        // 4. Uptime
        try
        {
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch (Exception ex)
        {
            warnings.Add($"Uptime unavailable: {ex.Message}");
        }

        // 5. Disks
        var disks = new List<DiskSpaceInfoDto>();
        try
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (DriveInfo drive in drives)
            {
                try
                {
                    if (drive.IsReady)
                    {
                        long totalBytes = drive.TotalSize;
                        long freeBytes = drive.TotalFreeSpace;
                        double freePercent = totalBytes > 0
                            ? Math.Round((double)freeBytes / totalBytes * 100.0, 2)
                            : 0.0;

                        disks.Add(new DiskSpaceInfoDto
                        {
                            DriveName = drive.Name,
                            VolumeLabel = drive.VolumeLabel,
                            TotalBytes = totalBytes,
                            FreeBytes = freeBytes,
                            FreePercent = freePercent,
                        });
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not read drive {drive.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Drive enumeration failed: {ex.Message}");
        }

        return new SystemStatusDto
        {
            CpuUsagePercent = cpuUsage,
            TotalMemoryBytes = totalMemory,
            AvailableMemoryBytes = availableMemory,
            MemoryUsagePercent = memoryUsagePercent,
            Disks = disks,
            BatteryPercent = batteryPercent,
            IsBatteryCharging = isBatteryCharging,
            SystemUptime = uptime,
            OsDescription = global::System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            MachineName = Environment.MachineName,
            Warnings = warnings,
        };
    }

    private static async Task<double?> MeasureCpuUsageAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        lock (SyncLock)
        {
            if (_lastIdleTime == 0 && GetSystemTimes(out var idle, out var kernel, out var user))
            {
                _lastIdleTime = ToUlong(idle);
                _lastKernelTime = ToUlong(kernel);
                _lastUserTime = ToUlong(user);
            }
        }

        await Task.Delay(60, cancellationToken);

        if (!GetSystemTimes(out var currentIdle, out var currentKernel, out var currentUser))
        {
            return null;
        }

        ulong idleNow = ToUlong(currentIdle);
        ulong kernelNow = ToUlong(currentKernel);
        ulong userNow = ToUlong(currentUser);

        ulong idleDiff;
        ulong kernelDiff;
        ulong userDiff;

        lock (SyncLock)
        {
            idleDiff = idleNow - _lastIdleTime;
            kernelDiff = kernelNow - _lastKernelTime;
            userDiff = userNow - _lastUserTime;

            _lastIdleTime = idleNow;
            _lastKernelTime = kernelNow;
            _lastUserTime = userNow;
        }

        ulong total = kernelDiff + userDiff;
        if (total == 0)
        {
            return 0.0;
        }

        // On Windows, kernel time includes idle time
        ulong busy = total > idleDiff ? total - idleDiff : 0;
        double percent = Math.Round((double)busy / total * 100.0, 2);
        return Math.Clamp(percent, 0.0, 100.0);
    }

    private static ulong ToUlong(global::System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out global::System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out global::System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out global::System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
}
