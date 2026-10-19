using System.Security.Cryptography;
using System.Text;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CyberPipe.Security;

/// <summary>
/// Авторизатор владельца по белому списку идентификаторов (TASK-073).
/// Пускает только Owner во всех каналах; решение — точное совпадение идентификатора
/// с белым списком своего канала. Совпадение по отображаемому имени без идентификатора
/// не авторизует. Любая неопределённость — отказ (fail-closed); положительных решений
/// без свежего снимка белого списка нет.
/// </summary>
public sealed class OwnerAuthorizer : IOwnerAuthorizer
{
    private readonly IOwnerWhitelistProvider _provider;
    private readonly ILogger<OwnerAuthorizer> _logger;

    /// <summary>Создаёт авторизатор поверх поставщика белого списка.</summary>
    public OwnerAuthorizer(IOwnerWhitelistProvider provider, ILogger<OwnerAuthorizer>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? NullLogger<OwnerAuthorizer>.Instance;
    }

    /// <inheritdoc />
    public async Task<OwnerAuthDecision> AuthorizeAsync(ChannelPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal is null || string.IsNullOrWhiteSpace(principal.SenderId))
        {
            return LogDecision(null, null, OwnerAuthDecision.Denied(OwnerAuthReason.MissingIdentity));
        }

        if (principal.Channel is OwnerChannel.Unknown or OwnerChannel.Webhook)
        {
            return LogDecision(principal.Channel, principal.SenderId, OwnerAuthDecision.Denied(OwnerAuthReason.UnknownChannel));
        }

        OwnerWhitelistSnapshot snapshot;
        try
        {
            snapshot = await _provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                _logger.LogWarning("OwnerAuth: whitelist provider returned no snapshot. Denying {Channel}.", principal.Channel);
                return LogDecision(principal.Channel, principal.SenderId, OwnerAuthDecision.Denied(OwnerAuthReason.WhitelistUnavailable));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnerAuth: whitelist read failed. Denying {Channel} (fail-closed).", principal.Channel);
            return LogDecision(principal.Channel, principal.SenderId, OwnerAuthDecision.Denied(OwnerAuthReason.WhitelistUnavailable));
        }

        string senderId = principal.SenderId;
        bool listEmpty = principal.Channel switch
        {
            OwnerChannel.Telegram => snapshot.TelegramOwnerIds.Count == 0,
            OwnerChannel.Desktop => string.IsNullOrWhiteSpace(snapshot.DesktopOwnerId),
            OwnerChannel.Cli => string.IsNullOrWhiteSpace(snapshot.CliOwnerId),
            _ => true,
        };

        if (listEmpty)
        {
            return LogDecision(principal.Channel, senderId, OwnerAuthDecision.Denied(OwnerAuthReason.EmptyWhitelist));
        }

        bool matched = principal.Channel switch
        {
            OwnerChannel.Telegram => ContainsExact(snapshot.TelegramOwnerIds, senderId),
            OwnerChannel.Desktop => IsExactMatch(snapshot.DesktopOwnerId, senderId),
            OwnerChannel.Cli => IsExactMatch(snapshot.CliOwnerId, senderId),
            _ => false,
        };

        return matched
            ? LogDecision(principal.Channel, senderId, OwnerAuthDecision.Authorized())
            : LogDecision(principal.Channel, senderId, OwnerAuthDecision.Denied(OwnerAuthReason.NotInWhitelist));
    }

    /// <inheritdoc />
    public async Task<bool> IsOwnerAsync(ChannelPrincipal principal, CancellationToken cancellationToken = default) =>
        (await AuthorizeAsync(principal, cancellationToken).ConfigureAwait(false)).IsAuthorized;

    /// <inheritdoc />
    public async Task<OwnerWhitelistSummary> GetWhitelistSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            OwnerWhitelistSnapshot snapshot = await _provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot?.ToSummary() ?? new OwnerWhitelistSummary();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnerAuth: whitelist summary unavailable (fail-closed).");
            return new OwnerWhitelistSummary();
        }
    }

    private OwnerAuthDecision LogDecision(OwnerChannel? channel, string? senderId, OwnerAuthDecision decision)
    {
        // В журнал — только канал, хэш идентификатора и решение: полные идентификаторы
        // и содержимое сообщений сюда не попадают.
        _logger.LogInformation(
            "OwnerAuth {Channel} identity {IdentityHash} -> {Outcome} ({Reason})",
            channel?.ToString() ?? "Unknown",
            HashSenderId(senderId),
            decision.Outcome,
            decision.Reason);
        return decision;
    }

    private static bool ContainsExact(IReadOnlyList<string> allowed, string senderId)
    {
        foreach (string entry in allowed)
        {
            if (IsExactMatch(entry, senderId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExactMatch(string? allowed, string senderId) =>
        !string.IsNullOrWhiteSpace(allowed) && string.Equals(allowed, senderId, StringComparison.Ordinal);

    internal static string HashSenderId(string? senderId)
    {
        if (string.IsNullOrEmpty(senderId))
        {
            return "none";
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(senderId));
        return Convert.ToHexString(hash)[..8];
    }
}

/// <summary>
/// Поставщик белого списка владельца поверх конфигурации TASK-003 (TASK-073).
/// Читает раздел <c>Owner</c> через <see cref="IOptionsMonitor{CyberPipeOptions}" /> при каждой
/// проверке, поэтому изменение списка применяется без перезапуска Service, а отзыв
/// идентификатора действует немедленно со следующей проверки.
/// </summary>
public sealed class OptionsOwnerWhitelistProvider : IOwnerWhitelistProvider, IDisposable
{
    private readonly IOptionsMonitor<CyberPipeOptions> _monitor;
    private readonly IDisposable? _subscription;

    /// <summary>Создаёт поставщика поверх монитора конфигурации.</summary>
    public OptionsOwnerWhitelistProvider(IOptionsMonitor<CyberPipeOptions> monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _subscription = monitor.OnChange(_ => WhitelistChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc />
    public event EventHandler? WhitelistChanged;

    /// <inheritdoc />
    public Task<OwnerWhitelistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        OwnerOptions owner = _monitor.CurrentValue?.Owner ?? new OwnerOptions();
        return Task.FromResult(new OwnerWhitelistSnapshot
        {
            TelegramOwnerIds = owner.TelegramOwnerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray(),
            CliOwnerId = Normalize(owner.CliOwnerId),
            DesktopOwnerId = Normalize(owner.DesktopOwnerId),
        });
    }

    /// <inheritdoc />
    public void Dispose() => _subscription?.Dispose();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
