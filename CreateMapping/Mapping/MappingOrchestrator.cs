using CreateMapping.AI;
using CreateMapping.Models;
using Microsoft.Extensions.Logging;

namespace CreateMapping.Mapping;

public interface IMappingOrchestrator
{
    Task<MappingResult> GenerateAsync(TableMetadata source, TableMetadata target, WeightsConfig weights, CancellationToken ct = default);
}

public sealed class MappingOrchestrator : IMappingOrchestrator
{
    private readonly IAiMapper _ai;
    private readonly ILogger<MappingOrchestrator> _logger;

    public MappingOrchestrator(IAiMapper ai, ILogger<MappingOrchestrator> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<MappingResult> GenerateAsync(TableMetadata source, TableMetadata target, WeightsConfig weights, CancellationToken ct = default)
    {
        if (target.Columns.Count == 0)
        {
            return new MappingResult(source, target, Array.Empty<MappingCandidate>(), Array.Empty<MappingCandidate>(), source.Columns.Select(c => c.Name).ToList(), Array.Empty<string>(), DateTime.UtcNow, weights);
        }

        IReadOnlyList<AiMappingSuggestion> aiSuggestions = Array.Empty<AiMappingSuggestion>();
        try
        {
            var sourceColumnNames = source.Columns.Select(c => c.Name).ToList();
            _logger.LogInformation("Requesting AI mapping suggestions (source cols: {SourceCount}, target cols: {TargetCount})", source.Columns.Count, target.Columns.Count);
            aiSuggestions = await _ai.SuggestMappingsAsync(source, target, sourceColumnNames, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI mapping failed; producing empty mapping");
        }

        var accepted = new List<MappingCandidate>();
        var review = new List<MappingCandidate>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in aiSuggestions)
        {
            if (usedTargets.Contains(s.TargetColumn) || usedSources.Contains(s.SourceColumn)) continue;
            if (!source.Columns.Any(c => c.Name.Equals(s.SourceColumn, StringComparison.OrdinalIgnoreCase))) continue;
            if (!target.Columns.Any(c => c.Name.Equals(s.TargetColumn, StringComparison.OrdinalIgnoreCase))) continue;
            var conf = s.Confidence * weights.AiSimilarity;
            var candidate = new MappingCandidate(s.SourceColumn, s.TargetColumn, conf, "AI", s.Transformation, s.Rationale);
            if (conf >= weights.HighThreshold)
            {
                accepted.Add(candidate);
            }
            else if (conf >= weights.ReviewThreshold)
            {
                review.Add(candidate);
            }
            usedTargets.Add(s.TargetColumn);
            usedSources.Add(s.SourceColumn);
        }

        var unresolved = source.Columns.Select(c => c.Name)
            .Except(accepted.Select(a => a.SourceColumn).Concat(review.Select(r => r.SourceColumn)), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unused = target.Columns.Select(c => c.Name)
            .Except(accepted.Select(a => a.TargetColumn).Concat(review.Select(r => r.TargetColumn)), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MappingResult(source, target, accepted, review, unresolved, unused, DateTime.UtcNow, weights);
    }
}
