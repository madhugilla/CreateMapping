# CreateMapping CLI

Generates a proposed mapping between a SQL Server table and a Microsoft Dataverse table using Azure OpenAI for end‑to‑end column pairing. The tool is now AI‑only (deterministic rule engine removed). See `docs/MappingTool_TechSpec.md` for original heuristic design (kept for potential future hybrid mode).

## Prerequisites

- .NET 9 SDK
- Network access to SQL Server & Dataverse environment.

## Configure

Set environment variables (recommended) or `appsettings.json` (avoid committing secrets):

```bash
set SQL__ConnectionString="Server=.;Database=YourDb;Trusted_Connection=True;Encrypt=True;"
set Dataverse__Url="https://yourorg.crm.dynamics.com"
set Dataverse__Username="someone@tenant.onmicrosoft.com"
set Dataverse__Password="YourStrongPassword"

# Azure OpenAI (required for mappings)
set Ai__Enabled=true
set Ai__Endpoint="https://your-openai-resource.openai.azure.com/"
set Ai__ApiKey="YOUR_API_KEY"
set Ai__Deployment="<your-deployment-name>"  # e.g. gpt-4o or gpt-4o-mini
set Ai__Temperature=0.2
set Ai__RetryCount=2          # retries for transient errors (429/5xx)
set Ai__LogRaw=false          # set true to log raw JSON responses (debug)
```

(`:` replaced by `__` in environment variable names for hierarchical keys.)

## Usage

```bash
dotnet run --project CreateMapping -- <sql-table> <dataverse-table> --output outputDir [--sql-script path]
```

Examples:

```bash
dotnet run --project CreateMapping -- dbo.Customer account
dotnet run --project CreateMapping -- OrderHeader salesorder --output mappings
dotnet run --project CreateMapping -- dbo.Customer account --sql-script sample_table.sql --output mappings
```

Outputs: CSV + JSON mapping files in the output directory. If the model returns no suggestions the CSV will be effectively empty aside from headers and unresolved columns will be listed in JSON.

## Current Limitations

- Pure AI approach: no deterministic fallback if the LLM yields poor or empty output.
- Username/password authentication for Dataverse (should migrate to OAuth client secret or certificate for production).
- Limited transformation logic; AI may propose transformations but they are not executed—only documented.
- Offline Dataverse mode (no URL) produces empty target metadata, thus AI returns no mappings.

## Exit Codes

- 0 success (even if some columns unresolved)
- 1 unexpected failure

## Potential Future Enhancements

- Optional deterministic baseline (re‑introduce previous engine) for validation & fallback.
- Semantic domain & transformation library execution (apply suggested transformations).
- OAuth / Managed Identity auth for Dataverse.
- Tests & CI pipeline.
- Retry / rate limit handling and prompt optimization.
