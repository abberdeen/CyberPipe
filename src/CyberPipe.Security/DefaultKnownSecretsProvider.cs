namespace CyberPipe.Security;

using CyberPipe.Contracts;

/// <summary>
/// Стандартный поставщик известных секретов приложения, опрашивающий хранилище секретов (TASK-072).
/// Возвращает открытые значения только для внутреннего сопоставления санитизатором.
/// </summary>
public sealed class DefaultKnownSecretsProvider : IKnownSecretsProvider
{
    private readonly ISecretStore _secretStore;

    /// <summary>Создаёт поставщик с заданным хранилищем секретов.</summary>
    public DefaultKnownSecretsProvider(ISecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetKnownSecretValuesAsync(CancellationToken cancellationToken = default)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in SecretKeys.All)
        {
            try
            {
                var secret = await _secretStore.GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (secret is not null)
                {
                    var revealed = secret.Reveal();
                    if (!string.IsNullOrWhiteSpace(revealed))
                    {
                        values.Add(revealed);
                    }
                }
            }
            catch
            {
                // Изолируем сбой чтения отдельного секрета
            }
        }

        return values;
    }
}
