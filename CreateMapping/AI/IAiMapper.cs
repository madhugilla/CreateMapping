using CreateMapping.Models;

namespace CreateMapping.AI;

/// <summary>
/// AI-produced mapping suggestion between a source SQL column and a target Dataverse attribute.
/// </summary>
/// <param name="SourceColumn">Source column name.</param>
/// <param name="TargetColumn">Target column name.</param>
/// <param name="Confidence">Confidence score in [0,1].</param>
/// <param name="Transformation">Optional transformation or conversion guidance.</param>
/// <param name="Rationale">Reasoning text returned by the model.</param>
public record AiMappingSuggestion(
    string SourceColumn,
    string TargetColumn,
    double Confidence,
    string? Transformation,
    string? Rationale
);

/// <summary>
/// Contract for generating AI mapping suggestions. Implementations may log request and response metadata.
/// </summary>
public interface IAiMapper
{
    /// <summary>
    /// Produces mapping suggestions. Implementations (like <see cref="AzureOpenAiMapper"/>) log:
    ///  - prompt payload size and truncated preview when enabled (Ai:LogRequest, defaults true)
    ///  - raw/truncated model response (Ai:LogRaw to avoid truncation)
    ///  - timing and token usage metrics
    /// </summary>
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
