using System.Text.RegularExpressions;
using CreateMapping.Models;
using Microsoft.Extensions.Logging;

namespace CreateMapping.Services;

public sealed class SqlScriptParser : ISqlScriptParser
{
    private readonly ILogger<SqlScriptParser> _logger;

    public SqlScriptParser(ILogger<SqlScriptParser> logger)
    {
        _logger = logger;
    }

    private static readonly Regex CreateTableRegex = new(
        @"CREATE\s+TABLE\s+(?<name>\[?[^\s\(]+\]?\.?\[?[^\s\(]+\]?)[\s\r\n]*\((?<body>.*?)\)" ,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ColumnLineRegex = new(
        @"^(?<col>\[?[A-Za-z0-9_]+\]?)[\t ]+(?<type>[A-Za-z0-9_]+)(\((?<len>[^\)]+)\))?" ,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<TableMetadata> ParseAsync(string scriptPath, string tableName, CancellationToken ct = default)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Script not found", scriptPath);
        }
        var script = File.ReadAllText(scriptPath);

        var matches = CreateTableRegex.Matches(script);
        string normalizedInputName = NormalizeName(tableName);
        foreach (Match m in matches)
        {
            var name = m.Groups["name"].Value.Trim();
            var normName = NormalizeName(name);
            if (normName.Equals(normalizedInputName, StringComparison.OrdinalIgnoreCase) || matches.Count == 1)
            {
                var body = m.Groups["body"].Value;
                var columns = ParseColumns(body);
                return Task.FromResult(new TableMetadata("SQL_SCRIPT", tableName, columns));
            }
        }

        _logger.LogWarning("No matching CREATE TABLE found for {Table} in script {Script}", tableName, scriptPath);
        return Task.FromResult(new TableMetadata("SQL_SCRIPT", tableName, new List<ColumnMetadata>()));
    }

    private List<ColumnMetadata> ParseColumns(string body)
    {
        var result = new List<ColumnMetadata>();
        var lines = body.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) || line.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) || line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase))
                continue;
            line = line.TrimEnd(',');
            var colMatch = ColumnLineRegex.Match(line);
            if (!colMatch.Success) continue;
            var colName = TrimBrackets(colMatch.Groups["col"].Value);
            var type = colMatch.Groups["type"].Value;
            int? length = null;
            int? precision = null;
            int? scale = null;
            if (colMatch.Groups["len"].Success)
            {
                var lenText = colMatch.Groups["len"].Value;
                if (lenText.Contains(','))
                {
                    var parts = lenText.Split(',', StringSplitOptions.TrimEntries);
                    if (int.TryParse(parts[0], out var prec)) precision = prec;
                    if (parts.Length > 1 && int.TryParse(parts[1], out var sc)) scale = sc;
                }
                else if (int.TryParse(lenText, out var l))
                {
                    length = l;
                }
            }
            result.Add(new ColumnMetadata(colName, type, true, length, precision, scale));
        }
        return result;
    }

    private static string TrimBrackets(string v) => v.Trim().TrimStart('[').TrimEnd(']');

    private static string NormalizeName(string raw)
    {
        return raw.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
    }
}
