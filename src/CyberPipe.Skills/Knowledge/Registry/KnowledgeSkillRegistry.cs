using System.Collections.Concurrent;
using CyberPipe.Contracts;
using CyberPipe.Skills.Knowledge.Services;
using CyberPipe.Skills.Knowledge.Skills;

namespace CyberPipe.Skills.Knowledge.Registry;

/// <summary>
/// Реестр навыков базы знаний и памяти (TASK-053).
/// Реализует ISkillInvoker для прозрачной интеграции с роутером и цепочками.
/// </summary>
public sealed class KnowledgeSkillRegistry : IKnowledgeSkillRegistry
{
    private readonly ConcurrentDictionary<string, ISkill> _skills = new(StringComparer.OrdinalIgnoreCase);

    public KnowledgeSkillRegistry()
    {
    }

    public KnowledgeSkillRegistry(IKnowledgeService knowledgeService)
    {
        ArgumentNullException.ThrowIfNull(knowledgeService);
        RegisterStandardKnowledgeSkills(knowledgeService);
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
                    Code = "KnowledgeSkill.NotFound",
                    Message = $"Knowledge skill '{request.SkillId}' was not found in registry.",
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
                    Code = result.ErrorCode ?? $"KnowledgeSkill.{result.Status}",
                    Message = result.Message ?? "Knowledge skill execution failed.",
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
                    Code = "KnowledgeSkill.UnhandledException",
                    Message = ex.Message,
                },
            };
        }
    }

    private void RegisterStandardKnowledgeSkills(IKnowledgeService knowledgeService)
    {
        RegisterSkill(new KnowledgeIngestSkill(knowledgeService));
        RegisterSkill(new KnowledgeSearchSkill(knowledgeService));
        RegisterSkill(new KnowledgeForgetSkill(knowledgeService));
        RegisterSkill(new MemoryRememberSkill(knowledgeService));
        RegisterSkill(new MemoryRecallSkill(knowledgeService));
    }
}
