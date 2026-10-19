using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Skills.Host;
using CyberPipe.Skills.Host.Media;
using CyberPipe.Skills.Host.Registry;
using CyberPipe.Skills.Host.Skills;
using CyberPipe.Skills.Host.System;
using CyberPipe.Skills.Host.Timers;
using Xunit;

namespace CyberPipe.Tests.Unit.Skills.Host;

public sealed class SkillsHostTests
{
    [Fact]
    public async Task MediaSkills_ExecuteCommands_UpdatesMediaControllerAndReturnsSuccess()
    {
        var media = new FakeMediaController();
        var play = new MediaPlaySkill(media);
        var pause = new MediaPauseSkill(media);
        var playpause = new MediaPlayPauseSkill(media);
        var next = new MediaNextSkill(media);
        var prev = new MediaPreviousSkill(media);

        HostSkillResult playResult = await play.ExecuteAsync("{}");
        Assert.True(playResult.IsSuccess);
        Assert.True(media.IsPlaying);
        Assert.Equal("play", media.LastCommand);

        HostSkillResult pauseResult = await pause.ExecuteAsync("{}");
        Assert.True(pauseResult.IsSuccess);
        Assert.False(media.IsPlaying);
        Assert.Equal("pause", media.LastCommand);

        HostSkillResult toggleResult = await playpause.ExecuteAsync("{}");
        Assert.True(toggleResult.IsSuccess);
        Assert.True(media.IsPlaying);
        Assert.Equal("playpause", media.LastCommand);

        HostSkillResult nextResult = await next.ExecuteAsync("{}");
        Assert.True(nextResult.IsSuccess);
        Assert.Equal("next", media.LastCommand);

        HostSkillResult prevResult = await prev.ExecuteAsync("{}");
        Assert.True(prevResult.IsSuccess);
        Assert.Equal("previous", media.LastCommand);
    }

