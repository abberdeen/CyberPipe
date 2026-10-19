using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Security;

/// <summary>
/// Файловая реализация <see cref="ISecretStore" /> ТОЛЬКО для тестов и CI.
/// НЕБЕЗОПАСНА для продакшена: значения лишь кодируются, а не шифруются платформенным хранилищем.
/// В рабочем коде используйте <see cref="PlatformSecretStore" />.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    /// <summary>Создаёт файловое хранилище по явному пути; предназначено для тестов.</summary>
    /// <param name="path">Путь к файлу секретов тестового хранилища.</param>
    public FileSecretStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <inheritdoc />
    public bool IsPlatformStore => false;

    /// <summary>Явный признак небезопасности: файл не является платформенным хранилищем ОС.</summary>
    public bool IsSecure => false;

    /// <inheritdoc />
    public async Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> entries = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            entries[key] = Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Reveal()));
            await WriteAllAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> entries = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            return entries.TryGetValue(key, out string? encoded)
                ? SecretValue.FromPlainText(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> entries = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.Remove(key))
                return false;

            await WriteAllAsync(entries, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        string text = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(text, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task WriteAllAsync(Dictionary<string, string> entries, CancellationToken cancellationToken)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (dir is not null)
            Directory.CreateDirectory(dir);

        string tempPath = _path + ".tmp";
        string json = JsonSerializer.Serialize(entries, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }
}
