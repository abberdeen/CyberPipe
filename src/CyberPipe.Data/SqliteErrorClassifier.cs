using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>
/// Классификация ошибок SQLite в коды причин bootstrap.
/// В результат и логи переносятся только коды, не тексты ошибок и не пути.
/// </summary>
public static class SqliteErrorClassifier
{
    /// <summary>Классифицирует исключение открытия или команды.</summary>
    /// <param name="exception">Исключение драйвера или ввода-вывода.</param>
    /// <returns>Код причины bootstrap.</returns>
    public static BootstrapReasonCode Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SqliteException sqlite:
                    return sqlite.SqliteErrorCode switch
                    {
                        26 or 11 => BootstrapReasonCode.DatabaseCorrupt, // SQLITE_NOTADB, SQLITE_CORRUPT
                        3 or 23 or 8 => BootstrapReasonCode.InsufficientPrivilege, // SQLITE_PERM, SQLITE_AUTH, SQLITE_READONLY
                        5 or 6 => BootstrapReasonCode.Timeout, // SQLITE_BUSY, SQLITE_LOCKED
                        14 or 10 => BootstrapReasonCode.ServerUnavailable, // SQLITE_CANTOPEN, SQLITE_IOERR
                        _ => BootstrapReasonCode.ServerUnavailable,
                    };
                case TimeoutException:
                    return BootstrapReasonCode.Timeout;
                case UnauthorizedAccessException:
                    return BootstrapReasonCode.InsufficientPrivilege;
                case IOException:
                    return BootstrapReasonCode.ServerUnavailable;
            }
        }

        return BootstrapReasonCode.Unknown;
    }
}
