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

        var authMode = (config["Dataverse:AuthMode"] ?? "Password").Trim(); // Password (ROPC) default
        var user = config["Dataverse:Username"];
        var pwd = config["Dataverse:Password"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
        {
            _logger.LogWarning("Dataverse username/password not configured; returning empty metadata.");
            _client = null!;
            return;
        }

        if (!string.Equals(authMode, "Password", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unsupported Dataverse:AuthMode '{AuthMode}' specified. Falling back to Password (ROPC) flow.", authMode);
        }

        // Configurable OAuth parameters
        var clientId = config["Dataverse:ClientId"] ?? "51f81489-12ee-4a9e-aaae-a2591f45987d"; // Public client id (first-party) if not provided
        var redirect = config["Dataverse:RedirectUri"] ?? "app://58145B91-0C36-4500-8554-080854F2AC97";
        var tenantId = config["Dataverse:TenantId"]; // optional

        // Build connection string for ServiceClient using ROPC. NOTE: ROPC is not recommended for production; prefer interactive or client secret/certificate flows.
        // Authority is optional; ServiceClient infers from URL if not provided.
        var csParts = new List<string>
        {
            "AuthType=OAuth",
            $"Username={user}",
            $"Password={EscapeSemiColons(pwd)}",
            $"Url={url}",
            $"AppId={clientId}",
            $"RedirectUri={redirect}",
            "LoginPrompt=Never"
        };
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            csParts.Add($"Authority=https://login.microsoftonline.com/{tenantId}");
        }
        var cs = string.Join(';', csParts);
        _logger.LogInformation("Initializing Dataverse ServiceClient using OAuth password flow (AuthMode=Password). ClientId={ClientId} AuthorityTenantSet={TenantSet}", clientId, !string.IsNullOrWhiteSpace(tenantId));
        _client = new ServiceClient(cs);
    }

    private static string EscapeSemiColons(string input) => input?.Replace(";", ";;") ?? string.Empty;

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
