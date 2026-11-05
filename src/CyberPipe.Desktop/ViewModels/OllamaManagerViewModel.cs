using System.Collections.ObjectModel;
using System.Globalization;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

/// <summary>Модель, которая сейчас занимает RAM/VRAM рантайма Ollama.</summary>
public sealed class RunningOllamaModelItemViewModel : ViewModelBase
{
    public required string ModelId { get; init; }

    public long SizeBytes { get; init; }

    public required string SizeFormatted { get; init; }

    public AsyncRelayCommand? UnloadCommand { get; set; }
}

/// <summary>Модель, реально установленная в Ollama (<c>GET /api/tags</c>).</summary>
public sealed class InstalledOllamaModelItemViewModel : ViewModelBase
{
    public required string ModelId { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public string? VerdictText { get; init; }

    public required string MetaText { get; init; }

    public string? ParameterSize { get; init; }

    public string? QuantizationLevel { get; init; }

    public bool IsCatalogModel { get; init; }

    public RelayCommand? AssignToFastAssistantCommand { get; set; }

    public RelayCommand? AssignToSmartExpertCommand { get; set; }

    public RelayCommand? TestCommand { get; set; }

    public AsyncRelayCommand? VerifyCommand { get; set; }

    public AsyncRelayCommand? RemoveCommand { get; set; }
}

/// <summary>Карточка курируемой модели из локального каталога.</summary>
public sealed class RecommendedOllamaModelItemViewModel : ViewModelBase
{
    private LocalModelInstallState _state;
    private int _progressPercent;
    private string? _progressText;
    private string? _statusMessage;
    private bool _needsRiskConfirmation;
    private HardwareFitVerdict? _verdict;
    private string _verdictText = string.Empty;
    private string _explanation = string.Empty;
    private double _requiredMemoryGb;

    public required ModelDescriptorDto Model { get; init; }

    public bool RiskConfirmed { get; set; }

    public string DisplayName => Model.Name;

    public string SizeText => Model.Q4SizeGb is double size
        ? $"{size.ToString("0.0", CultureInfo.InvariantCulture)} ГБ"
        : "размер не указан";

    public HardwareFitVerdict? Verdict
    {
        get => _verdict;
        private set => SetProperty(ref _verdict, value);
    }

    public string VerdictText
    {
        get => _verdictText;
        private set => SetProperty(ref _verdictText, value);
    }

    public string Explanation
    {
        get => _explanation;
        private set => SetProperty(ref _explanation, value);
    }

    public double RequiredMemoryGb
    {
        get => _requiredMemoryGb;
        private set => SetProperty(ref _requiredMemoryGb, value);
    }

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
                CancelCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInstalled => State == LocalModelInstallState.Installed;

    public bool IsDownloading => State == LocalModelInstallState.Downloading;

    public bool CanInstall => State != LocalModelInstallState.Downloading && State != LocalModelInstallState.Installed;

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

    public AsyncRelayCommand? InstallCommand { get; internal set; }

    public RelayCommand? CancelCommand { get; internal set; }

    public RelayCommand? ConfirmRiskCommand { get; internal set; }

