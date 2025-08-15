# CreateMapping CLI

Generates a proposed mapping between a SQL Server table and a Microsoft Dataverse table using Azure OpenAI for end‑to‑end column pairing. The tool is now AI‑only (deterministic rule engine removed). See `docs/MappingTool_TechSpec.md` for original heuristic design (kept for potential future hybrid mode).

## Prerequisites

- .NET 9 SDK
- Network access to SQL Server & Dataverse environment.

## Configure

Set environment variables (recommended) or `appsettings.json` (avoid committing secrets). The tool currently reads:

- `Ai:*` for standard chat completion model (uses Temperature)
- `Ai_Reasoning:*` placeholder for reasoning models (o1-preview / o1-mini) – not yet wired into the orchestrator; kept for forward compatibility (no Temperature setting used)
- SQL connection via either `ConnectionStrings:Sql` OR `Sql:ConnectionString` (the code calls `GetConnectionString("Sql")` first, then `Sql:ConnectionString`)

Example (PowerShell syntax shown for brevity; adapt to your shell):

```bash
set ConnectionStrings__Sql="Server=.;Database=YourDb;Trusted_Connection=True;Encrypt=True;"  # Preferred key
# or (fallback key)
set Sql__ConnectionString="Server=.;Database=YourDb;Trusted_Connection=True;Encrypt=True;"
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

# (Optional future reasoning model section – not active yet)
set Ai_Reasoning__Enabled=false
set Ai_Reasoning__Deployment="o1-preview"
set Ai_Reasoning__RetryCount=2
```

(`:` replaced by `__` in environment variable names for hierarchical keys.)

## Usage

Basic syntax (arguments first, then options):

```bash
dotnet run --project CreateMapping -- <sql-table> <dataverse-table> [--output dir] [--sql-script path]
```

Arguments:

- `sql-table`  Source SQL table name. You can include schema (e.g. `dbo.Customer`).
- `dataverse-table`  Dataverse logical name (e.g. `account`, `contact`, `salesorder`).

Options:

- `--output` Directory for generated files (default: `output`). Will be created if missing.
- `--sql-script` Path to a `.sql` file containing a single `CREATE TABLE` statement. When supplied, the tool parses the script instead of connecting to SQL Server.
- `--check-dataverse` Only test Dataverse connectivity & list entity columns (no mapping files written).
- `--check-sql` Only test SQL connectivity & list table columns (no mapping files written).

### Examples

Live SQL metadata (must have `SQL__ConnectionString` set):

```bash
dotnet run --project CreateMapping -- dbo.Customer account
```

Custom output directory:

```bash
dotnet run --project CreateMapping -- OrderHeader salesorder --output mappings
```

Offline (no live SQL connection) using a script file:

```bash
dotnet run --project CreateMapping -- dbo.Customer account --sql-script sample_table.sql --output mappings
```

Running from the project folder directly (shorthand):

```bash
dotnet run -- dbo.Customer account
```

Connectivity / metadata checks (skip mapping generation):

```bash
dotnet run -- dbo.Customer account --check-dataverse
dotnet run -- dbo.Customer account --check-sql
dotnet run -- dbo.Customer account --check-sql --check-dataverse
```

Combine with debug wait:

```bash
dotnet run -- dbo.Customer account --check-dataverse --debug-wait
```

### Output

Two files are produced per run (timestamped for uniqueness):

```text
<outputDir>/mapping_<sql-table>_<dataverse-table>_<UTCtimestamp>.csv
<outputDir>/mapping_<sql-table>_<dataverse-table>_<UTCtimestamp>.json
```

File naming replaces any `.` in the SQL table (schema separator) with `_`.

CSV columns typically include: SourceColumn, TargetColumn, Confidence, MatchType, Transformation, Rationale. The JSON contains the full structured `MappingResult` including lists of unresolved source columns and unused target columns.

If Azure OpenAI returns no suggestions, the CSV will still be written (only headers) and all source columns appear under `unresolved` in the JSON.

### Classification & Confidence

The AI suggestions return a confidence (0–1). Internally we scale by an AI similarity weight (currently fixed at default) and apply two thresholds:

- High threshold: >= `WeightsConfig.HighThreshold` (accepted automatically)
- Review threshold: >= `WeightsConfig.ReviewThreshold` and below high (flagged for manual review)
- Below review: discarded (not listed in CSV; source remains unresolved)

Custom (non‑system) Dataverse fields get a slight confidence boost; system fields are slightly penalized to bias toward meaningful business field mappings. Each target field is used at most once.

### Environment Variables (PowerShell)

PowerShell uses `setx` for persistent or `$Env:` for the current session. Examples below use temporary session variables:

```powershell
$Env:SQL__ConnectionString = "Server=.;Database=YourDb;Trusted_Connection=True;Encrypt=True;"
$Env:Dataverse__Url = "https://yourorg.crm.dynamics.com"
$Env:Dataverse__Username = "user@tenant.onmicrosoft.com"
$Env:Dataverse__Password = "P@ssw0rd!"

$Env:Ai__Enabled = "true"
$Env:Ai__Endpoint = "https://your-openai-resource.openai.azure.com/"
$Env:Ai__ApiKey = "<KEY>"
$Env:Ai__Deployment = "gpt-4o-mini"
$Env:Ai__Temperature = "0.2"
$Env:Ai__RetryCount = "2"
$Env:Ai__LogRaw = "false"
```

(In Bash, use `export` or the earlier `set` examples. Remember the double underscore `__` represents the `:` hierarchy separator.)

Security note: DO NOT commit real credentials or API keys into `appsettings.json`. Use user secrets (`dotnet user-secrets`) or environment variables in CI/CD. The sample `appsettings.json` values should be replaced locally and excluded from commits if they contain anything sensitive.

### Azure OpenAI Deployment Notes

Ensure your deployment model supports sufficient context tokens for both table schemas. If schemas are large, consider pruning non-essential columns or future enhancement: streaming / chunking (not yet implemented).

### Non‑AI Mode (Fallback Stub)

If AI configuration is missing, a no‑op mapper is injected and you'll get empty suggestions—useful only to validate pipeline wiring. Provide endpoint + key to obtain real mappings.

### Troubleshooting

- Empty mapping: Verify `Ai__Endpoint`, `Ai__ApiKey`, and `Ai__Deployment` are set and deployment name matches Azure OpenAI resource.
- 401/403 in logs: Incorrect key or resource endpoint region mismatch.
- Many system fields accepted: Adjust code (future enhancement) or manually filter; current heuristic already down-weights system fields.
- Script parse failures: Confirm the file contains exactly one `CREATE TABLE` statement and uses standard SQL types.

### Example End-to-End (PowerShell)

```powershell
$Env:SQL__ConnectionString = "Server=.;Database=Sales;Trusted_Connection=True;Encrypt=True;"
$Env:Dataverse__Url = "https://contoso.crm.dynamics.com"
$Env:Dataverse__Username = "mapper@contoso.onmicrosoft.com"
$Env:Dataverse__Password = "<PW>"
$Env:Ai__Endpoint = "https://contoso-openai.openai.azure.com/"
$Env:Ai__ApiKey = "<KEY>"
$Env:Ai__Deployment = "gpt-4o"
dotnet run --project CreateMapping -- dbo.Customer account --output mappings
```

Result: `mappings/mapping_dbo_Customer_account_YYYYMMDD_HHMMSS.csv` & `.json`.

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
