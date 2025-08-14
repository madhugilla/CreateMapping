using CreateMapping.Models;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CreateMapping.Services;

public sealed class DataverseMetadataProvider : IDataverseMetadataProvider
{
    private readonly ServiceClient _client;
    private readonly ILogger<DataverseMetadataProvider> _logger;
    private readonly ISystemFieldClassifier _systemFieldClassifier;

    public DataverseMetadataProvider(IConfiguration config, ILogger<DataverseMetadataProvider> logger, ISystemFieldClassifier systemFieldClassifier)
    {
        _logger = logger;
        _systemFieldClassifier = systemFieldClassifier;
        var url = config["Dataverse:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Dataverse:Url not configured; operating in offline mode (empty Dataverse metadata).");
            _client = null!;
            return;
        }
        // Username/password per user request (NOT recommended long term). Expect env variables for security.
        var user = config["Dataverse:Username"];
        var pwd = config["Dataverse:Password"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
        {
            _logger.LogWarning("Dataverse username/password not configured; returning empty metadata.");
            _client = null!; // Will short circuit in method.
            return;
        }

        // Connection string form: AuthType=OAuth;Username=...;Password=...;Url=...;AppId=...;RedirectUri=...; but for simple user/pwd legacy we can try simpler
        // Using simplified connection string relying on username/password (must have proper config of TLS & security). For production should use OAuth/ClientSecret.
        var cs = $"AuthType=OAuth;Username={user};Password={pwd};Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Never";
        _client = new ServiceClient(cs);
    }

    public async Task<TableMetadata> GetTableMetadataAsync(string logicalName, CancellationToken ct = default)
    {
        if (_client is null || !_client.IsReady)
        {
            return new TableMetadata("DATAVERSE", logicalName, new List<ColumnMetadata>());
        }

        var req = new RetrieveEntityRequest
        {
            LogicalName = logicalName,
            EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes
        };
        var resp = (RetrieveEntityResponse)await _client.ExecuteAsync(req);
        var entity = resp.EntityMetadata;
        var cols = new List<ColumnMetadata>();
        foreach (var attr in entity.Attributes)
        {
            if (attr is null) continue;
            cols.Add(MapAttribute(attr, entity));
        }

        return new TableMetadata("DATAVERSE", logicalName, cols);
    }

    private ColumnMetadata MapAttribute(AttributeMetadata attr, EntityMetadata entity)
    {
        int? maxLength = attr switch
        {
            StringAttributeMetadata s => s.MaxLength,
            MemoAttributeMetadata m => m.MaxLength,
            _ => null
        };
        bool required = attr.RequiredLevel?.Value == AttributeRequiredLevel.ApplicationRequired || attr.RequiredLevel?.Value == AttributeRequiredLevel.SystemRequired;
        List<string>? optionValues = null;
        if (attr is EnumAttributeMetadata e && e.OptionSet is not null)
        {
            optionValues = e.OptionSet.Options.Select(o => o.Label?.UserLocalizedLabel?.Label ?? o.Value?.ToString() ?? string.Empty).Where(v => !string.IsNullOrEmpty(v)).ToList();
        }

        var logicalName = attr.LogicalName ?? string.Empty;
        var dataType = attr.AttributeTypeName?.Value ?? attr.AttributeType?.ToString() ?? "unknown";
        var (isSystemField, systemFieldType) = _systemFieldClassifier.ClassifyField(logicalName, dataType);

        return new ColumnMetadata(
            Name: logicalName,
            DataType: dataType,
            IsNullable: !required,
            Length: maxLength,
            Precision: (attr as DecimalAttributeMetadata)?.Precision ?? (attr as MoneyAttributeMetadata)?.Precision ?? null,
            Scale: (attr as DecimalAttributeMetadata)?.Precision ?? null,
            IsIdentity: false,
            IsComputed: false,
            DefaultDefinition: null,
            IsPrimaryId: entity.PrimaryIdAttribute == attr.LogicalName,
            IsPrimaryName: entity.PrimaryNameAttribute == attr.LogicalName,
            IsRequired: required,
            OptionSetValues: optionValues,
            IsSystemField: isSystemField,
            SystemFieldType: systemFieldType
        );
    }
}
