namespace CreateMapping.Models;

public sealed record ColumnMetadata(
    string Name,
    string DataType,
    bool IsNullable,
    int? Length,
    int? Precision,
    int? Scale,
    bool IsIdentity = false,
    bool IsComputed = false,
    string? DefaultDefinition = null,
    bool IsPrimaryId = false,
    bool IsPrimaryName = false,
    bool IsRequired = false,
    IReadOnlyList<string>? OptionSetValues = null,
    bool IsSystemField = false,
    SystemFieldType SystemFieldType = SystemFieldType.None,
    string? DisplayName = null,
    string? AttributeTypeName = null
);

public enum SystemFieldType
{
    None,
    CreatedOn,
    CreatedBy,
    ModifiedOn,
    ModifiedBy,
    Owner,
    BusinessUnit,
    State,
    Status,
    Version,
    ImportSequence,
    OverriddenCreatedOn,
    TimeZoneRule,
    UtcConversionTimeZone,
    Other
}

public sealed record TableMetadata(
    string SourceSystem, // "SQL" or "DATAVERSE"
    string Name,
    IReadOnlyList<ColumnMetadata> Columns
);

public sealed record MappingCandidate(
    string SourceColumn,
    string TargetColumn,
    double Confidence,
    string MatchType,
    string? Transformation = null,
    string? Rationale = null
);

public sealed record MappingResult(
    TableMetadata Source,
    TableMetadata Target,
    IReadOnlyList<MappingCandidate> Accepted,
    IReadOnlyList<MappingCandidate> NeedsReview,
    IReadOnlyList<string> UnresolvedSourceColumns,
    IReadOnlyList<string> UnusedTargetColumns,
    DateTime GeneratedAtUtc,
    WeightsConfig Weights
);

public sealed record WeightsConfig(
    double ExactName = 0.50,
    double CaseInsensitive = 0.45,
    double Normalized = 0.40,
    double SemanticDomain = 0.15,
    double TypeCompatibility = 0.20,
    // AiSimilarity previously defaulted to 0.30 which excessively suppressed AI suggestions (multiplicative scaling)
    // Set to 1.0 so raw AI confidence is used unless explicitly lowered.
    double AiSimilarity = 1.0,
    double LengthPenaltyPer10Pct = -0.02,
    double HighThreshold = 0.70,
    double ReviewThreshold = 0.40
)
{
    public static WeightsConfig Default { get; } = new();
}
