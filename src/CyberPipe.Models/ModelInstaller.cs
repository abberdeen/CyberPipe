using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using Microsoft.Extensions.Options;

namespace CyberPipe.Models;

/// <summary>
/// Проверка свободного места на диске (TASK-081). Абстракция для тестов.
/// </summary>
public interface IDiskSpaceProbe
{
    /// <summary>Возвращает свободные байты на диске каталога; null — неизвестно.</summary>
    Task<long?> GetFreeBytesAsync(string? directory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Проверка места через <c>DriveInfo</c> (TASK-081).
/// </summary>
public sealed class DriveDiskSpaceProbe : IDiskSpaceProbe
{
    /// <inheritdoc />
    public Task<long?> GetFreeBytesAsync(string? directory, CancellationToken cancellationToken = default)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(directory ?? AppContext.BaseDirectory)) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                return Task.FromResult<long?>(null);
            }

            return Task.FromResult<long?>(new DriveInfo(root).AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult<long?>(null);
        }
    }
}

/// <summary>
/// Установщик локального рантайма Ollama (TASK-081): обнаружение, установка
/// официального дистрибутива по подтверждению владельца, ожидание готовности API.
/// </summary>
public sealed class OllamaLocalRuntimeInstaller : ILocalRuntimeInstaller
{
    private readonly HttpClient _http;
    private readonly LocalModelInstallerOptions _options;
    private readonly Func<string, IProgress<string>?, CancellationToken, Task<bool>> _installHook;

    /// <summary>Создаёт установщик рантайма.</summary>
    public OllamaLocalRuntimeInstaller(
        HttpClient httpClient,
        IOptions<LocalModelInstallerOptions>? options = null,
        Func<string, IProgress<string>?, CancellationToken, Task<bool>>? installHook = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? new LocalModelInstallerOptions();
        _installHook = installHook ?? DefaultInstallAsync;
    }

