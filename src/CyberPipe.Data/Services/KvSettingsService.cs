using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Настройки ключ-значение (таблица app.kv_settings); SetAsync — upsert по ключу.
/// Все чтения — LINQ с AsNoTracking, сырых SQL-строк нет.
/// </summary>
public sealed class KvSettingsService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["key"] = nameof(SettingEntity.Key),
        ["created_at"] = nameof(SettingEntity.CreatedAt),
        ["updated_at"] = nameof(SettingEntity.UpdatedAt),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт сервис настроек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public KvSettingsService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<KvSettingsService>? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _metrics = metrics ?? new UnitOfWorkMetrics();
    }

    /// <summary>Возвращает настройку по ключу; отсутствие — null.</summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Настройка или null.</returns>
    public Task<KeyValueSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync<KeyValueSettingRecord?>("settings.get", async (db, token) =>
        {
            SettingEntity? entity = await db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница настроек; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница настроек.</returns>
    public Task<PagedResult<KeyValueSettingRecord>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            IQueryable<SettingEntity> source = db.Settings.AsNoTracking();
            IQueryable<SettingEntity> ordered = EfServiceHelper.ApplyOrdering(source, sort, query.Descending, nameof(SettingEntity.Key));
            long total = await source.LongCountAsync(token).ConfigureAwait(false);
            List<SettingEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<KeyValueSettingRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>
    /// Добавляет настройку. Идемпотентность: существующий ключ возвращает существующую запись
    /// без перезаписи; для перезаписи — UpdateAsync/SetAsync.
    /// </summary>
    /// <param name="setting">Настройка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая или существующая настройка.</returns>
    public Task<KeyValueSettingRecord> AddAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.add", async (db, token) =>
        {
            SettingEntity? existing = await db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new SettingEntity
            {
                Key = setting.Key,
                ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)),
                Description = setting.Description,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Settings.Add(entity);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                SettingEntity? raced = await db.Settings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <summary>Создаёт или перезаписывает настройку одним запросом (upsert по ключу).</summary>
    /// <param name="setting">Настройка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая настройка.</returns>
    public Task<KeyValueSettingRecord> SetAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.set", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SettingEntity? existing = await db.Settings
                .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
            if (existing is null)
            {
                var created = new SettingEntity
                {
                    Key = setting.Key,
                    ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)),
                    Description = setting.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Settings.Add(created);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return Map(created);
            }

            existing.ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson));
            existing.Description = setting.Description;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(existing);
        }, cancellationToken);
    }

    /// <summary>Обновляет настройку при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="setting">Новое состояние настройки.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённая настройка с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    public Task<KeyValueSettingRecord> UpdateAsync(KeyValueSettingRecord setting, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int affected = await db.Settings
                .Where(e => e.Key == setting.Key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ValueJson, RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)))
                    .SetProperty(e => e.Description, setting.Description)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("kv_settings", setting.Key);
            return setting with { RowVersion = now };
        }, cancellationToken);
    }

    /// <summary>Удаляет настройку; отсутствие — false.</summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.delete", async (db, token) =>
        {
            int affected = await db.Settings
                .Where(e => e.Key == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static KeyValueSettingRecord Map(SettingEntity entity) => new()
    {
        Key = entity.Key,
        ValueJson = entity.ValueJson,
        Description = entity.Description,
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}
