using CyberPipe.Contracts;

namespace CyberPipe.Setup;

/// <summary>
/// Реестр владельцев мастера первого запуска (TASK-090): реализует порт регистрации
/// TASK-063 и поставщика белого списка TASK-073 поверх одного состояния.
/// Начальное состояние — из секции Owner конфигурации; регистрации мастера
/// применяются немедленно и рассылают сигнал изменения без перезапуска.
/// </summary>
public sealed class SetupOwnerRegistry : IOwnerRegistrationService, IOwnerWhitelistProvider
{
    private readonly object _sync = new();
    private readonly List<string> _telegram = [];
    private string? _desktop;
    private string? _cli;

    /// <summary>Создаёт реестр с начальным состоянием из конфигурации.</summary>
    public SetupOwnerRegistry(OwnerOptions? initial = null)
    {
        if (initial is not null)
        {
            _telegram.AddRange(initial.TelegramOwnerIds.Where(id => !string.IsNullOrWhiteSpace(id)));
            _desktop = Normalize(initial.DesktopOwnerId);
            _cli = Normalize(initial.CliOwnerId);
        }
    }

    /// <inheritdoc />
    public event EventHandler? WhitelistChanged;

    /// <inheritdoc />
    public Task<Result<bool>> RegisterOwnerAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Task.FromResult(Result<bool>.Failure("Owner.EmptyId", "Owner id is required."));
        }

        lock (_sync)
        {
            switch (channel)
            {
                case ChannelKind.Telegram:
                    if (!_telegram.Contains(ownerId, StringComparer.Ordinal))
                    {
                        _telegram.Add(ownerId);
                    }

                    break;
                case ChannelKind.Desktop:
                    _desktop = ownerId;
                    break;
                case ChannelKind.Cli:
                    _cli = ownerId;
                    break;
                default:
                    return Task.FromResult(Result<bool>.Failure("Owner.UnknownChannel", $"Unknown channel '{channel}'."));
            }
        }

        WhitelistChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(Result<bool>.Success(true));
    }

    /// <inheritdoc />
    public Task<bool> IsOwnerRegisteredAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Task.FromResult(false);
        }

        lock (_sync)
        {
            return Task.FromResult(channel switch
            {
                ChannelKind.Telegram => _telegram.Contains(ownerId, StringComparer.Ordinal),
                ChannelKind.Desktop => string.Equals(_desktop, ownerId, StringComparison.Ordinal),
                ChannelKind.Cli => string.Equals(_cli, ownerId, StringComparison.Ordinal),
                _ => false,
            });
        }
    }

    /// <inheritdoc />
    public Task<OwnerWhitelistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(new OwnerWhitelistSnapshot
            {
                TelegramOwnerIds = [.. _telegram],
                DesktopOwnerId = _desktop,
                CliOwnerId = _cli,
            });
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
