using CyberPipe.Data;
using Xunit;

namespace CyberPipe.Data.Tests;

public sealed class SchemaMigrationCatalogTests
{
    private static MigrationDefinition Def(int version, bool reversible = true, bool withDown = true) => new()
    {
        Version = version,
        Slug = $"m{version}",
        UpSql = $"-- reversible: {(reversible ? "true" : "false")}\nCREATE TABLE t{version} (id int);",
        DownSql = withDown ? $"DROP TABLE t{version};" : null,
        IsReversible = reversible,
    };

    [Fact]
    public void Validate_OrdersByVersion()
    {
        IReadOnlyList<MigrationDefinition> ordered = MigrationCatalog.Validate([Def(3), Def(1), Def(2)]);
        Assert.Equal([1, 2, 3], ordered.Select(m => m.Version));
    }

    [Fact]
    public void Validate_DetectsNumberingGap() =>
        Assert.Throws<MigrationCatalogException>(() => MigrationCatalog.Validate([Def(1), Def(3)]));

    [Fact]
    public void Validate_DetectsDuplicateVersion() =>
        Assert.Throws<MigrationCatalogException>(() => MigrationCatalog.Validate([Def(1), Def(1)]));

    [Fact]
    public void Validate_RejectsReversibleWithoutDown() =>
        Assert.Throws<MigrationCatalogException>(() => MigrationCatalog.Validate([Def(1, withDown: false)]));

    [Fact]
    public void Validate_AllowsIrreversibleWithoutDown() =>
        Assert.Single(MigrationCatalog.Validate([Def(1, reversible: false, withDown: false)]));

    [Fact]
    public void FromFiles_RejectsOrphanDown() =>
        Assert.Throws<MigrationCatalogException>(() => MigrationCatalog.FromFiles(new Dictionary<string, string>
        {
            ["0001_a.sql"] = "-- reversible: true\nSELECT 1;",
            ["0002_b.down.sql"] = "SELECT 1;",
        }));

    [Theory]
    [InlineData("0001_schemas.sql", 1, "schemas", false)]
    [InlineData("0012_some-thing.down.sql", 12, "some-thing", true)]
    public void TryParseFileName_Parses(string name, int version, string slug, bool isDown)
    {
        Assert.True(MigrationCatalog.TryParseFileName(name, out int v, out string s, out bool d));
        Assert.Equal(version, v);
        Assert.Equal(slug, s);
        Assert.Equal(isDown, d);
    }

    [Fact]
    public void Checksum_IsStableAndChangesWithSql()
    {
        Assert.Equal(MigrationChecksum.Compute("SELECT 1;\r\n"), MigrationChecksum.Compute("SELECT 1;\n"));
        Assert.NotEqual(MigrationChecksum.Compute("SELECT 1;"), MigrationChecksum.Compute("SELECT 2;"));
    }

    [Fact]
    public void EmbeddedCatalog_IsSequentialReversibleAndHasNoSecretsOrVectorIndex()
    {
        IReadOnlyList<MigrationDefinition> catalog = new EmbeddedMigrationSource().GetMigrations();
        Assert.Equal(Enumerable.Range(1, catalog.Count), catalog.Select(m => m.Version));
        Assert.All(catalog, m =>
        {
            Assert.True(m.IsReversible);
            Assert.False(string.IsNullOrWhiteSpace(m.DownSql));
            Assert.DoesNotContain("password", m.UpSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hnsw", m.UpSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ivfflat", m.UpSql, StringComparison.OrdinalIgnoreCase);
        });
        string all = string.Join("\n", catalog.Select(m => m.UpSql));
        foreach (string table in new[] { "embeddings", "semantic_cache", "chains", "chain_runs", "chain_checkpoints", "approvals", "token_budget_ledger", "kv_settings", "audit_log", "schema_migrations" })
            Assert.Contains(table, all);
        // SQLite: single main schema — no CREATE SCHEMA, no schema-qualified tables.
        Assert.DoesNotContain("CREATE SCHEMA", all);
        Assert.DoesNotContain("CREATE EXTENSION", all);
        Assert.DoesNotContain("app.", all);
        // Dimension is not hard-coded: embedding is stored as TEXT, TASK-012 supplies dimension via the column.
        // No pgvector remnants: no vector type, casts or distance operators (the word may appear in comments only).
        Assert.Contains("embedding text NOT NULL", all);
        Assert.DoesNotContain("vector NOT NULL", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("::vector", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<=>", all, StringComparison.Ordinal);
    }
}
