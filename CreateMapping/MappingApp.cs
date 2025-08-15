using CreateMapping.Export;
using CreateMapping.Mapping;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;

namespace CreateMapping;

public sealed class MappingApp
{
    private readonly IDataverseMetadataProvider _dataverse;
    private readonly ISqlIntrospector _sql;
    private readonly ISqlScriptParser _scriptParser;
    private readonly IMappingOrchestrator _orchestrator;
    private readonly ICsvMappingExporter _csv;
    private readonly IJsonMappingExporter _json;
    private readonly ILogger<MappingApp> _logger;

    public MappingApp(
        IDataverseMetadataProvider dataverse,
        ISqlIntrospector sql,
        ISqlScriptParser scriptParser,
        IMappingOrchestrator orchestrator,
        ICsvMappingExporter csv,
        IJsonMappingExporter json,
        ILogger<MappingApp> logger)
    {
        _dataverse = dataverse;
        _sql = sql;
        _scriptParser = scriptParser;
        _orchestrator = orchestrator;
        _csv = csv;
        _json = json;
        _logger = logger;
    }

    public async Task<int> RunAsync(string sqlTable, string dvTable, string outputDir, string? sqlScriptPath, bool checkDv, bool checkSql, CancellationToken ct = default)
    {
        string? schema = null;
        string tableOnly = sqlTable;
        if (sqlTable.Contains('.'))
        {
            var parts = sqlTable.Split('.', 2);
            schema = parts[0];
            tableOnly = parts[1];
        }

        TableMetadata sqlMeta;
        if (!string.IsNullOrWhiteSpace(sqlScriptPath))
        {
            _logger.LogInformation("Parsing SQL metadata from script {Script}", sqlScriptPath);
            sqlMeta = await _scriptParser.ParseAsync(sqlScriptPath, sqlTable, ct);
        }
        else
        {
            _logger.LogInformation("Retrieving SQL metadata for {Table}", sqlTable);
            sqlMeta = await _sql.GetTableMetadataAsync(tableOnly, schema, ct);
        }

        _logger.LogInformation("Retrieving Dataverse metadata for {Table}", dvTable);
        var dvMeta = await _dataverse.GetTableMetadataAsync(dvTable, ct);

        if (checkDv || checkSql)
        {
            if (checkSql)
            {
                PrintSqlSummary(sqlTable, sqlMeta);
            }
            if (checkDv)
            {
                PrintDataverseSummary(dvTable, dvMeta);
            }
            return 0;
        }

        var weights = WeightsConfig.Default; // future: configurable
        var mapping = await _orchestrator.GenerateAsync(sqlMeta, dvMeta, weights, ct);

        Directory.CreateDirectory(outputDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var baseName = $"mapping_{sqlTable.Replace('.', '_')}_{dvTable}_{timestamp}";
        var csvPath = Path.Combine(outputDir, baseName + ".csv");
        var jsonPath = Path.Combine(outputDir, baseName + ".json");

        await _csv.WriteAsync(mapping, csvPath, ct);
        await _json.WriteAsync(mapping, jsonPath, ct);
        _logger.LogInformation("Mapping files written: {Csv} | {Json}", csvPath, jsonPath);
        return 0;
    }

    private void PrintSqlSummary(string sqlTable, TableMetadata sqlMeta)
    {
        _logger.LogInformation("SQL connectivity OK. Table: {Table} | Columns: {Count}", sqlTable, sqlMeta.Columns.Count);
        foreach (var col in sqlMeta.Columns.Take(25))
        {
            _logger.LogInformation(" - {Name} ({Type})", col.Name, col.DataType);
        }
        if (sqlMeta.Columns.Count > 25)
        {
            _logger.LogInformation(" ... ({Extra} more columns omitted)", sqlMeta.Columns.Count - 25);
        }
    }

    private void PrintDataverseSummary(string dvTable, TableMetadata dvMeta)
    {
        _logger.LogInformation("Dataverse connectivity OK. Entity: {Entity} | Columns: {Count} (Custom: {Custom} System: {System})",
            dvTable, dvMeta.Columns.Count, dvMeta.Columns.Count(c => !c.IsSystemField), dvMeta.Columns.Count(c => c.IsSystemField));
        foreach (var col in dvMeta.Columns.Take(25))
        {
            _logger.LogInformation(" - {Name} ({Type}){SystemFlag}", col.Name, col.DataType, col.IsSystemField ? " [system]" : string.Empty);
        }
        if (dvMeta.Columns.Count > 25)
        {
            _logger.LogInformation(" ... ({Extra} more columns omitted)", dvMeta.Columns.Count - 25);
        }
    }
}
