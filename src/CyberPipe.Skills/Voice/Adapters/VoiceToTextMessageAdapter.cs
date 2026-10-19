using CyberPipe.Contracts;

namespace CyberPipe.Skills.Voice.Adapters;

/// <summary>
/// Адаптер преобразования голосовых сообщений в текстовые входящие сообщения (TASK-052).
/// Позволяет голосовому вводу из Telegram/Avalonia/CLI попадать в обычную маршрутизацию Tier 0–3 как текстовый запрос.
/// </summary>
public sealed class VoiceToTextMessageAdapter
{
    private readonly IVoiceSkill _voiceSkill;
    private readonly IMessageBus? _messageBus;

    public VoiceToTextMessageAdapter(IVoiceSkill voiceSkill, IMessageBus? messageBus = null)
    {
        _voiceSkill = voiceSkill ?? throw new ArgumentNullException(nameof(voiceSkill));
        _messageBus = messageBus;
    }

    /// <summary>
    /// Транскрибирует аудиовход и формирует IncomingMessage для роутера.
    /// Если передан IMessageBus, автоматически публикует его.
    /// </summary>
    public async Task<(IncomingMessage? Message, TranscriptionResult Result)> ProcessVoiceInputAsync(
        AudioInput input,
        ChannelKind channel,
        string senderId,
        string? chatId = null,
        CancellationToken ct = default)
    {
        TranscriptionResult result = await _voiceSkill.TranscribeAsync(input, ct).ConfigureAwait(false);

        if (result.Status != TranscriptionStatus.Ok || string.IsNullOrWhiteSpace(result.Text))
        {
            return (null, result);
        }

        var message = new IncomingMessage
        {
            Id = new MessageId(Guid.NewGuid().ToString("N")),
            Channel = channel,
            SenderId = senderId,
            ChatId = chatId,
            Text = result.Text,
            Attachments = [],
            SentAtUtc = DateTimeOffset.UtcNow
        };

        if (_messageBus != null)
        {
            await _messageBus.PublishAsync(message, ct).ConfigureAwait(false);
        }

        return (message, result);
    }
}
