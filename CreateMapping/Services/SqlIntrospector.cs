using System.Data;
using Microsoft.Data.SqlClient;
using CreateMapping.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CreateMapping.Services;

public sealed class SqlIntrospector : ISqlIntrospector
{
    private readonly string _connectionString;
    private readonly ILogger<SqlIntrospector> _logger;

    public SqlIntrospector(IConfiguration config, ILogger<SqlIntrospector> logger)
    {
        _connectionString = config.GetConnectionString("Sql") ?? config["Sql:ConnectionString"] ?? throw new InvalidOperationException("SQL connection string not configured");
        _logger = logger;
    }

    public async Task<TableMetadata> GetTableMetadataAsync(string tableName, string? schema, CancellationToken ct = default)
    {
        var fullName = string.IsNullOrWhiteSpace(schema) ? tableName : $"{schema}.{tableName}";
        var cols = new List<ColumnMetadata>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // First attempt INFORMATION_SCHEMA
        var infoSchemaCmd = conn.CreateCommand();
        infoSchemaCmd.CommandText = @"SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME = @Table AND (@Schema IS NULL OR c.TABLE_SCHEMA = @Schema)
ORDER BY c.ORDINAL_POSITION";
        infoSchemaCmd.Parameters.Add(new SqlParameter("@Table", SqlDbType.NVarChar, 256){ Value = tableName});
        infoSchemaCmd.Parameters.Add(new SqlParameter("@Schema", SqlDbType.NVarChar, 256){ Value = (object?)schema ?? DBNull.Value});

        await using (var reader = await infoSchemaCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                cols.Add(new ColumnMetadata(
                    Name: reader.GetString(0),
                    DataType: reader.GetString(1),
                    IsNullable: string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                    Length: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Precision: reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                    Scale: reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5))
                ));
            }
        }

        if (cols.Count == 0)
        {
            _logger.LogWarning("No columns found for table {Table}; verifying existence via sys.columns", fullName);
        }

        // Supplement with sys.* for identity/computed/default
        var sysCmd = conn.CreateCommand();
        sysCmd.CommandText = @"SELECT col.name, t.name, col.is_nullable, col.max_length, col.precision, col.scale, col.is_identity, col.is_computed, dc.definition
FROM sys.columns col
JOIN sys.types t ON col.user_type_id = t.user_type_id
LEFT JOIN sys.default_constraints dc ON col.default_object_id = dc.object_id
WHERE col.object_id = OBJECT_ID(@FullTable)";
        sysCmd.Parameters.Add(new SqlParameter("@FullTable", SqlDbType.NVarChar, 512){ Value = fullName});

        var sysLookup = new Dictionary<string, (bool identity,bool computed,string? def)>(StringComparer.OrdinalIgnoreCase);
        await using (var reader2 = await sysCmd.ExecuteReaderAsync(ct))
        {
            while (await reader2.ReadAsync(ct))
            {
                var name = reader2.GetString(0);
                sysLookup[name] = (
                    identity: reader2.GetBoolean(6),
                    computed: reader2.GetBoolean(7),
                    def: reader2.IsDBNull(8) ? null : reader2.GetString(8)
                );
            }
        }

        // Merge
        for (int i=0;i<cols.Count;i++)
        {
            if (sysLookup.TryGetValue(cols[i].Name, out var meta))
            {
                cols[i] = cols[i] with { IsIdentity = meta.identity, IsComputed = meta.computed, DefaultDefinition = meta.def };
            }
        }

        return new TableMetadata("SQL", fullName, cols);
    }
}