    internal void ApplyEvaluation(ModelCompatibilityEvaluation? evaluation)
    {
        Verdict = evaluation?.Verdict;
        RequiredMemoryGb = evaluation?.RequiredMemoryGb ?? Model.MinRamGb ?? 0;
        Explanation = evaluation?.Explanation ?? string.Empty;
        VerdictText = evaluation?.Verdict switch
        {
            HardwareFitVerdict.Optimal => "🟢 Оптимально",
            HardwareFitVerdict.Compatible => "🟡 Совместимо",
            HardwareFitVerdict.InsufficientResources => "🔴 Мало памяти",
            _ => string.Empty,
        };
    }
}

/// <summary>
/// Окно-враппер локального рантайма Ollama: статус демона, память, установленные модели,
/// витрина под ПК и скачивание произвольного тега.
/// </summary>
public sealed class OllamaManagerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ModelDescriptorDto> _localCatalog;
    private readonly ILocalModelInstaller _installer;
    private readonly ILocalRuntimeInstaller _runtime;
    private readonly IModelCompatibilityAdvisor? _advisor;
    private readonly IDesktopNotificationService? _notifications;
    private readonly List<RecommendedOllamaModelItemViewModel> _recommended = [];
    private readonly Dictionary<string, CancellationTokenSource> _activeInstalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HardwareFitVerdict> _verdicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private string _runtimeStatusText = "⚪ Статус Ollama неизвестен";
    private bool _isRuntimeReady;
    private string _apiBaseUrl = "http://localhost:11434";
    private string _hardwareSummaryText = "Характеристики ПК недоступны.";
    private string _memoryStatusText = "Память свободна (нет загруженных моделей)";
    private string _installedTabHeader = "📦 Установленные модели (0)";
    private string _selectedFilter = "all";
    private string _searchQuery = string.Empty;
    private string _customPullTag = string.Empty;
    private bool _isCustomPulling;
    private int _customPullProgressPercent;
    private string? _customPullProgressText;
    private string _customPullStatusMessage = string.Empty;
    private int _selectedTabIndex;
    private string _embeddingStateText = "Эмбеддинг не установлен.";
    private bool _isEmbeddingDownloading;
    private bool _isEmbeddingInstalled;
    private int _embeddingProgressPercent;
    private string? _embeddingProgressText;
    private bool _embeddingNeedsRemoveConfirmation;
    private CancellationTokenSource? _customPullCts;
    private CancellationTokenSource? _embeddingCts;

    public OllamaManagerViewModel(
        IReadOnlyList<ModelDescriptorDto> localCatalog,
        ILocalModelInstaller installer,
        ILocalRuntimeInstaller runtime,
        IModelCompatibilityAdvisor? advisor = null,
        IDesktopNotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(localCatalog);
        _localCatalog = localCatalog;
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _advisor = advisor;
        _notifications = notifications;

        foreach (ModelDescriptorDto model in localCatalog)
        {
            var item = new RecommendedOllamaModelItemViewModel { Model = model };
            item.InstallCommand = new AsyncRelayCommand(() => InstallRecommendedAsync(item), () => item.CanInstall);
            item.CancelCommand = new RelayCommand(() => CancelRecommendedInstall(item), () => item.IsDownloading);
            item.ConfirmRiskCommand = new RelayCommand(() =>
            {
                item.RiskConfirmed = true;
                item.NeedsRiskConfirmation = false;
                _ = InstallRecommendedAsync(item);
            });
            _recommended.Add(item);
        }

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        EnsureRuntimeCommand = new AsyncRelayCommand(EnsureRuntimeAsync);
        SetFilterCommand = new RelayCommand(param =>
        {
            if (param is string filter && !string.IsNullOrWhiteSpace(filter))
            {
                SelectedFilter = filter;
            }
        });
        UsePopularTagCommand = new RelayCommand(param =>
        {
            if (param is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                CustomPullTag = tag;
            }
        });
        PullCustomTagCommand = new AsyncRelayCommand(PullCustomTagAsync, () => CanPullCustomTag);
        CancelCustomPullCommand = new RelayCommand(CancelCustomPull, () => IsCustomPulling);
        GoToRecommendedCommand = new RelayCommand(() => SelectedTabIndex = 1);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        EnsureEmbeddingCommand = new AsyncRelayCommand(EnsureEmbeddingAsync, () => CanDownloadEmbedding);
        CancelEmbeddingCommand = new RelayCommand(CancelEmbedding, () => IsEmbeddingDownloading);
        RequestRemoveEmbeddingCommand = new RelayCommand(() => EmbeddingNeedsRemoveConfirmation = true, () => IsEmbeddingInstalled && !IsEmbeddingDownloading);
        ConfirmRemoveEmbeddingCommand = new AsyncRelayCommand(ConfirmRemoveEmbeddingAsync);
        CancelRemoveEmbeddingCommand = new RelayCommand(() => EmbeddingNeedsRemoveConfirmation = false);

        ApplyRecommendedFilter();
        _ = RefreshAsync();
    }

