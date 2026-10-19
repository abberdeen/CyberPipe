namespace CyberPipe.Tests.Unit;

using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using CyberPipe.Models;
using Xunit;

public sealed class ModelCatalogTests
{
    private static string CatalogSourcePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "CyberPipe.Models", "Catalogs", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Catalog file {fileName} not found above the test assembly.");
    }

    private static FileModelCatalog LoadShipped() =>
        FileModelCatalog.Load(
            CatalogSourcePath("local-models.json"),
            CatalogSourcePath("cloud-models.json"),
            CatalogSourcePath("embedding-models.json"));

    private static string WriteTempCatalog(string sourceFileName, Func<JsonNode, JsonNode> mutate)
    {
        string text = File.ReadAllText(CatalogSourcePath(sourceFileName));
        JsonNode mutated = mutate(JsonNode.Parse(text)!);
        string temp = Path.Combine(Path.GetTempPath(), $"cyberpipe-catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(temp, mutated.ToJsonString());
        return temp;
    }

    private static FileModelCatalog LoadWithLocalReplacement(string localTempPath) =>
        FileModelCatalog.Load(
            localTempPath,
            CatalogSourcePath("cloud-models.json"),
            CatalogSourcePath("embedding-models.json"));

    [Fact]
    public void ShippedCatalogs_ContainExactly15LocalAnd8CloudModels()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.Equal(15, catalog.LocalModels.Count);
        Assert.Equal(8, catalog.CloudModels.Count);
        Assert.False(string.IsNullOrWhiteSpace(catalog.LocalCatalogVersion));
        Assert.False(string.IsNullOrWhiteSpace(catalog.CloudCatalogVersion));
        Assert.False(string.IsNullOrWhiteSpace(catalog.EmbeddingCatalogVersion));
    }

    [Fact]
    public void ShippedCatalogs_ModelIdsAreUniqueWithinAndAcrossCatalogs()
    {
        FileModelCatalog catalog = LoadShipped();

        string[] ids = catalog.LocalModels.Select(m => m.Id)
            .Concat(catalog.CloudModels.Select(m => m.Id))
            .Append(catalog.EmbeddingModel.Model.Id)
            .Concat(catalog.AlternativeEmbeddingModels.Select(e => e.Model.Id))
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ShippedLocalModels_UseOnlyTier2OrTier3Categories()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.All(catalog.LocalModels, m =>
            Assert.True(
                m.RecommendedFor is ModelTaskCategory.LightFastTier2 or ModelTaskCategory.HeavyReasoningTier3,
                $"Local model {m.Id} must target Tier 2 or Tier 3."));
        Assert.Contains(catalog.LocalModels, m => m.RecommendedFor == ModelTaskCategory.LightFastTier2);
        Assert.Contains(catalog.LocalModels, m => m.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3);
    }

    [Fact]
    public void ShippedCloudModels_CoverBothTier2AndTier3()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.All(catalog.CloudModels, m => Assert.Equal(ModelHostingType.RemoteCloud, m.HostingType));
        Assert.Contains(catalog.CloudModels, m => m.RecommendedFor == ModelTaskCategory.LightFastTier2);
        Assert.Contains(catalog.CloudModels, m => m.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3);
    }

    [Fact]
    public void ShippedEmbeddingCatalog_ExposesMultilingualE5SmallWithDimensions()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.Equal("multilingual-e5-small", catalog.EmbeddingModel.Model.Id);
        Assert.Equal(ModelTaskCategory.EmbeddingTier1, catalog.EmbeddingModel.Model.RecommendedFor);
        Assert.True(catalog.EmbeddingModel.VectorDimensions > 0);
        Assert.False(string.IsNullOrWhiteSpace(catalog.EmbeddingModel.Normalization));
        Assert.NotNull(catalog.GetById("multilingual-e5-small"));
    }

    [Fact]
    public void CatalogRecords_RoundTripThroughDtoWithoutFieldLoss()
    {
        FileModelCatalog catalog = LoadShipped();

        foreach (ModelDescriptorDto model in catalog.LocalModels.Concat(catalog.CloudModels).Append(catalog.EmbeddingModel.Model))
        {
            string json = JsonSerializer.Serialize(model);
            var restored = JsonSerializer.Deserialize<ModelDescriptorDto>(json);
            Assert.Equal(model, restored);
        }
    }

    [Fact]
    public void CatalogContent_ComesFromJsonNotCode()
    {
        string temp = WriteTempCatalog("cloud-models.json", root =>
        {
            var node = root.AsObject();
            var models = node["models"]!.AsArray();
            models.RemoveAt(models.Count - 1);
            return node;
        });
        try
        {
            FileModelCatalog catalog = FileModelCatalog.Load(
                CatalogSourcePath("local-models.json"),
                temp,
                CatalogSourcePath("embedding-models.json"),
                expectedLocalCount: 15,
                expectedCloudCount: 7);

            Assert.Equal(7, catalog.CloudModels.Count);
            Assert.DoesNotContain(catalog.CloudModels, m => m.Id == "deepseek-r1-cloud");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void DeepSeekDistillEntry_KeepsBothSizeVariantsInSingleRecord()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.Single(catalog.LocalModels, m => m.Id == "deepseek-r1-distill");

        string text = File.ReadAllText(CatalogSourcePath("local-models.json"));
        JsonNode root = JsonNode.Parse(text)!;
        JsonObject entry = root["models"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["id"]!.GetValue<string>() == "deepseek-r1-distill");

        Assert.Equal(2, entry["variants"]!.AsArray().Count);
    }

    [Fact]
    public void ModelSources_ContainNoHardcodedPricesOrSizes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? modelsDir = null;
        while (dir is not null)
        {
            var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "src", "CyberPipe.Models"));
            if (candidate.Exists)
            {
                modelsDir = candidate;
                break;
            }

            dir = dir.Parent;
        }

        Assert.NotNull(modelsDir);
        string[] sources = Directory.GetFiles(modelsDir!.FullName, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(sources);

        string[] forbiddenLiterals = ["0.075", "2.19", "15.00", "10.00", "gemini-2.5-pro", "qwen3:8b"];
        foreach (string file in sources)
        {
            string text = File.ReadAllText(file);
            foreach (string literal in forbiddenLiterals)
            {
                Assert.False(
                    text.Contains(literal, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} must not hardcode catalog value {literal}.");
            }
        }
    }

    [Fact]
    public void GetById_FindsModelsAcrossCatalogs()
    {
        FileModelCatalog catalog = LoadShipped();

        Assert.Equal("Qwen3-8B", catalog.GetById("qwen3-8b")!.Name);
        Assert.Equal("GPT-4o-mini", catalog.GetById("gpt-4o-mini")!.Name);
        Assert.Null(catalog.GetById("no-such-model"));
        Assert.Null(catalog.GetById(""));
    }

    [Fact]
    public void Find_FiltersByCategoryAndHosting()
    {
        FileModelCatalog catalog = LoadShipped();

        IReadOnlyList<ModelDescriptorDto> tier2Local =
            catalog.Find(ModelTaskCategory.LightFastTier2, ModelHostingType.Local);
        Assert.NotEmpty(tier2Local);
        Assert.All(tier2Local, m =>
        {
            Assert.Equal(ModelTaskCategory.LightFastTier2, m.RecommendedFor);
            Assert.Equal(ModelHostingType.Local, m.HostingType);
        });

        IReadOnlyList<ModelDescriptorDto> cloud =
            catalog.Find(hostingType: ModelHostingType.RemoteCloud);
        Assert.Equal(8, cloud.Count);

        IReadOnlyList<ModelDescriptorDto> embedding =
            catalog.Find(category: ModelTaskCategory.EmbeddingTier1);
        Assert.Contains(embedding, m => m.Id == "multilingual-e5-small");
    }

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    public void LocalCatalog_WrongRecordCount_IsRejected(int keep)
    {
        string temp = WriteTempCatalog("local-models.json", root =>
        {
            var node = root.AsObject();
            var models = node["models"]!.AsArray();
            while (models.Count > keep)
            {
                models.RemoveAt(models.Count - 1);
            }

            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => LoadWithLocalReplacement(temp));
            Assert.Contains(temp, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void CloudCatalog_WrongRecordCount_IsRejected()
    {
        string temp = WriteTempCatalog("cloud-models.json", root =>
        {
            var node = root.AsObject();
            var models = node["models"]!.AsArray();
            models.RemoveAt(0);
            return node;
        });
        try
        {
            Assert.Throws<ModelCatalogException>(() => FileModelCatalog.Load(
                CatalogSourcePath("local-models.json"),
                temp,
                CatalogSourcePath("embedding-models.json")));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Catalog_DuplicateId_IsRejectedWithFileAndId()
    {
        string temp = WriteTempCatalog("local-models.json", root =>
        {
            var node = root.AsObject();
            var models = node["models"]!.AsArray();
            models.Add(JsonNode.Parse(models[0]!.ToJsonString()));
            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => LoadWithLocalReplacement(temp));
            Assert.Contains(temp, ex.Message, StringComparison.Ordinal);
            Assert.Contains("qwen3-8b", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void LocalModel_WithoutQ4Size_IsRejected()
    {
        string temp = WriteTempCatalog("local-models.json", root =>
        {
            var node = root.AsObject();
            node["models"]![0]!.AsObject().Remove("q4SizeGb");
            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => LoadWithLocalReplacement(temp));
            Assert.Contains("qwen3-8b", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void CloudModel_WithoutPrices_IsRejected()
    {
        string temp = WriteTempCatalog("cloud-models.json", root =>
        {
            var node = root.AsObject();
            node["models"]![0]!.AsObject().Remove("inputPricePer1mUsd");
            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => FileModelCatalog.Load(
                CatalogSourcePath("local-models.json"),
                temp,
                CatalogSourcePath("embedding-models.json")));
            Assert.Contains("gemini-2.5-flash-lite", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Catalog_DefaultContextAboveWindow_IsRejected()
    {
        string temp = WriteTempCatalog("local-models.json", root =>
        {
            var node = root.AsObject();
            node["models"]![0]!.AsObject()["defaultContextTokens"] = 65536;
            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => LoadWithLocalReplacement(temp));
            Assert.Contains("qwen3-8b", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void CrossCatalog_DuplicateId_IsRejected()
    {
        string temp = WriteTempCatalog("cloud-models.json", root =>
        {
            var node = root.AsObject();
            node["models"]![0]!.AsObject()["id"] = "qwen3-8b";
            return node;
        });
        try
        {
            ModelCatalogException ex = Assert.Throws<ModelCatalogException>(() => FileModelCatalog.Load(
                CatalogSourcePath("local-models.json"),
                temp,
                CatalogSourcePath("embedding-models.json")));
            Assert.Contains("qwen3-8b", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
