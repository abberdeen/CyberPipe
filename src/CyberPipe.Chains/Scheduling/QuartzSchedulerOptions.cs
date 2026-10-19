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

    /// <summary>Строка подключения к PostgreSQL.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Схема базы данных для таблиц Quartz (по умолчанию quartz).</summary>
    public string Schema { get; set; } = "quartz";

    /// <summary>Префикс таблиц Quartz (по умолчанию quartz.qrtz_).</summary>
    public string TablePrefix { get; set; } = "quartz.qrtz_";

    /// <summary>Признак использования PostgreSQL (JobStoreTX). Если false, используется RAMJobStore.</summary>
    public bool UsePostgres { get; set; }

    /// <summary>Порог опоздания в минутах для политики SkipIfLate и AskUser по умолчанию.</summary>
    public int DefaultMisfireLateMinutes { get; set; } = 5;
}
