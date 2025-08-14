using CreateMapping.Models;

namespace CreateMapping.AI;

public record AiMappingSuggestion(
    string SourceColumn,
    string TargetColumn,
    double Confidence,
    string? Transformation,
    string? Rationale
);

public interface IAiMapper
{
    Task<IReadOnlyList<AiMappingSuggestion>> SuggestMappingsAsync(
        TableMetadata source,
        TableMetadata target,
        IReadOnlyCollection<string> unresolvedSourceColumns,
        CancellationToken ct = default);
}

public sealed class NoOpAiMapper : IAiMapper
{
    public Task<IReadOnlyList<AiMappingSuggestion>> SuggestMappingsAsync(TableMetadata source, TableMetadata target, IReadOnlyCollection<string> unresolvedSourceColumns, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiMappingSuggestion>>(Array.Empty<AiMappingSuggestion>());
}
