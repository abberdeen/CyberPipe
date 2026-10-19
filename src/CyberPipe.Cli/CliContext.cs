using CyberPipe.Contracts;

namespace CyberPipe.Cli;

/// <summary>
/// Контекст выполнения CLI-команд: потоки ввода/вывода, флаги и зависимости контрактов.
/// Позволяет тестировать все команды изолированно на любых фейках.
/// </summary>
public sealed class CliContext
{
    public TextWriter Out { get; init; } = Console.Out;
    public TextWriter Error { get; init; } = Console.Error;
    public TextReader In { get; init; } = Console.In;

    public IIntentRouter? Router { get; init; }
    public ITokenBudgetGovernor? BudgetGovernor { get; init; }
    public IServiceControl? ServiceControl { get; init; }
    public IChainDefinitionRepository? ChainRepo { get; init; }
    public IChainExecutionRepository? ExecutionRepo { get; init; }
    public ITriggerInvoker? TriggerInvoker { get; init; }
    public IHitlApprovalService? HitlService { get; init; }
    public ISetupWizardEngine? SetupWizard { get; init; }
    public CyberPipeOptions? Options { get; init; }

    public bool IsJson { get; set; }
    public bool IsVerbose { get; set; }
}
