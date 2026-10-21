using System.Collections.Concurrent;
using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Skills.Personal.Skills;

namespace CyberPipe.Skills.Personal.Registry;

/// <summary>
/// Реестр персональных навыков владельца (TASK-051).
/// Реализует ISkillInvoker для прозрачной интеграции с роутером и цепочками.
/// </summary>
public sealed class PersonalSkillRegistry : IPersonalSkillRegistry
{
    private readonly ConcurrentDictionary<string, ISkill> _skills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создаёт пустой реестр персональных навыков.
    /// </summary>
    public PersonalSkillRegistry()
    {
    }

    /// <summary>
    /// Создаёт реестр и регистрирует стандартный набор навыков заметок, финансов, напоминаний и CRM.
    /// </summary>
    public PersonalSkillRegistry(
        INoteStoreService noteRepository,
        IFinanceLedgerService financeRepository,
        IReminderStoreService reminderRepository,
        IContactStoreService contactRepository,
        TimeProvider? timeProvider = null)
    {
        RegisterStandardPersonalSkills(noteRepository, financeRepository, reminderRepository, contactRepository, timeProvider);
    }

    /// <inheritdoc />
    public void RegisterSkill(ISkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (string.IsNullOrWhiteSpace(skill.SkillId))
        {
            throw new ArgumentException("Skill ID must not be empty.", nameof(skill));
        }

        _skills[skill.SkillId] = skill;
    }

    /// <inheritdoc />
    public ISkill? FindSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        _skills.TryGetValue(skillId, out ISkill? skill);
        return skill;
    }

    /// <inheritdoc />
    public IReadOnlyList<ISkill> GetAllSkills()
    {
        return _skills.Values.OrderBy(s => s.SkillId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
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
                    Code = "PersonalSkill.NotFound",
                    Message = $"Personal skill '{request.SkillId}' was not found in registry.",
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
                    Code = result.ErrorCode ?? $"PersonalSkill.{result.Status}",
                    Message = result.Message ?? "Personal skill execution failed.",
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
                    Code = "PersonalSkill.UnhandledException",
                    Message = ex.Message,
                },
            };
        }
    }

    /// <summary>
    /// Регистрирует стандартные персональные навыки: заметки, финансы, напоминания и CRM.
    /// </summary>
    public void RegisterStandardPersonalSkills(
        INoteStoreService noteRepository,
        IFinanceLedgerService financeRepository,
        IReminderStoreService reminderRepository,
        IContactStoreService contactRepository,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(noteRepository);
        ArgumentNullException.ThrowIfNull(financeRepository);
        ArgumentNullException.ThrowIfNull(reminderRepository);
        ArgumentNullException.ThrowIfNull(contactRepository);

        // Notes
        RegisterSkill(new NoteCreateSkill(noteRepository));
        RegisterSkill(new NoteSearchSkill(noteRepository));
        RegisterSkill(new NoteGetSkill(noteRepository));
        RegisterSkill(new NoteUpdateSkill(noteRepository));
        RegisterSkill(new NoteDeleteSkill(noteRepository));

        // Finance
        RegisterSkill(new FinanceAddExpenseSkill(financeRepository));
        RegisterSkill(new FinanceAddIncomeSkill(financeRepository));
        RegisterSkill(new FinanceBalanceSkill(financeRepository));
        RegisterSkill(new FinanceReportSkill(financeRepository, timeProvider));

        // Reminders
        RegisterSkill(new ReminderCreateSkill(reminderRepository, timeProvider));
        RegisterSkill(new ReminderListSkill(reminderRepository));
        RegisterSkill(new ReminderCancelSkill(reminderRepository));
        RegisterSkill(new ReminderCompleteSkill(reminderRepository));

        // CRM
        RegisterSkill(new CrmContactUpsertSkill(contactRepository));
        RegisterSkill(new CrmContactFindSkill(contactRepository));
        RegisterSkill(new CrmInteractionAddSkill(contactRepository));
        RegisterSkill(new CrmInteractionHistorySkill(contactRepository));
    }
}
