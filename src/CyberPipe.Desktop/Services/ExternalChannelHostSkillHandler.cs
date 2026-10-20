using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Desktop.Services;

/// <summary>
/// Принимает сообщение внешнего канала и вызывает хост-навык, указанный в тексте.
/// Текст сообщения: {"skillId":"...","payload":{...}}.
/// </summary>
public sealed class ExternalChannelHostSkillHandler(ISkillInvoker skills) : IMessageHandler<IncomingMessage>
{
    public ExternalChannelReply? LastReply { get; private set; }

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Channel != ExternalChannelInbox.OutsideChannel)
        {
            return;
        }

        if (!TryReadCommand(message.Text, out string skillId, out string payloadJson, out string? error))
        {
            LastReply = new ExternalChannelReply(false, error!, null, "ExternalChannel.InvalidMessage");
            return;
        }

        SkillInvocationResult result = await skills.InvokeSkillAsync(
            new SkillInvocationRequest
            {
                SkillId = skillId,
                PayloadJson = payloadJson,
                RunId = new RunId(Guid.NewGuid()),
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            string msg = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : result.OutputJson;
            LastReply = new ExternalChannelReply(true, msg, result.OutputJson, null);
            return;
        }

        LastReply = new ExternalChannelReply(
            false,
            result.Message ?? result.Error?.Message ?? "Host skill did not succeed.",
            result.OutputJson,
            result.Error?.Code);
    }

    private static bool TryReadCommand(string? text, out string skillId, out string payloadJson, out string? error)
    {
        skillId = string.Empty;
        payloadJson = "{}";
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "External channel message is empty.";
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("skillId", out JsonElement skillProp) ||
                skillProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(skillProp.GetString()))
            {
                error = "External channel message must contain skillId.";
                return false;
            }

            skillId = skillProp.GetString()!;
            if (root.TryGetProperty("payload", out JsonElement payload))
            {
                payloadJson = payload.ValueKind == JsonValueKind.String
                    ? payload.GetString() ?? "{}"
                    : payload.GetRawText();
            }

            return true;
        }
        catch (JsonException)
        {
            error = "External channel message is not valid JSON.";
            return false;
        }
    }
}

/// <summary>Ответ, который внешний канал получил бы обратно.</summary>
public sealed record ExternalChannelReply(bool Accepted, string Text, string? OutputJson, string? ErrorCode);
