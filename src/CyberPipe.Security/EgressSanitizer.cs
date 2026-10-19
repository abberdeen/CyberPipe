namespace CyberPipe.Security;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Реализация санитизатора исходящих данных перед отправкой за пределы локальной машины (TASK-072).
/// Очищает от секретов, ключей, токенов и персональных данных; fail-closed при нарушениях.
/// </summary>
public sealed class EgressSanitizer : IEgressSanitizer
{
    private static readonly Regex PrivateKeyRegex = new(
        @"-----BEGIN [A-Z0-9 \-_]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 \-_]*PRIVATE KEY-----|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UriCredentialsRegex = new(
        @"\b(?:postgres(?:ql)?|mongodb(?:\+srv)?|mysql|redis|amqp|mssql):\/\/[^\s\/?#:]+:([^\s@\/?#]+)@[^\s\/?#]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AdoNetCredentialsRegex = new(
        @"(?:(?:Server|Host|Data Source|Addr)\s*=\s*[^;]+;.*?(?:Password|Pwd)\s*=\s*[^;]+|(?:Password|Pwd)\s*=\s*[^;]+;.*?(?:Server|Host|Data Source|Addr)\s*=\s*[^;]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PrefixedApiKeyRegex = new(
        @"\b(?:sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{20,}|gho_[a-zA-Z0-9]{20,}|glpat-[a-zA-Z0-9\-]{20,}|xox[baprs]-[a-zA-Z0-9\-]{10,})\b",
        RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex = new(
        @"Bearer\s+([A-Za-z0-9_\-\.=]{20,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JwtRegex = new(
        @"\b(?:eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]+)\b",
        RegexOptions.Compiled);

    private static readonly Regex InlineKeyValueSecretRegex = new(
        @"(?i)\b(api[_-]?key|access[_-]?token|secret[_-]?key|password|pwd|пароль|ключ[ _-]?доступа)\s*[:=]\s*[""']?([^""'\s,;]+)[""']?",
        RegexOptions.Compiled);

    private static readonly Regex CardCandidateRegex = new(
        @"\b(?:\d[ -]?){12,18}\d\b",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex PhoneRegex = new(
        @"(?:\+?[0-9]{1,3}[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{2}[-.\s]?[0-9]{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex SnilsRegex = new(
        @"\b\d{3}-\d{3}-\d{3}[\s-]\d{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex RuPassportRegex = new(
        @"\b\d{4}\s\d{6}\b",
        RegexOptions.Compiled);

    private static readonly Regex SsnRegex = new(
        @"\b\d{3}-\d{2}-\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex HighEntropyTokenCandidateRegex = new(
        @"[^\s""',;]{20,}",
        RegexOptions.Compiled);

    private readonly EgressSanitizerOptions _options;
    private readonly IKnownSecretsProvider? _knownSecretsProvider;
    private readonly ILogger<EgressSanitizer> _logger;

    /// <summary>Создаёт экземпляр санитизатора.</summary>
    public EgressSanitizer(
        IOptions<CyberPipeOptions>? options = null,
        IKnownSecretsProvider? knownSecretsProvider = null,
        ILogger<EgressSanitizer>? logger = null)
    {
        _options = options?.Value?.EgressSanitizer ?? new EgressSanitizerOptions();
        _knownSecretsProvider = knownSecretsProvider;
        _logger = logger ?? NullLogger<EgressSanitizer>.Instance;
    }

    /// <summary>Создаёт экземпляр санитизатора с явными настройками.</summary>
    public EgressSanitizer(
        EgressSanitizerOptions options,
        IKnownSecretsProvider? knownSecretsProvider = null,
        ILogger<EgressSanitizer>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _knownSecretsProvider = knownSecretsProvider;
        _logger = logger ?? NullLogger<EgressSanitizer>.Instance;
    }

    /// <inheritdoc />
    public async Task<EgressSanitizeResult> SanitizeAsync(EgressPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            // 1. Проверка размера payload (fail-closed при превышении)
            long totalBytes = CalculatePayloadSizeBytes(payload);
            if (totalBytes > _options.MaxPayloadSizeBytes)
            {
                _logger.LogWarning(
                    "Egress payload exceeded maximum size limit {LimitBytes} bytes (actual {ActualBytes} bytes). Destination: {Destination}",
                    _options.MaxPayloadSizeBytes,
                    totalBytes,
                    payload.Destination);

                return EgressSanitizeResult.Blocked(
                    $"Payload size {totalBytes} bytes exceeds maximum allowed limit {_options.MaxPayloadSizeBytes} bytes");
            }

            // 2. Получение известных секретов
            IReadOnlyCollection<string> knownSecrets = [];
            if (_knownSecretsProvider is not null)
            {
                try
                {
                    knownSecrets = await _knownSecretsProvider.GetKnownSecretValuesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load known secrets for egress comparison; proceeding with pattern detectors.");
                }
            }

            // 3. Санитизация по виду содержимого
            var findings = new List<SanitizeFinding>();
            EgressPayload? sanitizedPayload = null;

            switch (payload.ContentKind)
            {
                case EgressContentKind.Text:
                    sanitizedPayload = SanitizeTextPayload(payload, knownSecrets, findings);
                    break;

                case EgressContentKind.KeyValue:
                    sanitizedPayload = SanitizeKeyValuePayload(payload, knownSecrets, findings);
                    break;

                case EgressContentKind.Json:
                    sanitizedPayload = SanitizeJsonPayload(payload, knownSecrets, findings);
                    break;

                default:
                    return EgressSanitizeResult.Blocked($"Unsupported content kind: {payload.ContentKind}");
            }

            // 4. Проверка исхода на наличие блокирующих находок
            var blockingFinding = findings.FirstOrDefault(f => f.Action == SanitizeAction.Block);
            if (blockingFinding is not null)
            {
                _logger.LogWarning(
                    "Egress payload blocked for {Destination}. Sensitive class: {Class} at {Path}",
                    payload.Destination,
                    blockingFinding.Class,
                    blockingFinding.PathOrPosition);

                return EgressSanitizeResult.Blocked(
                    $"Blocked by egress policy due to sensitive finding: {blockingFinding.Class} at {blockingFinding.PathOrPosition}",
                    findings);
            }

            // 5. Логирование и возврат результата
            if (findings.Count == 0)
            {
                return EgressSanitizeResult.AllowedUnchanged(payload);
            }

            _logger.LogInformation(
                "Egress payload sanitized for {Destination}. Findings count: {Count}",
                payload.Destination,
                findings.Count);

            return EgressSanitizeResult.AllowedCleaned(sanitizedPayload ?? payload, findings);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Fail-closed: malformed JSON payload in egress for {Destination}", payload.Destination);
            return EgressSanitizeResult.Blocked("Malformed JSON payload: invalid JSON syntax");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fail-closed: unexpected error in egress sanitizer for {Destination}", payload.Destination);
            return EgressSanitizeResult.Blocked($"Egress sanitization failed unexpectedly: {ex.Message}");
        }
    }

    private EgressPayload SanitizeTextPayload(
        EgressPayload payload,
        IReadOnlyCollection<string> knownSecrets,
        List<SanitizeFinding> findings)
    {
        string text = payload.TextContent ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return payload;
        }

        string cleaned = SanitizeString(text, "text", knownSecrets, findings);
        if (findings.Count == 0)
        {
            return payload;
        }

        return payload with { TextContent = cleaned };
    }

