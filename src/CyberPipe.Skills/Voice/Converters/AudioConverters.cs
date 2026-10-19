using System.Diagnostics;
using System.Text;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Voice.Converters;

/// <summary>
/// Фейковый конвертер аудио для детерминированного тестирования и работы без ffmpeg (TASK-052).
/// </summary>
public sealed class FakeAudioConverter : IAudioConverter
{
    private static readonly byte[] CorruptMarker = "CORRUPT"u8.ToArray();

    public HashSet<string> SupportedFormats { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "wav", "wave", "mp3", "ogg", "flac", "m4a", "aac", "webm"
    };

    public Func<Stream, string, CancellationToken, Task<Stream>>? CustomConverter { get; set; }
    public bool SimulateCorruptInput { get; set; }
    public int ConversionCount { get; private set; }

    public async Task<Stream> ConvertToWav16kMonoAsync(
        Stream inputStream,
        string inputFormat,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ConversionCount++;

        if (CustomConverter != null)
        {
            return await CustomConverter(inputStream, inputFormat, ct).ConfigureAwait(false);
        }

        string normalizedFormat = (inputFormat ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();

        if (string.IsNullOrEmpty(normalizedFormat) || !SupportedFormats.Contains(normalizedFormat))
        {
            throw new AudioFormatNotSupportedException(inputFormat ?? string.Empty);
        }

        if (SimulateCorruptInput)
        {
            throw new AudioConversionException("Simulated corrupt audio stream");
        }

        // Check if stream explicitly contains the corrupt marker
        if (inputStream.CanSeek && inputStream.Length >= CorruptMarker.Length)
        {
            long originalPos = inputStream.Position;
            byte[] headerBuffer = new byte[CorruptMarker.Length];
            int read = await inputStream.ReadAsync(headerBuffer.AsMemory(0, CorruptMarker.Length), ct).ConfigureAwait(false);
            inputStream.Position = originalPos;

            if (read == CorruptMarker.Length && headerBuffer.SequenceEqual(CorruptMarker))
            {
                throw new AudioConversionException("Corrupted audio data detected in stream header.");
            }
        }

        // Return a normalized 16kHz mono WAV representation
        var ms = new MemoryStream();
        if (string.Equals(normalizedFormat, "wav", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedFormat, "wave", StringComparison.OrdinalIgnoreCase))
        {
            await inputStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        }
        else
        {
            // Synthesize standard 44-byte WAV header followed by stream payload
            byte[] rawPayload;
            using (var copyMs = new MemoryStream())
            {
                await inputStream.CopyToAsync(copyMs, ct).ConfigureAwait(false);
                rawPayload = copyMs.ToArray();
            }

            WriteWavHeader(ms, rawPayload.Length, sampleRate: 16000, channels: 1, bitsPerSample: 16);
            await ms.WriteAsync(rawPayload, ct).ConfigureAwait(false);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteWavHeader(Stream stream, int dataLength, int sampleRate, short channels, short bitsPerSample)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // Subchunk1Size (16 for PCM)
        writer.Write((short)1); // AudioFormat (1 for PCM)
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // ByteRate
        writer.Write((short)(channels * bitsPerSample / 8)); // BlockAlign
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
    }
}

/// <summary>
/// Реальный аудиоконвертер на базе внешней утилиты ffmpeg (TASK-052).
/// Конвертирует произвольные аудиофайлы в стандартный 16 kHz mono 16-bit PCM WAV.
/// </summary>
public sealed class FfmpegAudioConverter : IAudioConverter
{
    private readonly string _ffmpegPath;

    public FfmpegAudioConverter(string? ffmpegPath = null)
    {
        _ffmpegPath = !string.IsNullOrWhiteSpace(ffmpegPath)
            ? ffmpegPath
            : Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
    }

    public async Task<Stream> ConvertToWav16kMonoAsync(
        Stream inputStream,
        string inputFormat,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string normalized = (inputFormat ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = "-hide_banner -loglevel error -i pipe:0 -ar 16000 -ac 1 -c:a pcm_s16le -f wav pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new AudioConversionException("Failed to launch ffmpeg process.");
        }
        catch (Exception ex) when (ex is not AudioConversionException)
        {
            // If ffmpeg was not found and input is already wav, pass it through directly
            if (string.Equals(normalized, "wav", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "wave", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackMs = new MemoryStream();
                await inputStream.CopyToAsync(fallbackMs, ct).ConfigureAwait(false);
                fallbackMs.Position = 0;
                return fallbackMs;
            }

            throw new AudioConversionException(
                $"ffmpeg was not found at '{_ffmpegPath}' to convert format '{inputFormat}'. Make sure ffmpeg is installed.",
                ex);
        }

        using (proc)
        {
            var outputMs = new MemoryStream();
            var stderrBuilder = new StringBuilder();

            Task pumpInput = Task.Run(async () =>
            {
                try
                {
                    await inputStream.CopyToAsync(proc.StandardInput.BaseStream, ct).ConfigureAwait(false);
                    await proc.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    proc.StandardInput.Close();
                }
            }, ct);

            Task pumpOutput = proc.StandardOutput.BaseStream.CopyToAsync(outputMs, ct);
            Task pumpError = Task.Run(async () =>
            {
                string err = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(err))
                {
                    stderrBuilder.Append(err);
                }
            }, ct);

            await Task.WhenAll(pumpInput, pumpOutput, pumpError).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                string errText = stderrBuilder.ToString();
                if (errText.Contains("Unknown format", StringComparison.OrdinalIgnoreCase) ||
                    errText.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AudioFormatNotSupportedException(inputFormat ?? "unknown");
                }

                throw new AudioConversionException($"ffmpeg conversion failed with exit code {proc.ExitCode}: {errText}");
            }

            outputMs.Position = 0;
            return outputMs;
        }
    }
}
