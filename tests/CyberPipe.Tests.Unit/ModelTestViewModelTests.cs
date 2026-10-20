using CyberPipe.Contracts;
using CyberPipe.Desktop.Services;
using CyberPipe.Desktop.ViewModels;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ModelTestViewModelTests
{
    private sealed class FakeModelTestService : IModelTestService
    {
        public ModelTestResponse? ResponseToReturn { get; set; }
        public bool ShouldHang { get; set; }
        public string? LastTestedModelId { get; private set; }
        public string? LastPrompt { get; private set; }
        public ModelHostingType? LastHostingType { get; private set; }

        public async Task<ModelTestResponse> TestModelAsync(
            string modelId,
            ModelHostingType hostingType,
            string prompt,
            string? provider = null,
            CancellationToken cancellationToken = default)
        {
            LastTestedModelId = modelId;
            LastHostingType = hostingType;
            LastPrompt = prompt;

            if (ShouldHang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return ResponseToReturn ?? new ModelTestResponse
            {
                Success = true,
                ResponseText = "Привет! Я тестовая модель.",
                ModelUsed = modelId,
                LatencyMs = 120,
                InputTokens = 12,
                OutputTokens = 24,
            };
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? CopiedText { get; private set; }

        public Task SetTextAsync(string? text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        var descriptor = new ModelDescriptorDto
        {
            Id = "qwen2.5:3b",
            ModelId = "qwen2.5:3b",
            Name = "Qwen 2.5 3B",
            Provider = "Ollama",
            HostingType = ModelHostingType.Local,
            RecommendedFor = ModelTaskCategory.LightFastTier2,
            ParamsSummary = "3.1B params, Q4",
            LicenseOrPricing = "Apache 2.0",
            BestFor = "Быстрые задачи",
        };

        var vm = new ModelTestViewModel(descriptor);

        Assert.Equal("qwen2.5:3b", vm.ModelId);
        Assert.Equal("Qwen 2.5 3B", vm.ModelName);
        Assert.Equal(ModelHostingType.Local, vm.HostingType);
        Assert.Equal("Локальный (Ollama)", vm.HostingTypeText);
        Assert.Equal("Ollama", vm.Provider);
        Assert.False(string.IsNullOrWhiteSpace(vm.Prompt));
        Assert.True(vm.CanSend);
        Assert.False(vm.IsSending);
        Assert.False(vm.HasResponse);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task SendPromptAsync_OnSuccess_PopulatesResponseAndMetadata()
    {
        var fakeService = new FakeModelTestService
        {
            ResponseToReturn = new ModelTestResponse
            {
                Success = true,
                ResponseText = "Ответ от Qwen",
                ThinkingText = "Рассуждаю...",
                LatencyMs = 350,
                InputTokens = 15,
                OutputTokens = 42,
                ModelUsed = "qwen2.5:3b",
            }
        };

        var vm = new ModelTestViewModel(
            "qwen2.5:3b",
            ModelHostingType.Local,
            "Ollama",
            "Qwen 2.5 3B",
            fakeService);

        vm.Prompt = "Тестовый вопрос";

        await vm.SendPromptAsync();

        Assert.True(vm.HasResponse);
        Assert.False(vm.HasError);
        Assert.Equal("Ответ от Qwen", vm.ResponseText);
        Assert.Equal("Рассуждаю...", vm.ThinkingText);
        Assert.True(vm.HasThinking);
        Assert.Equal(350, vm.LatencyMs);
        Assert.Equal("350 мс", vm.LatencyText);
        Assert.Equal(15, vm.InputTokens);
        Assert.Equal(42, vm.OutputTokens);
        Assert.Equal("Вход: 15 тк. / Выход: 42 тк.", vm.TokensSummary);
        Assert.Equal("qwen2.5:3b", fakeService.LastTestedModelId);
        Assert.Equal("Тестовый вопрос", fakeService.LastPrompt);
    }

    [Fact]
    public async Task SendPromptAsync_OnError_SetsErrorFlagsAndMessage()
    {
        var fakeService = new FakeModelTestService
        {
            ResponseToReturn = new ModelTestResponse
            {
                Success = false,
                ErrorMessage = "Модель не найдена в Ollama",
                LatencyMs = 45,
            }
        };

        var vm = new ModelTestViewModel(
            "unknown:model",
            ModelHostingType.Local,
            "Ollama",
            "Unknown Model",
            fakeService);

        await vm.SendPromptAsync();

        Assert.False(vm.HasResponse);
        Assert.True(vm.HasError);
        Assert.Equal("Модель не найдена в Ollama", vm.ErrorMessage);
        Assert.Equal(45, vm.LatencyMs);
    }

    [Fact]
    public async Task SendPromptAsync_WhenCancelled_SetsCancelledError()
    {
        var fakeService = new FakeModelTestService { ShouldHang = true };
        var vm = new ModelTestViewModel(
            "qwen2.5:3b",
            ModelHostingType.Local,
            "Ollama",
            "Qwen 2.5 3B",
            fakeService);

        Task sendTask = vm.SendPromptAsync();
        Assert.True(vm.IsSending);

        vm.CancelTest();
        await sendTask;

        Assert.False(vm.IsSending);
        Assert.True(vm.HasError);
        Assert.Contains("отменён", vm.ErrorMessage);
    }

    [Fact]
    public void Presets_UpdatePromptCorrectly()
    {
        var vm = new ModelTestViewModel("qwen2.5:3b", ModelHostingType.Local, "Ollama", "Qwen");

        vm.SetLogicPromptCommand.Execute(null);
        Assert.Contains("овец", vm.Prompt);

        vm.SetCodePromptCommand.Execute(null);
        Assert.Contains("C#", vm.Prompt);

        vm.SetGreetingPromptCommand.Execute(null);
        Assert.Contains("Привет", vm.Prompt);

        vm.ClearPromptCommand.Execute(null);
        Assert.Empty(vm.Prompt);
        Assert.False(vm.CanSend);

        vm.ResetPromptCommand.Execute(null);
        Assert.NotEmpty(vm.Prompt);
        Assert.True(vm.CanSend);
    }

    [Fact]
    public async Task CopyResponseAsync_CopiesToClipboard()
    {
        var clipboard = new FakeClipboardService();
        var vm = new ModelTestViewModel(
            "qwen2.5:3b",
            ModelHostingType.Local,
            "Ollama",
            "Qwen",
            clipboard: clipboard)
        {
            ResponseText = "Текст для копирования",
            HasResponse = true,
        };

        await vm.CopyResponseAsync();

        Assert.Equal("Текст для копирования", clipboard.CopiedText);
        Assert.True(vm.IsCopied);
    }

    [Fact]
    public void CloseCommand_InvokesRequestClose()
    {
        var vm = new ModelTestViewModel("qwen2.5:3b", ModelHostingType.Local, "Ollama", "Qwen");
        bool closed = false;
        vm.RequestClose = () => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void ModelsViewModel_TestModelCommand_InvokesOpenModelTestRequested()
    {
        var descriptor = new ModelDescriptorDto
        {
            Id = "qwen2.5:3b",
            ModelId = "qwen2.5:3b",
            Name = "Qwen 2.5 3B",
            Provider = "Ollama",
            HostingType = ModelHostingType.Local,
            RecommendedFor = ModelTaskCategory.LightFastTier2,
            ParamsSummary = "3.1B params, Q4",
            LicenseOrPricing = "Apache 2.0",
            BestFor = "Быстрые задачи",
        };

        var modelsVm = new ModelsViewModel(catalog: [descriptor]);

        ModelTestViewModel? requestedVm = null;
        modelsVm.OpenModelTestRequested = testVm =>
        {
            requestedVm = testVm;
            return Task.CompletedTask;
        };

        // Test from CatalogItemViewModel
        CatalogItemViewModel catalogItem = Assert.Single(modelsVm.CatalogItems);
        Assert.NotNull(catalogItem.TestCommand);
        catalogItem.TestCommand.Execute(null);

        Assert.NotNull(requestedVm);
        Assert.Equal("qwen2.5:3b", requestedVm.ModelId);

        // Test from TierModelAssignmentViewModel
        requestedVm = null;
        TierModelAssignmentViewModel t2 = modelsVm.Assignments.First(a => a.Tier == IntentTier.Tier2);
        Assert.NotNull(t2.TestCommand);
        t2.TestCommand.Execute(null);

        Assert.NotNull(requestedVm);
        Assert.Equal(t2.SelectedModel, requestedVm.ModelId);
    }

    [Fact]
    public async Task ModelTestService_EmbeddingModel_ReturnsInformativeResult()
    {
        var service = new ModelTestService();
        ModelTestResponse response = await service.TestModelAsync(
            "multilingual-e5-small",
            ModelHostingType.Local,
            "тест",
            "ONNX");

        Assert.True(response.Success);
        Assert.Contains("эмбеддинг", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.LatencyMs);
    }

    [Fact]
    public async Task ModelTestService_EmptyPrompt_ReturnsError()
    {
        var service = new ModelTestService();
        ModelTestResponse response = await service.TestModelAsync(
            "qwen2.5:3b",
            ModelHostingType.Local,
            "   ",
            "Ollama");

        Assert.False(response.Success);
        Assert.Contains("пустым", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