    private EgressPayload SanitizeKeyValuePayload(
        EgressPayload payload,
        IReadOnlyCollection<string> knownSecrets,
        List<SanitizeFinding> findings)
    {
        var sourceDict = payload.KeyValueContent;
        if (sourceDict is null || sourceDict.Count == 0)
        {
            return payload;
        }

        var resultDict = new Dictionary<string, string?>(sourceDict.Count, StringComparer.Ordinal);

        foreach (var (key, value) in sourceDict)
        {
            if (value is null)
            {
                resultDict[key] = null;
                continue;
            }

            var fieldClass = ClassifyFieldName(key);
            if (fieldClass.HasValue)
            {
                var action = ResolveAction(fieldClass.Value);
                findings.Add(new SanitizeFinding
                {
                    Class = fieldClass.Value,
                    PathOrPosition = key,
                    Action = action,
                    Description = $"Sensitive key '{key}' classified as {fieldClass.Value}"
                });

                if (action == SanitizeAction.Block)
                {
                    resultDict[key] = null;
                }
                else if (action == SanitizeAction.PartialMask)
                {
                    resultDict[key] = ApplyPartialMask(value, fieldClass.Value);
                }
                else
                {
                    resultDict[key] = _options.MaskPlaceholder;
                }
            }
            else
            {
                string cleanedVal = SanitizeString(value, key, knownSecrets, findings);
                resultDict[key] = cleanedVal;
            }
        }

        if (findings.Count == 0)
        {
            return payload;
        }

        return payload with { KeyValueContent = resultDict };
    }

