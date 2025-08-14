using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CreateMapping.AI;
using CreateMapping.Mapping;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CreateMapping.Tests;

public class PrioritizedMappingTests
{
    [Fact]
    public async Task PrioritizesCustomFieldsOverSystemFields()
    {
        // Setup source with mixed fields
        var source = new TableMetadata("SQL", "orders", new List<ColumnMetadata>
        {
            new("order_id", "int", false, null, null, null, IsPrimaryId: true),
            new("customer_name", "nvarchar", true, 100, null, null),
            new("created_date", "datetime", false, null, null, null),
            new("updated_by", "nvarchar", true, 50, null, null)
        });

        // Setup target with custom and system fields
        var target = new TableMetadata("DATAVERSE", "order", new List<ColumnMetadata>
        {
            new("orderid", "uniqueidentifier", false, null, null, null, IsPrimaryId: true, IsSystemField: false), // Custom primary
            new("customername", "string", true, 100, null, null, IsSystemField: false), // Custom field
            new("createdon", "datetime", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.CreatedOn),
            new("modifiedby", "lookup", true, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.ModifiedBy)
        });

        // AI suggests mappings for both custom and system fields
        var suggestions = new List<AiMappingSuggestion>
        {
            new("order_id", "orderid", 0.85, null, "Primary key match"),
            new("customer_name", "customername", 0.80, null, "Custom field match"), 
            new("created_date", "createdon", 0.75, null, "System audit field"),
            new("updated_by", "modifiedby", 0.70, null, "System audit field")
        };

        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(suggestions);

        var systemClassifier = new SystemFieldClassifier();
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, systemClassifier, logger);

        var weights = WeightsConfig.Default with { AiSimilarity = 1.0, HighThreshold = 0.65, ReviewThreshold = 0.4 };
        var result = await orchestrator.GenerateAsync(source, target, weights);

        // Verify mappings are created (some may be in review due to confidence adjustments)
        var totalMappings = result.Accepted.Count + result.NeedsReview.Count;
        Assert.Equal(4, totalMappings);
        
        // Custom fields should have AI-Custom match type and slightly higher confidence due to boost
        var allMappings = result.Accepted.Concat(result.NeedsReview).ToList();
        var customMappings = allMappings.Where(m => m.MatchType.StartsWith("AI-Custom")).ToList();
        var systemMappings = allMappings.Where(m => m.MatchType.StartsWith("AI-System")).ToList();
        
        Assert.Equal(2, customMappings.Count);
        Assert.Equal(2, systemMappings.Count);

        // Custom field mappings should have received the 1.05 boost
        var orderIdMapping = allMappings.First(m => m.SourceColumn == "order_id");
        var customerMapping = allMappings.First(m => m.SourceColumn == "customer_name");
        
        Assert.Equal("AI-Custom", orderIdMapping.MatchType);
        Assert.Equal("AI-Custom", customerMapping.MatchType);
        Assert.True(orderIdMapping.Confidence > 0.85, $"Expected > 0.85 but got {orderIdMapping.Confidence}"); // 0.85 * 1.05 = 0.8925
        Assert.True(customerMapping.Confidence > 0.80, $"Expected > 0.80 but got {customerMapping.Confidence}"); // 0.80 * 1.05 = 0.84

        // System field mappings should have received the 0.95 adjustment
        var createdMapping = allMappings.First(m => m.SourceColumn == "created_date");
        var modifiedMapping = allMappings.First(m => m.SourceColumn == "updated_by");
        
        Assert.Equal("AI-System-CreatedOn", createdMapping.MatchType);
        Assert.Equal("AI-System-ModifiedBy", modifiedMapping.MatchType);
        Assert.True(createdMapping.Confidence < 0.75, $"Expected < 0.75 but got {createdMapping.Confidence}"); // 0.75 * 0.95 = 0.7125
        Assert.True(modifiedMapping.Confidence < 0.70, $"Expected < 0.70 but got {modifiedMapping.Confidence}"); // 0.70 * 0.95 = 0.665
    }

    [Fact]
    public async Task SystemFieldsOrderedByPriorityWithinSystemType()
    {
        var source = new TableMetadata("SQL", "test", new List<ColumnMetadata>
        {
            new("field1", "nvarchar", true, 50, null, null),
            new("field2", "nvarchar", true, 50, null, null)
        });

        var target = new TableMetadata("DATAVERSE", "test", new List<ColumnMetadata>
        {
            new("createdon", "datetime", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.CreatedOn),
            new("versionnumber", "bigint", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.Version),
            new("msft_other", "string", true, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.Other)
        });

        // Both source fields could map to any target, but priorities should determine order
        var suggestions = new List<AiMappingSuggestion>
        {
            new("field1", "versionnumber", 0.60, null, "Version field"),
            new("field1", "createdon", 0.60, null, "Created field"), // Same confidence
            new("field2", "msft_other", 0.60, null, "Other field")
        };

        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(suggestions);

        var systemClassifier = new SystemFieldClassifier();
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, systemClassifier, logger);

        var weights = WeightsConfig.Default with { AiSimilarity = 1.0, HighThreshold = 0.5 };
        var result = await orchestrator.GenerateAsync(source, target, weights);

        Assert.Equal(2, result.Accepted.Count);
        
        // field1 should map to createdon (priority 2) instead of versionnumber (priority 5)
        var field1Mapping = result.Accepted.FirstOrDefault(m => m.SourceColumn == "field1");
        Assert.NotNull(field1Mapping);
        Assert.Equal("createdon", field1Mapping.TargetColumn);
    }
}