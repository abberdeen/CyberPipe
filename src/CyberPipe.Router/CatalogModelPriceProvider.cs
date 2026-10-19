using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Провайдер цен моделей из каталога и словаря расценок (TASK-033).
/// </summary>
public sealed class CatalogModelPriceProvider : IModelPriceProvider
{
    private readonly ConcurrentDictionary<string, (decimal InputPricePer1mUsd, decimal OutputPricePer1mUsd)> _prices =
        new(StringComparer.OrdinalIgnoreCase);

    public CatalogModelPriceProvider(IEnumerable<ModelDescriptorDto>? models = null)
    {
        if (models != null)
        {
            foreach (ModelDescriptorDto model in models)
            {
                if (model.HostingType == ModelHostingType.RemoteCloud
                    && model.InputPricePer1mUsd.HasValue
                    && model.OutputPricePer1mUsd.HasValue)
                {
                    _prices[model.Id] = (model.InputPricePer1mUsd.Value, model.OutputPricePer1mUsd.Value);
                    if (!string.Equals(model.Id, model.ModelId, StringComparison.OrdinalIgnoreCase))
                    {
                        _prices[model.ModelId] = (model.InputPricePer1mUsd.Value, model.OutputPricePer1mUsd.Value);
                    }
                }
            }
        }
    }

    public void SetPrice(string modelId, decimal inputPricePer1mUsd, decimal outputPricePer1mUsd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _prices[modelId] = (inputPricePer1mUsd, outputPricePer1mUsd);
    }

    public (decimal InputPricePer1mUsd, decimal OutputPricePer1mUsd)? GetModelPricing(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        if (_prices.TryGetValue(modelId, out var pricing))
        {
            return pricing;
        }

        return null;
    }
}
