using CreateMapping.Models;

namespace CreateMapping.Services;

public interface ISqlScriptParser
{
    /// <summary>
    /// Parse a CREATE TABLE script file and return TableMetadata. The provided tableName parameter is the logical
    /// table identifier argument passed by user (schema-qualified). If multiple CREATE TABLE statements exist,
    /// the first matching the name is used.
    /// </summary>
    Task<TableMetadata> ParseAsync(string scriptPath, string tableName, CancellationToken ct = default);
}
