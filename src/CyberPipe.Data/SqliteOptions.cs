using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Параметры единственной базы SQLite (WAL). Пароля и сервера нет:
/// файл задаётся <see cref="Path" />, конкурентная запись сериализуется
/// блокировками SQLite и <see cref="BusyTimeoutMs" />.
/// </summary>
public sealed record SqliteOptions
{
    /// <summary>Путь к файлу базы SQLite.</summary>
    public string Path { get; init; } = "data/cyberpipe.db";

    /// <summary>Тайм-аут ожидания снятия блокировки записи в миллисекундах.</summary>
    public int BusyTimeoutMs { get; init; } = 5000;

    /// <summary>Тайм-аут команды в секундах.</summary>
    public int CommandTimeout { get; init; } = 30;

    /// <summary>Создаёт опции из секции Database корневой конфигурации.</summary>
    /// <param name="database">Секция Database из CyberPipeOptions.</param>
    /// <returns>Опции подключения SQLite.</returns>
    public static SqliteOptions From(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new SqliteOptions
        {
            Path = database.Path,
            BusyTimeoutMs = database.BusyTimeoutMs,
            CommandTimeout = database.CommandTimeout,
        };
    }

    /// <summary>Полный путь к файлу базы; относительный резолвится от рабочего каталога.</summary>
    /// <returns>Абсолютный путь к файлу базы.</returns>
    public string FullPath() => System.IO.Path.GetFullPath(Path);

    /// <summary>Возвращает эквивалентные опции контракта.</summary>
    /// <returns>Опции секции Database.</returns>
    public DatabaseOptions ToDatabaseOptions() => new()
    {
        Path = Path,
        BusyTimeoutMs = BusyTimeoutMs,
        CommandTimeout = CommandTimeout,
    };

    /// <summary>Проверяет опции; ошибки содержат имена полей.</summary>
    /// <returns>Список ошибок; пуст при валидных опциях.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Path))
            errors.Add("Sqlite:Path must not be empty.");
        if (BusyTimeoutMs <= 0)
            errors.Add("Sqlite:BusyTimeoutMs must be positive.");
        if (CommandTimeout <= 0)
            errors.Add("Sqlite:CommandTimeout must be positive.");
        return errors;
    }
}