    public ObservableCollection<RunningOllamaModelItemViewModel> RunningModels { get; } = [];

    public ObservableCollection<InstalledOllamaModelItemViewModel> InstalledModels { get; } = [];

    public ObservableCollection<RecommendedOllamaModelItemViewModel> FilteredRecommendedModels { get; } = [];

    public IReadOnlyList<string> PopularPullTags { get; } =
        ["qwen2.5-coder:7b", "llama3.2:3b", "deepseek-r1:7b", "nomic-embed-text"];

    public bool HasRunningModels => RunningModels.Count > 0;

    public bool HasInstalledModels => InstalledModels.Count > 0;

    public string RuntimeStatusText
    {
        get => _runtimeStatusText;
        private set => SetProperty(ref _runtimeStatusText, value);
    }

    public bool IsRuntimeReady
    {
        get => _isRuntimeReady;
        private set => SetProperty(ref _isRuntimeReady, value);
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        private set => SetProperty(ref _apiBaseUrl, value);
    }

    public string HardwareSummaryText
    {
        get => _hardwareSummaryText;
        private set => SetProperty(ref _hardwareSummaryText, value);
    }

    public string MemoryStatusText
    {
        get => _memoryStatusText;
        private set => SetProperty(ref _memoryStatusText, value);
    }

