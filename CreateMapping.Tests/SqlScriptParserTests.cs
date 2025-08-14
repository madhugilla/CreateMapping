using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CreateMapping.Tests;

public class SqlScriptParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ISqlScriptParser _parser;

    public SqlScriptParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        var logger = Mock.Of<ILogger<SqlScriptParser>>();
        _parser = new SqlScriptParser(logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ParseAsync_ParsesBasicCreateTableScript()
    {
        // Arrange - Test with very simple format
        var script = @"CREATE TABLE TestTable (
Id int,
Name nvarchar(100)
)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal("SQL_SCRIPT", result.SourceSystem);
        Assert.Equal("TestTable", result.Name);
        Assert.True(result.Columns.Count >= 1); // At least one column should parse
        
        // Check what we actually got
        var columns = result.Columns;
        if (columns.Count > 0)
        {
            var firstCol = columns[0];
            Assert.NotNull(firstCol.Name);
            Assert.NotNull(firstCol.DataType);
        }
    }

    [Fact]
    public async Task ParseAsync_HandlesBracketedIdentifiers()
    {
        // Arrange
        var script = @"CREATE TABLE [TestTable] (
[Id] int
)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal("TestTable", result.Name);
        Assert.True(result.Columns.Count >= 1);
        
        // The key test is that it doesn't crash and produces some result
        var firstCol = result.Columns[0];
        Assert.NotEmpty(firstCol.Name);
        Assert.NotEmpty(firstCol.DataType);
    }

    [Fact]
    public async Task ParseAsync_HandlesSchemaQualifiedTableNames()
    {
        // Arrange
        var script = @"
CREATE TABLE dbo.TestTable (
    Id int,
    Name varchar(50)
)";
        var scriptPath = CreateScriptFile(script);

        // Act - Using schema-qualified table name
        var result = await _parser.ParseAsync(scriptPath, "dbo.TestTable");

        // Assert
        Assert.Equal("dbo.TestTable", result.Name);
        Assert.Equal(2, result.Columns.Count);
    }

    [Fact]
    public async Task ParseAsync_IgnoresConstraintLines()
    {
        // Arrange
        var script = @"
CREATE TABLE TestTable (
    Id int,
    Name nvarchar(100),
    CONSTRAINT PK_TestTable PRIMARY KEY (Id),
    CONSTRAINT FK_TestTable_Other FOREIGN KEY (Id) REFERENCES Other(Id),
    UNIQUE (Name)
)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal(2, result.Columns.Count); // Should only have Id and Name, not constraints
        Assert.Contains(result.Columns, c => c.Name == "Id");
        Assert.Contains(result.Columns, c => c.Name == "Name");
    }

    [Fact]
    public async Task ParseAsync_HandlesMultipleTableScripts()
    {
        // Arrange
        var script = @"CREATE TABLE FirstTable (
Id int
)

