namespace CyberPipe.Contracts;

/// <summary>
/// Статус результата выполнения навыка (TASK-050).
/// </summary>
public enum SkillResultStatus
{
    /// <summary>Успешное выполнение.</summary>
    Ok = 0,

    /// <summary>Операция не поддерживается на текущей платформе.</summary>
    NotSupported = 1,

    /// <summary>Ошибка валидации аргументов.</summary>
    ValidationError = 2,

    /// <summary>Требуется подтверждение владельца (HITL).</summary>
    RequiresApproval = 3,

    /// <summary>Внутренняя ошибка выполнения.</summary>
    Failed = 4,
}

/// <summary>
/// Типизированный результат выполнения хост-навыка (TASK-050).
/// Гарантирует отсутствие непойманных исключений в вызывающий код.
/// </summary>
public sealed record HostSkillResult
{
    /// <summary>Статус выполнения.</summary>
    public required SkillResultStatus Status { get; init; }

    /// <summary>Признак успешного завершения.</summary>
    public bool IsSuccess => Status == SkillResultStatus.Ok;

    /// <summary>Человекочитаемое сообщение или статус.</summary>
    public string? Message { get; init; }

    /// <summary>Выходная полезная нагрузка в JSON.</summary>
    public string OutputJson { get; init; } = "{}";

    /// <summary>Выходные файлы навыка.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    /// <summary>Признак того, что действие требует подтверждения владельца.</summary>
    public bool RequiresApproval => Status == SkillResultStatus.RequiresApproval;

    /// <summary>Код ошибки при неудаче.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Создаёт успешный результат.</summary>
    public static HostSkillResult Ok(string outputJson = "{}", string? message = null, IReadOnlyList<string>? outputFiles = null) =>
        new()
        {
            Status = SkillResultStatus.Ok,
            Message = message,
            OutputJson = outputJson,
            OutputFiles = outputFiles ?? [],
        };

    /// <summary>Создаёт результат о неподдерживаемой операции.</summary>
    public static HostSkillResult NotSupported(string message) =>
        new()
        {
            Status = SkillResultStatus.NotSupported,
            Message = message,
            ErrorCode = "Host.NotSupported",
        };

    /// <summary>Создаёт результат ошибки валидации аргументов.</summary>
    public static HostSkillResult ValidationError(string message) =>
        new()
        {
            Status = SkillResultStatus.ValidationError,
            Message = message,
            ErrorCode = "Host.ValidationError",
        };

    /// <summary>Создаёт результат, требующий подтверждения владельца.</summary>
    public static HostSkillResult NeedsApproval(string reason, string outputJson = "{}") =>
        new()
        {
            Status = SkillResultStatus.RequiresApproval,
            Message = reason,
            OutputJson = outputJson,
            ErrorCode = "Host.RequiresApproval",
        };

    /// <summary>Создаёт результат внутренней ошибки.</summary>
    public static HostSkillResult Failed(string message, string? code = null) =>
        new()
        {
            Status = SkillResultStatus.Failed,
            Message = message,
            ErrorCode = code ?? "Host.Failed",
        };
}

/// <summary>
/// Интерфейс исполняемого хост-навыка (TASK-050).
/// </summary>
public interface IHostSkill : ISkill
{
}

/// <summary>
/// Абстракция платформенного контроллера медиа и громкости (TASK-050).
/// </summary>
public interface IHostMediaController
{
    /// <summary>Запускает воспроизведение.</summary>
    Task<HostSkillResult> PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Ставит воспроизведение на паузу.</summary>
    Task<HostSkillResult> PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Переключает режим воспроизведения/паузы.</summary>
    Task<HostSkillResult> PlayPauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Переходит к следующему треку.</summary>
    Task<HostSkillResult> NextTrackAsync(CancellationToken cancellationToken = default);

    /// <summary>Переходит к предыдущему треку.</summary>
    Task<HostSkillResult> PreviousTrackAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает текущую громкость в диапазоне 0–100.</summary>
    Task<int> GetVolumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Устанавливает громкость в диапазоне 0–100.</summary>
    Task<HostSkillResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);

    /// <summary>Увеличивает громкость на указанный процент.</summary>
    Task<HostSkillResult> VolumeUpAsync(int step = 5, CancellationToken cancellationToken = default);

    /// <summary>Уменьшает громкость на указанный процент.</summary>
    Task<HostSkillResult> VolumeDownAsync(int step = 5, CancellationToken cancellationToken = default);

    /// <summary>Включает беззвучный режим.</summary>
    Task<HostSkillResult> MuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Выключает беззвучный режим.</summary>
    Task<HostSkillResult> UnmuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Переключает беззвучный режим.</summary>
    Task<HostSkillResult> ToggleMuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает признак включённого беззвучного режима.</summary>
    Task<bool> IsMutedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Информация о свободном и общем месте на диске (TASK-050).
/// </summary>
public sealed record DiskSpaceInfoDto
{
    /// <summary>Имя диска (например, "C:\").</summary>
    public required string DriveName { get; init; }

    /// <summary>Метка тома.</summary>
    public string? VolumeLabel { get; init; }

    /// <summary>Общий объём диска в байтах.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Свободное место в байтах.</summary>
    public long FreeBytes { get; init; }

    /// <summary>Процент свободного места от 0.0 до 100.0.</summary>
    public double FreePercent { get; init; }
}

