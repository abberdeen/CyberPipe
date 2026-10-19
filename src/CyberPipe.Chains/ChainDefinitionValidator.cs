using System.Text.Json;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;

namespace CyberPipe.Chains;

/// <summary>
/// Валидатор определений цепочек (TASK-040).
/// Проверяет строго 4-шаговую линейную структуру Trigger → Skill → Script → Delivery
/// и отвергает любые признаки ветвлений, условий, графов или вторичных скриптов.
/// </summary>
public sealed partial class ChainDefinitionValidator : IChainDefinitionValidator
{
    private static readonly HashSet<string> AllowedTopLevelProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "name",
        "isenabled",
        "trigger",
        "skill",
        "script",
        "delivery",
        "requireshitl",
        "retrypolicy",
        "misfirepolicy",
        "version",
        "createdatutc",
        "updatedatutc",
    };

    private static readonly HashSet<string> BranchingPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nodes",
        "edges",
        "condition",
        "conditions",
        "transition",
        "transitions",
        "branch",
        "branches",
        "switch",
        "parallel",
        "parallelism",
        "if",
        "else",
        "llm",
        "llm_step",
        "llmstep",
        "script2",
        "scripts",
        "second_script",
        "secondscript",
        "skills",
        "triggers",
        "subchains",
        "sub_chains",
        "workflow",
        "graph",
    };

    private static readonly HashSet<string> KnownTriggerKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ChainTriggerKind.Webhook),
        nameof(ChainTriggerKind.Cron),
        nameof(ChainTriggerKind.FunctionCall),
        nameof(ChainTriggerKind.Chat),
    };

    private static readonly HashSet<string> AllowedMappingSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "trigger",
        "skill",
        "script",
    };

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex KebabCaseSlugRegex();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled)]
    private static partial Regex SemVerRegex();

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex TemplateInterpolationRegex();

    /// <inheritdoc />
    public ChainValidationResult Validate(ChainDefinitionDto definition)
    {
        if (definition is null)
        {
            return ChainValidationResult.Failure(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "$",
                Message = "Chain definition cannot be null.",
            });
        }

        var errors = new List<ChainValidationError>();

        // Id & Name
        if (definition.Id.Value == Guid.Empty)
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Id",
                Message = "Chain Id must be non-empty.",
            });
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Name",
                Message = "Chain Name is required.",
            });
        }

        // 1. Trigger step
        if (definition.Trigger is null)
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Trigger",
                Message = "Trigger step is required.",
            });
        }
        else
        {
            ValidateTrigger(definition.Trigger, errors);
        }

        // 2. Skill step
        if (definition.Skill is null)
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Skill",
                Message = "Skill step is required.",
            });
        }
        else
        {
            ValidateSkill(definition.Skill, errors);
        }

        // 3. Script step
        if (definition.Script is null)
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Script",
                Message = "Script step is required.",
            });
        }
        else
        {
            ValidateScript(definition.Script, errors);
        }

        // 4. Delivery step
        if (definition.Delivery is null)
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Delivery",
                Message = "Delivery step is required.",
            });
        }
        else
        {
            ValidateDelivery(definition.Delivery, errors);
        }

        return errors.Count == 0
            ? ChainValidationResult.Success
            : ChainValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public ChainValidationResult ValidateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ChainValidationResult.Failure(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "$",
                Message = "Chain definition JSON cannot be empty.",
            });
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ChainValidationResult.Failure(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "$",
                Message = $"Malformed JSON: {ex.Message}",
            });
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ChainValidationResult.Failure(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "$",
                    Message = "Root of chain definition must be a JSON object.",
                });
            }

            var errors = new List<ChainValidationError>();
            JsonElement root = doc.RootElement;

            // Check for branching / DAG / non-linear keys
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (BranchingPropertyNames.Contains(prop.Name))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.BranchingNotSupported,
                        Path = prop.Name,
                        Message = $"Branching, parallel, DAG, condition, or multi-step structures ('{prop.Name}') are not supported.",
                    });
                }
                else if (!AllowedTopLevelProperties.Contains(prop.Name))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.UnknownProperty,
                        Path = prop.Name,
                        Message = $"Unknown top-level property '{prop.Name}'.",
                    });
                }
            }

            // Check mandatory steps in JSON
            bool hasTrigger = root.TryGetProperty("trigger", out JsonElement triggerElement)
                || root.TryGetProperty("Trigger", out triggerElement);
            bool hasSkill = root.TryGetProperty("skill", out _) || root.TryGetProperty("Skill", out _);
            bool hasScript = root.TryGetProperty("script", out _) || root.TryGetProperty("Script", out _);
            bool hasDelivery = root.TryGetProperty("delivery", out _) || root.TryGetProperty("Delivery", out _);

            if (!hasTrigger || triggerElement.ValueKind == JsonValueKind.Null)
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "Trigger",
                    Message = "Trigger step is required.",
                });
            }
            else
            {
                // Check Trigger.Kind string value before deserialization
                bool hasKind = triggerElement.TryGetProperty("kind", out JsonElement kindElement)
                    || triggerElement.TryGetProperty("Kind", out kindElement);

                if (!hasKind || kindElement.ValueKind != JsonValueKind.String)
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.MissingStep,
                        Path = "Trigger.Kind",
                        Message = "Trigger Kind is required.",
                    });
                }
                else
                {
                    string? kindStr = kindElement.GetString();
                    if (string.IsNullOrWhiteSpace(kindStr) || !KnownTriggerKinds.Contains(kindStr))
                    {
                        errors.Add(new ChainValidationError
                        {
                            Code = ChainErrorCodes.UnknownTriggerKind,
                            Path = "Trigger.Kind",
                            Message = $"Unknown trigger kind '{kindStr}'. Supported kinds: Webhook, Cron, FunctionCall, Chat.",
                        });
                    }
                }
            }

            if (!hasSkill)
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "Skill",
                    Message = "Skill step is required.",
                });
            }

            if (!hasScript)
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "Script",
                    Message = "Script step is required.",
                });
            }

            if (!hasDelivery)
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "Delivery",
                    Message = "Delivery step is required.",
                });
            }

            // If any critical step or trigger kind error occurred, don't attempt deserialization
            if (errors.Count > 0)
            {
                return ChainValidationResult.Failure(errors);
            }

            // Deserialization & deep DTO validation
            ChainDefinitionDto dto;
            try
            {
                dto = ChainDefinitionSerializer.Deserialize(json);
            }
            catch (Exception ex)
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.MissingStep,
                    Path = "$",
                    Message = $"Deserialization error: {ex.Message}",
                });
                return ChainValidationResult.Failure(errors);
            }

            ChainValidationResult dtoResult = Validate(dto);
            if (!dtoResult.IsValid)
            {
                errors.AddRange(dtoResult.Errors);
            }

            return errors.Count == 0
                ? ChainValidationResult.Success
                : ChainValidationResult.Failure(errors);
        }
    }

    private static void ValidateTrigger(ChainTriggerDto trigger, List<ChainValidationError> errors)
    {
        if (!Enum.IsDefined(typeof(ChainTriggerKind), trigger.Kind))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.UnknownTriggerKind,
                Path = "Trigger.Kind",
                Message = $"Unknown trigger kind '{(int)trigger.Kind}'.",
            });
            return;
        }

        switch (trigger.Kind)
        {
            case ChainTriggerKind.Webhook:
                if (string.IsNullOrWhiteSpace(trigger.WebhookPath))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.MissingStep,
                        Path = "Trigger.WebhookPath",
                        Message = "WebhookPath is required for Webhook trigger.",
                    });
                }
                break;

            case ChainTriggerKind.Cron:
                if (string.IsNullOrWhiteSpace(trigger.CronExpression))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.MissingStep,
                        Path = "Trigger.CronExpression",
                        Message = "CronExpression is required for Cron trigger.",
                    });
                }
                break;

            case ChainTriggerKind.FunctionCall:
                if (string.IsNullOrWhiteSpace(trigger.FunctionName))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.MissingStep,
                        Path = "Trigger.FunctionName",
                        Message = "FunctionName is required for FunctionCall trigger.",
                    });
                }
                break;

            case ChainTriggerKind.Chat:
                if (string.IsNullOrWhiteSpace(trigger.ChatFilter))
                {
                    errors.Add(new ChainValidationError
                    {
                        Code = ChainErrorCodes.MissingStep,
                        Path = "Trigger.ChatFilter",
                        Message = "ChatFilter is required for Chat trigger.",
                    });
                }
                break;
        }
    }

    private static void ValidateSkill(ChainSkillStepDto skill, List<ChainValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(skill.SkillId))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Skill.SkillId",
                Message = "SkillId is required.",
            });
        }

        if (skill.InputMapping is not null)
        {
            foreach (var (key, template) in skill.InputMapping)
            {
                ValidateMappingExpression(
                    template,
                    $"Skill.InputMapping[{key}]",
                    errors,
                    allowedSources: ["trigger"]);
            }
        }
    }

    private static void ValidateScript(ChainScriptStepDto script, List<ChainValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(script.PackageSlug))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Script.PackageSlug",
                Message = "PackageSlug is required.",
            });
        }
        else if (!KebabCaseSlugRegex().IsMatch(script.PackageSlug))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.InvalidSlug,
                Path = "Script.PackageSlug",
                Message = $"PackageSlug '{script.PackageSlug}' must be lowercase kebab-case.",
            });
        }

        if (string.IsNullOrWhiteSpace(script.PackageVersion))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Script.PackageVersion",
                Message = "PackageVersion is required.",
            });
        }
        else if (!SemVerRegex().IsMatch(script.PackageVersion))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.InvalidVersion,
                Path = "Script.PackageVersion",
                Message = $"PackageVersion '{script.PackageVersion}' must be valid SemVer.",
            });
        }

        if (script.InputMapping is not null)
        {
            foreach (var (key, template) in script.InputMapping)
            {
                ValidateMappingExpression(
                    template,
                    $"Script.InputMapping[{key}]",
                    errors,
                    allowedSources: ["trigger", "skill"]);
            }
        }

        if (script.FileInputBindings is not null)
        {
            foreach (var (key, template) in script.FileInputBindings)
            {
                ValidateMappingExpression(
                    template,
                    $"Script.FileInputBindings[{key}]",
                    errors,
                    allowedSources: ["trigger", "skill"]);
            }
        }
    }

    private static void ValidateDelivery(ChainDeliveryStepDto delivery, List<ChainValidationError> errors)
    {
        if (!Enum.IsDefined(typeof(ChannelKind), delivery.Channel))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Delivery.Channel",
                Message = $"Unknown delivery channel '{(int)delivery.Channel}'.",
            });
        }

        if (string.IsNullOrWhiteSpace(delivery.Target))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Delivery.Target",
                Message = "Delivery Target is required.",
            });
        }

        if (string.IsNullOrWhiteSpace(delivery.MessageTemplate))
        {
            errors.Add(new ChainValidationError
            {
                Code = ChainErrorCodes.MissingStep,
                Path = "Delivery.MessageTemplate",
                Message = "Delivery MessageTemplate is required.",
            });
        }
        else
        {
            ValidateMappingExpression(
                delivery.MessageTemplate,
                "Delivery.MessageTemplate",
                errors,
                allowedSources: ["trigger", "skill", "script"]);
        }
    }

    private static void ValidateMappingExpression(
        string template,
        string path,
        List<ChainValidationError> errors,
        string[] allowedSources)
    {
        if (string.IsNullOrEmpty(template))
        {
            return;
        }

        MatchCollection matches = TemplateInterpolationRegex().Matches(template);
        foreach (Match match in matches)
        {
            string expression = match.Groups[1].Value.Trim();
            int dotIndex = expression.IndexOf('.');
            string root = dotIndex > 0 ? expression[..dotIndex] : expression;

            // Check if root is fundamentally recognized as a chain mapping source
            if (!AllowedMappingSources.Contains(root))
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.UnknownMappingSource,
                    Path = path,
                    Message = $"Unknown mapping source '{root}'. Only 'trigger', 'skill', and 'script' are supported.",
                });
                continue;
            }

            // Check if root is available at this step of the linear chain
            if (!allowedSources.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new ChainValidationError
                {
                    Code = ChainErrorCodes.UnknownMappingSource,
                    Path = path,
                    Message = $"Mapping source '{root}' is not available at this step (available: {string.Join(", ", allowedSources)}).",
                });
            }
        }
    }
}
