using System.Collections.Concurrent;
using CyberPipe.Contracts;
using CyberPipe.Skills.Host.Skills;

namespace CyberPipe.Skills.Host.Registry;

/// <summary>
/// Реестр хост-навыков и исполнитель вызовов навыков в цепочках через ISkillInvoker (TASK-050).
/// </summary>
public sealed class HostSkillRegistry : IHostSkillRegistry
{
    private readonly ConcurrentDictionary<string, IHostSkill> _skills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создаёт пустой реестр хост-навыков.
    /// </summary>
    public HostSkillRegistry()
    {
    }

    /// <summary>
    /// Создаёт реестр и регистрирует стандартный набор хост-навыков.
    /// </summary>
    public HostSkillRegistry(
        IHostMediaController mediaController,
        ISystemStatusProvider systemStatusProvider,
        ITimerService timerService,
        IAlarmService alarmService)
    {
        RegisterStandardHostSkills(mediaController, systemStatusProvider, timerService, alarmService);
    }

    public HostSkillRegistry(
        IHostMediaController mediaController,
        ISystemStatusProvider systemStatusProvider,
        ITimerService timerService,
        IAlarmService alarmService,
        IAudioContentSafetyEvaluator? audioSafetyEvaluator)
    {
        RegisterStandardHostSkills(mediaController, systemStatusProvider, timerService, alarmService, audioSafetyEvaluator);
    }

    /// <inheritdoc />
    public void RegisterSkill(IHostSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (string.IsNullOrWhiteSpace(skill.SkillId))
        {
            throw new ArgumentException("Skill ID must not be empty.", nameof(skill));
        }

        _skills[skill.SkillId] = skill;
    }

    /// <inheritdoc />
    public IHostSkill? FindSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        _skills.TryGetValue(skillId, out IHostSkill? skill);
        return skill;
    }

    /// <inheritdoc />
    public IReadOnlyList<IHostSkill> GetAllSkills()
    {
        return _skills.Values.OrderBy(s => s.SkillId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<SkillInvocationResult> InvokeSkillAsync(
        SkillInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IHostSkill? skill = FindSkill(request.SkillId);
        if (skill is null)
        {
            return new SkillInvocationResult
            {
                IsSuccess = false,
                Error = new ErrorDto
                {
                    Code = "HostSkill.NotFound",
                    Message = $"Host skill '{request.SkillId}' was not found in registry.",
                },
            };
        }

        try
        {
            HostSkillResult result = await skill.ExecuteAsync(request.PayloadJson, cancellationToken);
            if (result.IsSuccess)
            {
                return new SkillInvocationResult
                {
                    IsSuccess = true,
                    OutputJson = result.OutputJson,
                    OutputFiles = result.OutputFiles,
                };
            }

            return new SkillInvocationResult
            {
                IsSuccess = false,
                OutputJson = result.OutputJson,
                OutputFiles = result.OutputFiles,
                Error = new ErrorDto
                {
                    Code = result.ErrorCode ?? $"HostSkill.{result.Status}",
                    Message = result.Message ?? "Host skill execution did not succeed.",
                },
            };
        }
        catch (Exception ex)
        {
            return new SkillInvocationResult
            {
                IsSuccess = false,
                Error = new ErrorDto
                {
                    Code = "HostSkill.UnhandledException",
                    Message = ex.Message,
                },
            };
        }
    }

    /// <summary>
    /// Регистрирует стандартные хост-навыки: медиа, громкость, статус, таймеры и будильники.
    /// </summary>
    public void RegisterStandardHostSkills(
        IHostMediaController mediaController,
        ISystemStatusProvider systemStatusProvider,
        ITimerService timerService,
        IAlarmService alarmService,
        IAudioContentSafetyEvaluator? audioSafetyEvaluator = null)
    {
        ArgumentNullException.ThrowIfNull(mediaController);
        ArgumentNullException.ThrowIfNull(systemStatusProvider);
        ArgumentNullException.ThrowIfNull(timerService);
        ArgumentNullException.ThrowIfNull(alarmService);

        // Media
        RegisterSkill(new MediaPlaySkill(mediaController, audioSafetyEvaluator));
        RegisterSkill(new MediaPauseSkill(mediaController));
        RegisterSkill(new MediaPlayPauseSkill(mediaController));
        RegisterSkill(new MediaNextSkill(mediaController));
        RegisterSkill(new MediaPreviousSkill(mediaController));

        // Volume
        RegisterSkill(new VolumeGetSkill(mediaController));
        RegisterSkill(new VolumeSetSkill(mediaController));
        RegisterSkill(new VolumeUpSkill(mediaController));
        RegisterSkill(new VolumeDownSkill(mediaController));
        RegisterSkill(new VolumeMuteSkill(mediaController));
        RegisterSkill(new VolumeUnmuteSkill(mediaController));
        RegisterSkill(new VolumeToggleMuteSkill(mediaController));

        // System status
        RegisterSkill(new SystemStatusSkill(systemStatusProvider));

        // Timers
        RegisterSkill(new TimerSetSkill(timerService));
        RegisterSkill(new TimerCancelSkill(timerService));
        RegisterSkill(new TimerListSkill(timerService));

        // Alarms
        RegisterSkill(new AlarmSetSkill(alarmService));
        RegisterSkill(new AlarmCancelSkill(alarmService));
        RegisterSkill(new AlarmListSkill(alarmService));
    }
}