    /// <inheritdoc />
    public async Task<LocalRuntimeInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? version = await TryGetVersionAsync(cancellationToken).ConfigureAwait(false);
        return new LocalRuntimeInfo
        {
            State = version is null ? LocalRuntimeState.NotInstalled : LocalRuntimeState.Ready,
            Version = version,
            ApiBaseUrl = _options.OllamaApiBaseUrl,
        };
    }

    /// <inheritdoc />
    public async Task<Result<LocalRuntimeInfo>> EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LocalRuntimeInfo current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (current.State == LocalRuntimeState.Ready)
        {
            return Result<LocalRuntimeInfo>.Success(current);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Result<LocalRuntimeInfo>.Failure(
                "Runtime.ManualInstallRequired",
                _options.OllamaManualInstallHint);
        }

        progress?.Report("Downloading Ollama distribution.");
        bool installed;
        try
        {
            installed = await _installHook(_options.OllamaWindowsDownloadUrl, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<LocalRuntimeInfo>.Failure("Runtime.InstallFailed", $"Ollama install failed: {ex.Message}");
        }

        if (!installed)
        {
            return Result<LocalRuntimeInfo>.Failure("Runtime.InstallFailed", "Ollama installer did not complete.");
        }

        LocalRuntimeInfo ready = await WaitForReadyAsync(progress, cancellationToken).ConfigureAwait(false);
        return ready.State == LocalRuntimeState.Ready
            ? Result<LocalRuntimeInfo>.Success(ready)
            : Result<LocalRuntimeInfo>.Failure("Runtime.NotReady", "Ollama API did not become ready in time.");
    }

    private async Task<LocalRuntimeInfo> WaitForReadyAsync(IProgress<string>? progress, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.RuntimeReadyTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        while (!linked.Token.IsCancellationRequested)
        {
            string? version = await TryGetVersionAsync(linked.Token).ConfigureAwait(false);
            if (version is not null)
            {
                return new LocalRuntimeInfo
                {
                    State = LocalRuntimeState.Ready,
                    Version = version,
                    ApiBaseUrl = _options.OllamaApiBaseUrl,
                };
            }

            progress?.Report("Waiting for Ollama API.");
            try
            {
                await Task.Delay(1000, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return new LocalRuntimeInfo { State = LocalRuntimeState.NotInstalled, ApiBaseUrl = _options.OllamaApiBaseUrl };
    }

    private async Task<string?> TryGetVersionAsync(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                $"{_options.OllamaApiBaseUrl.TrimEnd('/')}/api/version", ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("version", out JsonElement version) &&
                version.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(version.GetString())
                ? version.GetString()
                : "unknown";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<bool> DefaultInstallAsync(string downloadUrl, IProgress<string>? progress, CancellationToken ct)
    {
        string installerPath = Path.Combine(Path.GetTempPath(), $"OllamaSetup-{Guid.NewGuid():N}.exe");
        using (HttpResponseMessage response = await _http.GetAsync(downloadUrl, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using FileStream file = File.Create(installerPath);
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        progress?.Report("Running Ollama installer.");
        using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}

/// <summary>
/// Установщик локальных моделей через API рантайма Ollama и установщик
/// стандартного эмбеддинга Tier 1 (TASK-081).
/// </summary>
public sealed class OllamaLocalModelInstaller : ILocalModelInstaller
{
    private readonly HttpClient _http;
    private readonly IModelCatalog _catalog;
    private readonly LocalModelInstallerOptions _options;
    private readonly IDiskSpaceProbe _disk;

    /// <summary>Создаёт установщик моделей.</summary>
    public OllamaLocalModelInstaller(
        HttpClient httpClient,
        IModelCatalog catalog,
        IOptions<LocalModelInstallerOptions>? options = null,
        IDiskSpaceProbe? diskSpaceProbe = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options?.Value ?? new LocalModelInstallerOptions();
        _disk = diskSpaceProbe ?? new DriveDiskSpaceProbe();
    }

    private string Api(string path) => $"{_options.OllamaApiBaseUrl.TrimEnd('/')}{path}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledLocalModel>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(Api("/api/tags"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var result = new List<InstalledLocalModel>();
        if (doc.RootElement.TryGetProperty("models", out JsonElement models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in models.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out JsonElement name) &&
                    name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    long? size = entry.TryGetProperty("size", out JsonElement sizeElement) &&
                        sizeElement.ValueKind == JsonValueKind.Number &&
                        sizeElement.TryGetInt64(out long bytes)
                        ? bytes
                        : null;
                    result.Add(new InstalledLocalModel { ModelId = name.GetString()!, SizeBytes = size });
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result<ModelProbeResult>> InstallAsync(
        ModelDescriptorDto model,
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.HostingType != ModelHostingType.Local)
        {
            return Result<ModelProbeResult>.Failure("Model.NotLocal", $"Model '{model.Id}' is not a local model.");
        }

        if (string.IsNullOrWhiteSpace(model.ModelId))
        {
            return Result<ModelProbeResult>.Failure("Model.NoRuntimeId", $"Model '{model.Id}' has no runtime model id.");
        }

        long? freeBytes = await _disk.GetFreeBytesAsync(null, cancellationToken).ConfigureAwait(false);
        if (freeBytes.HasValue && model.Q4SizeGb.HasValue)
        {
            long required = (long)(model.Q4SizeGb.Value * 1024 * 1024 * 1024 * _options.DiskHeadroomFactor);
            if (freeBytes.Value < required)
            {
                return Result<ModelProbeResult>.Failure(
                    "Model.NoDiskSpace",
                    $"Not enough free disk space for '{model.Name}': need ~{required / 1024 / 1024 / 1024} GB.");
            }
        }

        Result<ModelProbeResult> pull;
        try
        {
            pull = await PullAsync(model, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryDeleteAsync(model.ModelId, CancellationToken.None).ConfigureAwait(false);
            return Result<ModelProbeResult>.Failure("Model.InstallCancelled", $"Install of '{model.Name}' was cancelled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return Result<ModelProbeResult>.Failure("Model.PullFailed", $"Download of '{model.Name}' failed: {ex.Message}");
        }

        if (!pull.IsSuccess)
        {
            return pull;
        }

        return await VerifyAsync(model, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Result.Failure("Model.NoRuntimeId", "Model id is required.");
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, Api("/api/delete"))
                {
                    Content = JsonContent.Create(new { name = modelId }),
                },
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure("Model.RemoveFailed", $"Runtime refused to remove '{modelId}': {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure("Model.RemoveFailed", $"Remove of '{modelId}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<ModelProbeResult>> VerifyAsync(
        ModelDescriptorDto model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            using HttpResponseMessage response = await _http.PostAsync(
                Api("/api/generate"),
                JsonContent.Create(new
                {
                    model = model.ModelId,
                    prompt = "ok",
                    stream = false,
                    options = new { num_ctx = model.DefaultContextTokens },
                }),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result<ModelProbeResult>.Failure(
                    "Model.ProbeFailed",
                    $"Probe request for '{model.Name}' failed with status {(int)response.StatusCode}.");
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            string? sample = doc.RootElement.TryGetProperty("response", out JsonElement text) &&
                text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
            return Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true, SampleText = sample });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result<ModelProbeResult>.Failure("Model.ProbeFailed", $"Probe request for '{model.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<EmbeddingInstallResult>> EnsureEmbeddingAsync(
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string directory = _options.EmbeddingDataDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyberPipe", "embeddings", "multilingual-e5-small");
        string sentinel = Path.Combine(directory, "model.onnx");

        if (File.Exists(sentinel) && new FileInfo(sentinel).Length > 0)
        {
            return Result<EmbeddingInstallResult>.Success(new EmbeddingInstallResult
            {
                AlreadyInstalled = true,
                InstallPath = directory,
                VectorDimensions = _catalog.EmbeddingModel.VectorDimensions,
            });
        }

        Directory.CreateDirectory(directory);
        try
        {
            int index = 0;
            foreach (string file in _options.EmbeddingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DownloadFileAsync(
                    $"{_options.EmbeddingSourceBaseUrl.TrimEnd('/')}/{file}",
                    Path.Combine(directory, file),
                    file,
                    index,
                    _options.EmbeddingFiles.Count,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                index++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Result<EmbeddingInstallResult>.Failure(
                "Embedding.DownloadFailed", $"Embedding download failed: {ex.Message}");
        }

        if (!File.Exists(sentinel) || new FileInfo(sentinel).Length == 0)
        {
            return Result<EmbeddingInstallResult>.Failure(
                "Embedding.Incomplete", "Embedding files are missing after download.");
        }

        return Result<EmbeddingInstallResult>.Success(new EmbeddingInstallResult
        {
            AlreadyInstalled = false,
            InstallPath = directory,
            VectorDimensions = _catalog.EmbeddingModel.VectorDimensions,
        });
    }

    private async Task<Result<ModelProbeResult>> PullAsync(
        ModelDescriptorDto model,
        IProgress<ModelInstallProgress>? progress,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsync(
            Api("/api/pull"),
            JsonContent.Create(new { name = model.ModelId, stream = true }),
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? line;
        long completed = 0;
        long total = 0;
        string? stage = null;
        bool success = false;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument update = JsonDocument.Parse(line);
            JsonElement root = update.RootElement;
            if (root.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.String)
            {
                stage = status.GetString();
                if (string.Equals(stage, "success", StringComparison.OrdinalIgnoreCase))
                {
                    success = true;
                }
            }

            if (root.TryGetProperty("completed", out JsonElement done) &&
                done.ValueKind == JsonValueKind.Number && done.TryGetInt64(out long doneBytes))
            {
                completed = doneBytes;
            }

            if (root.TryGetProperty("total", out JsonElement all) &&
                all.ValueKind == JsonValueKind.Number && all.TryGetInt64(out long totalBytes))
            {
                total = totalBytes;
            }

            progress?.Report(new ModelInstallProgress
            {
                ModelId = model.ModelId,
                Percent = total > 0 ? (int)Math.Min(100, completed * 100 / total) : 0,
                DownloadedBytes = completed,
                TotalBytes = total > 0 ? total : null,
                Stage = stage,
            });
        }

        if (!success)
        {
            return Result<ModelProbeResult>.Failure("Model.PullFailed", $"Runtime did not confirm pull of '{model.Name}'.");
        }

        return Result<ModelProbeResult>.Success(new ModelProbeResult { Succeeded = true });
    }

    private async Task TryDeleteAsync(string modelId, CancellationToken ct)
    {
        try
        {
            await RemoveAsync(modelId, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        string fileName,
        int index,
        int count,
        IProgress<ModelInstallProgress>? progress,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream target = File.Create(destination);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            long filePercent = total.GetValueOrDefault() > 0 ? downloaded * 100 / total!.Value : 0;
            int percent = (int)Math.Min(100L, (index * 100L + filePercent) / Math.Max(1, count));
            progress?.Report(new ModelInstallProgress
            {
                ModelId = fileName,
                Percent = percent,
                DownloadedBytes = downloaded,
                TotalBytes = total,
                Stage = "downloading",
            });
        }
    }
}
