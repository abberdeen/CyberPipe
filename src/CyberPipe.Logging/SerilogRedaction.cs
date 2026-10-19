using System.Collections;
using CyberPipe.Contracts;
using Serilog.Core;
using Serilog.Events;

namespace CyberPipe.Logging;

/// <summary>
/// Р”РµСЃС‚СЂСѓРєС‚РѕСЂ Serilog: SecretValue РЅР° Р»СЋР±РѕРј СѓСЂРѕРІРЅРµ РІР»РѕР¶РµРЅРЅРѕСЃС‚Рё Рё Р·РЅР°С‡РµРЅРёСЏ РїРѕРґ СЃРµРєСЂРµС‚РЅС‹РјРё РёРјРµРЅР°РјРё
/// СЃРІРѕР№СЃС‚РІ Рё РєР»СЋС‡РµР№ СЃР»РѕРІР°СЂРµР№ Р·Р°РјРµРЅСЏСЋС‚СЃСЏ РЅР° [REDACTED] РґРѕ Р·Р°РїРёСЃРё СЃРѕР±С‹С‚РёСЏ РІ Р»СЋР±РѕР№ РїСЂРёС‘РјРЅРёРє.
/// </summary>
public sealed class SecretRedactionDestructuringPolicy : IDestructuringPolicy
{
    /// <inheritdoc />
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        if (value is SecretValue)
        {
            result = new ScalarValue(SecretRedaction.Redacted);
            return true;
        }

        Type type = value.GetType();
        if (IsSimple(type) || value is string)
        {
            result = null!;
            return false;
        }

        if (value is IDictionary dictionary)
        {
            var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
            foreach (DictionaryEntry entry in dictionary)
            {
                string keyText = entry.Key?.ToString() ?? string.Empty;
                LogEventPropertyValue elementValue = SecretRedaction.IsSecretPropertyName(keyText)
                    ? new ScalarValue(SecretRedaction.Redacted)
                    : propertyValueFactory.CreatePropertyValue(entry.Value, destructureObjects: true);
                elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(keyText), elementValue));
            }

            result = new DictionaryValue(elements);
            return true;
        }

        if (value is IEnumerable)
        {
            result = null!;
            return false;
        }

        var properties = new List<LogEventProperty>();
        try
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (SecretRedaction.IsSecretPropertyName(property.Name))
                {
                    properties.Add(new LogEventProperty(property.Name, new ScalarValue(SecretRedaction.Redacted)));
                    continue;
                }

                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                properties.Add(new LogEventProperty(
                    property.Name,
                    propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true)));
            }
        }
        catch
        {
            result = null!;
            return false;
        }

        result = new StructureValue(properties, type.Name);
        return true;
    }

    private static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime)
        || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan)
        || Nullable.GetUnderlyingType(type) is not null;
}

/// <summary>
/// РћР±РѕРіР°С‚РёС‚РµР»СЊ РІРµСЂС…РЅРµРіРѕ СѓСЂРѕРІРЅСЏ: СЃРІРѕР№СЃС‚РІР° СЃРѕР±С‹С‚РёСЏ СЃ СЃРµРєСЂРµС‚РЅС‹РјРё РёРјРµРЅР°РјРё, Р·Р°РґР°РЅРЅС‹Рµ РїСЂСЏРјРѕ С€Р°Р±Р»РѕРЅРѕРј
/// СЃРѕРѕР±С‰РµРЅРёСЏ, Р·Р°РјРµРЅСЏСЋС‚СЃСЏ РЅР° [REDACTED] РЅРµР·Р°РІРёСЃРёРјРѕ РѕС‚ РІР»РѕР¶РµРЅРЅРѕР№ РґРµСЃС‚СЂСѓРєС‚СѓСЂРёР·Р°С†РёРё.
/// </summary>
public sealed class SecretRedactionEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (string name in logEvent.Properties.Keys.ToList())
        {
            if (SecretRedaction.IsSecretPropertyName(name))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SecretRedaction.Redacted)));
        }
    }
}

/// <summary>РћР±РѕРіР°С‚РёС‚РµР»СЊ РєРѕРЅС‚РµРєСЃС‚Р° РѕРїРµСЂР°С†РёРё: RunId, ChainId, ChannelKind Рё IntentTier РёР· AsyncLocal.</summary>
public sealed class OperationContextEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        OperationContextSnapshot? context = OperationContext.Current;
        if (context is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RunId", context.RunId.ToString()));
        if (context.ChainId is { } chainId)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ChainId", chainId.ToString()));
        if (context.ChannelKind is { } channelKind)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ChannelKind", channelKind.ToString()));
        if (context.IntentTier is { } intentTier)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("IntentTier", intentTier.ToString()));
    }
}
