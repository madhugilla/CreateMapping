using CreateMapping.Models;

namespace CreateMapping.Services;

public interface ISystemFieldClassifier
{
    /// <summary>
    /// Determines if a Dataverse attribute is a system field and classifies its type
    /// </summary>
    (bool IsSystemField, SystemFieldType SystemFieldType) ClassifyField(string logicalName, string? dataType = null);
    
    /// <summary>
    /// Gets priority for mapping - lower numbers get mapped first
    /// Custom fields: 1, System fields: 2-10 based on importance
    /// </summary>
    int GetMappingPriority(ColumnMetadata column);
}

public sealed class SystemFieldClassifier : ISystemFieldClassifier
{
    private static readonly Dictionary<string, SystemFieldType> SystemFieldMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audit fields
        ["createdon"] = SystemFieldType.CreatedOn,
        ["createdby"] = SystemFieldType.CreatedBy,
        ["modifiedon"] = SystemFieldType.ModifiedOn,
        ["modifiedby"] = SystemFieldType.ModifiedBy,
        ["overriddencreatedon"] = SystemFieldType.OverriddenCreatedOn,
        
        // Ownership fields
        ["ownerid"] = SystemFieldType.Owner,
        ["owningbusinessunit"] = SystemFieldType.BusinessUnit,
        ["owningteam"] = SystemFieldType.BusinessUnit,
        ["owninguser"] = SystemFieldType.Owner,
        
        // State management
        ["statecode"] = SystemFieldType.State,
        ["statuscode"] = SystemFieldType.Status,
        
        // System tracking
        ["versionnumber"] = SystemFieldType.Version,
        ["importsequencenumber"] = SystemFieldType.ImportSequence,
        
        // Time zone
        ["timezoneruleversionnumber"] = SystemFieldType.TimeZoneRule,
        ["utcconversiontimezonecode"] = SystemFieldType.UtcConversionTimeZone,
        
        // Exchange sync (if present)
        ["exchangerate"] = SystemFieldType.Other,
        ["transactioncurrencyid"] = SystemFieldType.Other,
    };
    
    private static readonly HashSet<string> SystemFieldPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msft_",
        "msdyn_",
        "mspcat_"
    };

    public (bool IsSystemField, SystemFieldType SystemFieldType) ClassifyField(string logicalName, string? dataType = null)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            return (false, SystemFieldType.None);

        // Check direct mappings first
        if (SystemFieldMappings.TryGetValue(logicalName, out var systemType))
            return (true, systemType);

        // Check system prefixes
        if (SystemFieldPrefixes.Any(prefix => logicalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return (true, SystemFieldType.Other);

        // Check for common patterns
        if (logicalName.EndsWith("id", StringComparison.OrdinalIgnoreCase) && 
            (logicalName.StartsWith("created", StringComparison.OrdinalIgnoreCase) ||
             logicalName.StartsWith("modified", StringComparison.OrdinalIgnoreCase)))
            return (true, SystemFieldType.Other);

        return (false, SystemFieldType.None);
    }

    public int GetMappingPriority(ColumnMetadata column)
    {
        if (!column.IsSystemField)
            return 1; // Custom fields have highest priority

        return column.SystemFieldType switch
        {
            SystemFieldType.CreatedOn => 2,      // Important audit fields
            SystemFieldType.CreatedBy => 2,
            SystemFieldType.ModifiedOn => 2,
            SystemFieldType.ModifiedBy => 2,
            SystemFieldType.State => 3,          // State management
            SystemFieldType.Status => 3,
            SystemFieldType.Owner => 4,          // Ownership
            SystemFieldType.BusinessUnit => 4,
            SystemFieldType.Version => 5,        // System tracking
            SystemFieldType.ImportSequence => 6,
            SystemFieldType.OverriddenCreatedOn => 7,
            SystemFieldType.TimeZoneRule => 8,
            SystemFieldType.UtcConversionTimeZone => 8,
            SystemFieldType.Other => 9,
            _ => 10
        };
    }
}