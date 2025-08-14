using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CreateMapping.AI;
using CreateMapping.Mapping;
using CreateMapping.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CreateMapping.Tests;

public class OrchestratorTests
{
    private static TableMetadata SampleSource() => new("SQL", "dbo.Sample", new List<ColumnMetadata>
    {
        new("Id","int", false,null,null,null),
        new("Name","nvarchar", false,100,null,null),
        new("Amount","decimal", false,null,18,2)
    });

    private static TableMetadata SampleTarget() => new("DATAVERSE", "sample", new List<ColumnMetadata>
    {
        new("sampleid","int", false,null,null,null, IsPrimaryId:true),
        new("name","string", true,200,null,null, IsPrimaryName:true),
        new("totalamount","decimal", true,null,18,2)
    });

    [Fact]
    public async Task ReturnsEmptyWhenTargetEmpty()
    {
        var source = SampleSource();
        var target = new TableMetadata("DATAVERSE","empty", new List<ColumnMetadata>());
        var ai = new Mock<IAiMapper>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, logger);
        var result = await orchestrator.GenerateAsync(source, target, WeightsConfig.Default);
        Assert.Empty(result.Accepted);
        Assert.Equal(source.Columns.Count, result.UnresolvedSourceColumns.Count);
    }

    [Fact]
    public async Task AppliesAiSuggestionsWithThresholds()
    {
        var source = SampleSource();
        var target = SampleTarget();
        var suggestions = new List<AiMappingSuggestion>
        {
            new("Id","sampleid",0.95,null,"Primary key match"),
            new("Name","name",0.80,null,"Name mapping"),
            new("Amount","totalamount",0.50,null,"Amount mapping")
        };
        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestions);
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, logger);
        var weights = WeightsConfig.Default with { HighThreshold = 0.70, ReviewThreshold = 0.40, AiSimilarity = 1.0 };
        var result = await orchestrator.GenerateAsync(source, target, weights);
        Assert.Equal(2, result.Accepted.Count); // 0.95 and 0.80
        Assert.Single(result.NeedsReview); // 0.50
        Assert.Empty(result.UnresolvedSourceColumns);
    }

    [Fact]
    public async Task FiltersDuplicateTargetsKeepingFirst()
    {
        var source = SampleSource();
        var target = SampleTarget();
        var suggestions = new List<AiMappingSuggestion>
        {
            new("Id","sampleid",0.9,null,null),
            new("Name","sampleid",0.85,null,null) // duplicate target should be ignored
        };
        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestions);
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, logger);
        var weights = WeightsConfig.Default with { AiSimilarity = 1.0 };
        var result = await orchestrator.GenerateAsync(source, target, weights);
        Assert.Single(result.Accepted);
        Assert.Equal("Id", result.Accepted[0].SourceColumn);
    }

    [Fact]
    public async Task AppliesAiSimilarityScaling()
    {
        var source = SampleSource();
        var target = SampleTarget();
        var suggestions = new List<AiMappingSuggestion>
        {
            new("Amount","totalamount",0.60,null,null)
        };
        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestions);
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, logger);
        var weights = WeightsConfig.Default with { AiSimilarity = 0.5, HighThreshold = 0.7, ReviewThreshold = 0.4 };
        var result = await orchestrator.GenerateAsync(source, target, weights);
        // raw 0.60 * 0.5 = 0.30 below review threshold so unresolved
        Assert.Empty(result.Accepted);
        Assert.Empty(result.NeedsReview);
        Assert.Contains("Amount", result.UnresolvedSourceColumns);
    }

    [Fact]
    public async Task DropsBelowReviewThreshold()
    {
        var source = SampleSource();
        var target = SampleTarget();
        var suggestions = new List<AiMappingSuggestion>
        {
            new("Amount","totalamount",0.35,null,null)
        };
        var ai = new Mock<IAiMapper>();
        ai.Setup(a => a.SuggestMappingsAsync(source, target, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestions);
        var logger = Mock.Of<ILogger<MappingOrchestrator>>();
        var orchestrator = new MappingOrchestrator(ai.Object, logger);
        var weights = WeightsConfig.Default with { AiSimilarity = 1.0, HighThreshold = 0.7, ReviewThreshold = 0.4 };
        var result = await orchestrator.GenerateAsync(source, target, weights);
        Assert.Empty(result.Accepted);
        Assert.Empty(result.NeedsReview);
        Assert.Contains("Amount", result.UnresolvedSourceColumns);
    }
}
