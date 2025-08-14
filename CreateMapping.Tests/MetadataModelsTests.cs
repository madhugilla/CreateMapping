using System;
using System.Collections.Generic;
using CreateMapping.Models;
using Xunit;

namespace CreateMapping.Tests;

public class MetadataModelsTests
{
    [Fact]
    public void ColumnMetadata_CreateWithBasicProperties_SetsPropertiesCorrectly()
    {
        // Act
        var column = new ColumnMetadata(
            Name: "TestColumn",
            DataType: "varchar",
            IsNullable: true,
            Length: 100,
            Precision: null,
            Scale: null
        );

        // Assert
        Assert.Equal("TestColumn", column.Name);
        Assert.Equal("varchar", column.DataType);
        Assert.True(column.IsNullable);
        Assert.Equal(100, column.Length);
        Assert.Null(column.Precision);
        Assert.Null(column.Scale);
        Assert.False(column.IsIdentity); // Default value
        Assert.False(column.IsComputed); // Default value
        Assert.Null(column.DefaultDefinition); // Default value
        Assert.False(column.IsPrimaryId); // Default value
        Assert.False(column.IsPrimaryName); // Default value
        Assert.False(column.IsRequired); // Default value
        Assert.Null(column.OptionSetValues); // Default value
        Assert.False(column.IsSystemField); // Default value
        Assert.Equal(SystemFieldType.None, column.SystemFieldType); // Default value
    }

    [Fact]
    public void ColumnMetadata_CreateWithAllProperties_SetsAllPropertiesCorrectly()
    {
        // Arrange
        var optionValues = new List<string> { "Option1", "Option2" };

        // Act
        var column = new ColumnMetadata(
            Name: "StatusField",
            DataType: "int",
            IsNullable: false,
            Length: null,
            Precision: null,
            Scale: null,
            IsIdentity: true,
            IsComputed: false,
            DefaultDefinition: "NEWID()",
            IsPrimaryId: true,
            IsPrimaryName: false,
            IsRequired: true,
            OptionSetValues: optionValues,
            IsSystemField: true,
            SystemFieldType: SystemFieldType.Status
        );

        // Assert
        Assert.Equal("StatusField", column.Name);
        Assert.Equal("int", column.DataType);
        Assert.False(column.IsNullable);
        Assert.True(column.IsIdentity);
        Assert.False(column.IsComputed);
        Assert.Equal("NEWID()", column.DefaultDefinition);
        Assert.True(column.IsPrimaryId);
        Assert.False(column.IsPrimaryName);
        Assert.True(column.IsRequired);
        Assert.Equal(optionValues, column.OptionSetValues);
        Assert.True(column.IsSystemField);
        Assert.Equal(SystemFieldType.Status, column.SystemFieldType);
    }

    [Fact]
    public void ColumnMetadata_WithRecordSyntax_SupportsImmutableUpdates()
    {
        // Arrange
        var original = new ColumnMetadata("TestColumn", "varchar", true, 100, null, null);

        // Act
        var updated = original with { IsNullable = false, Length = 200 };

        // Assert
        Assert.Equal("TestColumn", updated.Name); // Unchanged
        Assert.Equal("varchar", updated.DataType); // Unchanged
        Assert.False(updated.IsNullable); // Changed
        Assert.Equal(200, updated.Length); // Changed
        
        // Original should remain unchanged
        Assert.True(original.IsNullable);
        Assert.Equal(100, original.Length);
    }

    [Theory]
    [InlineData(SystemFieldType.None)]
    [InlineData(SystemFieldType.CreatedOn)]
    [InlineData(SystemFieldType.CreatedBy)]
    [InlineData(SystemFieldType.ModifiedOn)]
    [InlineData(SystemFieldType.ModifiedBy)]
    [InlineData(SystemFieldType.Owner)]
    [InlineData(SystemFieldType.BusinessUnit)]
    [InlineData(SystemFieldType.State)]
    [InlineData(SystemFieldType.Status)]
    [InlineData(SystemFieldType.Version)]
    [InlineData(SystemFieldType.ImportSequence)]
    [InlineData(SystemFieldType.OverriddenCreatedOn)]
    [InlineData(SystemFieldType.TimeZoneRule)]
    [InlineData(SystemFieldType.UtcConversionTimeZone)]
    [InlineData(SystemFieldType.Other)]
    public void SystemFieldType_AllEnumValues_AreValid(SystemFieldType fieldType)
    {
        // Arrange & Act
        var column = new ColumnMetadata(
            "TestField", "string", true, null, null, null, 
            SystemFieldType: fieldType
        );

        // Assert
        Assert.Equal(fieldType, column.SystemFieldType);
    }

