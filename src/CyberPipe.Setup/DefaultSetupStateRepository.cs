using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Setup;

/// <summary>
/// Репозиторий состояния мастера первого запуска (TASK-063).
/// Хранит состояние в памяти с возможностью подключения постоянного хранилища (SQLite kv_settings).
/// Секреты (пароли, токены) в состояние никогда не записываются.
/// </summary>
public sealed class DefaultSetupStateRepository : ISetupStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private const string SettingKey = "setup.wizard.state";

    private readonly Func<string, CancellationToken, Task<string?>>? _readSetting;
    private readonly Func<string, string, CancellationToken, Task>? _writeSetting;
    private readonly Func<string, CancellationToken, Task>? _deleteSetting;

    private SetupWizardStateDto? _inMemoryState;
    private readonly object _lock = new();

    /// <summary>
    /// Создаёт репозиторий с хранением состояния в памяти.
    /// </summary>
    public DefaultSetupStateRepository()
    {
    }

    /// <summary>
    /// Создаёт репозиторий с подключением к хранилищу ключ-значение (SQLite kv_settings).
    /// </summary>
    /// <param name="readSetting">Чтение значения настройки по ключу.</param>
    /// <param name="writeSetting">Запись значения настройки по ключу.</param>
    /// <param name="deleteSetting">Удаление настройки по ключу.</param>
    public DefaultSetupStateRepository(
        Func<string, CancellationToken, Task<string?>> readSetting,
        Func<string, string, CancellationToken, Task> writeSetting,
        Func<string, CancellationToken, Task>? deleteSetting = null)
    {
        _readSetting = readSetting ?? throw new ArgumentNullException(nameof(readSetting));
        _writeSetting = writeSetting ?? throw new ArgumentNullException(nameof(writeSetting));
        _deleteSetting = deleteSetting;
    }

    /// <inheritdoc />
    public async Task<SetupWizardStateDto?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_readSetting is not null)
        {
            string? json = await _readSetting(SettingKey, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SetupWizardStateDto>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        lock (_lock)
        {
            return _inMemoryState;
        }
    }

    /// <inheritdoc />
    public async Task<SetupWizardStateDto> SaveStateAsync(SetupWizardStateDto state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_writeSetting is not null)
        {
            string json = JsonSerializer.Serialize(state, JsonOptions);
            await _writeSetting(SettingKey, json, cancellationToken).ConfigureAwait(false);
        }

        lock (_lock)
        {
            _inMemoryState = state;
        }

        return state;
    }

    /// <inheritdoc />
    public async Task<bool> IsSetupCompletedAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto? state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return state?.IsCompleted == true;
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        if (_deleteSetting is not null)
        {
            await _deleteSetting(SettingKey, cancellationToken).ConfigureAwait(false);
        }

        lock (_lock)
        {
            _inMemoryState = null;
        }
    }
}