    private EgressPayload SanitizeJsonPayload(
        EgressPayload payload,
        IReadOnlyCollection<string> knownSecrets,
        List<SanitizeFinding> findings)
    {
        string json = payload.JsonContent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return payload;
        }

        // Парсинг JSON; при синтаксической ошибке вызовет JsonException -> fail-closed
        var jsonNode = JsonNode.Parse(json);
        if (jsonNode is null)
        {
            return payload;
        }

        SanitizeJsonNode(jsonNode, "$", knownSecrets, findings);

        if (findings.Count == 0)
        {
            return payload;
        }

        string cleanJson = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return payload with { JsonContent = cleanJson };
    }

    private void SanitizeJsonNode(
        JsonNode node,
        string currentPath,
        IReadOnlyCollection<string> knownSecrets,
        List<SanitizeFinding> findings)
    {
        if (node is JsonObject obj)
        {
            var properties = obj.ToList();
            foreach (var prop in properties)
            {
                string fieldName = prop.Key;
                string childPath = $"{currentPath}['{fieldName}']";
                var childVal = prop.Value;

                if (childVal is null)
                {
                    continue;
                }

                var fieldClass = ClassifyFieldName(fieldName);
                if (fieldClass.HasValue)
                {
                    var action = ResolveAction(fieldClass.Value);
                    findings.Add(new SanitizeFinding
                    {
                        Class = fieldClass.Value,
                        PathOrPosition = childPath,
                        Action = action,
                        Description = $"Sensitive property '{fieldName}' classified as {fieldClass.Value}"
                    });

                    if (action == SanitizeAction.Block)
                    {
                        // Находка Block зафиксирована; дальнейшая обработка не критична
                        continue;
                    }

                    if (childVal is JsonValue val)
                    {
                        string str = val.ToString();
                        string masked = action == SanitizeAction.PartialMask
                            ? ApplyPartialMask(str, fieldClass.Value)
                            : _options.MaskPlaceholder;
                        obj[fieldName] = JsonValue.Create(masked);
                    }
                    else
                    {
                        // Сложный объект под чувствительным ключом
                        SanitizeJsonNode(childVal, childPath, knownSecrets, findings);
                    }
                }
                else
                {
                    if (childVal is JsonValue val && val.TryGetValue<string>(out var stringVal))
                    {
                        string cleanString = SanitizeString(stringVal, childPath, knownSecrets, findings);
                        if (!string.Equals(cleanString, stringVal, StringComparison.Ordinal))
                        {
                            obj[fieldName] = JsonValue.Create(cleanString);
                        }
                    }
                    else if (childVal is JsonObject or JsonArray)
                    {
                        SanitizeJsonNode(childVal, childPath, knownSecrets, findings);
                    }
                    // Числа, булевы и null остаются без изменений
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                if (item is null) continue;

                string itemPath = $"{currentPath}[{i}]";
                if (item is JsonValue val && val.TryGetValue<string>(out var stringVal))
                {
                    string cleanString = SanitizeString(stringVal, itemPath, knownSecrets, findings);
                    if (!string.Equals(cleanString, stringVal, StringComparison.Ordinal))
                    {
                        arr[i] = JsonValue.Create(cleanString);
                    }
                }
                else if (item is JsonObject or JsonArray)
                {
                    SanitizeJsonNode(item, itemPath, knownSecrets, findings);
                }
            }
        }
    }

    private string SanitizeString(
        string text,
        string contextPath,
        IReadOnlyCollection<string> knownSecrets,
        List<SanitizeFinding> findings)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string result = text;

        // 1. Приватные ключи (критичные -> Block)
        var pkMatch = PrivateKeyRegex.Match(result);
        if (pkMatch.Success)
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.PrivateKey,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.PrivateKey),
                Description = "PEM Private Key detected"
            });
            return _options.MaskPlaceholder;
        }

        // 2. Строки подключения с паролями (критичные -> Block)
        var uriMatch = UriCredentialsRegex.Match(result);
        if (uriMatch.Success)
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.ConnectionString,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.ConnectionString),
                Description = "URI Connection string with credentials detected"
            });
            return _options.MaskPlaceholder;
        }

        var adoMatch = AdoNetCredentialsRegex.Match(result);
        if (adoMatch.Success)
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.ConnectionString,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.ConnectionString),
                Description = "ADO.NET Connection string with password detected"
            });
            return _options.MaskPlaceholder;
        }

        // 3. Известные секреты приложения
        foreach (var secret in knownSecrets.OrderByDescending(s => s.Length))
        {
            if (secret.Length >= 4 && result.Contains(secret, StringComparison.Ordinal))
            {
                findings.Add(new SanitizeFinding
                {
                    Class = SensitivityClass.KnownSecret,
                    PathOrPosition = contextPath,
                    Action = ResolveAction(SensitivityClass.KnownSecret),
                    Description = "Known application secret matched"
                });
                result = result.Replace(secret, _options.MaskPlaceholder, StringComparison.Ordinal);
            }
        }

        // 4. Префиксные API-ключи (sk-, ghp_, gho_, glpat-, xox...)
        result = PrefixedApiKeyRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.ApiKey,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.ApiKey),
                Description = "Provider API key pattern detected"
            });
            return _options.MaskPlaceholder;
        });

        // 5. Bearer и JWT токены
        result = BearerTokenRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.Token,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.Token),
                Description = "Bearer authorization token detected"
            });
            return $"Bearer {_options.MaskPlaceholder}";
        });

        result = JwtRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.Token,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.Token),
                Description = "JWT token detected"
            });
            return _options.MaskPlaceholder;
        });

        // 6. Инлайн пары ключ=значение в тексте (пароли, ключи)
        result = InlineKeyValueSecretRegex.Replace(result, match =>
        {
            string keyPart = match.Groups[1].Value;
            string valPart = match.Groups[2].Value;
            var detectedClass = ClassifyFieldName(keyPart) ?? SensitivityClass.Password;

            findings.Add(new SanitizeFinding
            {
                Class = detectedClass,
                PathOrPosition = $"{contextPath}:{keyPart}",
                Action = ResolveAction(detectedClass),
                Description = $"Inline key-value secret detected for '{keyPart}'"
            });

            return match.Value.Replace(valPart, _options.MaskPlaceholder, StringComparison.Ordinal);
        });

        // 7. Номера карт с валидацией Луна
        result = CardCandidateRegex.Replace(result, match =>
        {
            string raw = match.Value;
            string digits = Regex.Replace(raw, @"\D", "");
            if (ValidateLuhn(digits))
            {
                findings.Add(new SanitizeFinding
                {
                    Class = SensitivityClass.CardNumber,
                    PathOrPosition = contextPath,
                    Action = ResolveAction(SensitivityClass.CardNumber),
                    Description = "Credit card number (Luhn validated) detected"
                });
                return MaskCard(raw);
            }
            return raw;
        });

        // 8. Персональные данные: E-mail
        result = EmailRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.Email,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.Email),
                Description = "Email address detected"
            });
            return MaskEmail(match.Value);
        });

        // 9. Персональные данные: Номера телефонов
        result = PhoneRegex.Replace(result, match =>
        {
            string digits = Regex.Replace(match.Value, @"\D", "");
            if (digits.Length >= 10 && digits.Length <= 15)
            {
                findings.Add(new SanitizeFinding
                {
                    Class = SensitivityClass.Phone,
                    PathOrPosition = contextPath,
                    Action = ResolveAction(SensitivityClass.Phone),
                    Description = "Phone number detected"
                });
                return MaskPhone(match.Value);
            }
            return match.Value;
        });

        // 10. СНИЛС / Паспорт / SSN
        result = SnilsRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.HighEntropyValue,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.HighEntropyValue),
                Description = "SNILS-like identifier detected"
            });
            return _options.MaskPlaceholder;
        });

        result = RuPassportRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.HighEntropyValue,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.HighEntropyValue),
                Description = "Passport number detected"
            });
            return _options.MaskPlaceholder;
        });

        result = SsnRegex.Replace(result, match =>
        {
            findings.Add(new SanitizeFinding
            {
                Class = SensitivityClass.HighEntropyValue,
                PathOrPosition = contextPath,
                Action = ResolveAction(SensitivityClass.HighEntropyValue),
                Description = "SSN-like identifier detected"
            });
            return _options.MaskPlaceholder;
        });

        // 11. Токены высокой энтропии
        result = HighEntropyTokenCandidateRegex.Replace(result, match =>
        {
            string token = match.Value;
            if (token.Length >= _options.HighEntropyMinLength &&
                !token.Equals(_options.MaskPlaceholder, StringComparison.Ordinal) &&
                !token.Contains("[REDACTED", StringComparison.Ordinal))
            {
                double entropy = CalculateShannonEntropy(token);
                if (entropy >= _options.HighEntropyThreshold)
                {
                    findings.Add(new SanitizeFinding
                    {
                        Class = SensitivityClass.HighEntropyValue,
                        PathOrPosition = contextPath,
                        Action = ResolveAction(SensitivityClass.HighEntropyValue),
                        Description = $"High entropy token detected (entropy {entropy:F2})"
                    });
                    return _options.MaskPlaceholder;
                }
            }
            return token;
        });

        return result;
    }

    /// <summary>
    /// Нормализует имя поля и относит его к классу чувствительности по семантике (TASK-072).
    /// Поддерживает короткие, полные и развёрнутые словесные формы на русском и английском.
    /// </summary>
    public static SensitivityClass? ClassifyFieldName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;

        // Нормализация: убираем регистр, пробелы, дефисы, подчёркивания, двоеточия
        string norm = Regex.Replace(fieldName.ToLowerInvariant(), @"[^a-z0-9а-яё]", "");

        if (norm.Contains("privatekey") || norm.Contains("приватныйключ"))
            return SensitivityClass.PrivateKey;

        if (norm.Contains("connectionstring") || norm.Contains("connstr") || norm.Contains("строкаподключения"))
            return SensitivityClass.ConnectionString;

        if (norm.Contains("password") || norm.Contains("пароль") || norm.EndsWith("pwd") || norm == "pass")
            return SensitivityClass.Password;

        if (norm.Contains("apikey") || norm == "key" || norm.EndsWith("secretkey") ||
            norm.Contains("ключдоступа") || norm.Contains("apiclientsecret") || norm.Contains("accesskey"))
            return SensitivityClass.ApiKey;

        if (norm.Contains("token") || norm.Contains("bearer") || norm.Contains("jwt") ||
            norm.Contains("accesstoken") || norm.Contains("authtoken") || norm.Contains("токен"))
            return SensitivityClass.Token;

        if (norm.Contains("secret") || norm.Contains("секрет"))
            return SensitivityClass.Password;

        if (norm.Contains("email") || norm.Contains("почта") || norm.Contains("mailaddress"))
            return SensitivityClass.Email;

        if (norm.Contains("phone") || norm.Contains("телефон") || norm.Contains("mobile") || norm.Contains("tel"))
            return SensitivityClass.Phone;

        if (norm.Contains("cardnumber") || norm.Contains("creditcard") || norm.Contains("номеркарты"))
            return SensitivityClass.CardNumber;

        return null;
    }

    /// <summary>Проверяет номер по алгоритму Луна.</summary>
    public static bool ValidateLuhn(string digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length < 13 || digits.Length > 19)
            return false;

        for (int i = 0; i < digits.Length; i++)
        {
            if (!char.IsDigit(digits[i])) return false;
        }

        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    /// <summary>Вычисляет информационную энтропию Шеннона строки.</summary>
    public static double CalculateShannonEntropy(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0.0;

        var charCounts = new Dictionary<char, int>();
        foreach (char c in s)
        {
            charCounts[c] = charCounts.GetValueOrDefault(c) + 1;
        }

        double len = s.Length;
        double entropy = 0.0;
        foreach (var count in charCounts.Values)
        {
            double p = count / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private SanitizeAction ResolveAction(SensitivityClass sensitivityClass)
    {
        if (_options.ClassActions.TryGetValue(sensitivityClass, out var action))
        {
            return action;
        }

        return sensitivityClass switch
        {
            SensitivityClass.PrivateKey => SanitizeAction.Block,
            SensitivityClass.ConnectionString => SanitizeAction.Block,
            SensitivityClass.CardNumber => SanitizeAction.PartialMask,
            SensitivityClass.Email => SanitizeAction.PartialMask,
            SensitivityClass.Phone => SanitizeAction.PartialMask,
            _ => SanitizeAction.Mask
        };
    }

    private string ApplyPartialMask(string raw, SensitivityClass sensitivityClass) =>
        sensitivityClass switch
        {
            SensitivityClass.Email => MaskEmail(raw),
            SensitivityClass.CardNumber => MaskCard(raw),
            SensitivityClass.Phone => MaskPhone(raw),
            _ => _options.MaskPlaceholder
        };

    private static string MaskEmail(string email)
    {
        int atIdx = email.IndexOf('@');
        if (atIdx <= 0) return "[REDACTED:EMAIL]";

        string user = email[..atIdx];
        string domain = email[atIdx..];

        if (user.Length <= 2)
            return $"{user[0]}***{domain}";

        return $"{user[0]}***{user[^1]}{domain}";
    }

    private static string MaskCard(string card)
    {
        string digits = Regex.Replace(card, @"\D", "");
        if (digits.Length < 8) return "[REDACTED:CARD]";

        return $"{digits[..4]}********{digits[^4..]}";
    }

    private static string MaskPhone(string phone)
    {
        string digits = Regex.Replace(phone, @"\D", "");
        if (digits.Length < 4) return "[REDACTED:PHONE]";

        return $"+***-***-**-{digits[^2..]}";
    }

    private static long CalculatePayloadSizeBytes(EgressPayload payload)
    {
        long bytes = 0;
        if (!string.IsNullOrEmpty(payload.TextContent))
        {
            bytes += Encoding.UTF8.GetByteCount(payload.TextContent);
        }

        if (!string.IsNullOrEmpty(payload.JsonContent))
        {
            bytes += Encoding.UTF8.GetByteCount(payload.JsonContent);
        }

        if (payload.KeyValueContent is not null)
        {
            foreach (var (k, v) in payload.KeyValueContent)
            {
                bytes += Encoding.UTF8.GetByteCount(k);
                if (!string.IsNullOrEmpty(v))
                {
                    bytes += Encoding.UTF8.GetByteCount(v);
                }
            }
        }

        return bytes;
    }
}
