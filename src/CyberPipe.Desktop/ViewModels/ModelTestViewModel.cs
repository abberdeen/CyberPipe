using System.Windows.Input;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ModelTestViewModel : ViewModelBase
{
    private readonly IModelTestService _testService;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _cts;

    private string _prompt = "Привет! Ответь коротко на русском языке: ты работаешь? Назови своё имя модели.";
    private string _responseText = string.Empty;
    private string? _thinkingText;
    private string? _errorMessage;
    private bool _isSending;
    private bool _hasResponse;
    private bool _hasError;
    private long _latencyMs;
    private int? _inputTokens;
    private int? _outputTokens;
    private string? _modelUsed;
    private bool _isCopied;

    public ModelTestViewModel(
        string modelId,
        ModelHostingType hostingType,
        string provider,
        string modelName,
        IModelTestService? testService = null,
        IClipboardService? clipboard = null)
    {
        ModelId = modelId ?? string.Empty;
        HostingType = hostingType;
        Provider = string.IsNullOrWhiteSpace(provider) ? "Ollama" : provider;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? ModelId : modelName;
        _testService = testService ?? new ModelTestService();
        _clipboard = clipboard ?? AvaloniaClipboardService.Default;

        SendCommand = new AsyncRelayCommand(SendPromptAsync, () => CanSend);
        CancelCommand = new RelayCommand(CancelTest, () => IsSending);
        CopyResponseCommand = new AsyncRelayCommand(CopyResponseAsync, () => HasResponse && !string.IsNullOrEmpty(ResponseText));
        ClearPromptCommand = new RelayCommand(() => Prompt = string.Empty);
        ResetPromptCommand = new RelayCommand(() => Prompt = "Привет! Ответь коротко на русском языке: ты работаешь? Назови своё имя модели.");
        SetGreetingPromptCommand = new RelayCommand(() => Prompt = "Привет! Кто ты и какие задачи ты умеешь решать?");
        SetLogicPromptCommand = new RelayCommand(() => Prompt = "У фермера было 17 овец. Все, кроме 9, убежали. Сколько овец осталось у фермера? Объясни кратко.");
        SetCodePromptCommand = new RelayCommand(() => Prompt = "Напиши компактный метод на C# для проверки, является ли строка палиндромом.");
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public ModelTestViewModel(
        ModelDescriptorDto model,
        IModelTestService? testService = null,
        IClipboardService? clipboard = null)
        : this(
            model?.ModelId ?? model?.Id ?? string.Empty,
            model?.HostingType ?? ModelHostingType.Local,
            model?.Provider ?? "Ollama",
            model?.Name ?? model?.Id ?? "Model",
            testService,
            clipboard)
    {
    }

    public string ModelId { get; }
    public ModelHostingType HostingType { get; }
    public string Provider { get; }
    public string ModelName { get; }

    public string HostingTypeText => HostingType switch
    {
        ModelHostingType.Local => "Локальный (Ollama)",
        ModelHostingType.RemoteCloud => "Удалённый (Cloud)",
        _ => HostingType.ToString(),
    };

    public string Prompt
    {
        get => _prompt;
        set
        {
            if (SetProperty(ref _prompt, value))
            {
                OnPropertyChanged(nameof(CanSend));
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResponseText
    {
        get => _responseText;
        set => SetProperty(ref _responseText, value);
    }

    public string? ThinkingText
    {
        get => _thinkingText;
        set
        {
            if (SetProperty(ref _thinkingText, value))
            {
                OnPropertyChanged(nameof(HasThinking));
            }
        }
    }

    public bool HasThinking => !string.IsNullOrWhiteSpace(_thinkingText);

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (SetProperty(ref _isSending, value))
            {
                OnPropertyChanged(nameof(CanSend));
                SendCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(Prompt);

    public bool HasResponse
    {
        get => _hasResponse;
        set
        {
            if (SetProperty(ref _hasResponse, value))
            {
                CopyResponseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public long LatencyMs
    {
        get => _latencyMs;
        set
        {
            if (SetProperty(ref _latencyMs, value))
            {
                OnPropertyChanged(nameof(LatencyText));
            }
        }
    }

    public string LatencyText => $"{LatencyMs} мс";

    public int? InputTokens
    {
        get => _inputTokens;
        set
        {
            if (SetProperty(ref _inputTokens, value))
            {
                OnPropertyChanged(nameof(TokensSummary));
            }
        }
    }

    public int? OutputTokens
    {
        get => _outputTokens;
        set
        {
            if (SetProperty(ref _outputTokens, value))
            {
                OnPropertyChanged(nameof(TokensSummary));
            }
        }
    }

    public string? ModelUsed
    {
        get => _modelUsed;
        set => SetProperty(ref _modelUsed, value);
    }

    public string TokensSummary
    {
        get
        {
            if (InputTokens.HasValue && OutputTokens.HasValue)
            {
                return $"Вход: {InputTokens.Value} тк. / Выход: {OutputTokens.Value} тк.";
            }
            if (OutputTokens.HasValue)
            {
                return $"Выход: {OutputTokens.Value} тк.";
            }
            return string.Empty;
        }
    }

    public bool IsCopied
    {
        get => _isCopied;
        set => SetProperty(ref _isCopied, value);
    }

    public Action? RequestClose { get; set; }

    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand CopyResponseCommand { get; }
    public RelayCommand ClearPromptCommand { get; }
    public RelayCommand ResetPromptCommand { get; }
    public RelayCommand SetGreetingPromptCommand { get; }
    public RelayCommand SetLogicPromptCommand { get; }
    public RelayCommand SetCodePromptCommand { get; }
    public RelayCommand CloseCommand { get; }

    public async Task SendPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        IsSending = true;
        HasResponse = false;
        HasError = false;
        ErrorMessage = null;
        ResponseText = string.Empty;
        ThinkingText = null;
        LatencyMs = 0;
        InputTokens = null;
        OutputTokens = null;
        ModelUsed = null;
        IsCopied = false;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            ModelTestResponse result = await _testService.TestModelAsync(
                ModelId,
                HostingType,
                Prompt,
                Provider,
                _cts.Token).ConfigureAwait(true);

            LatencyMs = result.LatencyMs;
            InputTokens = result.InputTokens;
            OutputTokens = result.OutputTokens;
            ModelUsed = result.ModelUsed;

            if (result.Success)
            {
                ResponseText = result.ResponseText ?? "(Пустой ответ от модели)";
                ThinkingText = result.ThinkingText;
                HasResponse = true;
                HasError = false;
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "Неизвестная ошибка вызова модели.";
                HasError = true;
                HasResponse = false;
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Запрос к модели был отменён.";
            HasError = true;
            HasResponse = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Непредвиденная ошибка: {ex.Message}";
            HasError = true;
            HasResponse = false;
        }
        finally
        {
            IsSending = false;
        }
    }

    public void CancelTest()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    public async Task CopyResponseAsync()
    {
        if (string.IsNullOrEmpty(ResponseText))
        {
            return;
        }

        await _clipboard.SetTextAsync(ResponseText).ConfigureAwait(true);
        IsCopied = true;
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            IsCopied = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
