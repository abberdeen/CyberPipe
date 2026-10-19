using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using CyberPipe.Contracts;

namespace CyberPipe.Models;

/// <summary>
/// Коэффициенты формулы требуемой памяти и порогов вердиктов (TASK-082).
/// Стартовые эвристики из документа 13; калибруются конфигурацией без правки кода.
/// </summary>
public sealed record AdvisorOptions
{
    /// <summary>Множитель весов Q4 (KV-Cache и накладные расходы). Дефолт 1.35.</summary>
    public double MemoryFactor { get; init; } = 1.35;

    /// <summary>Фиксированный запас рантайма в ГБ. Дефолт 1.0.</summary>
    public double RuntimeOverheadGb { get; init; } = 1.0;

    /// <summary>Запас общей RAM для вердикта Optimal (множитель MinRamGb). Дефолт 1.5.</summary>
    public double OptimalTotalRamFactor { get; init; } = 1.5;

    /// <summary>Запас свободной памяти для вердикта Optimal (множитель требуемой). Дефолт 1.4.</summary>
    public double OptimalFreeFactor { get; init; } = 1.4;
}

/// <summary>
/// Источник системных метрик для пробы железа (TASK-082). Абстракция позволяет
/// тестировать пробу на фейковых значениях ОС.
/// </summary>
public interface ISystemMetricsSource
{
    /// <summary>Общая RAM хоста в ГБ; null — определить не удалось.</summary>
    double? GetTotalRamGb();

    /// <summary>Свободная RAM хоста в ГБ; null — определить не удалось.</summary>
    double? GetAvailableRamGb();

    /// <summary>Число логических ядер CPU.</summary>
    int? GetCpuLogicalCores();

    /// <summary>Поддержка AVX2; null — неизвестно (не-x86).</summary>
    bool? GetSupportsAvx2();

    /// <summary>Модель GPU; null — нет или неизвестно.</summary>
    string? GetGpuModelName();

    /// <summary>Выделенная VRAM в ГБ; null — неизвестно.</summary>
    double? GetDedicatedVramGb();

    /// <summary>Свободное место на диске каталога данных в ГБ; null — неизвестно.</summary>
    double? GetFreeDiskSpaceGb(string? dataDirectory);
}

/// <summary>
/// Реальный источник метрик ОС: Windows — точные счётчики памяти,
/// Linux — <c>/proc/meminfo</c>, прочие ОС — неизвестные поля без исключений.
/// GPU средствами платформы без нативных вызовов не определяется — null.
/// </summary>
public sealed class OsSystemMetricsSource : ISystemMetricsSource
{
    /// <inheritdoc />
    public double? GetTotalRamGb()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsMemoryStatus()?.TotalGb;
        }

        if (OperatingSystem.IsLinux())
        {
            return GetLinuxMeminfoValueKb("MemTotal") is { } kb ? kb / 1024.0 / 1024.0 : null;
        }

        return null;
    }

    /// <inheritdoc />
    public double? GetAvailableRamGb()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsMemoryStatus()?.AvailableGb;
        }

        if (OperatingSystem.IsLinux())
        {
            return GetLinuxMeminfoValueKb("MemAvailable") is { } kb ? kb / 1024.0 / 1024.0 : null;
        }

        return null;
    }

    /// <inheritdoc />
    public int? GetCpuLogicalCores() => Environment.ProcessorCount;

    /// <inheritdoc />
    public bool? GetSupportsAvx2() =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 ? Avx2.IsSupported : null;

    /// <inheritdoc />
    public string? GetGpuModelName() => null;

    /// <inheritdoc />
    public double? GetDedicatedVramGb() => null;

    /// <inheritdoc />
    public double? GetFreeDiskSpaceGb(string? dataDirectory)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(dataDirectory ?? AppContext.BaseDirectory)) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed record WindowsMemoryStatus(double TotalGb, double AvailableGb);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static WindowsMemoryStatus? GetWindowsMemoryStatus()
    {
        try
        {
            var buffer = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref buffer))
            {
                return null;
            }

            const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            return new WindowsMemoryStatus(buffer.TotalPhys / bytesPerGb, buffer.AvailPhys / bytesPerGb);
        }
        catch
        {
            return null;
        }
    }

    private static double? GetLinuxMeminfoValueKb(string key)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && double.TryParse(parts[1], out double kb))
                    {
                        return kb;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}

/// <summary>
/// Проба железа ПК владельца (TASK-082): снимок без прав администратора и без сети,
/// кэш на время жизни процесса с явным сбросом.
/// </summary>
public sealed class SystemHardwareProbe : IHardwareCapabilitiesProbe
{
    private readonly ISystemMetricsSource _source;
    private readonly string? _dataDirectory;
    private readonly object _sync = new();
    private HostHardwareProfile? _cached;

    /// <summary>Создаёт пробу поверх источника метрик ОС.</summary>
    public SystemHardwareProbe(ISystemMetricsSource? source = null, string? dataDirectory = null)
    {
        _source = source ?? new OsSystemMetricsSource();
        _dataDirectory = dataDirectory;
    }

    /// <inheritdoc />
    public Task<HostHardwareProfile> ProbeAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _cached ??= new HostHardwareProfile
            {
                TotalRamGb = _source.GetTotalRamGb(),
                AvailableRamGb = _source.GetAvailableRamGb(),
                CpuLogicalCores = _source.GetCpuLogicalCores(),
                SupportsAvx2 = _source.GetSupportsAvx2(),
                GpuModelName = _source.GetGpuModelName(),
                DedicatedVramGb = _source.GetDedicatedVramGb(),
                FreeDiskSpaceGb = _source.GetFreeDiskSpaceGb(_dataDirectory),
            };

