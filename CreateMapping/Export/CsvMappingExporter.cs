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

        IEnumerable<MappingCandidate> all = result.Accepted.Concat(result.NeedsReview);
        foreach (var m in all)
        {
            var src = result.Source.Columns.First(c => c.Name == m.SourceColumn);
            var tgt = targetLookup[m.TargetColumn];
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

        await writer.FlushAsync();
    }
}
