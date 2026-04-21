using SonnetDB.Catalog;
using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Catalog;

/// <summary>
/// <see cref="SeriesCatalog"/> 单元测试。
/// </summary>
public sealed class SeriesCatalogTests
{
    private static SeriesCatalog CreateCatalog() => new();

    // ── GetOrAdd 幂等性 ───────────────────────────────────────────────────────

    [Fact]
    public void GetOrAdd_SameKey_ReturnsSameInstance()
    {
        var catalog = CreateCatalog();
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        var e1 = catalog.GetOrAdd("cpu", tags);
        var e2 = catalog.GetOrAdd("cpu", tags);
        Assert.Same(e1, e2);
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void GetOrAdd_DifferentMeasurements_DifferentEntries()
    {
        var catalog = CreateCatalog();
        var e1 = catalog.GetOrAdd("cpu", null);
        var e2 = catalog.GetOrAdd("mem", null);
        Assert.NotSame(e1, e2);
        Assert.NotEqual(e1.Id, e2.Id);
        Assert.Equal(2, catalog.Count);
    }

    [Fact]
    public void GetOrAdd_DifferentTags_DifferentEntries()
    {
        var catalog = CreateCatalog();
        var e1 = catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "a" });
        var e2 = catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "b" });
        Assert.NotSame(e1, e2);
        Assert.NotEqual(e1.Id, e2.Id);
    }

    [Fact]
    public void GetOrAdd_UnorderedTags_ReturnsSameEntry()
    {
        var catalog = CreateCatalog();
        var tagsAB = new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "2" };
        var tagsBA = new Dictionary<string, string> { ["beta"] = "2", ["alpha"] = "1" };
        var e1 = catalog.GetOrAdd("cpu", tagsAB);
        var e2 = catalog.GetOrAdd("cpu", tagsBA);
        Assert.Same(e1, e2);
        Assert.Equal(1, catalog.Count);
    }

    // ── GetOrAdd(Point) ───────────────────────────────────────────────────────

    [Fact]
    public void GetOrAdd_FromPoint_ReturnsSameEntryAsFromKey()
    {
        var catalog = CreateCatalog();
        var tags = new Dictionary<string, string> { ["region"] = "us" };
        var point = Point.Create("temp", 1000L, tags,
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(42.0) });

        var e1 = catalog.GetOrAdd(point);
        var e2 = catalog.GetOrAdd("temp", tags);
        Assert.Same(e1, e2);
    }

    [Fact]
    public void GetOrAdd_FromPoint_NullThrows()
        => Assert.Throws<ArgumentNullException>(() => CreateCatalog().GetOrAdd(null!));

    // ── TryGet ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_ById_HitAndMiss()
    {
        var catalog = CreateCatalog();
        var entry = catalog.GetOrAdd("cpu", null);
        Assert.Same(entry, catalog.TryGet(entry.Id));
        Assert.Null(catalog.TryGet(0xDEADBEEFDEADBEEFUL));
    }

    [Fact]
    public void TryGet_ByKey_HitAndMiss()
    {
        var catalog = CreateCatalog();
        var entry = catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "h" });
        var key = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "h" });
        Assert.Same(entry, catalog.TryGet(in key));

        var missing = new SeriesKey("cpu", null);
        Assert.Null(catalog.TryGet(in missing));
    }

    // ── Find ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Find_ByMeasurement_ReturnsAllMatchingEntries()
    {
        var catalog = CreateCatalog();
        catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "a" });
        catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "b" });
        catalog.GetOrAdd("mem", new Dictionary<string, string> { ["host"] = "a" });

        var cpuEntries = catalog.Find("cpu", null);
        Assert.Equal(2, cpuEntries.Count);
        Assert.All(cpuEntries, e => Assert.Equal("cpu", e.Measurement));
    }

    [Fact]
    public void Find_ByMeasurementAndTag_ReturnsOnlyMatching()
    {
        var catalog = CreateCatalog();
        catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "server-1" });
        catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "server-2" });

        var results = catalog.Find("cpu", new Dictionary<string, string> { ["host"] = "server-1" });
        Assert.Single(results);
        Assert.Equal("server-1", results[0].Tags["host"]);
    }

    [Fact]
    public void Find_NoMatch_ReturnsEmpty()
    {
        var catalog = CreateCatalog();
        catalog.GetOrAdd("cpu", null);
        Assert.Empty(catalog.Find("disk", null));
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ReturnsAllEntries()
    {
        var catalog = CreateCatalog();
        catalog.GetOrAdd("cpu", null);
        catalog.GetOrAdd("mem", null);
        var snap = catalog.Snapshot();
        Assert.Equal(2, snap.Count);
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var catalog = CreateCatalog();
        catalog.GetOrAdd("cpu", null);
        catalog.Clear();
        Assert.Equal(0, catalog.Count);
        Assert.Empty(catalog.Snapshot());
    }

    // ── Tags 不可变性 ─────────────────────────────────────────────────────────

    [Fact]
    public void Tags_Immutability_OriginalDictDoesNotAffectEntry()
    {
        var catalog = CreateCatalog();
        var originalTags = new Dictionary<string, string> { ["host"] = "original" };
        var entry = catalog.GetOrAdd("cpu", originalTags);

        // 修改原始字典
        originalTags["host"] = "mutated";

        // entry.Tags 应保持不变
        Assert.Equal("original", entry.Tags["host"]);
    }

    // ── 并发测试 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Concurrent_SameKey_AllReturnSameInstance()
    {
        var catalog = CreateCatalog();
        var tags = new Dictionary<string, string> { ["host"] = "srv" };

        var results = new SeriesEntry[1000];
        Parallel.For(0, 1000, i =>
        {
            results[i] = catalog.GetOrAdd("cpu", tags);
        });

        Assert.Equal(1, catalog.Count);
        var first = results[0];
        Assert.All(results, e => Assert.Same(first, e));
    }

    [Fact]
    public void Concurrent_DifferentKeys_AllUnique()
    {
        var catalog = CreateCatalog();
        const int count = 500;

        Parallel.For(0, count, i =>
        {
            catalog.GetOrAdd("series", new Dictionary<string, string> { ["id"] = i.ToString() });
        });

        Assert.Equal(count, catalog.Count);

        // 确保没有重复 ID
        var ids = catalog.Snapshot().Select(e => e.Id).ToHashSet();
        Assert.Equal(count, ids.Count);
    }
}
