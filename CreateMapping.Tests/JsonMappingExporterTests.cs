using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CreateMapping.Export;
using CreateMapping.Models;
using Xunit;

namespace CreateMapping.Tests;

public class JsonMappingExporterTests : IDisposable
{
    private readonly string _tempDir;

    public JsonMappingExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task WriteAsync_CreatesValidJsonFile()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateSampleMappingResult();
        var filePath = Path.Combine(_tempDir, "test.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify it's valid JSON by parsing as JsonDocument
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        Assert.Equal("SQL", root.GetProperty("Source").GetProperty("SourceSystem").GetString());
        Assert.Equal("DATAVERSE", root.GetProperty("Target").GetProperty("SourceSystem").GetString());
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateSampleMappingResult();
        var subDir = Path.Combine(_tempDir, "subdir", "nested");
        var filePath = Path.Combine(subDir, "test.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task WriteAsync_ProducesIndentedJson()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateSampleMappingResult();
        var filePath = Path.Combine(_tempDir, "indented.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        
        // Indented JSON should contain newlines and spaces
        Assert.Contains("\n", content);
        Assert.Contains("  ", content); // Indentation spaces
    }

    [Fact]
    public async Task WriteAsync_SerializesAllMappingData()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateSampleMappingResult();
        var filePath = Path.Combine(_tempDir, "complete.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify JSON structure by parsing as JsonDocument instead of deserializing to complex object
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        
        Assert.Equal("TestTable", root.GetProperty("Source").GetProperty("Name").GetString());
        Assert.Equal("test", root.GetProperty("Target").GetProperty("Name").GetString());
        Assert.Equal(1, root.GetProperty("Accepted").GetArrayLength());
        Assert.Equal(1, root.GetProperty("NeedsReview").GetArrayLength());
        
        var acceptedMapping = root.GetProperty("Accepted")[0];
        Assert.Equal("TestId", acceptedMapping.GetProperty("SourceColumn").GetString());
        Assert.Equal("testid", acceptedMapping.GetProperty("TargetColumn").GetString());
        Assert.Equal(0.95, acceptedMapping.GetProperty("Confidence").GetDouble());
        Assert.Equal("AI-Custom", acceptedMapping.GetProperty("MatchType").GetString());
    }

    [Fact]
    public async Task WriteAsync_HandlesMappingResultWithEmptyCollections()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateEmptyMappingResult();
        var filePath = Path.Combine(_tempDir, "empty.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify structure using JsonDocument instead of full deserialization
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("Source").GetProperty("Columns").GetArrayLength());
        Assert.Equal(0, root.GetProperty("Target").GetProperty("Columns").GetArrayLength());
        Assert.Equal(0, root.GetProperty("Accepted").GetArrayLength());
        Assert.Equal(0, root.GetProperty("NeedsReview").GetArrayLength());
        Assert.Equal(0, root.GetProperty("UnresolvedSourceColumns").GetArrayLength());
        Assert.Equal(0, root.GetProperty("UnusedTargetColumns").GetArrayLength());
    }

    [Fact]
    public async Task WriteAsync_PreservesSystemFieldTypes()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateMappingResultWithSystemFields();
        var filePath = Path.Combine(_tempDir, "system_fields.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify JSON contains system field information
        Assert.Contains("\"SystemFieldType\":", content);
        Assert.Contains("\"CreatedOn\"", content);
        Assert.Contains("\"IsSystemField\":", content);
        Assert.Contains("\"createdon\"", content);
    }

    [Fact]
    public async Task WriteAsync_HandlesPrecisionAndScale()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateMappingResultWithDecimalColumns();
        var filePath = Path.Combine(_tempDir, "decimal.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify precision and scale are preserved in JSON
        Assert.Contains("\"Precision\": 18", content);
        Assert.Contains("\"Scale\": 2", content);
        Assert.Contains("\"decimal\"", content);
    }

    [Fact]
    public async Task WriteAsync_HandlesNullOptionalFields()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateMappingResultWithNulls();
        var filePath = Path.Combine(_tempDir, "nulls.json");

        // Act & Assert - Should not throw
        await exporter.WriteAsync(result, filePath);
        
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify null values are handled correctly in JSON
        Assert.Contains("\"Transformation\": null", content);
        Assert.Contains("\"Rationale\": null", content);
    }

