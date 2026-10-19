namespace CyberPipe.Cli;

/// <summary>
/// Стандартизированные коды выхода CLI CyberPipe (TASK-062).
/// Маппинг между доменными результатами/ошибками и кодами выхода процесса.
/// </summary>
public static class CliExitCodes
{
    /// <summary>Команда выполнена успешно.</summary>
    public const int Success = 0;

    /// <summary>Общая ошибка выполнения команды.</summary>
    public const int GeneralError = 1;

    /// <summary>Служба CyberPipe недоступна (канал связи отключён).</summary>
    public const int ServiceUnavailable = 2;

    /// <summary>Запрошенный ресурс (цепочка, запуск, HITL-запрос) не найден.</summary>
    public const int NotFound = 3;

    /// <summary>Некорректные аргументы командной строки.</summary>
    public const int InvalidArgument = 4;

    /// <summary>Запрос подтверждения уже разрешён (AlreadyResolved).</summary>
    public const int AlreadyResolved = 5;

    /// <summary>Отказ в доступе (только Owner имеет доступ).</summary>
    public const int AccessDenied = 6;

    /// <summary>Запрос отклонён токен-бюджетом.</summary>
    public const int BudgetDenied = 7;
}
