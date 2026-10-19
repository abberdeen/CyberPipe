using System.Collections.Concurrent;
using CyberPipe.Contracts;
using CyberPipe.Skills.Voice.Skills;

namespace CyberPipe.Skills.Voice.Registry;

/// <summary>
/// Реестр голосовых навыков системы (TASK-052).
/// Реализует ISkillInvoker для прозрачной интеграции с роутером и цепочками.
/// </summary>
public sealed class VoiceSkillRegistry : IVoiceSkillRegistry
{
    private readonly ConcurrentDictionary<string, ISkill> _skills = new(StringComparer.OrdinalIgnoreCase);

    public VoiceSkillRegistry()
    {
    }

    public VoiceSkillRegistry(IVoiceSkill voiceTranscribeSkill)
    {
        ArgumentNullException.ThrowIfNull(voiceTranscribeSkill);
        RegisterSkill(voiceTranscribeSkill);
    }

    public VoiceSkillRegistry(
        ISpeechToTextEngine engine,
        IAudioConverter converter,
        VoiceSkillLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(converter);
        var skill = new VoiceTranscribeSkill(engine, converter, limits);
        RegisterSkill(skill);
    }

    public void RegisterSkill(ISkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (string.IsNullOrWhiteSpace(skill.SkillId))
        {
            throw new ArgumentException("Skill ID must not be empty.", nameof(skill));
        }

        _skills[skill.SkillId] = skill;
    }

    public ISkill? FindSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        _skills.TryGetValue(skillId, out ISkill? skill);
        return skill;
    }

    public IReadOnlyList<ISkill> GetAllSkills()
    {
        return _skills.Values.OrderBy(s => s.SkillId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<SkillInvocationResult> InvokeSkillAsync(
        SkillInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ISkill? skill = FindSkill(request.SkillId);
        if (skill is null)
        {
            return new SkillInvocationResult
            {
                IsSuccess = false,
                Error = new ErrorDto
                {
                    Code = "VoiceSkill.NotFound",
                    Message = $"Voice skill '{request.SkillId}' was not found in registry.",
                },
            };
        }

        try
        {
            HostSkillResult result = await skill.ExecuteAsync(request.PayloadJson, cancellationToken).ConfigureAwait(false);
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
                    Code = result.ErrorCode ?? $"VoiceSkill.{result.Status}",
                    Message = result.Message ?? "Voice skill execution failed.",
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
                    Code = "VoiceSkill.UnhandledException",
                    Message = ex.Message,
                },
            };
        }
    }
}
