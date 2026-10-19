using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ChainsViewModel : ViewModelBase
{
    private readonly IChainDefinitionRepository _chainRepo;
    private readonly IChainExecutionRepository _executionRepo;
    private readonly ITriggerInvoker _triggerInvoker;
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;

    private ChainItemViewModel? _selectedChain;
    private ChainRunStateViewModel? _selectedRun;
    private string _functionCallPayload = "{}";
    private bool _isLoading;
    private bool _isExecuting;
    private string? _statusMessage;

    public ChainsViewModel(
        IChainDefinitionRepository chainRepo,
        IChainExecutionRepository executionRepo,
        ITriggerInvoker triggerInvoker,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null)
    {
        _chainRepo = chainRepo ?? throw new ArgumentNullException(nameof(chainRepo));
        _executionRepo = executionRepo ?? throw new ArgumentNullException(nameof(executionRepo));
        _triggerInvoker = triggerInvoker ?? throw new ArgumentNullException(nameof(triggerInvoker));
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;

        LoadChainsCommand = new AsyncRelayCommand(LoadChainsAsync);
        TriggerFunctionCallCommand = new AsyncRelayCommand(
            TriggerFunctionCallAsync,
            () => SelectedChain != null && !IsExecuting);

        _ = LoadChainsAsync();
    }

    public ObservableCollection<ChainItemViewModel> Chains { get; } = [];
    public ObservableCollection<ChainRunStateViewModel> Runs { get; } = [];

    public ChainItemViewModel? SelectedChain
    {
        get => _selectedChain;
        set
        {
            if (SetProperty(ref _selectedChain, value))
            {
                TriggerFunctionCallCommand.RaiseCanExecuteChanged();
                _ = LoadRunsForSelectedChainAsync();
            }
        }
    }

    public ChainRunStateViewModel? SelectedRun
    {
        get => _selectedRun;
        set => SetProperty(ref _selectedRun, value);
    }

    public string FunctionCallPayload
    {
        get => _functionCallPayload;
        set => SetProperty(ref _functionCallPayload, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                TriggerFunctionCallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand LoadChainsCommand { get; }
    public AsyncRelayCommand TriggerFunctionCallCommand { get; }

    public async Task LoadChainsAsync()
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            IReadOnlyList<ChainDefinitionDto> definitions = await _chainRepo.ListEnabledAsync().ConfigureAwait(false);
            Chains.Clear();
            foreach (ChainDefinitionDto def in definitions)
            {
                Chains.Add(ChainItemViewModel.FromDto(def));
            }

            if (SelectedChain == null && Chains.Count > 0)
            {
                SelectedChain = Chains[0];
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load chains: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task TriggerFunctionCallAsync()
    {
        if (SelectedChain == null || IsExecuting)
        {
            return;
        }

        IsExecuting = true;
        StatusMessage = "Invoking chain...";
        try
        {
            RunId runId = await _triggerInvoker.InvokeFunctionCallAsync(
                SelectedChain.Id,
                FunctionCallPayload).ConfigureAwait(false);

            StatusMessage = $"Chain triggered successfully. RunId: {runId.Value}";
            _notifications?.ShowNotification(
                "Chain Triggered",
                $"Run {runId.Value} initiated for chain {SelectedChain.Name}",
                NotificationSeverity.Success);

            await LoadRunsForSelectedChainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Invocation failed: {ex.Message}";
            _notifications?.ShowNotification(
                "Chain Trigger Failed",
                ex.Message,
                NotificationSeverity.Error);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public async Task LoadRunsForSelectedChainAsync()
    {
        if (SelectedChain == null)
        {
            Runs.Clear();
            return;
        }

        try
        {
            IReadOnlyList<RunId> unfinished = await _executionRepo.ListUnfinishedRunsAsync().ConfigureAwait(false);
            Runs.Clear();
            foreach (RunId runId in unfinished)
            {
                ChainRunState? state = await _executionRepo.GetRunAsync(runId).ConfigureAwait(false);
                if (state != null && state.ChainId == SelectedChain.Id)
                {
                    Runs.Add(ChainRunStateViewModel.FromDto(state));
                }
            }

            if (Runs.Count > 0)
            {
                SelectedRun = Runs[0];
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load runs: {ex.Message}";
        }
    }
}
