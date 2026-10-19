using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Brainstem;

public static class Program
{
    private static readonly SemaphoreSlim PipeWriteLock = new(1, 1);
    private static volatile bool HeartbeatSuppressed = false;

    public static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        string role = "default";
        string? initialJobId = null;
        int heartbeatIntervalMs = 1000;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipeName = args[++i];
            }
            else if (string.Equals(args[i], "--role", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                role = args[++i];
            }
            else if (string.Equals(args[i], "--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                initialJobId = args[++i];
            }
            else if (string.Equals(args[i], "--heartbeat-interval-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int parsedInterval) && parsedInterval > 0)
                {
                    heartbeatIntervalMs = parsedInterval;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.Error.WriteLine("Error: --pipe <name> argument is required.");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await using var clientPipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            // Connect to supervisor's Named Pipe server
            await clientPipe.ConnectAsync(10_000, cts.Token).ConfigureAwait(false);

            // Start background Heartbeat task
            Task heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(heartbeatIntervalMs));
                    while (!cts.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                    {
                        if (HeartbeatSuppressed)
                        {
                            // Simulated hang - do not send heartbeats
                            continue;
                        }

                        await SendAsync(clientPipe, BrainstemIpc.WriteHeartbeatAsync, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Shutdown expected
                }
                catch (Exception)
                {
                    cts.Cancel();
                }
            }, cts.Token);

            // Main message dispatch loop
            while (!cts.IsCancellationRequested)
            {
                (BrainstemMessageType msgType, byte[] payload) =
                    await BrainstemIpc.ReadFrameAsync(clientPipe, cts.Token).ConfigureAwait(false);

                if (msgType == BrainstemMessageType.Shutdown)
                {
                    cts.Cancel();
                    break;
                }

                if (msgType == BrainstemMessageType.JobRequest)
                {
                    BrainstemJobRequest req = BrainstemIpc.DeserializeJobRequest(payload);
                    BrainstemJobResult result = await ExecuteJobAsync(req, cts).ConfigureAwait(false);

                    await SendAsync(
                        clientPipe,
                        (stream, ct) => BrainstemIpc.WriteJobResultAsync(stream, result, ct),
                        cts.Token).ConfigureAwait(false);
                }
            }

            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (EndOfStreamException)
        {
            // Supervisor closed the pipe connection
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Brainstem error: {ex.Message}");
            return 2;
        }
    }

    private static async Task SendAsync(
        Stream stream,
        Func<Stream, CancellationToken, Task> action,
        CancellationToken ct)
    {
        await PipeWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(stream, ct).ConfigureAwait(false);
        }
        finally
        {
            PipeWriteLock.Release();
        }
    }

    private static async Task<BrainstemJobResult> ExecuteJobAsync(
        BrainstemJobRequest req,
        CancellationTokenSource procCts)
    {
        try
        {
            switch (req.Kind.ToLowerInvariant())
            {
                case "ping":
                    return new BrainstemJobResult(req.JobId, true, "PONG"u8.ToArray(), null);

                case "echo":
                    return new BrainstemJobResult(req.JobId, true, req.Payload, null);

                case "hang":
                    // Simulate hang by suppressing heartbeats and waiting
                    HeartbeatSuppressed = true;
                    await Task.Delay(Timeout.InfiniteTimeSpan, procCts.Token).ConfigureAwait(false);
                    return new BrainstemJobResult(req.JobId, false, null, "Woke from hang unexpectedly");

                case "allocate":
                    // Allocate memory in large chunks to test Job Object limit
                    int targetMb = 512;
                    if (req.Payload.Length > 0)
                    {
                        string str = Encoding.UTF8.GetString(req.Payload);
                        if (int.TryParse(str, out int parsed))
                        {
                            targetMb = parsed;
                        }
                    }

                    var chunks = new List<byte[]>();
                    try
                    {
                        for (int i = 0; i < targetMb; i++)
                        {
                            byte[] chunk = new byte[1024 * 1024];
                            Array.Fill(chunk, (byte)0xCC);
                            chunks.Add(chunk);
                        }
                    }
                    catch (OutOfMemoryException)
                    {
                        Environment.Exit(1067);
                    }

                    return new BrainstemJobResult(
                        req.JobId,
                        true,
                        Encoding.UTF8.GetBytes($"Allocated {chunks.Count} MB"),
                        null);

                case "crash":
                    Environment.Exit(42);
                    return new BrainstemJobResult(req.JobId, false, null, "Should have exited");

                case "whisper":
                case "stt":
                    return await ExecuteWhisperJobAsync(req, procCts.Token).ConfigureAwait(false);

                default:
                    return new BrainstemJobResult(
                        req.JobId,
                        false,
                        null,
                        $"Unknown Brainstem job kind '{req.Kind}'");
            }
        }
        catch (Exception ex)
        {
            return new BrainstemJobResult(req.JobId, false, null, ex.Message);
        }
    }

    private static async Task<BrainstemJobResult> ExecuteWhisperJobAsync(
        BrainstemJobRequest req,
        CancellationToken ct)
    {
        try
        {
            string payloadStr = req.Payload.Length > 0 ? Encoding.UTF8.GetString(req.Payload) : "{}";
            using var jsonDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadStr) ? "{}" : payloadStr);
            JsonElement root = jsonDoc.RootElement;

            // Check if mock/simulation requested
            if (root.TryGetProperty("simulate", out var simProp))
            {
                string sim = simProp.GetString() ?? "";
                if (sim.Equals("unavailable", StringComparison.OrdinalIgnoreCase))
                {
                    return new BrainstemJobResult(req.JobId, false, null, "EngineUnavailable: Whisper model or executable not found.");
                }

                if (sim.Equals("nospeech", StringComparison.OrdinalIgnoreCase))
                {
                    var noSpeechResult = TranscriptionResult.NoSpeech();
                    return new BrainstemJobResult(req.JobId, true, JsonSerializer.SerializeToUtf8Bytes(noSpeechResult), null);
                }

                if (sim.Equals("error", StringComparison.OrdinalIgnoreCase))
                {
                    return new BrainstemJobResult(req.JobId, false, null, "Simulated transcription error");
                }
            }

            string? filePath = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
            string? audioBase64 = root.TryGetProperty("audioBase64", out var ab) ? ab.GetString() : null;
            string language = root.TryGetProperty("language", out var lang) ? lang.GetString() ?? "auto" : "auto";
            string? modelPath = root.TryGetProperty("modelPath", out var mp) ? mp.GetString() : null;

            byte[]? audioBytes = null;
            if (!string.IsNullOrEmpty(audioBase64))
            {
                audioBytes = Convert.FromBase64String(audioBase64);
            }
            else if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                audioBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            }

            if (audioBytes == null || audioBytes.Length == 0)
            {
                var noSpeechResult = TranscriptionResult.NoSpeech();
                return new BrainstemJobResult(req.JobId, true, JsonSerializer.SerializeToUtf8Bytes(noSpeechResult), null);
            }

            // Check if audio is silence (all zeroes in PCM payload after 44-byte WAV header)
            bool isSilence = true;
            for (int i = 0; i < audioBytes.Length; i++)
            {
                if (audioBytes[i] != 0 && audioBytes[i] != 0x80)
                {
                    if (i >= 44)
                    {
                        isSilence = false;
                        break;
                    }
                }
            }

            if (isSilence && audioBytes.Length > 44)
            {
                var noSpeechResult = TranscriptionResult.NoSpeech();
                return new BrainstemJobResult(req.JobId, true, JsonSerializer.SerializeToUtf8Bytes(noSpeechResult), null);
            }

            string? whisperExe = Environment.GetEnvironmentVariable("WHISPER_CLI_PATH");
            string? effectiveModel = modelPath ?? Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH");

            if (string.IsNullOrEmpty(whisperExe) || !File.Exists(whisperExe) ||
                string.IsNullOrEmpty(effectiveModel) || !File.Exists(effectiveModel))
            {
                return new BrainstemJobResult(
                    req.JobId,
                    false,
                    null,
                    $"EngineUnavailable: Whisper executable ('{whisperExe}') or model ('{effectiveModel}') not found.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = whisperExe,
                Arguments = $"-m \"{effectiveModel}\" -l {language} --output-json",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = startInfo };
            proc.Start();

            await proc.StandardInput.BaseStream.WriteAsync(audioBytes, ct).ConfigureAwait(false);
            await proc.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
            proc.StandardInput.Close();

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                return new BrainstemJobResult(req.JobId, false, null, $"Whisper process failed with code {proc.ExitCode}: {stderr}");
            }

            var segments = new List<TranscriptionSegment>
            {
                new(TimeSpan.Zero, TimeSpan.FromSeconds(audioBytes.Length / 32000.0), stdout.Trim(), 0.95)
            };
            var transResult = TranscriptionResult.Success(stdout.Trim(), language, segments, TimeSpan.FromMilliseconds(req.TimeoutMs));
            return new BrainstemJobResult(req.JobId, true, JsonSerializer.SerializeToUtf8Bytes(transResult), null);
        }
        catch (OperationCanceledException)
        {
            return new BrainstemJobResult(req.JobId, false, null, "Operation cancelled");
        }
        catch (Exception ex)
        {
            return new BrainstemJobResult(req.JobId, false, null, $"Whisper job error: {ex.Message}");
        }
    }
}