            return Task.FromResult(_cached);
        }
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        lock (_sync)
        {
            _cached = null;
        }
    }
}

/// <summary>
/// Советник совместимости локальных моделей с ПК владельца (TASK-082).
/// Формула: <c>requiredMemoryGb = Round(Q4SizeGb * 1.35 + 1.0, 1)</c>.
/// </summary>
public sealed class ModelCompatibilityAdvisor : IModelCompatibilityAdvisor
{
    private readonly IModelCatalog _catalog;
    private readonly IHardwareCapabilitiesProbe _probe;
    private readonly AdvisorOptions _options;

    /// <summary>Создаёт советник поверх каталога и пробы железа.</summary>
    public ModelCompatibilityAdvisor(
        IModelCatalog catalog,
        IHardwareCapabilitiesProbe probe,
        AdvisorOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _options = options ?? new AdvisorOptions();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelCompatibilityEvaluation>> EvaluateLocalModelsAsync(
        ModelTaskCategory? filterByCategory = null,
        CancellationToken cancellationToken = default)
    {
        HostHardwareProfile hardware;
        try
        {
            hardware = await _probe.ProbeAsync(cancellationToken).ConfigureAwait(false) ?? HostHardwareProfile.Empty;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            hardware = HostHardwareProfile.Empty;
        }

        var result = new List<ModelCompatibilityEvaluation>();
        foreach (ModelDescriptorDto model in _catalog.LocalModels)
        {
            if (filterByCategory.HasValue && model.RecommendedFor != filterByCategory.Value)
            {
                continue;
            }

            result.Add(Evaluate(model, hardware));
        }

        result.Sort(static (a, b) =>
        {
            int verdict = a.Verdict.CompareTo(b.Verdict);
            return verdict != 0 ? verdict : a.RequiredMemoryGb.CompareTo(b.RequiredMemoryGb);
        });

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptorDto> GetCloudModels(ModelTaskCategory? filterByCategory = null) =>
        filterByCategory.HasValue
            ? _catalog.CloudModels.Where(m => m.RecommendedFor == filterByCategory.Value).ToList()
            : _catalog.CloudModels;

    /// <inheritdoc />
    public Task<HostHardwareProfile> GetHardwareProfileAsync(CancellationToken cancellationToken = default) =>
        _probe.ProbeAsync(cancellationToken);

    /// <summary>Рассчитывает требуемую память модели в ГБ.</summary>
    public double RequiredMemoryGb(ModelDescriptorDto model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Math.Round((model.Q4SizeGb ?? 0) * _options.MemoryFactor + _options.RuntimeOverheadGb, 1);
    }

    private ModelCompatibilityEvaluation Evaluate(ModelDescriptorDto model, HostHardwareProfile hardware)
    {
        double required = RequiredMemoryGb(model);
        double minRam = model.MinRamGb ?? 0;

        bool hardwareUnknown = hardware.TotalRamGb is null && hardware.AvailableRamGb is null;
        bool canBeOptimal = hardware.TotalRamGb.HasValue && hardware.AvailableRamGb.HasValue;

        HardwareFitVerdict verdict;
        string reason;
        if ((hardware.TotalRamGb.HasValue && hardware.TotalRamGb.Value < minRam) ||
            (hardware.AvailableRamGb.HasValue && hardware.AvailableRamGb.Value < required))
        {
            verdict = HardwareFitVerdict.InsufficientResources;
            reason = "памяти недостаточно: риск свопинга и фризов ПК";
        }
        else if (canBeOptimal &&
            hardware.TotalRamGb is { } totalGb &&
            hardware.AvailableRamGb is { } availGb &&
            ((hardware.DedicatedVramGb.HasValue && hardware.DedicatedVramGb.Value >= required) ||
             (totalGb >= minRam * _options.OptimalTotalRamFactor &&
              availGb >= required * _options.OptimalFreeFactor)))
        {
            verdict = HardwareFitVerdict.Optimal;
            reason = "идеально для вашего ПК: быстрый отклик без просадки системы";
        }
        else
        {
            verdict = HardwareFitVerdict.Compatible;
            reason = "запустится на вашем ПК, но во время генерации возможна заметная нагрузка на CPU/RAM";
        }

        var notes = new List<string>();
        if (hardwareUnknown)
        {
            if (verdict == HardwareFitVerdict.Optimal)
            {
                verdict = HardwareFitVerdict.Compatible;
            }

            notes.Add("данные железа недоступны");
        }
        else
        {
            if (!canBeOptimal && verdict == HardwareFitVerdict.Optimal)
            {
                verdict = HardwareFitVerdict.Compatible;
            }

            if (hardware.AvailableRamGb is null)
            {
                notes.Add("не удалось определить свободную память");
            }

            if (hardware.TotalRamGb is null)
            {
                notes.Add("не удалось определить общую память");
            }
        }

        string available = hardware.AvailableRamGb.HasValue
            ? $"свободно {hardware.AvailableRamGb.Value.ToString("F1", CultureInfo.InvariantCulture)} ГБ RAM"
            : "свободная память неизвестна";
        string explanation = $"Нужно ~{required.ToString("F1", CultureInfo.InvariantCulture)} ГБ с учётом KV-Cache (окно {FormatWindow(model.DefaultContextTokens)}), {available}. {reason}.";
        if (notes.Count != 0)
        {
            explanation += " " + string.Join(" ", notes) + ".";
        }

        return new ModelCompatibilityEvaluation
        {
            Model = model,
            Verdict = verdict,
            RequiredMemoryGb = required,
            Explanation = explanation,
        };
    }

    private static string FormatWindow(int tokens) => tokens >= 1024 && tokens % 1024 == 0 ? $"{tokens / 1024}K" : tokens.ToString();
}
