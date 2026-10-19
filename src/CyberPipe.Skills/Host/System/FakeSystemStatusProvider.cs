using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.System;

/// <summary>
/// Фейковый провайдер системного статуса для тестов (TASK-050).
/// Позволяет задавать статус или провоцировать контролируемые предупреждения.
/// </summary>
public sealed class FakeSystemStatusProvider : ISystemStatusProvider
{
    /// <summary>Настраиваемый снимок статуса для возврата.</summary>
    public SystemStatusDto StatusToReturn { get; set; } = new()
    {
        CpuUsagePercent = 25.5,
        TotalMemoryBytes = 16L * 1024 * 1024 * 1024,
        AvailableMemoryBytes = 8L * 1024 * 1024 * 1024,
        MemoryUsagePercent = 50.0,
        Disks =
        [
            new DiskSpaceInfoDto
            {
                DriveName = "C:\\",
                VolumeLabel = "System",
                TotalBytes = 500L * 1024 * 1024 * 1024,
                FreeBytes = 250L * 1024 * 1024 * 1024,
                FreePercent = 50.0,
            },
        ],
        BatteryPercent = 85,
        IsBatteryCharging = true,
        SystemUptime = TimeSpan.FromHours(10),
        OsDescription = "Microsoft Windows 11 Pro Test",
        MachineName = "TEST-HOST",
        Warnings = [],
    };

    /// <inheritdoc />
    public Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StatusToReturn);
    }
}
