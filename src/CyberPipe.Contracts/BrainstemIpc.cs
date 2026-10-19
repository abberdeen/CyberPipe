using System.Buffers.Binary;
using System.Text.Json;

namespace CyberPipe.Contracts;

/// <summary>
/// Типы сообщений бинарного протокола IPC между супервизором и Brainstem-воркером (TASK-022).
/// </summary>
public enum BrainstemMessageType : byte
{
    Heartbeat = 1,
    JobRequest = 2,
    JobResult = 3,
    Shutdown = 4
}

/// <summary>
/// Фреймовый протокол поверх потока (Named Pipe) для обмена между супервизором и воркером.
/// Формат фрейма: [4 байта: длина полезной нагрузки N + 1] [1 байт: тип сообщения] [N байт: данные].
/// </summary>
public static class BrainstemIpc
{
    public static async Task WriteFrameAsync(
        Stream stream,
        BrainstemMessageType type,
        byte[]? payload,
        CancellationToken ct)
    {
        int payloadLen = payload?.Length ?? 0;
        int frameLen = 1 + payloadLen;

        byte[] header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), frameLen);
        header[4] = (byte)type;

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload != null && payloadLen > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(BrainstemMessageType Type, byte[] Payload)> ReadFrameAsync(
        Stream stream,
        CancellationToken ct)
    {
        byte[] lengthBuffer = new byte[4];
        await ReadExactAsync(stream, lengthBuffer, ct).ConfigureAwait(false);
        int frameLen = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (frameLen < 1 || frameLen > 64 * 1024 * 1024)
        {
            throw new InvalidOperationException($"Invalid Brainstem IPC frame length: {frameLen}");
        }

        byte[] frameBuffer = new byte[frameLen];
        await ReadExactAsync(stream, frameBuffer, ct).ConfigureAwait(false);

        var type = (BrainstemMessageType)frameBuffer[0];
        byte[] payload = new byte[frameLen - 1];
        if (frameLen > 1)
        {
            Buffer.BlockCopy(frameBuffer, 1, payload, 0, frameLen - 1);
        }

        return (type, payload);
    }

    public static Task WriteHeartbeatAsync(Stream stream, CancellationToken ct) =>
        WriteFrameAsync(stream, BrainstemMessageType.Heartbeat, null, ct);

    public static Task WriteShutdownAsync(Stream stream, CancellationToken ct) =>
        WriteFrameAsync(stream, BrainstemMessageType.Shutdown, null, ct);

    public static Task WriteJobRequestAsync(Stream stream, BrainstemJobRequest request, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(request);
        return WriteFrameAsync(stream, BrainstemMessageType.JobRequest, json, ct);
    }

    public static Task WriteJobResultAsync(Stream stream, BrainstemJobResult result, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(result);
        return WriteFrameAsync(stream, BrainstemMessageType.JobResult, json, ct);
    }

    public static BrainstemJobRequest DeserializeJobRequest(byte[] payload) =>
        JsonSerializer.Deserialize<BrainstemJobRequest>(payload)
        ?? throw new InvalidOperationException("Failed to deserialize BrainstemJobRequest");

    public static BrainstemJobResult DeserializeJobResult(byte[] payload) =>
        JsonSerializer.Deserialize<BrainstemJobResult>(payload)
        ?? throw new InvalidOperationException("Failed to deserialize BrainstemJobResult");

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Stream closed prematurely after reading {totalRead} of {buffer.Length} bytes.");
            }
            totalRead += read;
        }
    }
}
