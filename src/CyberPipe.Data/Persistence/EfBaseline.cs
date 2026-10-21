using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CyberPipe.Data.Persistence;

/// <summary>
/// Baseline старых баз для EF-миграций: базы, созданные SQL-каталогом 0001–0007
/// (таблица schema_migrations), помечаются применённой миграцией InitialCreate,
/// чтобы <c>MigrateAsync</c> не пытался пересоздать существующие таблицы.
/// Свежие базы baseline не затрагивает: их полностью создаёт InitialCreate.
/// Сырой SQL ограничен этим хелпером bootstrap-слоя; репозитории — только LINQ.
/// </summary>
internal static class EfBaseline
{
    /// <summary>Идентификатор базовой EF-миграции, эквивалентной SQL-каталогу 0001–0007.</summary>
    internal const string InitialCreateMigrationId = "20261021103630_InitialCreate";

    /// <summary>Версия SQL-каталога, эквивалентная InitialCreate.</summary>
    internal const int LegacyBaselineVersion = 7;

    /// <summary>
    /// Помечает legacy-базу как смигрированную, если её SQL-схема актуальна.
    /// Идемпотентно: повторный вызов ничего не меняет.
    /// </summary>
    /// <param name="db">Контекст базы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task EnsureBaselineAsync(CyberPipeDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if ((await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .Contains(InitialCreateMigrationId, StringComparer.Ordinal))
        {
            return;
        }

        if (!await IsLegacySchemaCurrentAsync(db, cancellationToken).ConfigureAwait(false))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT {0}, {1}
            WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = {0});
            """,
            [InitialCreateMigrationId, "10.0.13"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsLegacySchemaCurrentAsync(CyberPipeDbContext db, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        bool wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using System.Data.Common.DbCommand tableCheck = connection.CreateCommand();
            tableCheck.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('schema_migrations', 'chains');";
            object? tables = await tableCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (Convert.ToInt32(tables, CultureInfo.InvariantCulture) < 2)
                return false;

            await using System.Data.Common.DbCommand versionCheck = connection.CreateCommand();
            versionCheck.CommandText = "SELECT max(version) FROM schema_migrations;";
            object? version = await versionCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(version, CultureInfo.InvariantCulture) >= LegacyBaselineVersion;
        }
        catch (SqliteException)
        {
            return false;
        }
        finally
        {
            if (wasClosed)
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
