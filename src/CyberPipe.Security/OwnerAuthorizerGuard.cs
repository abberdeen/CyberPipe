using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Security;

/// <summary>
/// Адаптер проверки владельца для каналов и HITL (TASK-046, TASK-060, TASK-090)
/// поверх единого авторизатора <see cref="IOwnerAuthorizer" /> (TASK-073).
/// Привязан к фиксированному каналу: тот же идентификатор в чужом канале не авторизует.
/// </summary>
public sealed class OwnerAuthorizerGuard : IOwnerGuard
{
    private readonly IOwnerAuthorizer _authorizer;
    private readonly OwnerChannel _channel;
    private readonly ILogger<OwnerAuthorizerGuard> _logger;

    /// <summary>Создаёт проверку владельца для фиксированного канала.</summary>
    public OwnerAuthorizerGuard(
        IOwnerAuthorizer authorizer,
        OwnerChannel channel = OwnerChannel.Telegram,
        ILogger<OwnerAuthorizerGuard>? logger = null)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _channel = channel;
        _logger = logger ?? NullLogger<OwnerAuthorizerGuard>.Instance;
    }

    /// <inheritdoc />
    public async Task<bool> IsOwnerAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        try
        {
            return await _authorizer.IsOwnerAsync(
                new ChannelPrincipal { Channel = _channel, SenderId = userId },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Owner guard check failed (fail-closed).");
            return false;
        }
    }
}

/// <summary>
/// Проверка владельца в любом из трёх каналов сообщений (TASK-090): используется
/// HITL-сервисом, получающим идентификатор и канал раздельно. Отказоустойчива:
/// сбой проверки — отказ, а не доступ.
/// </summary>
public sealed class AnyChannelOwnerGuard : IOwnerGuard
{
    private readonly IOwnerAuthorizer _authorizer;

    /// <summary>Создаёт проверку по всем каналам.</summary>
    public AnyChannelOwnerGuard(IOwnerAuthorizer authorizer)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    /// <inheritdoc />
    public async Task<bool> IsOwnerAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        foreach (OwnerChannel channel in new[]
                 {
                     OwnerChannel.Telegram, OwnerChannel.Desktop, OwnerChannel.Cli,
                 })
        {
            bool match;
            try
            {
                match = await _authorizer.IsOwnerAsync(
                    new ChannelPrincipal { Channel = channel, SenderId = userId },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                continue;
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
