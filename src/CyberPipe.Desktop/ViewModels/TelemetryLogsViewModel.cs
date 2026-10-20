using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;
using CyberPipe.Logging;

namespace CyberPipe.Desktop.ViewModels;

public sealed class TelemetryLogsViewModel : ViewModelBase
{
    private readonly ITelemetryLogReaderService _reader;
    private readonly IClipboardService _clipboard;
    private readonly IDesktopNotificationService? _notifications;
    private readonly DispatcherTimer? _timer;

    private readonly List<OpenTelemetryLogEntry> _allEntries = [];
    private OpenTelemetryLogEntry? _selectedEntry;
    private string _selectedProcessFilter = "All Processes";
    private string _selectedLevelFilter = "All Levels";
    private string _searchText = string.Empty;
    private bool _openTelemetryOnly;
    private string? _activeTraceIdFilter;
    private bool _isLiveAutoRefresh = true;
    private bool _isLoading;
    private string _statusText = "Загрузка логов...";

    public TelemetryLogsViewModel(
        ITelemetryLogReaderService? reader = null,
        IClipboardService? clipboard = null,
        IDesktopNotificationService? notifications = null,
        bool enableTimer = true)
    {
        _reader = reader ?? new TelemetryLogReaderService();
        _clipboard = clipboard ?? AvaloniaClipboardService.Default;
        _notifications = notifications;

        RefreshCommand = new AsyncRelayCommand(() => LoadLogsAsync(isAutoRefresh: false));
        ClearCommand = new RelayCommand(Clear);
        ToggleLiveCommand = new RelayCommand(() => IsLiveAutoRefresh = !IsLiveAutoRefresh);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        FilterByTraceCommand = new RelayCommand(FilterBySelectedTrace, () => SelectedEntry?.HasTrace == true);
        ClearTraceFilterCommand = new RelayCommand(() => ActiveTraceIdFilter = null);
        CopyTraceIdCommand = new AsyncRelayCommand(CopyTraceIdAsync, () => SelectedEntry?.HasTrace == true);
        CopySelectedJsonCommand = new AsyncRelayCommand(CopySelectedJsonAsync, () => SelectedEntry != null);
        OpenInSeparateWindowCommand = new RelayCommand(() => OpenInSeparateWindowRequested?.Invoke());

        LogDirectoryPath = CyberPipeLogging.GetDefaultLogDirectory();

        if (enableTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (_, _) =>
            {
                if (IsLiveAutoRefresh && !IsLoading)
                {
                    await LoadLogsAsync(isAutoRefresh: true).ConfigureAwait(true);
                }
            };
            _timer.Start();
        }

        _ = LoadLogsAsync(isAutoRefresh: false);
    }

    public ObservableCollection<OpenTelemetryLogEntry> FilteredEntries { get; } = [];

    public IReadOnlyList<string> ProcessFilters { get; } =
        ["All Processes", "Service", "Brainstem", "Desktop", "Cli"];

    public IReadOnlyList<string> LevelFilters { get; } =
        ["All Levels", "Information+", "Warning+", "Error", "Debug"];

    public string LogDirectoryPath { get; }

    public Action? OpenInSeparateWindowRequested { get; set; }

