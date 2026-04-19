using TSLite.Engine;
using Xunit;

namespace TSLite.Tests.Engine;

/// <summary>
/// <see cref="TsdbPaths"/> 单元测试。
/// </summary>
public sealed class TsdbPathsTests
{
    [Fact]
    public void CatalogPath_ReturnsCorrectPath()
    {
        string root = Path.Combine("data", "db");
        string expected = Path.Combine(root, "catalog.tslcat");
        Assert.Equal(expected, TsdbPaths.CatalogPath(root));
    }

    [Fact]
    public void WalDir_ReturnsCorrectPath()
    {
        string root = Path.Combine("data", "db");
        string expected = Path.Combine(root, "wal");
        Assert.Equal(expected, TsdbPaths.WalDir(root));
    }

    [Fact]
    public void ActiveWalPath_ReturnsCorrectPath()
    {
        string root = Path.Combine("data", "db");
        string expected = Path.Combine(root, "wal", "active.tslwal");
        Assert.Equal(expected, TsdbPaths.ActiveWalPath(root));
    }

    [Fact]
    public void SegmentsDir_ReturnsCorrectPath()
    {
        string root = Path.Combine("data", "db");
        string expected = Path.Combine(root, "segments");
        Assert.Equal(expected, TsdbPaths.SegmentsDir(root));
    }

    [Fact]
    public void SegmentPath_FormatsSegmentIdAsHex16()
    {
        string root = Path.Combine("data", "db");
        string path = TsdbPaths.SegmentPath(root, 1L);
        Assert.EndsWith("0000000000000001.tslseg", path);
        Assert.Contains("segments", path);
    }

    [Fact]
    public void SegmentPath_LargeSegmentId_FormatsCorrectly()
    {
        string root = "db";
        string path = TsdbPaths.SegmentPath(root, 0x0ABCDEF012345678L);
        Assert.EndsWith("0ABCDEF012345678.tslseg", path);
    }

    [Theory]
    [InlineData("0000000000000042.tslseg", true, 0x42L)]
    [InlineData("0000000000000001.tslseg", true, 1L)]
    [InlineData("7FFFFFFFFFFFFFFF.tslseg", true, long.MaxValue)]
    [InlineData("0000000000000000.tslseg", true, 0L)]
    [InlineData("bad.tslseg", false, 0L)]
    [InlineData("not-a-segment.txt", false, 0L)]
    [InlineData("too-short.tslseg", false, 0L)]
    [InlineData("0000000000000042.TSLSEG", true, 0x42L)]  // case-insensitive extension
    public void TryParseSegmentId_VariousCases(string fileName, bool expectSuccess, long expectedId)
    {
        bool result = TsdbPaths.TryParseSegmentId(fileName, out long segId);
        Assert.Equal(expectSuccess, result);
        if (expectSuccess)
            Assert.Equal(expectedId, segId);
    }

    [Fact]
    public void EnumerateSegments_NonExistentDir_ReturnsEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var segments = TsdbPaths.EnumerateSegments(root).ToList();
        Assert.Empty(segments);
    }

    [Fact]
    public void EnumerateSegments_WithFiles_ReturnsAllSegments()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(TsdbPaths.SegmentsDir(root));

            // Create some segment files
            File.WriteAllText(TsdbPaths.SegmentPath(root, 1L), "");
            File.WriteAllText(TsdbPaths.SegmentPath(root, 2L), "");
            File.WriteAllText(TsdbPaths.SegmentPath(root, 5L), "");
            // Also create a non-segment file that should be ignored
            File.WriteAllText(Path.Combine(TsdbPaths.SegmentsDir(root), "other.txt"), "");

            var segments = TsdbPaths.EnumerateSegments(root)
                .OrderBy(x => x.SegmentId)
                .ToList();

            Assert.Equal(3, segments.Count);
            Assert.Equal(1L, segments[0].SegmentId);
            Assert.Equal(2L, segments[1].SegmentId);
            Assert.Equal(5L, segments[2].SegmentId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TsdbPaths_Constants_HaveExpectedValues()
    {
        Assert.Equal("catalog.tslcat", TsdbPaths.CatalogFileName);
        Assert.Equal("wal", TsdbPaths.WalDirName);
        Assert.Equal("active.tslwal", TsdbPaths.ActiveWalFileName);
        Assert.Equal("segments", TsdbPaths.SegmentsDirName);
        Assert.Equal(".tslseg", TsdbPaths.SegmentFileExtension);
    }
}
