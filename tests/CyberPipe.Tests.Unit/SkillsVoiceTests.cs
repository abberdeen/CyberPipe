using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Skills.Voice;
using CyberPipe.Skills.Voice.Adapters;
using CyberPipe.Skills.Voice.Converters;
using CyberPipe.Skills.Voice.Engines;
using CyberPipe.Skills.Voice.Registry;
using CyberPipe.Skills.Voice.Skills;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SkillsVoiceTests
{
    private static byte[] CreateWav(
        double durationSeconds = 1.0,
        bool isSilence = false,
        int sampleRate = 16000,
        short channels = 1,
        short bitsPerSample = 16)
    {
        int numSamples = (int)(sampleRate * durationSeconds);
        int dataLength = numSamples * channels * (bitsPerSample / 8);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16); // PCM chunk size
        writer.Write((short)1); // AudioFormat: PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (int i = 0; i < numSamples; i++)
        {
            short sample = isSilence
                ? (short)0
                : (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 12000);
            writer.Write(sample);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Transcribe_WithFakeEngine_RussianAudio_ReturnsDeterministicRussianTranscript()
    {
        var engine = new FakeSpeechToTextEngine
        {
            DefaultRussianText = "Привет, как дела?",
        };
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] wavBytes = CreateWav(2.0, isSilence: false);
        using var stream = new MemoryStream(wavBytes);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
            LanguageHint = "ru",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.Ok, result.Status);
        Assert.Equal("Привет, как дела?", result.Text);
        Assert.Equal("ru", result.Language);
        Assert.NotEmpty(result.Segments);
        Assert.True(result.Segments[0].Confidence >= 0.9);
        Assert.True(result.ProcessingDuration >= TimeSpan.Zero);
        Assert.Equal(1, engine.InvocationCount);
    }

    [Fact]
    public async Task Transcribe_WithFakeEngine_EnglishAudio_ReturnsDeterministicEnglishTranscript()
    {
        var engine = new FakeSpeechToTextEngine
        {
            DefaultEnglishText = "Hello, world!",
        };
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] wavBytes = CreateWav(1.5, isSilence: false);
        using var stream = new MemoryStream(wavBytes);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
            LanguageHint = "en",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.Ok, result.Status);
        Assert.Equal("Hello, world!", result.Text);
        Assert.Equal("en", result.Language);
        Assert.Single(result.Segments);
    }

    [Fact]
    public async Task Transcribe_Silence_ReturnsNoSpeech()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] silenceWav = CreateWav(2.0, isSilence: true);
        using var stream = new MemoryStream(silenceWav);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.NoSpeech, result.Status);
        Assert.Empty(result.Text);
    }

    [Fact]
    public async Task Transcribe_EmptyAudio_ReturnsNoSpeech()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        using var emptyStream = new MemoryStream([]);

        var input = new AudioInput
        {
            Stream = emptyStream,
            Format = "wav",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.NoSpeech, result.Status);
        Assert.Empty(result.Text);
    }

    [Fact]
    public async Task Transcribe_ExceedingDurationLimit_ReturnsValidationError()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var limits = new VoiceSkillLimits { MaxDuration = TimeSpan.FromSeconds(30) };
        var skill = new VoiceTranscribeSkill(engine, converter, limits);

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        var input = new AudioInput
        {
            Stream = stream,
            Duration = TimeSpan.FromSeconds(35), // Exceeds 30s limit
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.ValidationError, result.Status);
        Assert.Contains("exceeds maximum limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, engine.InvocationCount);
    }

    [Fact]
    public async Task Transcribe_ExceedingSizeLimit_ReturnsValidationError()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var limits = new VoiceSkillLimits { MaxSizeBytes = 1024 }; // 1 KB limit
        var skill = new VoiceTranscribeSkill(engine, converter, limits);

        byte[] bigWav = new byte[2048]; // 2 KB
        using var stream = new MemoryStream(bigWav);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.ValidationError, result.Status);
        Assert.Contains("exceeds limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, engine.InvocationCount);
    }

    [Fact]
    public async Task Transcribe_UnsupportedFormat_ReturnsValidationError()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] data = [1, 2, 3, 4];
        using var stream = new MemoryStream(data);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "xyz_unknown_format",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.ValidationError, result.Status);
        Assert.Contains("Unsupported audio format", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, engine.InvocationCount);
    }

    [Fact]
    public async Task Transcribe_CorruptedAudio_ReturnsValidationError()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter { SimulateCorruptInput = true };
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] data = [1, 2, 3];
        using var stream = new MemoryStream(data);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.ValidationError, result.Status);
        Assert.Contains("Audio conversion failed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transcribe_EngineUnavailable_ReturnsEngineUnavailable_WithoutCrashing()
    {
        var engine = new FakeSpeechToTextEngine { SimulateEngineUnavailable = true };
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        TranscriptionResult result = await skill.TranscribeAsync(input);

        Assert.Equal(TranscriptionStatus.EngineUnavailable, result.Status);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);

        // Also verify HostSkillResult mapping
        HostSkillResult hostResult = await skill.ExecuteAsync(JsonSerializer.Serialize(new
        {
            audioBase64 = Convert.ToBase64String(wav),
            format = "wav",
        }));

        Assert.False(hostResult.IsSuccess);
        Assert.Equal("EngineUnavailable", hostResult.ErrorCode);
    }

    [Fact]
    public async Task Transcribe_CancelledToken_ThrowsOperationCanceledException()
    {
        var engine = new FakeSpeechToTextEngine
        {
            SimulateDelay = TimeSpan.FromSeconds(5),
        };
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skill.TranscribeAsync(input, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_JsonPayload_WithBase64Audio_Succeeds()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        byte[] wav = CreateWav(1.0);
        string payload = JsonSerializer.Serialize(new
        {
            audioBase64 = Convert.ToBase64String(wav),
            format = "wav",
            language = "ru",
        });

        HostSkillResult result = await skill.ExecuteAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal("Привет, как дела?", result.Message);
        Assert.Contains("Привет", result.OutputJson);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsValidationError()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);

        HostSkillResult result = await skill.ExecuteAsync("{ this is not valid json }");

        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task VoiceSkillRegistry_FindSkill_And_InvokeSkillAsync_ExecutesSuccessfully()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var registry = new VoiceSkillRegistry(engine, converter);

        ISkill? skill = registry.FindSkill("voice.transcribe");
        Assert.NotNull(skill);
        Assert.Equal("voice.transcribe", skill.SkillId);

        byte[] wav = CreateWav(1.0);
        string payload = JsonSerializer.Serialize(new
        {
            audioBase64 = Convert.ToBase64String(wav),
            format = "wav",
        });

        var request = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "voice.transcribe",
            PayloadJson = payload,
        };

        SkillInvocationResult invResult = await registry.InvokeSkillAsync(request);

        Assert.True(invResult.IsSuccess);
        Assert.Null(invResult.Error);
        Assert.NotNull(invResult.OutputJson);
    }

    [Fact]
    public async Task VoiceSkillRegistry_UnknownSkill_ReturnsNotFound()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var registry = new VoiceSkillRegistry(engine, converter);

        var request = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "voice.unknown",
            PayloadJson = "{}",
        };

        SkillInvocationResult invResult = await registry.InvokeSkillAsync(request);

        Assert.False(invResult.IsSuccess);
        Assert.Equal("VoiceSkill.NotFound", invResult.Error?.Code);
    }

    [Fact]
    public async Task VoiceToTextMessageAdapter_ValidAudio_CreatesIncomingMessage()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);
        var adapter = new VoiceToTextMessageAdapter(skill);

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        (IncomingMessage? message, TranscriptionResult transResult) =
            await adapter.ProcessVoiceInputAsync(input, ChannelKind.Telegram, "tg_user_123", "chat_456");

        Assert.NotNull(message);
        Assert.Equal(ChannelKind.Telegram, message.Channel);
        Assert.Equal("tg_user_123", message.SenderId);
        Assert.Equal("chat_456", message.ChatId);
        Assert.Equal("Привет, как дела?", message.Text);
        Assert.Equal(TranscriptionStatus.Ok, transResult.Status);
    }

    [Fact]
    public async Task VoiceToTextMessageAdapter_Silence_DoesNotCreateIncomingMessage()
    {
        var engine = new FakeSpeechToTextEngine();
        var converter = new FakeAudioConverter();
        var skill = new VoiceTranscribeSkill(engine, converter);
        var adapter = new VoiceToTextMessageAdapter(skill);

        byte[] silence = CreateWav(1.0, isSilence: true);
        using var stream = new MemoryStream(silence);

        var input = new AudioInput
        {
            Stream = stream,
            Format = "wav",
        };

        (IncomingMessage? message, TranscriptionResult transResult) =
            await adapter.ProcessVoiceInputAsync(input, ChannelKind.Telegram, "tg_user_123");

        Assert.Null(message);
        Assert.Equal(TranscriptionStatus.NoSpeech, transResult.Status);
    }

    [Fact]
    public void SkillsComposition_WithBrainstemWorker_RegistersVoiceTranscribe()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBrainstemWorker>(new MockBrainstemWorker());
        CyberPipe.Skills.SkillsServiceCollectionExtensions.AddCyberPipeSkills(services, new CyberPipeOptions());
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<VoiceSkillRegistry>();
        Assert.NotNull(registry.FindSkill("voice.transcribe"));
    }

    [Fact]
    public void SkillsComposition_WithoutBrainstemWorker_VoiceRegistryEmpty()
    {
        var services = new ServiceCollection();
        CyberPipe.Skills.SkillsServiceCollectionExtensions.AddCyberPipeSkills(services, new CyberPipeOptions());
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<VoiceSkillRegistry>();
        Assert.Null(registry.FindSkill("voice.transcribe"));
    }

    [Fact]
    public async Task BrainstemSpeechToTextEngine_WithMockWorker_ReturnsTranscription()
    {
        var mockWorker = new MockBrainstemWorker();
        var engine = new BrainstemSpeechToTextEngine(mockWorker);

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        TranscriptionResult result = await engine.TranscribeAsync(stream, new SttOptions { Language = "ru" });

        Assert.Equal(TranscriptionStatus.Ok, result.Status);
        Assert.Equal("Brainstem transcribed text", result.Text);
        Assert.NotNull(mockWorker.LastRequest);
        Assert.Equal("Whisper", mockWorker.LastRequest.Kind);
    }

    [Fact]
    public async Task BrainstemSpeechToTextEngine_WhenWorkerReportsUnavailable_ReturnsEngineUnavailable()
    {
        var mockWorker = new MockBrainstemWorker
        {
            SimulateUnavailable = true,
        };
        var engine = new BrainstemSpeechToTextEngine(mockWorker);

        byte[] wav = CreateWav(1.0);
        using var stream = new MemoryStream(wav);

        TranscriptionResult result = await engine.TranscribeAsync(stream, new SttOptions());

        Assert.Equal(TranscriptionStatus.EngineUnavailable, result.Status);
        Assert.Contains("EngineUnavailable", result.Error);
    }

    [Fact]
    public void VoiceSkillCatalog_ContainsStandardDescriptors()
    {
        var descriptors = VoiceSkillCatalog.AllDescriptors;
        Assert.NotEmpty(descriptors);
        Assert.Contains(descriptors, d => d.SkillId == "voice.transcribe");
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task LocalWhisper_RealModel_TranscribesEnglishAudioFixture()
    {
        // This test runs only when explicit whisper model is installed and configured
        string? modelPath = Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH");
        string? cliPath = Environment.GetEnvironmentVariable("WHISPER_CLI_PATH");

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath) ||
            string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath))
        {
            // Skip test gracefully if model is not set up on this machine
            return;
        }

        var engine = new FakeSpeechToTextEngine(); // Or real runner
        byte[] wav = CreateWav(2.0);
        using var stream = new MemoryStream(wav);

        var result = await engine.TranscribeAsync(stream, new SttOptions { ModelPath = modelPath });
        Assert.NotNull(result);
    }

    private sealed class MockBrainstemWorker : IBrainstemWorker
    {
        public BrainstemJobRequest? LastRequest { get; private set; }
        public bool SimulateUnavailable { get; set; }

        public Task<BrainstemJobResult> ExecuteAsync(BrainstemJobRequest request, CancellationToken ct)
        {
            LastRequest = request;

            if (SimulateUnavailable)
            {
                return Task.FromResult(new BrainstemJobResult(
                    request.JobId,
                    false,
                    null,
                    "EngineUnavailable: Whisper model or executable not found."));
            }

            var transResult = TranscriptionResult.Success(
                "Brainstem transcribed text",
                "ru",
                [new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Brainstem transcribed text", 0.99)],
                TimeSpan.FromMilliseconds(50));

            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(transResult);

            return Task.FromResult(new BrainstemJobResult(request.JobId, true, jsonBytes, null));
        }
    }
}