    [Fact]
    public async Task WriteAsync_SupportsCancellation()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var result = CreateSampleMappingResult();
        var filePath = Path.Combine(_tempDir, "cancel.json");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException is derived from OperationCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await exporter.WriteAsync(result, filePath, cts.Token));
    }

    [Fact]
    public async Task WriteAsync_PreservesDateTimeUtc()
    {
        // Arrange
        var exporter = new JsonMappingExporter();
        var specificTime = new DateTime(2023, 12, 25, 15, 30, 45, DateTimeKind.Utc);
        var result = CreateMappingResultWithSpecificTime(specificTime);
        var filePath = Path.Combine(_tempDir, "datetime.json");

        // Act
        await exporter.WriteAsync(result, filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        
        // Verify the datetime is preserved in JSON (as ISO 8601)
        Assert.Contains("2023-12-25T15:30:45", content);
    }

    private static MappingResult CreateSampleMappingResult()
    {
        var sourceColumns = new[]
        {
            new ColumnMetadata("TestId", "int", false, null, null, null),
            new ColumnMetadata("TestName", "nvarchar", true, 100, null, null)
        };

        var targetColumns = new[]
        {
            new ColumnMetadata("testid", "uniqueidentifier", false, null, null, null, IsPrimaryId: true),
            new ColumnMetadata("name", "string", false, 100, null, null, IsPrimaryName: true)
        };

        var source = new TableMetadata("SQL", "TestTable", sourceColumns);
        var target = new TableMetadata("DATAVERSE", "test", targetColumns);

        var accepted = new[]
        {
            new MappingCandidate("TestId", "testid", 0.95, "AI-Custom", "NEWID()", "Primary key mapping")
        };

        var needsReview = new[]
        {
            new MappingCandidate("TestName", "name", 0.75, "AI-Custom", null, "Name field mapping")
        };

        return new MappingResult(
            source,
            target,
            accepted,
            needsReview,
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );
    }

    private static MappingResult CreateEmptyMappingResult()
    {
        var source = new TableMetadata("SQL", "TestTable", Array.Empty<ColumnMetadata>());
        var target = new TableMetadata("DATAVERSE", "test", Array.Empty<ColumnMetadata>());

        return new MappingResult(
            source,
            target,
            Array.Empty<MappingCandidate>(),
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );
    }

    private static MappingResult CreateMappingResultWithSystemFields()
    {
        var sourceColumns = new[]
        {
            new ColumnMetadata("CreatedDate", "datetime", false, null, null, null)
        };

        var targetColumns = new[]
        {
            new ColumnMetadata("createdon", "datetime", false, null, null, null, 
                IsSystemField: true, SystemFieldType: SystemFieldType.CreatedOn)
        };

        var source = new TableMetadata("SQL", "TestTable", sourceColumns);
        var target = new TableMetadata("DATAVERSE", "test", targetColumns);

        return new MappingResult(
            source,
            target,
            Array.Empty<MappingCandidate>(),
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );
    }

    private static MappingResult CreateMappingResultWithDecimalColumns()
    {
        var sourceColumns = new[]
        {
            new ColumnMetadata("Amount", "decimal", false, null, 18, 2)
        };

        var targetColumns = new[]
        {
            new ColumnMetadata("amount", "money", false, null, 18, 2)
        };

        var source = new TableMetadata("SQL", "TestTable", sourceColumns);
        var target = new TableMetadata("DATAVERSE", "test", targetColumns);

        return new MappingResult(
            source,
            target,
            Array.Empty<MappingCandidate>(),
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );
    }

    private static MappingResult CreateMappingResultWithNulls()
    {
        var sourceColumns = new[]
        {
            new ColumnMetadata("TestId", "int", false, null, null, null)
        };

        var targetColumns = new[]
        {
            new ColumnMetadata("testid", "uniqueidentifier", false, null, null, null, IsPrimaryId: true)
        };

        var source = new TableMetadata("SQL", "TestTable", sourceColumns);
        var target = new TableMetadata("DATAVERSE", "test", targetColumns);

        var accepted = new[]
        {
            new MappingCandidate("TestId", "testid", 0.95, "AI-Custom", null, null)
        };

        return new MappingResult(
            source,
            target,
            accepted,
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );
    }

    private static MappingResult CreateMappingResultWithSpecificTime(DateTime time)
    {
        var source = new TableMetadata("SQL", "TestTable", Array.Empty<ColumnMetadata>());
        var target = new TableMetadata("DATAVERSE", "test", Array.Empty<ColumnMetadata>());

        return new MappingResult(
            source,
            target,
            Array.Empty<MappingCandidate>(),
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            time,
            WeightsConfig.Default
        );
    }
}