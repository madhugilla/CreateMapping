using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CreateMapping.Export;
using CreateMapping.Models;
using Xunit;

namespace CreateMapping.Tests;

public class CsvExporterAllRowsTests
{
    [Fact]
    public async Task Exports_All_Row_Types()
    {
        var src = new TableMetadata("SQL", "s", new[] {
            new ColumnMetadata("A","int",false,null,null,null),
            new ColumnMetadata("B","nvarchar",true,50,null,null)
        });
        var tgt = new TableMetadata("DATAVERSE", "t", new[] {
            new ColumnMetadata("a","int",false,null,null,null),
            new ColumnMetadata("c","nvarchar",true,100,null,null)
        });
        var mapping = new MappingResult(
            Source: src,
            Target: tgt,
            Accepted: new[] { new MappingCandidate("A","a",0.9,"AI",null,null) },
            NeedsReview: Array.Empty<MappingCandidate>(),
            UnresolvedSourceColumns: new[] { "B" },
            UnusedTargetColumns: new[] { "c" },
            GeneratedAtUtc: System.DateTime.UtcNow,
            Weights: WeightsConfig.Default);
        var exporter = new CsvMappingExporter();
        var path = Path.GetTempFileName();
        try
        {
            await exporter.WriteAsync(mapping, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(1 + 1 + 1 + 1, lines.Length); // header + Accepted + UnresolvedSource + UnusedTarget
            Assert.Contains(lines, l => l.Contains("Accepted") && l.Contains(",A,"));
            Assert.Contains(lines, l => l.StartsWith("UnresolvedSource"));
            Assert.Contains(lines, l => l.StartsWith("UnusedTarget"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