    public OpenTelemetryLogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                FilterByTraceCommand.RaiseCanExecuteChanged();
                CopyTraceIdCommand.RaiseCanExecuteChanged();
                CopySelectedJsonCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedProcessFilter
    {
        get => _selectedProcessFilter;
        set
        {
            if (SetProperty(ref _selectedProcessFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedLevelFilter
    {
        get => _selectedLevelFilter;
        set
        {
            if (SetProperty(ref _selectedLevelFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OpenTelemetryOnly
    {
        get => _openTelemetryOnly;
        set
        {
            if (SetProperty(ref _openTelemetryOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    public string? ActiveTraceIdFilter
    {
        get => _activeTraceIdFilter;
        set
        {
            if (SetProperty(ref _activeTraceIdFilter, value))
            {
                OnPropertyChanged(nameof(HasActiveTraceFilter));
                ApplyFilters();
            }
        }
    }

    public bool HasActiveTraceFilter => !string.IsNullOrWhiteSpace(ActiveTraceIdFilter);

    public bool IsLiveAutoRefresh
    {
        get => _isLiveAutoRefresh;
        set => SetProperty(ref _isLiveAutoRefresh, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ToggleLiveCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand FilterByTraceCommand { get; }
    public RelayCommand ClearTraceFilterCommand { get; }
    public AsyncRelayCommand CopyTraceIdCommand { get; }
    public AsyncRelayCommand CopySelectedJsonCommand { get; }
    public RelayCommand OpenInSeparateWindowCommand { get; }

    public async Task LoadLogsAsync(bool isAutoRefresh = false)
    {
        if (IsLoading)
        {
            return;
        }

        if (!isAutoRefresh)
        {
            IsLoading = true;
        }

        try
        {
            IReadOnlyList<OpenTelemetryLogEntry> logs = await _reader.ReadLogsAsync(LogDirectoryPath).ConfigureAwait(true);

            _allEntries.Clear();
            _allEntries.AddRange(logs);

            ApplyFilters();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки логов: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplyFilters()
    {
        IEnumerable<OpenTelemetryLogEntry> query = _allEntries;

        if (!string.IsNullOrEmpty(SelectedProcessFilter) && SelectedProcessFilter != "All Processes")
        {
            query = query.Where(e => string.Equals(e.ProcessName, SelectedProcessFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SelectedLevelFilter) && SelectedLevelFilter != "All Levels")
        {
            query = SelectedLevelFilter switch
            {
                "Information+" => query.Where(e => !string.Equals(e.Level, "Debug", StringComparison.OrdinalIgnoreCase) && !string.Equals(e.Level, "Verbose", StringComparison.OrdinalIgnoreCase)),
                "Warning+" => query.Where(e => string.Equals(e.Level, "Warning", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Level, "Warn", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Level, "Fatal", StringComparison.OrdinalIgnoreCase)),
                "Error" => query.Where(e => string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Level, "Fatal", StringComparison.OrdinalIgnoreCase)),
                "Debug" => query.Where(e => string.Equals(e.Level, "Debug", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Level, "Verbose", StringComparison.OrdinalIgnoreCase)),
                _ => query,
            };
        }

        if (OpenTelemetryOnly)
        {
            query = query.Where(e => e.HasTrace);
        }

        if (!string.IsNullOrWhiteSpace(ActiveTraceIdFilter))
        {
            query = query.Where(e => string.Equals(e.TraceId, ActiveTraceIdFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string search = SearchText.Trim();
            query = query.Where(e =>
                (e.Message != null && e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (e.TraceId != null && e.TraceId.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (e.SpanId != null && e.SpanId.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (e.SourceContext != null && e.SourceContext.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (e.ProcessName != null && e.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (e.Exception != null && e.Exception.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var list = query.ToList();

        // Remember selected entry
        string? currentSelectedRaw = SelectedEntry?.RawJson;

        FilteredEntries.Clear();
        foreach (var entry in list)
        {
            FilteredEntries.Add(entry);
        }

        if (currentSelectedRaw != null)
        {
            SelectedEntry = FilteredEntries.FirstOrDefault(e => e.RawJson == currentSelectedRaw);
        }

        int traceCount = _allEntries.Count(e => e.HasTrace);
        StatusText = $"Показано {FilteredEntries.Count} из {_allEntries.Count} записей (с OpenTelemetry трейсами: {traceCount})";
    }

    public void Clear()
    {
        _allEntries.Clear();
        FilteredEntries.Clear();
        SelectedEntry = null;
        StatusText = "Логи очищены из представления.";
    }

    public void FilterBySelectedTrace()
    {
        if (SelectedEntry?.HasTrace == true)
        {
            ActiveTraceIdFilter = SelectedEntry.TraceId;
        }
    }

    public async Task CopyTraceIdAsync()
    {
        if (!string.IsNullOrEmpty(SelectedEntry?.TraceId))
        {
            await _clipboard.SetTextAsync(SelectedEntry.TraceId).ConfigureAwait(true);
            _notifications?.ShowNotification("OpenTelemetry", $"TraceId скопирован: {SelectedEntry.TraceId}", NotificationSeverity.Info);
        }
    }

    public async Task CopySelectedJsonAsync()
    {
        if (!string.IsNullOrEmpty(SelectedEntry?.RawJson))
        {
            await _clipboard.SetTextAsync(SelectedEntry.RawJson).ConfigureAwait(true);
            _notifications?.ShowNotification("Логи", "JSON лога скопирован в буфер обмена.", NotificationSeverity.Info);
        }
    }

    private void OpenFolder()
    {
        try
        {
            if (Directory.Exists(LogDirectoryPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LogDirectoryPath,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            _notifications?.ShowNotification("Логи", $"Не удалось открыть папку: {ex.Message}", NotificationSeverity.Warning);
        }
    }
}
