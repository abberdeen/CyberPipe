using CyberPipe.Contracts;

namespace CyberPipe.Chains;

/// <summary>
/// Загрузчик определений цепочек (TASK-040).
/// Поддерживает чтение из конфигурации (строка/файл) и из репозитория.
/// Слияние источников запрещено: один источник на вызов.
/// </summary>
public sealed class ChainDefinitionLoader : IChainDefinitionLoader
{
    private readonly IChainDefinitionRepository _repository;
    private readonly IChainDefinitionValidator _validator;

    /// <summary>
    /// Инициализирует загрузчик определений цепочек.
    /// </summary>
    /// <param name="repository">Репозиторий цепочек.</param>
    /// <param name="validator">Валидатор линейных цепочек.</param>
    public ChainDefinitionLoader(IChainDefinitionRepository repository, IChainDefinitionValidator validator)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public Task<ChainDefinitionDto> LoadFromConfigAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ChainValidationResult result = _validator.ValidateJson(json);
        if (!result.IsValid)
        {
            throw new ChainValidationException(result.Errors);
        }

        ChainDefinitionDto definition = ChainDefinitionSerializer.Deserialize(json);
        return Task.FromResult(definition);
    }

    /// <inheritdoc />
    public async Task<ChainDefinitionDto> LoadFromConfigFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Chain configuration file not found: {filePath}", filePath);
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await LoadFromConfigAsync(json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChainDefinitionDto?> LoadFromRepositoryAsync(ChainId id, CancellationToken cancellationToken = default)
    {
        ChainDefinitionDto? chain = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (chain is null)
        {
            return null;
        }

        ChainValidationResult result = _validator.Validate(chain);
        if (!result.IsValid)
        {
            throw new ChainValidationException(result.Errors);
        }

        return chain;
    }
}
