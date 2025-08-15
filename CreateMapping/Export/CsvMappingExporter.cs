using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CreateMapping.Models;

namespace CreateMapping.Export;

public interface ICsvMappingExporter
{
    Task WriteAsync(MappingResult result, string path, CancellationToken ct = default);
}

public sealed class CsvMappingExporter : ICsvMappingExporter
{
    public async Task WriteAsync(MappingResult result, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        // Write BOM for Excel compatibility
        var utf8bom = new System.Text.UTF8Encoding(true);
        await using var writer = new StreamWriter(stream, utf8bom);
        var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        // Expanded header to always include all source & target columns (mapped, unresolved, unused)
        csv.WriteField("Status"); // Accepted | Review | UnresolvedSource | UnusedTarget
        csv.WriteField("SourceColumn");
        csv.WriteField("SourceType");
        csv.WriteField("SourceNullable");
        csv.WriteField("SourceLength");
        csv.WriteField("TargetColumn");
        csv.WriteField("TargetType");
        csv.WriteField("TargetRequired");
        csv.WriteField("MatchType");
        csv.WriteField("Confidence");
        csv.WriteField("Transformation");
        csv.WriteField("Rationale");
        await csv.NextRecordAsync();

        var targetLookup = result.Target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        // 1. Write accepted mappings
        foreach (var m in result.Accepted)
        {
            var src = result.Source.Columns.First(c => c.Name == m.SourceColumn);
            var tgt = targetLookup[m.TargetColumn];
            csv.WriteField("Accepted");
            csv.WriteField(src.Name);
            csv.WriteField(src.DataType);
            csv.WriteField(src.IsNullable);
            csv.WriteField(src.Length?.ToString() ?? string.Empty);
            csv.WriteField(tgt.Name);
            csv.WriteField(tgt.DataType);
            csv.WriteField(tgt.IsRequired);
            csv.WriteField(m.MatchType);
            csv.WriteField(Math.Round(m.Confidence, 4));
            csv.WriteField(m.Transformation ?? string.Empty);
            csv.WriteField(m.Rationale ?? string.Empty);
            await csv.NextRecordAsync();
        }

        // 2. Write review mappings
        foreach (var m in result.NeedsReview)
        {
            var src = result.Source.Columns.First(c => c.Name == m.SourceColumn);
            var tgt = targetLookup[m.TargetColumn];
            csv.WriteField("Review");
            csv.WriteField(src.Name);
            csv.WriteField(src.DataType);
            csv.WriteField(src.IsNullable);
            csv.WriteField(src.Length?.ToString() ?? string.Empty);
            csv.WriteField(tgt.Name);
            csv.WriteField(tgt.DataType);
            csv.WriteField(tgt.IsRequired);
            csv.WriteField(m.MatchType);
            csv.WriteField(Math.Round(m.Confidence, 4));
            csv.WriteField(m.Transformation ?? string.Empty);
            csv.WriteField(m.Rationale ?? string.Empty);
            await csv.NextRecordAsync();
        }

        // 3. Unresolved source columns (no mapping)
        foreach (var srcName in result.UnresolvedSourceColumns.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var src = result.Source.Columns.First(c => c.Name.Equals(srcName, StringComparison.OrdinalIgnoreCase));
            csv.WriteField("UnresolvedSource");
            csv.WriteField(src.Name);
            csv.WriteField(src.DataType);
            csv.WriteField(src.IsNullable);
            csv.WriteField(src.Length?.ToString() ?? string.Empty);
            csv.WriteField(string.Empty); // target
            csv.WriteField(string.Empty); // target type
            csv.WriteField(string.Empty); // target required
            csv.WriteField(string.Empty); // match type
            csv.WriteField(string.Empty); // confidence
            csv.WriteField(string.Empty); // transformation
            csv.WriteField(string.Empty); // rationale
            await csv.NextRecordAsync();
        }

        // 4. Unused target columns (not mapped)
        var mappedTargets = new HashSet<string>(result.Accepted.Select(m => m.TargetColumn).Concat(result.NeedsReview.Select(m => m.TargetColumn)), StringComparer.OrdinalIgnoreCase);
        foreach (var tgt in result.Target.Columns.Where(c => !mappedTargets.Contains(c.Name)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            csv.WriteField("UnusedTarget");
            csv.WriteField(string.Empty); // source name
            csv.WriteField(string.Empty); // source type
            csv.WriteField(string.Empty); // source nullable
            csv.WriteField(string.Empty); // source length
            csv.WriteField(tgt.Name); // target
            csv.WriteField(tgt.DataType);
            csv.WriteField(tgt.IsRequired);
            csv.WriteField(string.Empty); // match type
            csv.WriteField(string.Empty); // confidence
            csv.WriteField(string.Empty); // transformation
            csv.WriteField(string.Empty); // rationale
            await csv.NextRecordAsync();
        }

        await writer.FlushAsync();
    }
}
