using CreateMapping.Models;

namespace CreateMapping.Services;

public interface IDataverseMetadataProvider
{
    Task<TableMetadata> GetTableMetadataAsync(string logicalName, CancellationToken ct = default);
}