    public string InstalledTabHeader
    {
        get => _installedTabHeader;
        private set => SetProperty(ref _installedTabHeader, value);
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant()))
            {
                ApplyRecommendedFilter();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyRecommendedFilter();
            }
        }
    }

    public string CustomPullTag
    {
        get => _customPullTag;
        set
        {
            if (SetProperty(ref _customPullTag, value))
            {
                OnPropertyChanged(nameof(CanPullCustomTag));
                PullCustomTagCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCustomPulling
    {
        get => _isCustomPulling;
        private set
        {
            if (SetProperty(ref _isCustomPulling, value))
            {
                OnPropertyChanged(nameof(CanPullCustomTag));
                PullCustomTagCommand.RaiseCanExecuteChanged();
                CancelCustomPullCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanPullCustomTag => !IsCustomPulling && !string.IsNullOrWhiteSpace(CustomPullTag);

    public int CustomPullProgressPercent
    {
        get => _customPullProgressPercent;
        private set => SetProperty(ref _customPullProgressPercent, value);
    }

    public string? CustomPullProgressText
    {
        get => _customPullProgressText;
        private set => SetProperty(ref _customPullProgressText, value);
    }

    public string CustomPullStatusMessage
    {
        get => _customPullStatusMessage;
        private set => SetProperty(ref _customPullStatusMessage, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
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
                EnsureEmbeddingCommand.RaiseCanExecuteChanged();
                CancelEmbeddingCommand.RaiseCanExecuteChanged();
                RequestRemoveEmbeddingCommand.RaiseCanExecuteChanged();
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
                EnsureEmbeddingCommand.RaiseCanExecuteChanged();
                RequestRemoveEmbeddingCommand.RaiseCanExecuteChanged();
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

    public Action<IntentTier, string>? OnAssignModelToTier { get; set; }

    public Action<string, string?>? OnTestModelRequested { get; set; }

    public Action? OnInstalledModelsChanged { get; set; }

    public Action? RequestClose { get; set; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand EnsureRuntimeCommand { get; }

    public RelayCommand SetFilterCommand { get; }

    public RelayCommand UsePopularTagCommand { get; }

    public AsyncRelayCommand PullCustomTagCommand { get; }

    public RelayCommand CancelCustomPullCommand { get; }

    public RelayCommand GoToRecommendedCommand { get; }

    public RelayCommand CloseCommand { get; }

    public AsyncRelayCommand EnsureEmbeddingCommand { get; }

    public RelayCommand CancelEmbeddingCommand { get; }

    public RelayCommand RequestRemoveEmbeddingCommand { get; }

    public AsyncRelayCommand ConfirmRemoveEmbeddingCommand { get; }

    public RelayCommand CancelRemoveEmbeddingCommand { get; }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await RefreshCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task EnsureRuntimeAsync()
    {
        RuntimeStatusText = "Установка и проверка Ollama...";
        Result<LocalRuntimeInfo> result = await _runtime.EnsureInstalledAsync().ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            RuntimeStatusText = result.Error?.Message ?? "Не удалось подготовить Ollama.";
            _notifications?.ShowNotification("Ollama", RuntimeStatusText, NotificationSeverity.Error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task UnloadModelAsync(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        Result result = await _installer.UnloadAsync(modelId).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _notifications?.ShowNotification(
                "Ollama",
                result.Error?.Message ?? $"Не удалось выгрузить {modelId}.",
                NotificationSeverity.Error);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task PullCustomTagAsync()
    {
        if (IsCustomPulling || string.IsNullOrWhiteSpace(CustomPullTag))
        {
            return;
        }

        string tag = CustomPullTag.Trim();
        _customPullCts?.Dispose();
        _customPullCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _customPullCts.Token;
        IsCustomPulling = true;
        CustomPullProgressPercent = 0;
        CustomPullProgressText = "Начало скачивания...";
        CustomPullStatusMessage = $"Скачивание {tag}...";

        var progress = new Progress<ModelInstallProgress>(update =>
        {
            CustomPullProgressPercent = update.Percent;
            CustomPullProgressText = update.TotalBytes is > 0
                ? $"{update.Stage}: {update.Percent}% ({FormatMegabytes(update.DownloadedBytes)} / {FormatMegabytes(update.TotalBytes.Value)} МБ)"
                : $"{update.Stage}: {update.Percent}%";
        });

        try
        {
            Result<ModelProbeResult> result = await _installer.PullByTagAsync(tag, progress, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                CustomPullProgressPercent = 100;
                CustomPullStatusMessage = $"Модель {tag} установлена и проверена.";
                await RefreshAsync().ConfigureAwait(true);
                OnInstalledModelsChanged?.Invoke();
                _notifications?.ShowNotification("Ollama", CustomPullStatusMessage, NotificationSeverity.Success);
            }
            else if (string.Equals(result.Error?.Code, "Model.InstallCancelled", StringComparison.Ordinal))
            {
                CustomPullStatusMessage = "Скачивание отменено.";
            }
            else
            {
                CustomPullStatusMessage = result.Error?.Message ?? "Не удалось скачать модель.";
                _notifications?.ShowNotification("Ollama", CustomPullStatusMessage, NotificationSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            CustomPullStatusMessage = "Скачивание отменено.";
            CustomPullProgressPercent = 0;
        }
        finally
        {
            IsCustomPulling = false;
        }
    }

    public void CancelCustomPull()
    {
        if (IsCustomPulling)
        {
            CustomPullProgressText = "Отмена скачивания...";
            _customPullCts?.Cancel();
        }
    }

    public async Task InstallRecommendedAsync(RecommendedOllamaModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_verdicts.TryGetValue(item.Model.Id, out HardwareFitVerdict verdict) &&
            verdict == HardwareFitVerdict.InsufficientResources &&
            !item.RiskConfirmed)
        {
            item.NeedsRiskConfirmation = true;
            item.StatusMessage = "Мало памяти: подтвердите риск свопинга перед установкой.";
            return;
        }

        item.NeedsRiskConfirmation = false;
        var cts = new CancellationTokenSource();
        _activeInstalls[item.Model.Id] = cts;
        item.State = LocalModelInstallState.Downloading;
        item.ProgressPercent = 0;
        item.ProgressText = "Начало скачивания.";

        var progress = new Progress<ModelInstallProgress>(update =>
        {
            item.ProgressPercent = update.Percent;
            item.ProgressText = $"{update.Stage}: {update.Percent}%";
        });

        try
        {
            Result<ModelProbeResult> result = await _installer.InstallAsync(item.Model, progress, cts.Token).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                item.State = LocalModelInstallState.Installed;
                item.StatusMessage = "Установлена и проверена.";
                item.ProgressText = null;
                await RefreshAsync().ConfigureAwait(true);
                OnInstalledModelsChanged?.Invoke();
            }
            else if (string.Equals(result.Error?.Code, "Model.InstallCancelled", StringComparison.Ordinal))
            {
                item.State = LocalModelInstallState.NotInstalled;
                item.StatusMessage = "Установка отменена.";
            }
            else
            {
                item.State = LocalModelInstallState.Error;
                item.StatusMessage = result.Error?.Message ?? "Не удалось установить модель.";
            }
        }
        catch (OperationCanceledException)
        {
            item.State = LocalModelInstallState.NotInstalled;
            item.StatusMessage = "Установка отменена.";
        }
        finally
        {
            _activeInstalls.Remove(item.Model.Id);
            cts.Dispose();
        }
    }

    public void CancelRecommendedInstall(RecommendedOllamaModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_activeInstalls.TryGetValue(item.Model.Id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
    }

    public async Task RemoveInstalledAsync(InstalledOllamaModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Result result = await _installer.RemoveAsync(item.ModelId).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _notifications?.ShowNotification(
                "Ollama",
                result.Error?.Message ?? $"Не удалось удалить {item.ModelId}.",
                NotificationSeverity.Error);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
        OnInstalledModelsChanged?.Invoke();
    }

    public async Task VerifyInstalledAsync(InstalledOllamaModelItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ModelDescriptorDto descriptor = FindCatalogModel(item.ModelId) ?? SyntheticDescriptor(item.ModelId);
        Result<ModelProbeResult> result = await _installer.VerifyAsync(descriptor).ConfigureAwait(true);
        _notifications?.ShowNotification(
            "Ollama",
            result.IsSuccess ? $"Проба {item.ModelId} прошла успешно." : result.Error?.Message ?? "Проба не удалась.",
            result.IsSuccess ? NotificationSeverity.Success : NotificationSeverity.Error);
    }

    public async Task EnsureEmbeddingAsync()
    {
        if (IsEmbeddingDownloading)
        {
            return;
        }

        _embeddingCts?.Dispose();
        _embeddingCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _embeddingCts.Token;
        IsEmbeddingDownloading = true;
        EmbeddingProgressPercent = 0;
        EmbeddingProgressText = "Подготовка к скачиванию...";
        EmbeddingStateText = "Скачивание multilingual-e5-small...";

        var progress = new Progress<ModelInstallProgress>(update =>
        {
            EmbeddingProgressPercent = update.Percent;
            EmbeddingProgressText = update.TotalBytes is > 0
                ? $"{update.Percent}% ({FormatMegabytes(update.DownloadedBytes)} / {FormatMegabytes(update.TotalBytes.Value)} МБ)"
                : $"{update.Percent}%";
        });

        try
        {
            Result<EmbeddingInstallResult> result = await _installer.EnsureEmbeddingAsync(progress, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                IsEmbeddingInstalled = true;
                EmbeddingProgressPercent = 100;
                EmbeddingStateText = result.Value!.AlreadyInstalled
                    ? "Модуль умного поиска уже установлен."
                    : "Модуль умного поиска multilingual-e5-small установлен.";
                _notifications?.ShowNotification("Поиск", EmbeddingStateText, NotificationSeverity.Success);
            }
            else
            {
                EmbeddingStateText = result.Error?.Message ?? "Не удалось скачать модуль поиска.";
                _notifications?.ShowNotification("Поиск", EmbeddingStateText, NotificationSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            EmbeddingStateText = "Загрузка эмбеддинга отменена.";
            EmbeddingProgressPercent = 0;
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

    public async Task ConfirmRemoveEmbeddingAsync()
    {
        EmbeddingNeedsRemoveConfirmation = false;
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyberPipe",
                "embeddings",
                "multilingual-e5-small");
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            IsEmbeddingInstalled = false;
            EmbeddingProgressPercent = 0;
            EmbeddingProgressText = null;
            EmbeddingStateText = "Модуль умного поиска удалён с диска.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EmbeddingStateText = $"Не удалось удалить модуль поиска: {ex.Message}";
            _notifications?.ShowNotification("Поиск", EmbeddingStateText, NotificationSeverity.Warning);
        }
    }

    private async Task RefreshCoreAsync()
    {
        LocalRuntimeInfo runtime = await _runtime.GetStatusAsync().ConfigureAwait(true);
        IsRuntimeReady = runtime.State == LocalRuntimeState.Ready;
        ApiBaseUrl = runtime.ApiBaseUrl;
        RuntimeStatusText = IsRuntimeReady
            ? $"🟢 Ollama запущена{(string.IsNullOrWhiteSpace(runtime.Version) ? string.Empty : $" (v{runtime.Version})")} • {runtime.ApiBaseUrl}"
            : "⚪ Ollama не запущена";

        if (_advisor is not null)
        {
            try
            {
                HostHardwareProfile profile = await _advisor.GetHardwareProfileAsync().ConfigureAwait(true);
                HardwareSummaryText = FormatHardware(profile);
            }
            catch (Exception)
            {
                HardwareSummaryText = "Характеристики ПК недоступны.";
            }
        }

        IReadOnlyList<OllamaRunningModelDto> running;
        try
        {
            running = await _installer.ListRunningAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            running = [];
        }

        RunningModels.Clear();
        foreach (OllamaRunningModelDto model in running)
        {
            var item = new RunningOllamaModelItemViewModel
            {
                ModelId = model.ModelId,
                SizeBytes = model.SizeBytes,
                SizeFormatted = $"{FormatGigabytes(model.SizeBytes)} в памяти (VRAM: {FormatGigabytes(model.SizeVramBytes)})",
            };
            item.UnloadCommand = new AsyncRelayCommand(() => UnloadModelAsync(item.ModelId));
            RunningModels.Add(item);
        }

        OnPropertyChanged(nameof(HasRunningModels));
        MemoryStatusText = RunningModels.Count == 0
            ? "Память свободна (нет загруженных моделей)"
            : $"В памяти сейчас: {RunningModels.Count} {ModelsWord(RunningModels.Count)} ({FormatGigabytes(RunningModels.Sum(item => item.SizeBytes))})";

        IReadOnlyList<InstalledLocalModel> installed;
        try
        {
            installed = await _installer.ListInstalledAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            installed = [];
        }

        await LoadVerdictsAsync().ConfigureAwait(true);

        InstalledModels.Clear();
        foreach (InstalledLocalModel model in installed)
        {
            ModelDescriptorDto? catalogModel = FindCatalogModel(model.ModelId);
            string? verdict = null;
            if (catalogModel is not null && _verdicts.TryGetValue(catalogModel.Id, out HardwareFitVerdict fit))
            {
                verdict = VerdictLabel(fit);
            }

            var item = new InstalledOllamaModelItemViewModel
            {
                ModelId = model.ModelId,
                DisplayName = catalogModel?.Name ?? model.ModelId,
                Description = catalogModel?.BestFor ?? "Модель установлена в Ollama вне стандартного каталога.",
                VerdictText = verdict,
                MetaText = BuildMeta(model),
                ParameterSize = model.ParameterSize,
                QuantizationLevel = model.QuantizationLevel,
                IsCatalogModel = catalogModel is not null,
            };
            item.AssignToFastAssistantCommand = new RelayCommand(() => OnAssignModelToTier?.Invoke(IntentTier.Tier2, item.ModelId));
            item.AssignToSmartExpertCommand = new RelayCommand(() => OnAssignModelToTier?.Invoke(IntentTier.Tier3, item.ModelId));
            item.TestCommand = new RelayCommand(() => OnTestModelRequested?.Invoke(item.ModelId, item.DisplayName));
            item.VerifyCommand = new AsyncRelayCommand(() => VerifyInstalledAsync(item));
            item.RemoveCommand = new AsyncRelayCommand(() => RemoveInstalledAsync(item));
            InstalledModels.Add(item);
        }

        InstalledTabHeader = $"📦 Установленные модели ({InstalledModels.Count})";
        OnPropertyChanged(nameof(HasInstalledModels));

        var installedIds = installed.Select(model => model.ModelId).ToList();
        foreach (RecommendedOllamaModelItemViewModel item in _recommended)
        {
            item.ApplyEvaluation(null);
            if (item.State == LocalModelInstallState.Downloading)
            {
                continue;
            }

            bool present = installedIds.Any(id => SameModel(id, item.Model.ModelId) || SameModel(id, item.Model.Id));
            item.State = present ? LocalModelInstallState.Installed : LocalModelInstallState.NotInstalled;
        }

        if (_advisor is not null)
        {
            try
            {
                IReadOnlyList<ModelCompatibilityEvaluation> evaluations =
                    await _advisor.EvaluateLocalModelsAsync().ConfigureAwait(true);
                foreach (RecommendedOllamaModelItemViewModel item in _recommended)
                {
                    ModelCompatibilityEvaluation? evaluation = evaluations.FirstOrDefault(candidate =>
                        string.Equals(candidate.Model.Id, item.Model.Id, StringComparison.OrdinalIgnoreCase) ||
                        SameModel(candidate.Model.ModelId, item.Model.ModelId));
                    item.ApplyEvaluation(evaluation);
                }
            }
            catch (Exception)
            {
            }
        }

        _recommended.Sort(static (left, right) =>
        {
            int rank = VerdictRank(left.Verdict).CompareTo(VerdictRank(right.Verdict));
            return rank != 0 ? rank : left.RequiredMemoryGb.CompareTo(right.RequiredMemoryGb);
        });
        ApplyRecommendedFilter();
        CheckEmbeddingInstalledState();
    }

    private async Task LoadVerdictsAsync()
    {
        _verdicts.Clear();
        if (_advisor is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<ModelCompatibilityEvaluation> evaluations =
                await _advisor.EvaluateLocalModelsAsync().ConfigureAwait(true);
            foreach (ModelCompatibilityEvaluation evaluation in evaluations)
            {
                _verdicts[evaluation.Model.Id] = evaluation.Verdict;
            }
        }
        catch (Exception)
        {
        }
    }

    private void ApplyRecommendedFilter()
    {
        IEnumerable<RecommendedOllamaModelItemViewModel> query = _recommended;
        query = SelectedFilter switch
        {
            "optimal" => query.Where(item => item.Verdict == HardwareFitVerdict.Optimal),
            "tier2" => query.Where(item => item.Model.RecommendedFor == ModelTaskCategory.LightFastTier2),
            "tier3" => query.Where(item => item.Model.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string term = SearchQuery.Trim();
            query = query.Where(item =>
                item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Model.ModelId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Model.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Model.BestFor.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        FilteredRecommendedModels.Clear();
        foreach (RecommendedOllamaModelItemViewModel item in query)
        {
            FilteredRecommendedModels.Add(item);
        }
    }

    private ModelDescriptorDto? FindCatalogModel(string modelId) =>
        _localCatalog.FirstOrDefault(model =>
            SameModel(model.ModelId, modelId) || SameModel(model.Id, modelId));

    private static ModelDescriptorDto SyntheticDescriptor(string modelId) =>
        new()
        {
            Id = modelId,
            Name = modelId,
            HostingType = ModelHostingType.Local,
            RecommendedFor = ModelTaskCategory.HeavyReasoningTier3,
            Provider = "Ollama",
            ModelId = modelId,
            ParamsSummary = modelId,
            DefaultContextTokens = 8192,
            LicenseOrPricing = "Ollama library",
            BestFor = modelId,
        };

    private void CheckEmbeddingInstalledState()
    {
        string sentinel = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberPipe",
            "embeddings",
            "multilingual-e5-small",
            "model.onnx");
        if (File.Exists(sentinel) && new FileInfo(sentinel).Length > 0)
        {
            IsEmbeddingInstalled = true;
            if (!IsEmbeddingDownloading)
            {
                double megabytes = new FileInfo(sentinel).Length / (1024d * 1024d);
                EmbeddingStateText = $"Модуль умного поиска установлен ({megabytes.ToString("0.0", CultureInfo.InvariantCulture)} МБ).";
            }
        }
        else if (!IsEmbeddingDownloading)
        {
            IsEmbeddingInstalled = false;
            EmbeddingStateText = "Модуль умного поиска не установлен.";
        }
    }

    private static string BuildMeta(InstalledLocalModel model)
    {
        var parts = new List<string> { FormatGigabytes(model.SizeBytes ?? 0) };
        if (!string.IsNullOrWhiteSpace(model.ParameterSize))
        {
            parts.Add(model.ParameterSize);
        }

        if (!string.IsNullOrWhiteSpace(model.QuantizationLevel))
        {
            parts.Add(model.QuantizationLevel);
        }

        if (!string.IsNullOrWhiteSpace(model.Family))
        {
            parts.Add(model.Family);
        }

        return string.Join(" • ", parts);
    }

    private static string FormatHardware(HostHardwareProfile profile)
    {
        var parts = new List<string>();
        if (profile.TotalRamGb is double total)
        {
            string free = profile.AvailableRamGb is double available
                ? $" (свободно {available.ToString("0.0", CultureInfo.InvariantCulture)} ГБ)"
                : string.Empty;
            parts.Add($"💻 ОЗУ: {total.ToString("0.0", CultureInfo.InvariantCulture)} ГБ{free}");
        }

        if (profile.FreeDiskSpaceGb is double disk)
        {
            parts.Add($"Свободно на диске: {disk.ToString("0.0", CultureInfo.InvariantCulture)} ГБ");
        }

        return parts.Count == 0 ? "Характеристики ПК недоступны." : string.Join(" • ", parts);
    }

    private static string FormatGigabytes(long bytes) =>
        (bytes / 1024d / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " ГБ";

    private static string FormatMegabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);

    private static string VerdictLabel(HardwareFitVerdict verdict) => verdict switch
    {
        HardwareFitVerdict.Optimal => "🟢 Оптимально",
        HardwareFitVerdict.Compatible => "🟡 Совместимо",
        HardwareFitVerdict.InsufficientResources => "🔴 Мало памяти",
        _ => string.Empty,
    };

    private static int VerdictRank(HardwareFitVerdict? verdict) => verdict switch
    {
        HardwareFitVerdict.Optimal => 0,
        HardwareFitVerdict.InsufficientResources => 2,
        _ => 1,
    };

    private static string ModelsWord(int count)
    {
        int mod100 = Math.Abs(count) % 100;
        int mod10 = mod100 % 10;
        if (mod100 is > 10 and < 20)
        {
            return "моделей";
        }

        if (mod10 == 1)
        {
            return "модель";
        }

        return mod10 is >= 2 and <= 4 ? "модели" : "моделей";
    }

    private static bool SameModel(string left, string right) =>
        string.Equals(StripLatest(left), StripLatest(right), StringComparison.OrdinalIgnoreCase);

    private static string StripLatest(string value)
    {
        const string suffix = ":latest";
        string trimmed = value.Trim();
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^suffix.Length]
            : trimmed;
    }
}
