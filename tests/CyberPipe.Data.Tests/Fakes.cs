using CyberPipe.Contracts;
using CyberPipe.Data;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Data.Tests;

/// <summary>Фейковое хранилище секретов в памяти.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, SecretValue> _values = new(StringComparer.Ordinal);

    public bool IsPlatformStore => false;

    public int SetCount { get; private set; }

    public void Seed(string key, string plainText) => _values[key] = SecretValue.FromPlainText(plainText);

    public string? StoredPlainText(string key) =>
        _values.TryGetValue(key, out SecretValue? value) ? value.Reveal() : null;

    public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
    {
        SetCount++;
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(key, out SecretValue? value) ? value : null);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.Remove(key));
}

/// <summary>Фейковый промпт: очередь ответов, считает вызовы, null в очереди — отказ пользователя.</summary>
internal sealed class FakeSecretPrompt(params string?[] answers) : ISecretPrompt
{
    private readonly Queue<string?> _answers = new(answers);

    public int CallCount { get; private set; }

    public List<string> Purposes { get; } = [];

    public Task<SecretValue?> RequestPasswordAsync(string purpose, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Purposes.Add(purpose);
        string? answer = _answers.Count > 0 ? _answers.Dequeue() : null;
        return Task.FromResult(answer is null ? null : SecretValue.FromPlainText(answer));
    }
}

/// <summary>
/// Фейковый шлюз: успешен только для пароля "correct-password". Эмулирует создание базы и маркер
/// данных, чтобы проверять идемпотентность без реальной БД.
/// </summary>
internal sealed class FakePostgresGateway : IPostgresBootstrapGateway
{
    public const string CorrectPassword = "correct-password";

    public BootstrapReasonCode Failure { get; set; } = BootstrapReasonCode.AuthenticationFailed;

    public bool UseFailure { get; set; }

    public int EnsureCallCount { get; private set; }

    public int ProbeCallCount { get; private set; }

    public bool DatabaseCreated { get; private set; }

    public string DataMarker { get; private set; } = "initial-data";

    public List<string?> AttemptedPasswords { get; } = [];

    public Task<PostgresServerInfo> EnsureReadyAsync(
        SecretValue? password,
        CancellationToken cancellationToken = default)
    {
        EnsureCallCount++;
        AttemptedPasswords.Add(password?.Reveal());
        if (UseFailure)
            throw new PostgresUnavailableException(Failure);
        if (password?.Reveal() != CorrectPassword)
            throw new PostgresUnavailableException(BootstrapReasonCode.AuthenticationFailed);

        DatabaseCreated = true;
        return Task.FromResult(new PostgresServerInfo("16.4", "0.8.0"));
    }

    public Task ProbeAsync(SecretValue? password, CancellationToken cancellationToken = default)
    {
        ProbeCallCount++;
        if (UseFailure)
            throw new PostgresUnavailableException(Failure);
        if (password?.Reveal() != CorrectPassword)
            throw new PostgresUnavailableException(BootstrapReasonCode.AuthenticationFailed);
        return Task.CompletedTask;
    }
}

/// <summary>Логгер, захватывающий форматированные сообщения для проверки отсутствия секретов.</summary>
internal sealed class CapturingLogger : ILogger<PostgresBootstrapper>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception is not null)
            Messages.Add(exception.Message);
    }
}
