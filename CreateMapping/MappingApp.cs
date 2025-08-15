using CreateMapping.Export;
using CreateMapping.Mapping;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;

namespace CreateMapping;

public sealed class MappingApp
{
    private readonly IDataverseMetadataProvider _dataverse;
    // Live SQL introspection removed for offline-only mode
    private readonly ISqlScriptParser _scriptParser;
    private readonly IMappingOrchestrator _orchestrator;
    private readonly ICsvMappingExporter _csv;
    private readonly IJsonMappingExporter _json;
    private readonly ILogger<MappingApp> _logger;

    public MappingApp(
        IDataverseMetadataProvider dataverse,
        ISqlScriptParser scriptParser,
        IMappingOrchestrator orchestrator,
        ICsvMappingExporter csv,
        IJsonMappingExporter json,
        ILogger<MappingApp> logger)
    {
        _dataverse = dataverse;
        _scriptParser = scriptParser;
        _orchestrator = orchestrator;
        _csv = csv;
        _json = json;
        _logger = logger;
    }

    public async Task<int> RunAsync(string sqlTableName, string dvTable, string outputDir, string sqlScriptPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sqlScriptPath) || !File.Exists(sqlScriptPath))
        {
            _logger.LogError("SQL script file not found: {File}", sqlScriptPath);
            return 1;
        }

    _logger.LogInformation("Parsing SQL metadata from script {Script}", sqlScriptPath);
    var sqlMeta = await _scriptParser.ParseAsync(sqlScriptPath, sqlTableName, ct);
    _logger.LogInformation("Parsed SQL table {Table} with {ColumnCount} columns", sqlMeta.Name, sqlMeta.Columns.Count);

        _logger.LogInformation("Retrieving Dataverse metadata for {Table}", dvTable);
        var dvMeta = await _dataverse.GetTableMetadataAsync(dvTable, ct);

        var weights = WeightsConfig.Default; // future: configurable
        var mapping = await _orchestrator.GenerateAsync(sqlMeta, dvMeta, weights, ct);

        Directory.CreateDirectory(outputDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    var baseName = $"mapping_{sqlTableName.Replace('.', '_')}_{dvTable}_{timestamp}";
        var csvPath = Path.Combine(outputDir, baseName + ".csv");
        var jsonPath = Path.Combine(outputDir, baseName + ".json");

        await _csv.WriteAsync(mapping, csvPath, ct);
        await _json.WriteAsync(mapping, jsonPath, ct);
        _logger.LogInformation("Mapping files written: {Csv} | {Json}", csvPath, jsonPath);
        return 0;
    }

    // Summary printing removed in offline-only simplification
}
