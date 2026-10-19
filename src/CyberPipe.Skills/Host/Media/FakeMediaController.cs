using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Media;

/// <summary>
/// Фейковый контроллер медиа и громкости для изолированного тестирования (TASK-050).
/// Позволяет имитировать платформу без поддержки и проверять историю вызовов.
/// </summary>
public sealed class FakeMediaController : IHostMediaController
{
    private readonly List<string> _commandHistory = [];

    /// <summary>Флаг симуляции неподдерживаемой платформы (например, не-Windows).</summary>
    public bool SimulateNotSupported { get; set; }

    /// <summary>Текущий уровень громкости (0–100).</summary>
    public int Volume { get; set; } = 50;

    /// <summary>Признак беззвучного режима.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Признак активности воспроизведения.</summary>
    public bool IsPlaying { get; set; }

    /// <summary>Последняя выполненная команда.</summary>
    public string? LastCommand { get; private set; }

    /// <summary>История выполненных команд.</summary>
    public IReadOnlyList<string> CommandHistory => _commandHistory;

    /// <inheritdoc />
    public Task<HostSkillResult> PlayAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        IsPlaying = true;
        Record("play");
        return Task.FromResult(HostSkillResult.Ok("{\"action\":\"play\",\"isPlaying\":true}", "Playback started."));
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        IsPlaying = false;
        Record("pause");
        return Task.FromResult(HostSkillResult.Ok("{\"action\":\"pause\",\"isPlaying\":false}", "Playback paused."));
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        IsPlaying = !IsPlaying;
        Record("playpause");
        return Task.FromResult(HostSkillResult.Ok($"{{\"action\":\"playpause\",\"isPlaying\":{(IsPlaying ? "true" : "false")}}}", "Toggled playback."));
    }

    /// <inheritdoc />
    public Task<HostSkillResult> NextTrackAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        Record("next");
        return Task.FromResult(HostSkillResult.Ok("{\"action\":\"next\"}", "Next track."));
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        Record("previous");
        return Task.FromResult(HostSkillResult.Ok("{\"action\":\"previous\"}", "Previous track."));
    }

    /// <inheritdoc />
    public Task<int> GetVolumeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Volume);
    }

    /// <inheritdoc />
    public Task<HostSkillResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        if (volumePercent is < 0 or > 100)
        {
            return Task.FromResult(HostSkillResult.ValidationError($"Volume percent must be between 0 and 100. Received: {volumePercent}."));
        }

        Volume = volumePercent;
        Record($"set_volume:{volumePercent}");
        return Task.FromResult(HostSkillResult.Ok($"{{\"volume\":{volumePercent}}}", $"Volume set to {volumePercent}%."));
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> VolumeUpAsync(int step = 5, CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return HostSkillResult.NotSupported("Fake media controller simulated not supported.");
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
        }

        int target = Math.Min(100, Volume + step);
        return await SetVolumeAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> VolumeDownAsync(int step = 5, CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return HostSkillResult.NotSupported("Fake media controller simulated not supported.");
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
        }

        int target = Math.Max(0, Volume - step);
        return await SetVolumeAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HostSkillResult> MuteAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        IsMuted = true;
        Record("mute");
        return Task.FromResult(HostSkillResult.Ok("{\"isMuted\":true}", "Muted."));
    }

    /// <inheritdoc />
    public Task<HostSkillResult> UnmuteAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return Task.FromResult(HostSkillResult.NotSupported("Fake media controller simulated not supported."));
        }

        IsMuted = false;
        Record("unmute");
        return Task.FromResult(HostSkillResult.Ok("{\"isMuted\":false}", "Unmuted."));
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNotSupported)
        {
            return HostSkillResult.NotSupported("Fake media controller simulated not supported.");
        }

        return IsMuted ? await UnmuteAsync(cancellationToken) : await MuteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsMutedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsMuted);
    }

    private void Record(string command)
    {
        LastCommand = command;
        _commandHistory.Add(command);
    }
}
