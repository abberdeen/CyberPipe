using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Contracts;

namespace CyberPipe.Channels.Telegram;

/// <summary>
/// Верификатор каналов для мастера первого запуска (TASK-090).
/// Telegram проверяется живым запросом к Bot API и отправляет тестовые сообщения
/// через зарегистрированный адаптер; Desktop/CLI в процессе Service не хостятся.
/// </summary>
public sealed class SetupChannelVerifier : IChannelVerifier
{
    private readonly ITelegramBotClientAdapter _adapter;

    /// <summary>Создаёт верификатор поверх адаптера Bot API.</summary>
    public SetupChannelVerifier(ITelegramBotClientAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> VerifyChannelAsync(
        ChannelKind channel,
        SecretValue? token = null,
        CancellationToken cancellationToken = default)
    {
        if (channel != ChannelKind.Telegram)
        {
            return Result<bool>.Failure(
                "Channel.NotHostedHere",
                $"Channel '{channel}' is not hosted in the Service process.");
        }

        if (token is null)
        {
            return Result<bool>.Failure("Channel.NoToken", "Telegram bot token is required.");
        }

        string revealed = token.Reveal();
        try
        {
            using var http = new HttpClient();
            var probe = new HttpTelegramBotClientAdapter(http, revealed);
            await probe.GetUpdatesAsync(offset: 0, limit: 1, timeoutSeconds: 1, ct: cancellationToken)
                .ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure("Channel.Unreachable", $"Telegram Bot API is unreachable: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SendTestMessageAsync(
        ChannelKind channel,
        string targetOwnerId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (channel != ChannelKind.Telegram)
        {
            return Result<bool>.Failure(
                "Channel.NotHostedHere",
                $"Channel '{channel}' is not hosted in the Service process.");
        }

        if (_adapter is FakeTelegramBotClientAdapter)
        {
            return Result<bool>.Failure("Channel.NotConfigured", "Telegram bot token is not configured.");
        }

        if (!long.TryParse(targetOwnerId, out long chatId))
        {
            return Result<bool>.Failure("Channel.BadTarget", "Telegram chat id must be numeric.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result<bool>.Failure("Channel.EmptyText", "Test message text is required.");
        }

        try
        {
            await _adapter.SendTextMessageAsync(chatId, text, ct: cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure("Channel.SendFailed", $"Test message failed: {ex.Message}");
        }
    }
}