/// <summary>
/// Снимок системного статуса хоста (TASK-050).
/// Недоступные счётчики заполняются null с добавлением предупреждения в Warnings.
/// </summary>
public sealed record SystemStatusDto
{
    /// <summary>Загрузка процессора в процентах (0.0–100.0) или null.</summary>
    public double? CpuUsagePercent { get; init; }

    /// <summary>Общий объём оперативной памяти в байтах или null.</summary>
    public long? TotalMemoryBytes { get; init; }

    /// <summary>Доступный объём оперативной памяти в байтах или null.</summary>
    public long? AvailableMemoryBytes { get; init; }

    /// <summary>Процент использования памяти или null.</summary>
    public double? MemoryUsagePercent { get; init; }

    /// <summary>Список дисков хоста.</summary>
    public IReadOnlyList<DiskSpaceInfoDto> Disks { get; init; } = [];

    /// <summary>Уровень заряда батареи (0–100) или null (если нет батареи).</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Признак подключения к сети зарядки или null.</summary>
    public bool? IsBatteryCharging { get; init; }

    /// <summary>Время непрерывной работы системы (uptime) или null.</summary>
    public TimeSpan? SystemUptime { get; init; }

    /// <summary>Описание операционной системы.</summary>
    public required string OsDescription { get; init; }

    /// <summary>Имя хоста.</summary>
    public required string MachineName { get; init; }

    /// <summary>Предупреждения при недоступности отдельных счётчиков.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Провайдер системного статуса хоста (TASK-050).
/// </summary>
public interface ISystemStatusProvider
{
    /// <summary>Возвращает текущий системный статус хоста без исключений при частичной недоступности.</summary>
    Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Запись таймера (TASK-050).
/// </summary>
public sealed record TimerRecord
{
    /// <summary>Уникальный идентификатор таймера.</summary>
    public required Guid TimerId { get; init; }

    /// <summary>Название таймера.</summary>
    public required string Title { get; init; }

    /// <summary>Продолжительность таймера.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Время создания (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Планируемое время срабатывания (UTC).</summary>
    public required DateTimeOffset FiresAtUtc { get; init; }

    /// <summary>Полезная нагрузка в JSON при срабатывании.</summary>
    public string PayloadJson { get; init; } = "{}";

    /// <summary>Признак активности таймера.</summary>
    public bool IsActive { get; init; }

    /// <summary>Признак отмены таймера.</summary>
    public bool IsCancelled { get; init; }

    /// <summary>Признак состоявшегося срабатывания.</summary>
    public bool IsFired { get; init; }
}

/// <summary>
/// Сервис управления таймерами в памяти (TASK-050).
/// </summary>
public interface ITimerService
{
    /// <summary>Устанавливает новый таймер.</summary>
    Task<TimerRecord> SetTimerAsync(TimeSpan duration, string title, string payloadJson = "{}", CancellationToken cancellationToken = default);

    /// <summary>Отменяет активный таймер.</summary>
    Task<bool> CancelTimerAsync(Guid timerId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список всех активных таймеров.</summary>
    Task<IReadOnlyList<TimerRecord>> ListActiveTimersAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает информацию о таймере по идентификатору.</summary>
    Task<TimerRecord?> GetTimerAsync(Guid timerId, CancellationToken cancellationToken = default);

    /// <summary>Событие срабатывания таймера.</summary>
    event Action<TimerRecord>? TimerFired;
}

/// <summary>
/// Запись будильника (TASK-050).
/// </summary>
public sealed record AlarmRecord
{
    /// <summary>Уникальный идентификатор будильника.</summary>
    public required Guid AlarmId { get; init; }

    /// <summary>Название будильника.</summary>
    public required string Title { get; init; }

    /// <summary>Время суток срабатывания.</summary>
    public required TimeOnly TimeOfDay { get; init; }

    /// <summary>Дни недели (пустой список — однократный на ближайшее время).</summary>
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; } = [];

    /// <summary>Признак активности будильника.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Время следующего срабатывания (UTC).</summary>
    public DateTimeOffset? NextFireTimeUtc { get; init; }
}

/// <summary>
/// Сервис управления будильниками (TASK-050).
/// </summary>
public interface IAlarmService
{
    /// <summary>Устанавливает будильник.</summary>
    Task<AlarmRecord> SetAlarmAsync(TimeOnly timeOfDay, string title, IReadOnlyList<DayOfWeek>? daysOfWeek = null, CancellationToken cancellationToken = default);

    /// <summary>Отменяет будильник.</summary>
    Task<bool> CancelAlarmAsync(Guid alarmId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список всех будильников.</summary>
    Task<IReadOnlyList<AlarmRecord>> ListAlarmsAsync(CancellationToken cancellationToken = default);

    /// <summary>Событие срабатывания будильника.</summary>
    event Action<AlarmRecord>? AlarmFired;
}

/// <summary>
/// Реестр хост-навыков (TASK-050).
/// Также реализует ISkillInvoker для прозрачной интеграции с исполнителями цепочек.
/// </summary>
public interface IHostSkillRegistry : ISkillInvoker
{
    /// <summary>Регистрирует новый хост-навык.</summary>
    void RegisterSkill(IHostSkill skill);

    /// <summary>Ищет зарегистрированный навык по идентификатору.</summary>
    IHostSkill? FindSkill(string skillId);

    /// <summary>Возвращает все зарегистрированные навыки.</summary>
    IReadOnlyList<IHostSkill> GetAllSkills();
}
