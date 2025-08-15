using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CreateMapping.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CreateMapping.Tests;

public class DataversePrefixFilterTests
{
    private string GetDocsDir()
    {
        var probe = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && probe != null; i++)
        {
            if (File.Exists(Path.Combine(probe, "CreateMapping.sln"))) return Path.Combine(probe, "docs");
            probe = Directory.GetParent(probe)?.FullName;
        }
        throw new InvalidOperationException("Failed to locate docs directory from base: " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task DefaultPrefix_IncludesOnlyM360Columns()
    {
        var docs = GetDocsDir();
        var dvFile = Path.Combine(docs, "m360_case_csv.csv");
        Environment.SetEnvironmentVariable("CM_DATAVERSE_FILE", dvFile);
        Environment.SetEnvironmentVariable("CM_DV_PREFIX", null); // ensure default applies
        var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IDataverseMetadataProvider, OfflineDataverseMetadataProvider>();
        var provider = services.BuildServiceProvider();
        var metaProvider = provider.GetRequiredService<IDataverseMetadataProvider>();
        var table = await metaProvider.GetTableMetadataAsync("m360_case");
        Assert.NotEmpty(table.Columns);
        Assert.All(table.Columns, c => Assert.StartsWith("m360_", c.Name, StringComparison.OrdinalIgnoreCase));
        // Spot check: createdon should be filtered out
        Assert.DoesNotContain(table.Columns, c => c.Name.Equals("createdon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Wildcard_IncludesSystemColumns()
    {
        var docs = GetDocsDir();
        var dvFile = Path.Combine(docs, "m360_case_csv.csv");
        Environment.SetEnvironmentVariable("CM_DATAVERSE_FILE", dvFile);
        Environment.SetEnvironmentVariable("CM_DV_PREFIX", "*");
        var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IDataverseMetadataProvider, OfflineDataverseMetadataProvider>();
        var provider = services.BuildServiceProvider();
        var metaProvider = provider.GetRequiredService<IDataverseMetadataProvider>();
        var table = await metaProvider.GetTableMetadataAsync("m360_case");
        Assert.Contains(table.Columns, c => c.Name.Equals("createdon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(table.Columns, c => c.Name.Equals("modifiedon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(table.Columns, c => c.Name.Equals("ownerid", StringComparison.OrdinalIgnoreCase));
    }
}