    [Fact]
    public void TableMetadata_CreateBasic_SetsPropertiesCorrectly()
    {
        // Arrange
        var columns = new List<ColumnMetadata>
        {
            new("Id", "int", false, null, null, null),
            new("Name", "varchar", true, 100, null, null)
        };

        // Act
        var table = new TableMetadata("SQL", "TestTable", columns);

        // Assert
        Assert.Equal("SQL", table.SourceSystem);
        Assert.Equal("TestTable", table.Name);
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("Id", table.Columns[0].Name);
        Assert.Equal("Name", table.Columns[1].Name);
    }

    [Fact]
    public void TableMetadata_WithEmptyColumns_HandlesEmptyCollection()
    {
        // Act
        var table = new TableMetadata("DATAVERSE", "EmptyTable", Array.Empty<ColumnMetadata>());

        // Assert
        Assert.Equal("DATAVERSE", table.SourceSystem);
        Assert.Equal("EmptyTable", table.Name);
        Assert.Empty(table.Columns);
    }

    [Fact]
    public void MappingCandidate_CreateBasic_SetsPropertiesCorrectly()
    {
        // Act
        var mapping = new MappingCandidate(
            SourceColumn: "SourceCol",
            TargetColumn: "TargetCol", 
            Confidence: 0.85,
            MatchType: "AI-Custom"
        );

        // Assert
        Assert.Equal("SourceCol", mapping.SourceColumn);
        Assert.Equal("TargetCol", mapping.TargetColumn);
        Assert.Equal(0.85, mapping.Confidence);
        Assert.Equal("AI-Custom", mapping.MatchType);
        Assert.Null(mapping.Transformation); // Default
        Assert.Null(mapping.Rationale); // Default
    }

    [Fact]
    public void MappingCandidate_CreateWithOptionalFields_SetsAllPropertiesCorrectly()
    {
        // Act
        var mapping = new MappingCandidate(
            SourceColumn: "SourceCol",
            TargetColumn: "TargetCol",
            Confidence: 0.92,
            MatchType: "AI-System-CreatedOn",
            Transformation: "GETUTCDATE()",
            Rationale: "System audit field mapping"
        );

        // Assert
        Assert.Equal("SourceCol", mapping.SourceColumn);
        Assert.Equal("TargetCol", mapping.TargetColumn);
        Assert.Equal(0.92, mapping.Confidence);
        Assert.Equal("AI-System-CreatedOn", mapping.MatchType);
        Assert.Equal("GETUTCDATE()", mapping.Transformation);
        Assert.Equal("System audit field mapping", mapping.Rationale);
    }

    [Fact]
    public void MappingResult_CreateBasic_SetsAllPropertiesCorrectly()
    {
        // Arrange
        var source = new TableMetadata("SQL", "Source", Array.Empty<ColumnMetadata>());
        var target = new TableMetadata("DATAVERSE", "Target", Array.Empty<ColumnMetadata>());
        var accepted = new List<MappingCandidate>
        {
            new("Col1", "col1", 0.95, "Exact")
        };
        var needsReview = new List<MappingCandidate>
        {
            new("Col2", "col2", 0.65, "Partial")
        };
        var unresolved = new List<string> { "Col3" };
        var unused = new List<string> { "col4" };
        var generatedTime = DateTime.UtcNow;
        var weights = WeightsConfig.Default;

        // Act
        var result = new MappingResult(
            source, target, accepted, needsReview, 
            unresolved, unused, generatedTime, weights
        );

        // Assert
        Assert.Equal(source, result.Source);
        Assert.Equal(target, result.Target);
        Assert.Single(result.Accepted);
        Assert.Equal("Col1", result.Accepted[0].SourceColumn);
        Assert.Single(result.NeedsReview);
        Assert.Equal("Col2", result.NeedsReview[0].SourceColumn);
        Assert.Single(result.UnresolvedSourceColumns);
        Assert.Equal("Col3", result.UnresolvedSourceColumns[0]);
        Assert.Single(result.UnusedTargetColumns);
        Assert.Equal("col4", result.UnusedTargetColumns[0]);
        Assert.Equal(generatedTime, result.GeneratedAtUtc);
        Assert.Equal(weights, result.Weights);
    }

