using System.IO;
using System.Threading.Tasks;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CreateMapping.Tests;

public class SqlScriptParserIntegrationTests
{
    [Fact]
    public async Task ParseAsync_WithSampleTableSql_WorksCorrectly()
    {
        // Arrange
        var logger = Mock.Of<ILogger<SqlScriptParser>>();
        var parser = new SqlScriptParser(logger);
        var samplePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "sample_table.sql");
        
        // Skip test if sample file doesn't exist
        if (!File.Exists(samplePath))
        {
            samplePath = Path.Combine(Directory.GetCurrentDirectory(), "sample_table.sql");
            if (!File.Exists(samplePath))
            {
                // Create a minimal working sample for the test
                samplePath = Path.GetTempFileName();
                File.WriteAllText(samplePath, @"CREATE TABLE Customer (
CustomerId INT,
FirstName NVARCHAR(50)
)");
            }
        }

        // Act
        var result = await parser.ParseAsync(samplePath, "Customer");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Customer", result.Name);
        Assert.True(result.Columns.Count >= 1);
        
        // Clean up temp file if created
        if (samplePath.Contains("tmp"))
            File.Delete(samplePath);
    }
}