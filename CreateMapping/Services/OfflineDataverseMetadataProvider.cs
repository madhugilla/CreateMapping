using System.Globalization;
using CreateMapping.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CreateMapping.Services;

/// <summary>
/// Offline Dataverse metadata provider loading column definitions from a CSV exported via XrmToolBox.
/// Expected headers (case-insensitive): LogicalName or SchemaName, AttributeType/Type, DisplayName(optional),
/// MaxLength, Required (Yes/No/True/False), OptionSetValues (semicolon or pipe separated), PrimaryId, PrimaryName.
/// If a 'Table' or 'EntityLogicalName' column exists and contains multiple tables, we filter by requested logicalName.
/// </summary>
public sealed class OfflineDataverseMetadataProvider : IDataverseMetadataProvider
{
    private readonly ILogger<OfflineDataverseMetadataProvider> _logger;
    private readonly string _filePath;
    private List<(string Table, ColumnMetadata Column)>? _cache;
    private bool _hasNoTableColumn;

    public OfflineDataverseMetadataProvider(ILogger<OfflineDataverseMetadataProvider> logger, IConfiguration config)
    {
        _logger = logger;
        _filePath = Environment.GetEnvironmentVariable("CM_DATAVERSE_FILE") ?? config["Dataverse:File"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            _logger.LogWarning("Offline provider initialized without a file path; will return empty metadata.");
        }
    }

    public Task<TableMetadata> GetTableMetadataAsync(string logicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            _logger.LogWarning("Dataverse offline metadata file not found: {File}", _filePath);
            return Task.FromResult(new TableMetadata("DATAVERSE_OFFLINE", logicalName, Array.Empty<ColumnMetadata>()));
        }

        EnsureLoaded();

        var cols = _cache!
            .Where(c => string.Equals(c.Table, logicalName, StringComparison.OrdinalIgnoreCase) || _hasNoTableColumn)
            .Select(c => c.Column)
            .ToList();
        return Task.FromResult(new TableMetadata("DATAVERSE_OFFLINE", logicalName, cols));
    }

    private void EnsureLoaded()
    {
        if (_cache != null) return;
        _cache = new List<(string, ColumnMetadata)>();
        try
        {
            using var reader = new StreamReader(_filePath);
            var headerLine = reader.ReadLine();
            if (headerLine == null)
            {
                _logger.LogWarning("Empty Dataverse metadata file: {File}", _filePath);
                return;
            }
            var headers = Split(headerLine).Select((h, idx) => (Name: h.Trim(), Index: idx)).ToList();
            string? GetCol(params string[] names)
                => headers.FirstOrDefault(h => names.Any(n => string.Equals(h.Name, n, StringComparison.OrdinalIgnoreCase))).Name;

            var colTable = GetCol("Table", "EntityLogicalName", "Entity");
            _hasNoTableColumn = string.IsNullOrEmpty(colTable);
            var colLogical = GetCol("LogicalName", "SchemaName", "Name") ?? string.Empty;
            var colType = GetCol("AttributeType", "Type", "DataType", "AttributeTypeName") ?? string.Empty;
            var colDisplay = GetCol("DisplayName", "Label");
            var colMaxLen = GetCol("MaxLength", "Length");
            var colReq = GetCol("Required", "IsRequired", "RequiredLevel");
            var colOpts = GetCol("OptionSetValues", "Options", "PicklistValues");
            var colPrimaryId = GetCol("PrimaryId", "IsPrimaryId");
            var colPrimaryName = GetCol("PrimaryName", "IsPrimaryName");

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = Split(line).ToArray();
                string GetVal(string? col)
                {
                    if (string.IsNullOrEmpty(col)) return string.Empty;
                    var idx = headers.First(h => h.Name == col).Index;
                    return idx < parts.Length ? parts[idx].Trim() : string.Empty;
                }
                var tableName = _hasNoTableColumn ? "_single_" : GetVal(colTable);
                var logical = GetVal(colLogical);
                if (string.IsNullOrEmpty(logical)) continue;
                var type = GetVal(colType);
                var display = GetVal(colDisplay);
                var maxLenStr = GetVal(colMaxLen);
                int? maxLen = int.TryParse(maxLenStr, out var ml) ? ml : null;
                var requiredFlag = GetVal(colReq);
                bool required = IsTrue(requiredFlag) || requiredFlag.Equals("ApplicationRequired", StringComparison.OrdinalIgnoreCase) || requiredFlag.Equals("SystemRequired", StringComparison.OrdinalIgnoreCase);
                var optionsRaw = GetVal(colOpts);
                IReadOnlyList<string>? optionValues = null;
                if (!string.IsNullOrWhiteSpace(optionsRaw))
                {
                    optionValues = optionsRaw.Split(new[] {';', '|'}, StringSplitOptions.RemoveEmptyEntries)
                        .Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                bool primaryId = IsTrue(GetVal(colPrimaryId));
                bool primaryName = IsTrue(GetVal(colPrimaryName));

                var column = new ColumnMetadata(
                    Name: logical,
                    DataType: string.IsNullOrWhiteSpace(type) ? "unknown" : type,
                    IsNullable: !required,
                    Length: maxLen,
                    Precision: null,
                    Scale: null,
                    IsIdentity: false,
                    IsComputed: false,
                    DefaultDefinition: null,
                    IsPrimaryId: primaryId,
                    IsPrimaryName: primaryName,
                    IsRequired: required,
                    OptionSetValues: optionValues,
                    IsSystemField: InferSystem(logical),
                    SystemFieldType: InferSystemType(logical)
                );
                _cache.Add((tableName, column));
            }
            _logger.LogInformation("Loaded {Count} Dataverse columns from offline file {File}", _cache.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load offline Dataverse metadata file {File}", _filePath);
        }
    }

    private static IEnumerable<string> Split(string line)
    {
        // Simple CSV splitter supporting quotes.
        if (line.Contains('"'))
        {
            var result = new List<string>();
            bool inQuotes = false; var current = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString()); current.Clear();
                }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }
        return line.Split(',');
    }

    private static bool IsTrue(string s) => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("y", StringComparison.OrdinalIgnoreCase) || s.Equals("1");

    private static bool InferSystem(string logical) => InferSystemType(logical) != Models.SystemFieldType.None;
    private static Models.SystemFieldType InferSystemType(string logical)
    {
        if (string.IsNullOrEmpty(logical)) return Models.SystemFieldType.None;
        if (logical.Equals("createdon", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.CreatedOn;
        if (logical.Equals("createdby", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.CreatedBy;
        if (logical.Equals("modifiedon", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.ModifiedOn;
        if (logical.Equals("modifiedby", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.ModifiedBy;
        if (logical.Equals("ownerid", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.Owner;
        if (logical.Equals("owningbusinessunit", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.BusinessUnit;
        if (logical.Equals("statecode", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.State;
        if (logical.Equals("statuscode", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.Status;
        if (logical.Equals("versionnumber", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.Version;
        if (logical.Equals("importsequencenumber", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.ImportSequence;
        if (logical.Equals("overriddencreatedon", StringComparison.OrdinalIgnoreCase)) return Models.SystemFieldType.OverriddenCreatedOn;
        return Models.SystemFieldType.None;
    }
}