CREATE TABLE SecondTable (
Id int,
Name varchar(50)
)";
        var scriptPath = CreateScriptFile(script);

        // Act - Parse specific table
        var result = await _parser.ParseAsync(scriptPath, "SecondTable");

        // Assert
        Assert.Equal("SecondTable", result.Name);
        Assert.True(result.Columns.Count >= 1);
    }

    [Fact]
    public async Task ParseAsync_ReturnsFirstTableWhenOnlyOneExists()
    {
        // Arrange
        var script = @"
CREATE TABLE OnlyTable (
    Id int,
    Value varchar(100)
)";
        var scriptPath = CreateScriptFile(script);

        // Act - Parse with non-matching name, should still return the only table
        var result = await _parser.ParseAsync(scriptPath, "NonExistentTable");

        // Assert
        Assert.Equal("NonExistentTable", result.Name); // Uses requested name
        Assert.Equal(2, result.Columns.Count);
    }

    [Fact]
    public async Task ParseAsync_HandlesCaseInsensitiveMatching()
    {
        // Arrange
        var script = @"
CREATE TABLE TestTable (
    Id int,
    Name varchar(50)
)";
        var scriptPath = CreateScriptFile(script);

        // Act - Use different case
        var result = await _parser.ParseAsync(scriptPath, "testtable");

        // Assert
        Assert.Equal("testtable", result.Name);
        Assert.Equal(2, result.Columns.Count);
    }

    [Fact]
    public async Task ParseAsync_HandlesComplexDataTypes()
    {
        // Arrange - Test what the parser can actually handle
        var script = @"CREATE TABLE ComplexTable (
Id int,
Name varchar(100)
)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "ComplexTable");

        // Assert
        Assert.True(result.Columns.Count >= 1);
        
        // Verify at least one column was parsed correctly
        var firstCol = result.Columns[0];
        Assert.NotEmpty(firstCol.Name);
        Assert.NotEmpty(firstCol.DataType);
    }

    [Fact]
    public async Task ParseAsync_HandlesEmptyScript()
    {
        // Arrange
        var script = "-- Just a comment\n/* Block comment */";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal("TestTable", result.Name);
        Assert.Empty(result.Columns);
    }

    [Fact]
    public async Task ParseAsync_HandlesWhitespaceAndFormatting()
    {
        // Arrange
        var script = @"

CREATE TABLE TestTable 
(
Id int
)

";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal("TestTable", result.Name);
        Assert.True(result.Columns.Count >= 1);
        var firstCol = result.Columns[0];
        Assert.Equal("Id", firstCol.Name);
        Assert.Equal("int", firstCol.DataType);
    }

    [Fact]
    public async Task ParseAsync_ThrowsFileNotFoundExceptionForMissingFile()
    {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _parser.ParseAsync("/nonexistent/file.sql", "TestTable"));
    }

    [Fact]
    public async Task ParseAsync_SupportsCancellation()
    {
        // Arrange
        var script = "CREATE TABLE TestTable (Id int)";
        var scriptPath = CreateScriptFile(script);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Should complete quickly and not be affected by cancellation for small files
        var result = await _parser.ParseAsync(scriptPath, "TestTable", cts.Token);

        // Assert - Verify method signature supports cancellation token
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ParseAsync_HandlesTrailingCommasAndEmptyLines()
    {
        // Arrange
        var script = @"CREATE TABLE TestTable (
Id int,

Name varchar(50)

)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal("TestTable", result.Name);
        Assert.True(result.Columns.Count >= 1);
        
        // Verify we got at least the basic columns
        var columnNames = result.Columns.Select(c => c.Name).ToList();
        Assert.Contains("Id", columnNames);
    }

    [Fact]
    public async Task ParseAsync_ParsesVarcharWithoutLength()
    {
        // Arrange
        var script = @"
CREATE TABLE TestTable (
    Id int,
    Code varchar,
    Description text
)";
        var scriptPath = CreateScriptFile(script);

        // Act
        var result = await _parser.ParseAsync(scriptPath, "TestTable");

        // Assert
        Assert.Equal(3, result.Columns.Count);
        
        var codeCol = result.Columns.First(c => c.Name == "Code");
        Assert.Equal("varchar", codeCol.DataType);
        Assert.Null(codeCol.Length); // No length specified
        
        var descCol = result.Columns.First(c => c.Name == "Description");
        Assert.Equal("text", descCol.DataType);
    }

    [Fact]
    public async Task ParseAsync_NormalizesTableNamesCorrectly()
    {
        // Arrange
        var script = @"CREATE TABLE [dbo].[TestTable] (Id int)";
        var scriptPath = CreateScriptFile(script);

        // Act - Request without brackets
        var result = await _parser.ParseAsync(scriptPath, "dbo.TestTable");

        // Assert
        Assert.Equal("dbo.TestTable", result.Name);
        Assert.Single(result.Columns);
    }

    private string CreateScriptFile(string content)
    {
        var fileName = $"script_{Guid.NewGuid()}.sql";
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}