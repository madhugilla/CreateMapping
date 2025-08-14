using CreateMapping.Models;

namespace CreateMapping.Services;

public interface ISqlIntrospector
{
    Task<TableMetadata> GetTableMetadataAsync(string tableName, string? schema, CancellationToken ct = default);
}
