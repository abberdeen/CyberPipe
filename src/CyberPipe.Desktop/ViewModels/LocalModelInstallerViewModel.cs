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
    public RelayCommand? AssignCommand { get; internal set; }
    public RelayCommand? TestCommand { get; internal set; }

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
    private bool _isEmbeddingDownloading;
    private bool _isEmbeddingInstalled;
    private int _embeddingProgressPercent;
    private string? _embeddingProgressText;
    private bool _embeddingNeedsRemoveConfirmation;
    private CancellationTokenSource? _embeddingCts;
    private Action<ModelDescriptorDto>? _onAssignModel;
    private Action<ModelDescriptorDto>? _onTestModel;
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
        EnsureEmbeddingCommand = new AsyncRelayCommand(EnsureEmbeddingAsync, () => CanDownloadEmbedding);
        CancelEmbeddingCommand = new RelayCommand(CancelEmbedding, () => IsEmbeddingDownloading);
        RequestRemoveEmbeddingCommand = new RelayCommand(RequestRemoveEmbedding, () => IsEmbeddingInstalled && !IsEmbeddingDownloading);
        ConfirmRemoveEmbeddingCommand = new AsyncRelayCommand(ConfirmRemoveEmbeddingAsync);
        CancelRemoveEmbeddingCommand = new RelayCommand(CancelRemoveEmbedding);

        CheckEmbeddingInstalledState();
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

    public bool IsEmbeddingDownloading
    {
        get => _isEmbeddingDownloading;
        private set
        {
            if (SetProperty(ref _isEmbeddingDownloading, value))
            {
                OnPropertyChanged(nameof(CanDownloadEmbedding));
                EnsureEmbeddingCommand?.RaiseCanExecuteChanged();
                CancelEmbeddingCommand?.RaiseCanExecuteChanged();
                RequestRemoveEmbeddingCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsEmbeddingInstalled
    {
        get => _isEmbeddingInstalled;
        private set
        {
            if (SetProperty(ref _isEmbeddingInstalled, value))
            {
                OnPropertyChanged(nameof(CanDownloadEmbedding));
                EnsureEmbeddingCommand?.RaiseCanExecuteChanged();
                RequestRemoveEmbeddingCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanDownloadEmbedding => !IsEmbeddingDownloading && !IsEmbeddingInstalled;

    public int EmbeddingProgressPercent
    {
        get => _embeddingProgressPercent;
        private set => SetProperty(ref _embeddingProgressPercent, value);
    }

    public string? EmbeddingProgressText
    {
        get => _embeddingProgressText;
        private set => SetProperty(ref _embeddingProgressText, value);
    }

    public bool EmbeddingNeedsRemoveConfirmation
    {
        get => _embeddingNeedsRemoveConfirmation;
        set => SetProperty(ref _embeddingNeedsRemoveConfirmation, value);
    }

    public Action<ModelDescriptorDto>? OnAssignModel
    {
        get => _onAssignModel;
        set
        {
            _onAssignModel = value;
            foreach (LocalModelItemViewModel item in Items)
            {
                item.AssignCommand = value != null ? new RelayCommand(() => value(item.Model)) : null;
            }
        }
    }

    public Action<ModelDescriptorDto>? OnTestModel
    {
        get => _onTestModel;
        set
        {
            _onTestModel = value;
            foreach (LocalModelItemViewModel item in Items)
            {
                item.TestCommand = value != null ? new RelayCommand(() => value(item.Model)) : null;
            }
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand EnsureRuntimeCommand { get; }

    public AsyncRelayCommand EnsureEmbeddingCommand { get; }

    public RelayCommand CancelEmbeddingCommand { get; }

    public RelayCommand RequestRemoveEmbeddingCommand { get; }

    public AsyncRelayCommand ConfirmRemoveEmbeddingCommand { get; }

    public RelayCommand CancelRemoveEmbeddingCommand { get; }

    public async Task RefreshAsync()
    {
        CheckEmbeddingInstalledState();

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
        if (IsEmbeddingDownloading)
        {
            return;
        }

        _embeddingCts?.Dispose();
        _embeddingCts = new CancellationTokenSource();
        CancellationToken ct = _embeddingCts.Token;

        IsEmbeddingDownloading = true;
        EmbeddingProgressPercent = 0;
        EmbeddingProgressText = "Подготовка к скачиванию...";
        EmbeddingStateText = "Скачивание модели эмбеддинга multilingual-e5-small...";

        var progress = new Progress<ModelInstallProgress>(p =>
        {
            EmbeddingProgressPercent = p.Percent;
            if (p.TotalBytes.HasValue && p.TotalBytes.Value > 0)
            {
                double mbDownloaded = p.DownloadedBytes / (1024.0 * 1024.0);
                double mbTotal = p.TotalBytes.Value / (1024.0 * 1024.0);
                EmbeddingProgressText = $"{p.ModelId}: {p.Percent}% ({mbDownloaded:F1} МБ из {mbTotal:F1} МБ)";
            }
            else
            {
                EmbeddingProgressText = $"{p.ModelId}: {p.Percent}%";
            }
        });

        try
        {
            Result<EmbeddingInstallResult> result =
                await _installer.EnsureEmbeddingAsync(progress, ct).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                IsEmbeddingInstalled = true;
                EmbeddingProgressPercent = 100;
                EmbeddingProgressText = "Загрузка завершена.";
                EmbeddingStateText = result.Value!.AlreadyInstalled
                    ? "Эмбеддинг уже установлен."
                    : $"Эмбеддинг успешно установлен в {result.Value.InstallPath}.";

                _notifications?.ShowNotification(
                    "Tier 1 Эмбеддинг",
                    "Модель multilingual-e5-small успешно установлена и готова к работе.",
                    NotificationSeverity.Success);
            }
            else
            {
                EmbeddingStateText = result.Error!.Message;
                _notifications?.ShowNotification(
                    "Ошибка загрузки",
                    result.Error.Message,
                    NotificationSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            EmbeddingStateText = "Загрузка эмбеддинга отменена.";
            EmbeddingProgressText = "Отменено пользователем.";
            EmbeddingProgressPercent = 0;

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string directory = Path.Combine(localAppData, "CyberPipe", "embeddings", "multilingual-e5-small");
                string sentinel = Path.Combine(directory, "model.onnx");
                if (Directory.Exists(directory) && (!File.Exists(sentinel) || new FileInfo(sentinel).Length == 0))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
            }

            _notifications?.ShowNotification(
                "Tier 1 Эмбеддинг",
                "Скачивание модели эмбеддинга отменено.",
                NotificationSeverity.Warning);
        }
        catch (Exception ex)
        {
            EmbeddingStateText = $"Ошибка: {ex.Message}";
            _notifications?.ShowNotification(
                "Ошибка загрузки",
                ex.Message,
                NotificationSeverity.Error);
        }
        finally
        {
            IsEmbeddingDownloading = false;
        }
    }

    public void CancelEmbedding()
    {
        if (IsEmbeddingDownloading)
        {
            EmbeddingProgressText = "Отмена загрузки...";
            _embeddingCts?.Cancel();
        }
    }

    public void RequestRemoveEmbedding()
    {
        EmbeddingNeedsRemoveConfirmation = true;
    }

    public void CancelRemoveEmbedding()
    {
        EmbeddingNeedsRemoveConfirmation = false;
    }

    public async Task ConfirmRemoveEmbeddingAsync()
    {
        EmbeddingNeedsRemoveConfirmation = false;
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(localAppData, "CyberPipe", "embeddings", "multilingual-e5-small");

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            IsEmbeddingInstalled = false;
            EmbeddingProgressPercent = 0;
            EmbeddingProgressText = null;
            EmbeddingStateText = "Эмбеддинг удалён с диска.";

            _notifications?.ShowNotification(
                "Tier 1 Эмбеддинг",
                "Файлы эмбеддинга multilingual-e5-small успешно удалены с диска.",
                NotificationSeverity.Info);
        }
        catch (IOException ex)
        {
            EmbeddingStateText = $"Не удалось удалить файлы (возможно, модель используется службой): {ex.Message}";
            _notifications?.ShowNotification(
                "Ошибка удаления",
                "Файлы эмбеддинга заблокированы процессом или службой. Остановите службу перед удалением.",
                NotificationSeverity.Warning);
        }
        catch (Exception ex)
        {
            EmbeddingStateText = $"Ошибка: {ex.Message}";
            _notifications?.ShowNotification(
                "Ошибка удаления",
                ex.Message,
                NotificationSeverity.Error);
        }
    }

    public void CheckEmbeddingInstalledState()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sentinel = Path.Combine(localAppData, "CyberPipe", "embeddings", "multilingual-e5-small", "model.onnx");
        if (File.Exists(sentinel) && new FileInfo(sentinel).Length > 0)
        {
            var fi = new FileInfo(sentinel);
            double mb = fi.Length / (1024.0 * 1024.0);
            IsEmbeddingInstalled = true;
            EmbeddingStateText = $"Эмбеддинг установлен на диске ({mb:F1} МБ).";
        }
        else
        {
            IsEmbeddingInstalled = false;
            if (!IsEmbeddingDownloading)
            {
                EmbeddingStateText = "Эмбеддинг не установлен.";
            }
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
