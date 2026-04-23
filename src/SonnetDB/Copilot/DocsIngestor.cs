using Microsoft.Extensions.Logging;
using SonnetDB.Engine;
using SonnetDB.Hosting;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Copilot;

/// <summary>
/// 文档摄入统计结果。
/// </summary>
internal sealed record DocsIngestStats(
    int ScannedFiles,
    int IndexedFiles,
    int SkippedFiles,
    int DeletedFiles,
    int WrittenChunks,
    bool DryRun);

/// <summary>
/// 扫描帮助文档、生成 embedding，并写入系统知识库 <c>__copilot__</c>。
/// </summary>
internal sealed class DocsIngestor
{
    internal const string CopilotDatabaseName = "__copilot__";
    internal const string DocsMeasurementName = "docs";
    internal const string DocsStateMeasurementName = "docs_state";
    internal const int ExpectedEmbeddingDimensions = 384;

    private readonly TsdbRegistry _registry;
    private readonly DocsSourceScanner _scanner;
    private readonly DocsChunker _chunker;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<DocsIngestor> _logger;

    public DocsIngestor(
        TsdbRegistry registry,
        DocsSourceScanner scanner,
        DocsChunker chunker,
        IEmbeddingProvider embeddingProvider,
        ILogger<DocsIngestor> logger)
    {
        _registry = registry;
        _scanner = scanner;
        _chunker = chunker;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    public async Task<DocsIngestStats> IngestAsync(
        IReadOnlyList<string> roots,
        bool force = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var files = _scanner.Scan(roots);
        var stateBySource = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var scannedSources = new HashSet<string>(files.Select(static item => item.Source), StringComparer.OrdinalIgnoreCase);

        var indexedFiles = 0;
        var skippedFiles = 0;
        var deletedFiles = 0;
        var writtenChunks = 0;

        foreach (var staleSource in stateBySource.Keys.Where(source => !scannedSources.Contains(source)).ToArray())
        {
            deletedFiles++;
            if (!dryRun)
                DeleteSource(GetKnowledgeDb(), staleSource);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!force && stateBySource.TryGetValue(file.Source, out var existing) && existing.Matches(file))
            {
                skippedFiles++;
                continue;
            }

            var chunks = _chunker.Chunk(file);
            indexedFiles++;
            writtenChunks += chunks.Count;

            if (dryRun)
                continue;

            var database = GetKnowledgeDb();
            DeleteSource(database, file.Source);
            await InsertChunksAsync(database, file, chunks, cancellationToken).ConfigureAwait(false);
            InsertState(database, file, chunks.Count);
        }

        return new DocsIngestStats(files.Count, indexedFiles, skippedFiles, deletedFiles, writtenChunks, dryRun);
    }

    internal Tsdb GetKnowledgeDb()
    {
        _registry.TryCreate(CopilotDatabaseName, out var tsdb);
        EnsureMeasurements(tsdb);
        return tsdb;
    }

    private async Task<Dictionary<string, DocsStateRow>> LoadStateAsync(CancellationToken cancellationToken)
    {
        var database = GetKnowledgeDb();
        var result = SqlExecutor.ExecuteStatement(database,
            new SelectStatement([new SelectItem(StarExpression.Instance, null)], DocsStateMeasurementName, null, []));
        if (result is not SelectExecutionResult selectResult)
            return new Dictionary<string, DocsStateRow>(StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<string, DocsStateRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in selectResult.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = row.Count > 1 ? row[1]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(source))
                continue;

            rows[source] = new DocsStateRow(
                source,
                Fingerprint: row.Count > 2 ? row[2]?.ToString() : null,
                ModifiedUtc: row.Count > 3 ? row[3]?.ToString() : null,
                ChunkCount: row.Count > 4 && row[4] is long chunkCount ? chunkCount : 0L);
        }

