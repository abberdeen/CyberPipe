namespace CyberPipe.Chains.Scheduling;

/// <summary>
/// Настройки планировщика Quartz.NET (TASK-045).
/// </summary>
public sealed class QuartzSchedulerOptions
{
    /// <summary>Имя экземпляра планировщика.</summary>
    public string InstanceName { get; set; } = "CyberPipeScheduler";

    /// <summary>Идентификатор экземпляра (по умолчанию AUTO).</summary>
    public string InstanceId { get; set; } = "AUTO";

    /// <summary>Строка подключения к SQLite (файл базы).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Префикс таблиц Quartz (по умолчанию qrtz_). Схемы в SQLite нет.</summary>
    public string TablePrefix { get; set; } = "qrtz_";

    /// <summary>Признак использования SQLite (JobStoreTX). Если false, используется RAMJobStore.</summary>
    public bool UseSqlite { get; set; }

    /// <summary>Порог опоздания в минутах для политики SkipIfLate и AskUser по умолчанию.</summary>
    public int DefaultMisfireLateMinutes { get; set; } = 5;
}