    [Fact]
    public async Task MediaSkills_WhenPlatformNotSupported_ReturnsNotSupportedResult()
    {
        var media = new FakeMediaController { SimulateNotSupported = true };
        var play = new MediaPlaySkill(media);

        HostSkillResult result = await play.ExecuteAsync("{}");

        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.NotSupported, result.Status);
        Assert.Equal("Host.NotSupported", result.ErrorCode);
    }

    [Fact]
    public async Task VolumeSkills_GetAndSetVolume_WorksCorrectly()
    {
        var media = new FakeMediaController { Volume = 40, IsMuted = false };
        var getSkill = new VolumeGetSkill(media);
        var setSkill = new VolumeSetSkill(media);
        var muteSkill = new VolumeMuteSkill(media);
        var unmuteSkill = new VolumeUnmuteSkill(media);
        var toggleSkill = new VolumeToggleMuteSkill(media);

        HostSkillResult getResult = await getSkill.ExecuteAsync("{}");
        Assert.True(getResult.IsSuccess);
        Assert.Contains("\"volume\":40", getResult.OutputJson);
        Assert.Contains("\"isMuted\":false", getResult.OutputJson);

        HostSkillResult setResult = await setSkill.ExecuteAsync("{\"volume\":60}");
        Assert.True(setResult.IsSuccess);
        Assert.Equal(60, media.Volume);

        HostSkillResult muteResult = await muteSkill.ExecuteAsync("{}");
        Assert.True(muteResult.IsSuccess);
        Assert.True(media.IsMuted);

        HostSkillResult unmuteResult = await unmuteSkill.ExecuteAsync("{}");
        Assert.True(unmuteResult.IsSuccess);
        Assert.False(media.IsMuted);

        HostSkillResult toggleResult = await toggleSkill.ExecuteAsync("{}");
        Assert.True(toggleResult.IsSuccess);
        Assert.True(media.IsMuted);
    }

    [Theory]
    [InlineData("{\"volume\":-5}")]
    [InlineData("{\"volume\":105}")]
    [InlineData("{\"volume\":\"abc\"}")]
    [InlineData("{}")]
    public async Task VolumeSet_InvalidArguments_ReturnValidationError(string payload)
    {
        var media = new FakeMediaController();
        var setSkill = new VolumeSetSkill(media);

        HostSkillResult result = await setSkill.ExecuteAsync(payload);

        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
        Assert.Equal("Host.ValidationError", result.ErrorCode);
    }

    [Fact]
    public async Task VolumeSet_DrasticChangeWithoutForce_RequiresHitlApproval()
    {
        var media = new FakeMediaController { Volume = 20 };
        var setSkill = new VolumeSetSkill(media);

        // Прыжок с 20% до 85% — резкое повышение громкости
        HostSkillResult result = await setSkill.ExecuteAsync("{\"volume\":85,\"force\":false}");

        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresApproval);
        Assert.Equal(SkillResultStatus.RequiresApproval, result.Status);
        Assert.Equal(20, media.Volume); // громкость не изменилась

        // С флагом force=true операция выполняется
        HostSkillResult forcedResult = await setSkill.ExecuteAsync("{\"volume\":85,\"force\":true}");
        Assert.True(forcedResult.IsSuccess);
        Assert.Equal(85, media.Volume);
    }

    [Fact]
    public async Task VolumeUpAndDown_ValidAndInvalidSteps_BehaveCorrectly()
    {
        var media = new FakeMediaController { Volume = 30 };
        var upSkill = new VolumeUpSkill(media);
        var downSkill = new VolumeDownSkill(media);

        HostSkillResult upResult = await upSkill.ExecuteAsync("{\"step\":10}");
        Assert.True(upResult.IsSuccess);
        Assert.Equal(40, media.Volume);

        HostSkillResult downResult = await downSkill.ExecuteAsync("{\"step\":15}");
        Assert.True(downResult.IsSuccess);
        Assert.Equal(25, media.Volume);

        HostSkillResult invalidStep = await upSkill.ExecuteAsync("{\"step\":-2}");
        Assert.Equal(SkillResultStatus.ValidationError, invalidStep.Status);

        HostSkillResult excessiveStep = await upSkill.ExecuteAsync("{\"step\":50}");
        Assert.Equal(SkillResultStatus.RequiresApproval, excessiveStep.Status);
    }

    [Fact]
    public async Task SystemStatusSkill_ReturnsMetricsWithoutUnhandledExceptions()
    {
        var fakeProvider = new FakeSystemStatusProvider();
        var skill = new SystemStatusSkill(fakeProvider);

        HostSkillResult result = await skill.ExecuteAsync("{}");

        Assert.True(result.IsSuccess);
        Assert.Contains("25.5", result.OutputJson);
        Assert.Contains("TEST-HOST", result.OutputJson);
    }

    [Fact]
    public async Task WindowsSystemStatusProvider_DoesNotThrowOnCurrentMachine()
    {
        var provider = new WindowsSystemStatusProvider();
        SystemStatusDto status = await provider.GetStatusAsync();

        Assert.NotNull(status);
        Assert.NotEmpty(status.OsDescription);
        Assert.NotEmpty(status.MachineName);
        Assert.NotNull(status.SystemUptime);
    }

    [Fact]
    public async Task TimerService_SetListCancelAndFire_BehavesDeterministically()
    {
        var clock = new ManualTestTimeProvider();
        var timerService = new InMemoryTimerService(clock);

        TimerRecord? firedRecord = null;
        timerService.TimerFired += r => firedRecord = r;

        var setSkill = new TimerSetSkill(timerService);
        var listSkill = new TimerListSkill(timerService);
        var cancelSkill = new TimerCancelSkill(timerService);

        // 1. Установка таймера на 30 секунд
        HostSkillResult setResult = await setSkill.ExecuteAsync("{\"durationSeconds\":30,\"title\":\"Boil eggs\"}");
        Assert.True(setResult.IsSuccess);

        IReadOnlyList<TimerRecord> activeBefore = await timerService.ListActiveTimersAsync();
        Assert.Single(activeBefore);
        Assert.Equal("Boil eggs", activeBefore[0].Title);

        // 2. Список таймеров через навык
        HostSkillResult listResult = await listSkill.ExecuteAsync("{}");
        Assert.True(listResult.IsSuccess);
        Assert.Contains("Boil eggs", listResult.OutputJson);

        // 3. Продвигаем время на 15 секунд — таймер ещё не должен сработать
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(firedRecord);

        // 4. Продвигаем ещё на 16 секунд (итого 31с) — таймер должен сработать
        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.NotNull(firedRecord);
        Assert.Equal("Boil eggs", firedRecord!.Title);
        Assert.True(firedRecord.IsFired);

        IReadOnlyList<TimerRecord> activeAfter = await timerService.ListActiveTimersAsync();
        Assert.Empty(activeAfter);

        // 5. Отмена нового таймера
        HostSkillResult secondTimerResult = await setSkill.ExecuteAsync("{\"durationSeconds\":60,\"title\":\"Cancelled timer\"}");
        using var doc = JsonDocument.Parse(secondTimerResult.OutputJson);
        Guid secondId = doc.RootElement.GetProperty("TimerId").GetGuid();

        HostSkillResult cancelResult = await cancelSkill.ExecuteAsync($"{{\"timerId\":\"{secondId}\"}}");
        Assert.True(cancelResult.IsSuccess);
        Assert.Contains("\"cancelled\":true", cancelResult.OutputJson);

        clock.Advance(TimeSpan.FromMinutes(2));
        // firedRecord не должен измениться на отменённый таймер
        Assert.Equal("Boil eggs", firedRecord.Title);
    }

    [Fact]
    public async Task TimerSkill_InvalidDuration_ReturnsValidationError()
    {
        var timerService = new InMemoryTimerService();
        var setSkill = new TimerSetSkill(timerService);

        HostSkillResult result = await setSkill.ExecuteAsync("{\"durationSeconds\":-10,\"title\":\"Negative\"}");
        Assert.False(result.IsSuccess);
        Assert.Equal(SkillResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task AlarmService_SetListCancel_WorksCorrectly()
    {
        var clock = new ManualTestTimeProvider();
        var alarmService = new InMemoryAlarmService(clock);

        var setSkill = new AlarmSetSkill(alarmService);
        var listSkill = new AlarmListSkill(alarmService);
        var cancelSkill = new AlarmCancelSkill(alarmService);

        HostSkillResult setResult = await setSkill.ExecuteAsync("{\"time\":\"08:30\",\"title\":\"Morning Wakeup\",\"daysOfWeek\":[\"Monday\",\"Wednesday\"]}");
        Assert.True(setResult.IsSuccess);

        HostSkillResult listResult = await listSkill.ExecuteAsync("{}");
        Assert.True(listResult.IsSuccess);
        Assert.Contains("Morning Wakeup", listResult.OutputJson);

        using var doc = JsonDocument.Parse(setResult.OutputJson);
        Guid alarmId = doc.RootElement.GetProperty("AlarmId").GetGuid();

        HostSkillResult cancelResult = await cancelSkill.ExecuteAsync($"{{\"alarmId\":\"{alarmId}\"}}");
        Assert.True(cancelResult.IsSuccess);
        Assert.Contains("\"cancelled\":true", cancelResult.OutputJson);
    }

    [Fact]
    public async Task HostSkillRegistry_RegistersStandardSkills_AndInvokesThroughISkillInvoker()
    {
        var media = new FakeMediaController();
        var status = new FakeSystemStatusProvider();
        var timers = new InMemoryTimerService();
        var alarms = new InMemoryAlarmService();

        var registry = new HostSkillRegistry(media, status, timers, alarms);

        IReadOnlyList<IHostSkill> allSkills = registry.GetAllSkills();
        Assert.Equal(19, allSkills.Count);
        Assert.NotNull(registry.FindSkill("media.play"));
        Assert.NotNull(registry.FindSkill("volume.set"));
        Assert.NotNull(registry.FindSkill("system.status"));
        Assert.NotNull(registry.FindSkill("timer.set"));
        Assert.NotNull(registry.FindSkill("alarm.set"));

        // Проверяем вызов через интерфейс ISkillInvoker для интеграции с цепочками
        ISkillInvoker invoker = registry;

        var request = new SkillInvocationRequest
        {
            SkillId = "media.play",
            PayloadJson = "{}",
            RunId = RunId.New(),
        };

        SkillInvocationResult result = await invoker.InvokeSkillAsync(request);
        Assert.True(result.IsSuccess);
        Assert.True(media.IsPlaying);

        // Проверяем вызов неизвестного навыка
        var unknownRequest = new SkillInvocationRequest
        {
            SkillId = "nonexistent.skill",
            PayloadJson = "{}",
            RunId = RunId.New(),
        };

        SkillInvocationResult unknownResult = await invoker.InvokeSkillAsync(unknownRequest);
        Assert.False(unknownResult.IsSuccess);
        Assert.Equal("HostSkill.NotFound", unknownResult.Error?.Code);
    }

    [Fact]
    public void HostSkillCatalog_ContainsAllStandardDescriptors()
    {
        Assert.Equal(19, HostSkillCatalog.AllDescriptors.Count);
        foreach (HostSkillDescriptor desc in HostSkillCatalog.AllDescriptors)
        {
            Assert.False(string.IsNullOrWhiteSpace(desc.SkillId));
            Assert.False(string.IsNullOrWhiteSpace(desc.Name));
            Assert.False(string.IsNullOrWhiteSpace(desc.Description));
            Assert.False(string.IsNullOrWhiteSpace(desc.Category));
            Assert.False(string.IsNullOrWhiteSpace(desc.InputSchemaJson));
        }
    }

    private sealed class ManualTestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = [];

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow += delta;
            DateTimeOffset now = _utcNow;

            // Срабатывание таймеров, чьё время наступило
            var ready = _timers.Where(t => t.DueTimeUtc <= now && !t.IsDisposed).ToList();
            foreach (ManualTimer timer in ready)
            {
                timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DateTimeOffset due = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : _utcNow.Add(dueTime);

            var timer = new ManualTimer(callback, state, due);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            public DateTimeOffset DueTimeUtc { get; private set; }
            public bool IsDisposed { get; private set; }

            public ManualTimer(TimerCallback callback, object? state, DateTimeOffset dueTimeUtc)
            {
                _callback = callback;
                _state = state;
                DueTimeUtc = dueTimeUtc;
            }

            public void Fire()
            {
                if (!IsDisposed)
                {
                    _callback(_state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTimeUtc = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : DateTimeOffset.UtcNow.Add(dueTime);
                return true;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
