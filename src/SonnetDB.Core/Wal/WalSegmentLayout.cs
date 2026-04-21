using System.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;

namespace SonnetDB.Wal;

/// <summary>
/// WAL segment 磁盘布局工具类：文件命名、路径生成、目录枚举与 legacy 升级。
/// </summary>
public static class WalSegmentLayout
{
    /// <summary>WAL segment 文件扩展名（含点）。</summary>
    public const string Extension = ".SDBWAL";

    /// <summary>旧版（PR #13）单文件 WAL 的文件名。</summary>
    public const string LegacyActiveFileName = "active.SDBWAL";

    /// <summary>
    /// 根据 startLsn 生成 segment 文件名（不含目录）：<c>{startLsn:X16}.SDBWAL</c>。
    /// </summary>
    /// <param name="startLsn">segment 起始 LSN。</param>
    /// <returns>segment 文件名，如 <c>0000000000000001.SDBWAL</c>。</returns>
    public static string SegmentFileName(long startLsn) =>
        $"{startLsn:X16}{Extension}";

    /// <summary>
    /// 根据 walDir 和 startLsn 生成 segment 完整路径。
    /// </summary>
    /// <param name="walDir">WAL 子目录路径。</param>
    /// <param name="startLsn">segment 起始 LSN。</param>
    /// <returns>segment 文件完整路径。</returns>
    public static string SegmentPath(string walDir, long startLsn) =>
        Path.Combine(walDir, SegmentFileName(startLsn));

    /// <summary>
    /// 尝试从文件名中解析 segment 的 startLsn。
    /// 合法格式：16 位大写/小写十六进制 + <see cref="Extension"/>。
    /// </summary>
    /// <param name="fileName">仅文件名部分（不含目录），如 <c>0000000000000A31.SDBWAL</c>。</param>
    /// <param name="startLsn">解析成功时的 startLsn；失败时为 0。</param>
    /// <returns>解析成功返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool TryParseStartLsn(string fileName, out long startLsn)
    {
        startLsn = 0;
        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        string hex = Path.GetFileNameWithoutExtension(fileName);
        if (hex.Length != 16)
            return false;

        return long.TryParse(hex,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out startLsn);
    }

    /// <summary>
    /// 按 startLsn 升序枚举一个 wal/ 目录下的全部合法 segment（自动跳过 .tmp、legacy active.SDBWAL 及未知文件）。
    /// </summary>
    /// <param name="walDir">WAL 子目录路径。</param>
    /// <returns>按 <see cref="WalSegmentInfo.StartLsn"/> 升序排列的 segment 信息列表。</returns>
    public static IReadOnlyList<WalSegmentInfo> Enumerate(string walDir)
    {
        if (!Directory.Exists(walDir))
            return Array.Empty<WalSegmentInfo>();

        var list = new List<WalSegmentInfo>();
        foreach (string file in Directory.EnumerateFiles(walDir, $"*{Extension}"))
        {
            string name = Path.GetFileName(file);
            if (TryParseStartLsn(name, out long lsn))
            {
                long len = 0;
                try { len = new FileInfo(file).Length; } catch { }
                list.Add(new WalSegmentInfo(lsn, file, len));
            }
        }

        list.Sort(static (a, b) => a.StartLsn.CompareTo(b.StartLsn));
        return list.AsReadOnly();
    }

    /// <summary>
    /// 若 <c>wal/active.SDBWAL</c>（旧版单文件）存在，则读取其 <c>WalFileHeader.FirstLsn</c>，
    /// 将文件重命名为 <c>{firstLsn:X16}.SDBWAL</c>，完成 legacy → segmented 格式升级。
    /// 文件不存在时静默返回。
    /// </summary>
    /// <param name="walDir">WAL 子目录路径。</param>
    public static void UpgradeLegacyIfPresent(string walDir)
    {
        string legacyPath = Path.Combine(walDir, LegacyActiveFileName);
        if (!File.Exists(legacyPath))
            return;

        long firstLsn = ReadFirstLsnFromWalFile(legacyPath);
        string newPath = SegmentPath(walDir, firstLsn);

        // 若目标名已存在（重复升级），直接删除 legacy 文件
        if (File.Exists(newPath))
        {
            File.Delete(legacyPath);
            return;
        }

        File.Move(legacyPath, newPath);
    }

    // ── 私有辅助 ─────────────────────────────────────────────────────────────

    private static long ReadFirstLsnFromWalFile(string path)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalFileHeaderSize);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = 0;
            while (read < FormatSizes.WalFileHeaderSize)
            {
                int n = fs.Read(buf, read, FormatSizes.WalFileHeaderSize - read);
                if (n == 0) break;
                read += n;
            }

            if (read < FormatSizes.WalFileHeaderSize)
                return 1L; // 文件头不完整，回退到 LSN=1

            var reader = new SpanReader(buf.AsSpan(0, FormatSizes.WalFileHeaderSize));
            var header = reader.ReadStruct<WalFileHeader>();
            return header.IsValid() ? header.FirstLsn : 1L;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