        return rows;
    }

    private async Task InsertChunksAsync(Tsdb database, DocsSourceFile file, IReadOnlyList<DocsChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = new List<IReadOnlyList<SqlExpression>>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await _embeddingProvider.EmbedAsync(chunks[i].Content, cancellationToken).ConfigureAwait(false);
            if (embedding.Length != ExpectedEmbeddingDimensions)
                throw new InvalidOperationException($"embedding 维度必须为 {ExpectedEmbeddingDimensions}，实际为 {embedding.Length}。");

            rows.Add([
                LiteralExpression.String(chunks[i].Source),
                LiteralExpression.String(chunks[i].Section),
                LiteralExpression.String(chunks[i].Title),
                LiteralExpression.String(chunks[i].Content),
                LiteralExpression.Integer(now + i),
                new VectorLiteralExpression(embedding.Select(static value => (double)value).ToArray()),
            ]);
        }

        var statement = new InsertStatement(
            DocsMeasurementName,
            ["source", "section", "title", "content", "time", "embedding"],
            rows);
        SqlExecutor.ExecuteStatement(database, statement);
        _logger.LogInformation("Indexed {ChunkCount} chunks for docs source {Source}.", chunks.Count, file.Source);
    }

    private static void InsertState(Tsdb database, DocsSourceFile file, int chunkCount)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var state = new InsertStatement(
            DocsStateMeasurementName,
            ["source", "fingerprint", "modified_utc", "chunk_count", "time"],
            [[
                LiteralExpression.String(file.Source),
                LiteralExpression.String(file.Fingerprint),
                LiteralExpression.String(file.LastWriteTimeUtc.UtcDateTime.ToString("O")),
                LiteralExpression.Integer(chunkCount),
                LiteralExpression.Integer(timestamp),
            ]]);
        SqlExecutor.ExecuteStatement(database, state);
    }

    private static void DeleteSource(Tsdb database, string source)
    {
        var predicate = new BinaryExpression(
            SqlBinaryOperator.Equal,
            new IdentifierExpression("source"),
            LiteralExpression.String(source));
        SqlExecutor.ExecuteStatement(database, new DeleteStatement(DocsMeasurementName, predicate));
        SqlExecutor.ExecuteStatement(database, new DeleteStatement(DocsStateMeasurementName, predicate));
    }

    private static void EnsureMeasurements(Tsdb database)
    {
        if (database.Measurements.TryGet(DocsMeasurementName) is null)
        {
            SqlExecutor.ExecuteStatement(database, new CreateMeasurementStatement(
                DocsMeasurementName,
                [
                    new ColumnDefinition("source", ColumnKind.Tag, SqlDataType.String),
                    new ColumnDefinition("section", ColumnKind.Tag, SqlDataType.String),
                    new ColumnDefinition("title", ColumnKind.Tag, SqlDataType.String),
                    new ColumnDefinition("content", ColumnKind.Field, SqlDataType.String),
                    new ColumnDefinition("embedding", ColumnKind.Field, SqlDataType.Vector, VectorDimension: ExpectedEmbeddingDimensions),
                ]));
        }

        if (database.Measurements.TryGet(DocsStateMeasurementName) is null)
        {
            SqlExecutor.ExecuteStatement(database, new CreateMeasurementStatement(
                DocsStateMeasurementName,
                [
                    new ColumnDefinition("source", ColumnKind.Tag, SqlDataType.String),
                    new ColumnDefinition("fingerprint", ColumnKind.Field, SqlDataType.String),
                    new ColumnDefinition("modified_utc", ColumnKind.Field, SqlDataType.String),
                    new ColumnDefinition("chunk_count", ColumnKind.Field, SqlDataType.Int64),
                ]));
        }
    }

    private sealed record DocsStateRow(string Source, string? Fingerprint, string? ModifiedUtc, long ChunkCount)
    {
        public bool Matches(DocsSourceFile file)
            => string.Equals(Fingerprint, file.Fingerprint, StringComparison.Ordinal)
               && string.Equals(ModifiedUtc, file.LastWriteTimeUtc.UtcDateTime.ToString("O"), StringComparison.Ordinal);
    }
}
