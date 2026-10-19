using System.Net.Sockets;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>
/// Классификация ошибок подключения PostgreSQL в коды причин bootstrap. Работает только по типу
/// исключения и SQLSTATE: текст ошибки драйвера (в нём может быть адрес и имя пользователя)
/// в результат и логи не переносится.
/// </summary>
public static class PostgresErrorClassifier
{
    /// <summary>Классифицирует исключение подключения или команды.</summary>
    /// <param name="exception">Исключение драйвера или сети.</param>
    /// <returns>Код причины bootstrap.</returns>
    public static BootstrapReasonCode Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException postgres:
                    return postgres.SqlState switch
                    {
                        PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification
                            => BootstrapReasonCode.AuthenticationFailed,
                        PostgresErrorCodes.InsufficientPrivilege => BootstrapReasonCode.InsufficientPrivilege,
                        // Расширение недоступно на сервере: undefined_file / feature_not_supported при CREATE EXTENSION.
                        PostgresErrorCodes.UndefinedFile or PostgresErrorCodes.FeatureNotSupported
                            => BootstrapReasonCode.PgVectorMissing,
                        _ => BootstrapReasonCode.ServerUnavailable,
                    };
                case TimeoutException:
                    return BootstrapReasonCode.Timeout;
                case SocketException:
                case IOException:
                    return BootstrapReasonCode.ServerUnavailable;
                case NpgsqlException:
                    return BootstrapReasonCode.ServerUnavailable;
            }
        }

        return BootstrapReasonCode.Unknown;
    }
}
