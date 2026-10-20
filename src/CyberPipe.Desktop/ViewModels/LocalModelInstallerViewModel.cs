using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class LocalModelItemViewModel : ViewModelBase
{
    private LocalModelInstallState _state;
    private int _progressPercent;
    private string? _progressText;
    private string? _verdictText;
    private string? _statusMessage;
    private bool _needsRiskConfirmation;

    public required ModelDescriptorDto Model { get; init; }

    public required string GroupTitle { get; init; }

    public bool RiskConfirmed { get; set; }

    public LocalModelInstallState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(CanInstall));
                InstallCommand?.RaiseCanExecuteChanged();
                RemoveCommand?.RaiseCanExecuteChanged();
                VerifyCommand?.RaiseCanExecuteChanged();
                CancelCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInstalled => State == LocalModelInstallState.Installed;
    public bool IsDownloading => State == LocalModelInstallState.Downloading;
    public bool CanInstall => State != LocalModelInstallState.Downloading && State != LocalModelInstallState.Installed;

    public AsyncRelayCommand? InstallCommand { get; internal set; }
    public AsyncRelayCommand? RemoveCommand { get; internal set; }
    public AsyncRelayCommand? VerifyCommand { get; internal set; }
    public RelayCommand? CancelCommand { get; internal set; }
    public RelayCommand? ConfirmRiskCommand { get; internal set; }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string? ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public string? VerdictText
    {
        get => _verdictText;
        set => SetProperty(ref _verdictText, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool NeedsRiskConfirmation
    {
        get => _needsRiskConfirmation;
        set => SetProperty(ref _needsRiskConfirmation, value);
    }
}

/// <summary>
/// ViewModel экрана установщика локальных моделей (TASK-081): состояние рантайма,
/// список моделей каталога по Tier 2 / Tier 3, прогресс и отмена, блок эмбеддинга Tier 1.
/// View в сеть не ходит и процессы не запускает: всё через контракты.
/// </summary>
public sealed class LocalModelInstallerViewModel : ViewModelBase
{
    private readonly ILocalModelInstaller _installer;
    private readonly ILocalRuntimeInstaller _runtime;
    private readonly IModelCompatibilityAdvisor? _advisor;
    private readonly IDesktopNotificationService? _notifications;
    private readonly Dictionary<string, CancellationTokenSource> _activeInstalls = new(StringComparer.Ordinal);

    private string _runtimeStatusText = "Runtime status unknown.";
    private string _embeddingStateText = "Embedding state unknown.";
    private IReadOnlyDictionary<string, HardwareFitVerdict> _verdicts =
        new Dictionary<string, HardwareFitVerdict>(StringComparer.Ordinal);

    public LocalModelInstallerViewModel(
        IReadOnlyList<ModelDescriptorDto> localCatalog,
        ILocalModelInstaller installer,
        ILocalRuntimeInstaller runtime,
        IModelCompatibilityAdvisor? advisor = null,
        IDesktopNotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(localCatalog);
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _advisor = advisor;
        _notifications = notifications;

        foreach (ModelDescriptorDto model in localCatalog)
        {
            var item = new LocalModelItemViewModel
            {
                Model = model,
                GroupTitle = model.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3
                    ? "Tier 3 — сложные задачи"
                    : "Tier 2 — лёгкие задачи",
            };
            item.InstallCommand = new AsyncRelayCommand(() => InstallModelAsync(item), () => item.CanInstall);
            item.RemoveCommand = new AsyncRelayCommand(() => RemoveModelAsync(item), () => item.IsInstalled);
            item.VerifyCommand = new AsyncRelayCommand(() => VerifyModelAsync(item), () => item.IsInstalled);
            item.CancelCommand = new RelayCommand(() => CancelInstall(item), () => item.IsDownloading);
            item.ConfirmRiskCommand = new RelayCommand(() =>
            {
                item.RiskConfirmed = true;
                item.NeedsRiskConfirmation = false;
                _ = InstallModelAsync(item);
            });
            Items.Add(item);
        }

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        EnsureRuntimeCommand = new AsyncRelayCommand(EnsureRuntimeAsync);
        EnsureEmbeddingCommand = new AsyncRelayCommand(EnsureEmbeddingAsync);

        _ = RefreshAsync();
    }

    public ObservableCollection<LocalModelItemViewModel> Items { get; } = [];

    public string RuntimeStatusText
    {
        get => _runtimeStatusText;
        private set => SetProperty(ref _runtimeStatusText, value);
    }

    public string EmbeddingStateText
    {
        get => _embeddingStateText;
        private set => SetProperty(ref _embeddingStateText, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand EnsureRuntimeCommand { get; }

    public AsyncRelayCommand EnsureEmbeddingCommand { get; }

    public async Task RefreshAsync()
    {
        LocalRuntimeInfo runtime = await _runtime.GetStatusAsync().ConfigureAwait(true);
        RuntimeStatusText = runtime.State == LocalRuntimeState.Ready
            ? $"Ollama ready{(runtime.Version is null ? string.Empty : $" ({runtime.Version})")}."
            : "Ollama is not installed.";

        IReadOnlyList<InstalledLocalModel> installed;
        try
        {
            installed = await _installer.ListInstalledAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            installed = [];
        }

        var installedIds = new HashSet<string>(installed.Select(m => m.ModelId), StringComparer.Ordinal);
        foreach (LocalModelItemViewModel item in Items)
        {
            if (item.State == LocalModelInstallState.Downloading)
            {
                continue;
            }

            item.State = installedIds.Contains(item.Model.ModelId)
                ? LocalModelInstallState.Installed
                : LocalModelInstallState.NotInstalled;
        }

        await LoadVerdictsAsync().ConfigureAwait(true);
    }

    public async Task EnsureRuntimeAsync()
    {
        RuntimeStatusText = "Installing Ollama.";
        Result<LocalRuntimeInfo> result = await _runtime.EnsureInstalledAsync().ConfigureAwait(true);
        if (result.IsSuccess)
        {
            RuntimeStatusText = $"Ollama ready{(result.Value!.Version is null ? string.Empty : $" ({result.Value.Version})")}.";
        }
        else
        {
            RuntimeStatusText = result.Error!.Message;
            _notifications?.ShowNotification("Ollama", result.Error.Message, NotificationSeverity.Error);
        }
    }

    public async Task EnsureEmbeddingAsync()
    {
        EmbeddingStateText = "Downloading embedding.";
        Result<EmbeddingInstallResult> result = await _installer.EnsureEmbeddingAsync().ConfigureAwait(true);
        if (result.IsSuccess)
        {
            EmbeddingStateText = result.Value!.AlreadyInstalled
                ? "Embedding already installed."
                : $"Embedding installed at {result.Value.InstallPath}.";
        }
        else
        {
            EmbeddingStateText = result.Error!.Message;
        }
    }

    public async Task InstallModelAsync(LocalModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_verdicts.TryGetValue(item.Model.Id, out HardwareFitVerdict verdict) &&
            verdict == HardwareFitVerdict.InsufficientResources &&
            !item.RiskConfirmed)
        {
            item.NeedsRiskConfirmation = true;
            item.StatusMessage = "Low memory verdict: confirm the risk of swapping and freezes before install.";
            return;
        }

        item.NeedsRiskConfirmation = false;
        var cts = new CancellationTokenSource();
        _activeInstalls[item.Model.Id] = cts;
        item.State = LocalModelInstallState.Downloading;
        item.ProgressPercent = 0;
        item.ProgressText = "Starting download.";

        var progress = new Progress<ModelInstallProgress>(p =>
        {
            item.ProgressPercent = p.Percent;
            item.ProgressText = p.TotalBytes.HasValue
                ? $"{p.Stage}: {p.Percent}%"
                : $"{p.Stage}: {p.Percent}%";
        });

        try
        {
            Result<ModelProbeResult> result =
                await _installer.InstallAsync(item.Model, progress, cts.Token).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                item.State = LocalModelInstallState.Installed;
                item.StatusMessage = "Installed and verified.";
                item.ProgressText = null;
            }
            else if (string.Equals(result.Error!.Code, "Model.InstallCancelled", StringComparison.Ordinal))
            {
                item.State = LocalModelInstallState.NotInstalled;
                item.StatusMessage = "Install cancelled.";
            }
            else
            {
                item.State = LocalModelInstallState.Error;
                item.StatusMessage = result.Error.Message;
            }
        }
        catch (OperationCanceledException)
        {
            item.State = LocalModelInstallState.NotInstalled;
            item.StatusMessage = "Install cancelled.";
        }
        finally
        {
            _activeInstalls.Remove(item.Model.Id);
            cts.Dispose();
        }
    }

    public void CancelInstall(LocalModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_activeInstalls.TryGetValue(item.Model.Id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
    }

    public async Task RemoveModelAsync(LocalModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Result result = await _installer.RemoveAsync(item.Model.ModelId).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            item.State = LocalModelInstallState.NotInstalled;
            item.StatusMessage = "Removed.";
        }
        else
        {
            item.State = LocalModelInstallState.Error;
            item.StatusMessage = result.Error!.Message;
        }
    }

    public async Task VerifyModelAsync(LocalModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Result<ModelProbeResult> result = await _installer.VerifyAsync(item.Model).ConfigureAwait(true);
        item.StatusMessage = result.IsSuccess
            ? "Probe succeeded."
            : result.Error!.Message;
        if (!result.IsSuccess)
        {
            item.State = LocalModelInstallState.Error;
        }
    }

    private async Task LoadVerdictsAsync()
    {
        if (_advisor is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<ModelCompatibilityEvaluation> evaluations =
                await _advisor.EvaluateLocalModelsAsync().ConfigureAwait(true);
            _verdicts = evaluations.ToDictionary(e => e.Model.Id, e => e.Verdict, StringComparer.Ordinal);
            foreach (LocalModelItemViewModel item in Items)
            {
                if (_verdicts.TryGetValue(item.Model.Id, out HardwareFitVerdict verdict))
                {
                    item.VerdictText = verdict switch
                    {
                        HardwareFitVerdict.Optimal => "Optimal for your PC",
                        HardwareFitVerdict.Compatible => "Compatible",
                        HardwareFitVerdict.InsufficientResources => "Low memory",
                        _ => null,
                    };
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
