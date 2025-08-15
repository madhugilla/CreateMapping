using CreateMapping.AI;
using CreateMapping.Models;
using CreateMapping.Services;
using Microsoft.Extensions.Logging;

namespace CreateMapping.Mapping;

public interface IMappingOrchestrator
{
    Task<MappingResult> GenerateAsync(TableMetadata source, TableMetadata target, WeightsConfig weights, CancellationToken ct = default);
}

public sealed class MappingOrchestrator : IMappingOrchestrator
{
    private readonly IAiMapper _ai;
    private readonly ISystemFieldClassifier _systemFieldClassifier;
    private readonly ILogger<MappingOrchestrator> _logger;

    public MappingOrchestrator(IAiMapper ai, ISystemFieldClassifier systemFieldClassifier, ILogger<MappingOrchestrator> logger)
    {
        _ai = ai;
        _systemFieldClassifier = systemFieldClassifier;
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
            _logger.LogInformation("Requesting AI mapping suggestions (source cols: {SourceCount}, target cols: {TargetCount}, custom target cols: {CustomCount}, system target cols: {SystemCount})", 
                source.Columns.Count, target.Columns.Count, 
                target.Columns.Count(c => !c.IsSystemField), 
                target.Columns.Count(c => c.IsSystemField));
            aiSuggestions = await _ai.SuggestMappingsAsync(source, target, Array.Empty<string>(), ct);
            _logger.LogInformation("Received {SuggestionCount} AI suggestions before filtering", aiSuggestions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI mapping failed; producing empty mapping");
        }

        var accepted = new List<MappingCandidate>();
        var review = new List<MappingCandidate>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sort AI suggestions by target field priority (custom fields first, then system fields by priority)
        var sortedSuggestions = aiSuggestions
            .Where(s => source.Columns.Any(c => c.Name.Equals(s.SourceColumn, StringComparison.OrdinalIgnoreCase)) &&
                       target.Columns.Any(c => c.Name.Equals(s.TargetColumn, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => {
                var targetColumn = target.Columns.First(c => c.Name.Equals(s.TargetColumn, StringComparison.OrdinalIgnoreCase));
                return _systemFieldClassifier.GetMappingPriority(targetColumn);
            })
            .ThenByDescending(s => s.Confidence) // Within same priority, prefer higher confidence
            .ToList();

        foreach (var s in sortedSuggestions)
        {
            if (usedTargets.Contains(s.TargetColumn) || usedSources.Contains(s.SourceColumn)) continue;
            
            var targetColumn = target.Columns.First(c => c.Name.Equals(s.TargetColumn, StringComparison.OrdinalIgnoreCase));
            var confidence = s.Confidence * weights.AiSimilarity;
            
            // Apply priority-based confidence adjustment
            if (!targetColumn.IsSystemField)
            {
                // Custom fields get a small boost
                confidence *= 1.05;
            }
            else
            {
                // System fields confidence is slightly reduced to prioritize custom mappings
                confidence *= 0.95;
            }
            
            var matchType = targetColumn.IsSystemField ? $"AI-System-{targetColumn.SystemFieldType}" : "AI-Custom";
            var candidate = new MappingCandidate(s.SourceColumn, s.TargetColumn, confidence, matchType, s.Transformation, s.Rationale);
            
            if (confidence >= weights.HighThreshold)
            {
                accepted.Add(candidate);
                _logger.LogDebug("Accepted mapping: {Source} -> {Target} (confidence: {Confidence:F3}, type: {Type})", 
                    s.SourceColumn, s.TargetColumn, confidence, matchType);
            }
            else if (confidence >= weights.ReviewThreshold)
            {
                review.Add(candidate);
                _logger.LogDebug("Review mapping: {Source} -> {Target} (confidence: {Confidence:F3}, type: {Type})", 
                    s.SourceColumn, s.TargetColumn, confidence, matchType);
            }
            else
            {
                _logger.LogDebug("Rejected mapping: {Source} -> {Target} (confidence: {Confidence:F3}, type: {Type}) - below review threshold", 
                    s.SourceColumn, s.TargetColumn, confidence, matchType);
                continue;
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

        _logger.LogInformation("Mapping complete: {AcceptedCount} accepted, {ReviewCount} need review, {UnresolvedCount} unresolved source columns, {UnusedCount} unused target columns",
            accepted.Count, review.Count, unresolved.Count, unused.Count);

        return new MappingResult(source, target, accepted, review, unresolved, unused, DateTime.UtcNow, weights);
    }
}
