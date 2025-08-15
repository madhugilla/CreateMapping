using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CreateMapping;
using CreateMapping.AI;
using CreateMapping.Export;
using CreateMapping.Mapping;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CreateMapping.Tests;

// Integration test for full offline pipeline using real sample assets
public class OfflineE2ETests
{
    [Fact]
    [Trait("Category","Integration")]
    public async Task Offline_EndToEnd_GeneratesMappingFiles()
    {
        // Resolve solution root by walking up until we find the solution file to avoid brittle relative hops
    var probe = AppContext.BaseDirectory;
    string solutionRoot = null!; // set after discovery
        for (int i = 0; i < 8 && probe != null; i++)
        {
            var candidate = Path.Combine(probe, "CreateMapping.sln");
            if (File.Exists(candidate)) { solutionRoot = probe; break; }
            probe = Directory.GetParent(probe)?.FullName;
        }
    Assert.True(solutionRoot != null, "Failed to locate solution root (CreateMapping.sln) starting from " + AppContext.BaseDirectory);

    // docs directory resides at solution root /docs
    var docsDir = Path.Combine(solutionRoot!, "docs");
        var sqlFile = Path.Combine(docsDir, "CaseMigrant.sql");
        var dvFile = Path.Combine(docsDir, "m360_case_csv.csv");
        if (!File.Exists(sqlFile) || !File.Exists(dvFile))
        {
            var diag = $"ProbeBase={AppContext.BaseDirectory}; SolutionRoot={solutionRoot}; DocsDirExists={Directory.Exists(docsDir)}; DocsContents=[{string.Join(',', Directory.Exists(docsDir) ? Directory.GetFiles(docsDir).Select(Path.GetFileName)! : Array.Empty<string>())}]";
            Assert.True(File.Exists(sqlFile), $"Missing SQL script: {sqlFile}. {diag}");
            Assert.True(File.Exists(dvFile), $"Missing Dataverse CSV: {dvFile}. {diag}");
        }

        Environment.SetEnvironmentVariable("CM_DATAVERSE_FILE", dvFile);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<ISqlScriptParser, SqlScriptParser>();
        services.AddSingleton<ISystemFieldClassifier, SystemFieldClassifier>();
        services.AddSingleton<IDataverseMetadataProvider, OfflineDataverseMetadataProvider>();
        services.AddSingleton<IAiMapper, NoOpAiMapper>();
        services.AddSingleton<IMappingOrchestrator, MappingOrchestrator>();
        services.AddSingleton<ICsvMappingExporter, CsvMappingExporter>();
        services.AddSingleton<IJsonMappingExporter, JsonMappingExporter>();
        services.AddSingleton<MappingApp>();
        var provider = services.BuildServiceProvider();

        var app = provider.GetRequiredService<MappingApp>();
    var outDir = Path.Combine(solutionRoot, "CreateMapping.Tests", "_e2e_output");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

        var exit = await app.RunAsync("CaseMigrant", "m360_case", outDir, sqlFile);

        Assert.Equal(0, exit);
        Assert.True(Directory.Exists(outDir));
        var csv = Directory.GetFiles(outDir, "mapping_CaseMigrant_m360_case_*.csv");
        Assert.Single(csv);
        var json = Directory.GetFiles(outDir, "mapping_CaseMigrant_m360_case_*.json");
        Assert.Single(json);
    var csvContent = File.ReadAllLines(csv[0]);
    // Header-only output is acceptable for NoOpAiMapper scenario (no automatic matches yet)
    Assert.True(csvContent.Length >= 1, "CSV should have at least a header row");
    var header = csvContent[0];
    Assert.Contains("SourceColumn", header);
    Assert.Contains("TargetColumn", header);
        var jsonContent = File.ReadAllText(json[0]);
        Assert.Contains("accepted", jsonContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unresolvedSourceColumns", jsonContent, StringComparison.OrdinalIgnoreCase);
    }
}
