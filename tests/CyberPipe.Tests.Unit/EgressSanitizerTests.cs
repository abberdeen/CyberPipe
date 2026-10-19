namespace CyberPipe.Tests.Unit;

using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using CyberPipe.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class EgressSanitizerTests
{
    private sealed class FakeKnownSecretsProvider(params string[] secrets) : IKnownSecretsProvider
    {
        public Task<IReadOnlyCollection<string>> GetKnownSecretValuesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(secrets);
    }

    [Fact]
    public async Task CleanPayload_Text_ReturnsAllowedUnchanged()
    {
        var sanitizer = new EgressSanitizer();
        var payload = EgressPayload.Text("Hello world! This is a completely safe message.");

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.Empty(result.Findings);
        Assert.NotNull(result.SanitizedPayload);
        Assert.Equal(payload.TextContent, result.SanitizedPayload.TextContent);
    }

    [Fact]
    public async Task CleanPayload_Json_PreservesTypesAndStructure()
    {
        var sanitizer = new EgressSanitizer();
        string cleanJson = """
        {
          "id": 101,
          "name": "Local Assistant",
          "active": true,
          "ratio": 3.14,
          "items": ["apple", "banana"]
        }
        """;
        var payload = EgressPayload.Json(cleanJson);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.Empty(result.Findings);
        Assert.NotNull(result.SanitizedPayload);

        var parsed = JsonNode.Parse(result.SanitizedPayload.JsonContent!);
        Assert.NotNull(parsed);
        Assert.Equal(101, parsed["id"]!.GetValue<int>());
        Assert.Equal("Local Assistant", parsed["name"]!.GetValue<string>());
        Assert.True(parsed["active"]!.GetValue<bool>());
        Assert.Equal(3.14, parsed["ratio"]!.GetValue<double>());
    }

    [Fact]
    public async Task SecretsInText_ApiKeyAndBearerJwt_MaskedAndAllowed()
    {
        var sanitizer = new EgressSanitizer();
        string rawText = "Calling API with sk-proj-1234567890abcdef1234567890 and Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var payload = EgressPayload.Text(rawText);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotEmpty(result.Findings);
        Assert.NotNull(result.SanitizedPayload);

        string sanitized = result.SanitizedPayload.TextContent!;
        Assert.DoesNotContain("sk-proj-1234567890abcdef1234567890", sanitized);
        Assert.DoesNotContain("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", sanitized);
        Assert.Contains("[REDACTED]", sanitized);

        // Убеждаемся, что в отчёте нет сырых секретов
        foreach (var finding in result.Findings)
        {
            Assert.DoesNotContain("sk-proj-1234567890abcdef1234567890", finding.Description ?? "");
            Assert.DoesNotContain("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", finding.Description ?? "");
        }
    }

    [Fact]
    public async Task PrivateKey_InText_BlocksPayload()
    {
        var sanitizer = new EgressSanitizer();
        string privateKeyPem = """
        -----BEGIN RSA PRIVATE KEY-----
        MIIEowIBAAKCAQEA0Y1+examplePrivateKeYContentNotReal1234567890
        -----END RSA PRIVATE KEY-----
        """;
        var payload = EgressPayload.Text($"My secret config: {privateKeyPem}");

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Blocked, result.Outcome);
        Assert.Null(result.SanitizedPayload);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.PrivateKey && f.Action == SanitizeAction.Block);
        Assert.Contains("PrivateKey", result.BlockReason);
    }

    [Fact]
    public async Task ConnectionString_WithPassword_BlocksPayload()
    {
        var sanitizer = new EgressSanitizer();
        string payloadText = "Database URI: postgresql://admin:superSecretPassword999@localhost:5432/cyberpipe";
        var payload = EgressPayload.Text(payloadText);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Blocked, result.Outcome);
        Assert.Null(result.SanitizedPayload);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.ConnectionString && f.Action == SanitizeAction.Block);
    }

    [Fact]
    public async Task KnownSecret_FromProvider_MaskedInPayload()
    {
        string secretValue = "superSecretTokenValue987654";
        var fakeSecrets = new FakeKnownSecretsProvider(secretValue);
        var sanitizer = new EgressSanitizer(new EgressSanitizerOptions(), fakeSecrets);

        var payload = EgressPayload.Text($"The secret code is {secretValue} inside notes.");
        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload);
        Assert.DoesNotContain(secretValue, result.SanitizedPayload.TextContent);
        Assert.Contains("[REDACTED]", result.SanitizedPayload.TextContent);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.KnownSecret);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("API key")]
    [InlineData("ключ доступа")]
    [InlineData("access_key")]
    [InlineData("password")]
    [InlineData("user_password")]
    [InlineData("пароль")]
    public async Task FieldNameNormalization_ShortFullAndLongPhrase_AllMaskedInKeyValue(string fieldName)
    {
        var sanitizer = new EgressSanitizer();
        var originalDict = new Dictionary<string, string?>
        {
            [fieldName] = "TopSecret123"
        };
        var payload = EgressPayload.KeyValue(originalDict);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload);
        Assert.NotNull(result.SanitizedPayload.KeyValueContent);
        Assert.Equal("[REDACTED]", result.SanitizedPayload.KeyValueContent[fieldName]);

        // Исходный словарь не должен быть мутирован
        Assert.Equal("TopSecret123", originalDict[fieldName]);
    }

    [Fact]
    public async Task NeutralFieldName_HighEntropyValue_Masked()
    {
        var sanitizer = new EgressSanitizer();
        // Строка высокой энтропии под нейтральным именем поля
        string highEntropyToken = "k7Z#m9P$v2L8*xQ1@w4^rT5!bY";
        var dict = new Dictionary<string, string?>
        {
            ["description"] = highEntropyToken,
            ["title"] = "Normal title"
        };
        var payload = EgressPayload.KeyValue(dict);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload?.KeyValueContent);
        Assert.Equal("[REDACTED]", result.SanitizedPayload.KeyValueContent["description"]);
        Assert.Equal("Normal title", result.SanitizedPayload.KeyValueContent["title"]);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.HighEntropyValue);
    }

    [Fact]
    public async Task CreditCard_ValidLuhn_PartialMasked_InvalidLuhn_NotTreatedAsCard()
    {
        var sanitizer = new EgressSanitizer();

        // 4532 0150 1234 5671 валиден по Луну (sum=50, 50%10==0)
        string validCard = "4532 0150 1234 5671";
        // 1234 5678 9012 3456 не проходит проверку Луна
        string invalidCard = "1234 5678 9012 3456";

        var payload = EgressPayload.Text($"Cards: Valid={validCard}, Invalid={invalidCard}");
        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload);

        string text = result.SanitizedPayload.TextContent!;
        // Валидная карта частично маскирована
        Assert.DoesNotContain("4532 0150 1234 5671", text);
        Assert.Contains("4532********5671", text);

        // Невалидная карта не маскируется как карта
        Assert.Contains(invalidCard, text);
    }

    [Fact]
    public async Task EmailAndPhone_PartialMasked()
    {
        var sanitizer = new EgressSanitizer();
        string email = "johndoe@example.com";
        string phone = "+7 (999) 123-45-67";
        var payload = EgressPayload.Text($"Contact: {email} or {phone}");

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload);

        string text = result.SanitizedPayload.TextContent!;
        Assert.DoesNotContain(email, text);
        Assert.Contains("j***e@example.com", text);
        Assert.DoesNotContain("123-45", text);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.Email);
        Assert.Contains(result.Findings, f => f.Class == SensitivityClass.Phone);
    }

    [Fact]
    public async Task Json_PreservesStructureAndMasksNestedSecret()
    {
        var sanitizer = new EgressSanitizer();
        string json = """
        {
          "status": "ok",
          "config": {
            "retries": 3,
            "apiKey": "sk-proj-xyz12345678901234567890"
          },
          "items": [1, 2, 3]
        }
        """;
        var payload = EgressPayload.Json(json);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Allowed, result.Outcome);
        Assert.NotNull(result.SanitizedPayload);

        var root = JsonNode.Parse(result.SanitizedPayload.JsonContent!);
        Assert.NotNull(root);
        Assert.Equal("ok", root["status"]!.GetValue<string>());
        Assert.Equal(3, root["config"]!["retries"]!.GetValue<int>());
        Assert.Equal("[REDACTED]", root["config"]!["apiKey"]!.GetValue<string>());
        Assert.Equal(3, root["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task MalformedJson_FailClosed_Blocked()
    {
        var sanitizer = new EgressSanitizer();
        var payload = EgressPayload.Json("{ bad json syntax without quotes or closing brace: ");

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Blocked, result.Outcome);
        Assert.Null(result.SanitizedPayload);
        Assert.Contains("Malformed JSON", result.BlockReason);
    }

    [Fact]
    public async Task PayloadSizeExceeded_FailClosed_Blocked()
    {
        var options = new EgressSanitizerOptions { MaxPayloadSizeBytes = 100 };
        var sanitizer = new EgressSanitizer(options);

        string largeText = new string('x', 200);
        var payload = EgressPayload.Text(largeText);

        var result = await sanitizer.SanitizeAsync(payload);

        Assert.Equal(EgressSanitizeOutcome.Blocked, result.Outcome);
        Assert.Null(result.SanitizedPayload);
        Assert.Contains("exceeds maximum allowed limit", result.BlockReason);
    }

    [Fact]
    public void ShannonEntropy_ComputesCorrectly()
    {
        double zeroEntropy = EgressSanitizer.CalculateShannonEntropy("aaaaaaaaaa");
        Assert.Equal(0.0, zeroEntropy, 3);

        double highEntropy = EgressSanitizer.CalculateShannonEntropy("a9Fj28Zq0L1mWx7VbN4p");
        Assert.True(highEntropy > 4.0, $"Expected high entropy > 4.0, got {highEntropy}");
    }

    [Fact]
    public void LuhnAlgorithm_ValidatesCorrectly()
    {
        Assert.True(EgressSanitizer.ValidateLuhn("4532015012345671"));
        Assert.False(EgressSanitizer.ValidateLuhn("4532015012345674"));
        Assert.False(EgressSanitizer.ValidateLuhn("123"));
        Assert.False(EgressSanitizer.ValidateLuhn(""));
    }
}