    [Fact]
    public void WeightsConfig_Default_HasExpectedValues()
    {
        // Act
        var weights = WeightsConfig.Default;

        // Assert
        Assert.Equal(0.50, weights.ExactName);
        Assert.Equal(0.45, weights.CaseInsensitive);
        Assert.Equal(0.40, weights.Normalized);
        Assert.Equal(0.15, weights.SemanticDomain);
        Assert.Equal(0.20, weights.TypeCompatibility);
        Assert.Equal(0.30, weights.AiSimilarity);
        Assert.Equal(-0.02, weights.LengthPenaltyPer10Pct);
        Assert.Equal(0.70, weights.HighThreshold);
        Assert.Equal(0.40, weights.ReviewThreshold);
    }

    [Fact]
    public void WeightsConfig_CreateCustom_OverridesDefaults()
    {
        // Act
        var weights = new WeightsConfig(
            ExactName: 0.60,
            AiSimilarity: 0.40,
            HighThreshold: 0.80
            // Other values should use defaults
        );

        // Assert
        Assert.Equal(0.60, weights.ExactName);
        Assert.Equal(0.45, weights.CaseInsensitive); // Default
        Assert.Equal(0.40, weights.Normalized); // Default
        Assert.Equal(0.15, weights.SemanticDomain); // Default
        Assert.Equal(0.20, weights.TypeCompatibility); // Default
        Assert.Equal(0.40, weights.AiSimilarity); // Overridden
        Assert.Equal(-0.02, weights.LengthPenaltyPer10Pct); // Default
        Assert.Equal(0.80, weights.HighThreshold); // Overridden
        Assert.Equal(0.40, weights.ReviewThreshold); // Default
    }

    [Fact]
    public void WeightsConfig_WithRecordSyntax_SupportsImmutableUpdates()
    {
        // Arrange
        var original = WeightsConfig.Default;

        // Act
        var updated = original with 
        { 
            AiSimilarity = 0.50, 
            HighThreshold = 0.75,
            ReviewThreshold = 0.35
        };

        // Assert
        Assert.Equal(0.30, original.AiSimilarity); // Original unchanged
        Assert.Equal(0.70, original.HighThreshold); // Original unchanged
        Assert.Equal(0.40, original.ReviewThreshold); // Original unchanged
        
        Assert.Equal(0.50, updated.AiSimilarity); // Updated
        Assert.Equal(0.75, updated.HighThreshold); // Updated
        Assert.Equal(0.35, updated.ReviewThreshold); // Updated
        
        // Other values remain the same
        Assert.Equal(original.ExactName, updated.ExactName);
        Assert.Equal(original.CaseInsensitive, updated.CaseInsensitive);
    }

    [Fact]
    public void ColumnMetadata_WithDecimalPrecisionScale_HandlesNumericCorrectly()
    {
        // Act
        var column = new ColumnMetadata(
            "Amount", "decimal", false, null, 18, 2
        );

        // Assert
        Assert.Equal("Amount", column.Name);
        Assert.Equal("decimal", column.DataType);
        Assert.False(column.IsNullable);
        Assert.Null(column.Length); // Length is null for decimal
        Assert.Equal(18, column.Precision);
        Assert.Equal(2, column.Scale);
    }

    [Fact]
    public void ColumnMetadata_WithOptionSetValues_HandlesListCorrectly()
    {
        // Arrange
        var options = new List<string> { "Active", "Inactive", "Pending" };

        // Act
        var column = new ColumnMetadata(
            "Status", "optionset", false, null, null, null,
            OptionSetValues: options
        );

        // Assert
        Assert.Equal("Status", column.Name);
        Assert.Equal("optionset", column.DataType);
        Assert.Equal(3, column.OptionSetValues!.Count);
        Assert.Contains("Active", column.OptionSetValues);
        Assert.Contains("Inactive", column.OptionSetValues);
        Assert.Contains("Pending", column.OptionSetValues);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(0.123456)]
    public void MappingCandidate_WithVariousConfidenceLevels_AcceptsValidRange(double confidence)
    {
        // Act
        var mapping = new MappingCandidate("Source", "Target", confidence, "Test");

        // Assert
        Assert.Equal(confidence, mapping.Confidence);
    }

    [Fact]
    public void MappingResult_WithEmptyCollections_HandlesEmptyInputsGracefully()
    {
        // Arrange
        var source = new TableMetadata("SQL", "Source", Array.Empty<ColumnMetadata>());
        var target = new TableMetadata("DATAVERSE", "Target", Array.Empty<ColumnMetadata>());

        // Act
        var result = new MappingResult(
            source, target, 
            Array.Empty<MappingCandidate>(),
            Array.Empty<MappingCandidate>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            WeightsConfig.Default
        );

        // Assert
        Assert.Empty(result.Accepted);
        Assert.Empty(result.NeedsReview);
        Assert.Empty(result.UnresolvedSourceColumns);
        Assert.Empty(result.UnusedTargetColumns);
    }
}