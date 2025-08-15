# CreateMapping CLI

Generates a proposed mapping between a SQL table schema (from a local CREATE TABLE script) and a Microsoft Dataverse table (from an exported metadata CSV) using Azure OpenAI for column pairing. The tool is now 100% offline for schema/metadata ingestion (no direct SQL or Dataverse connectivity). See `docs/MappingTool_TechSpec.md` for the earlier hybrid design retained only for reference.

## Prerequisites

- .NET 9 SDK
- Azure OpenAI (or compatible) access (only outbound HTTPS to your OpenAI endpoint is needed)

## Configure (Minimal)

Only one settings group is used: `Ai` (for Azure OpenAI). There are no live SQL / Dataverse credentials anymore.

Required when AI mapping is desired:

- `Ai:Endpoint`  Azure OpenAI resource endpoint
- `Ai:ApiKey`    API key (set via user secrets / env var)
- `Ai:Deployment` Model deployment (e.g. `gpt-4o-mini`)

Optional:

- `Ai:Enabled` (default true; set false to force no‑op)
- `Ai:Temperature` (default 0.2)
- `Ai:RetryCount` (default 2)
- `Ai:LogRaw` (debug raw JSON)

If endpoint or key is missing a no‑op mapper is used (outputs headers + unresolved list).

Example (PowerShell syntax):

```bash
 # Azure OpenAI (required for mappings)
set Ai__Enabled=true
set Ai__Endpoint="https://your-openai-resource.openai.azure.com/"
set Ai__ApiKey="YOUR_API_KEY"
set Ai__Deployment="<your-deployment-name>"  # e.g. gpt-4o or gpt-4o-mini
set Ai__Temperature=0.2
set Ai__RetryCount=2          # retries for transient errors (429/5xx)
set Ai__LogRaw=false          # set true to log raw JSON responses (debug)

# Reasoning models (o1, etc.) not yet wired; ignore any older Ai_Reasoning section
```

(`:` replaced by `__` in environment variable names for hierarchical keys.)

## Usage

Syntax (arguments first, then options):

```bash
dotnet run --project CreateMapping -- <sql-table> <dataverse-table> --sql-script <create-table.sql> --dataverse-file <metadata.csv> [--output dir]
```

Arguments:

| Arg | Description |
|-----|-------------|
| `<sql-table>` | Logical SQL table name (schema prefix allowed; used for naming & AI context). |
| `<dataverse-table>` | Dataverse logical table name present in the CSV (or arbitrary if single-table CSV). |

Options:

| Option | Required | Description |
|--------|----------|-------------|
| `--sql-script` | Yes | Path to a file with exactly one `CREATE TABLE` definition. |
| `--dataverse-file` | Yes* | Path to Dataverse metadata CSV (*optional if `CM_DATAVERSE_FILE` env var or `Dataverse:File` configured). |
| `--output` | No | Output directory (default `output`). |

### Example

Generate mapping using offline artifacts:

```bash
dotnet run -- dbo.Customer account --sql-script sample_table.sql --dataverse-file docs/m360_case_csv.csv --output mappings
```

You can also set an environment variable instead of the option:

```powershell
$Env:CM_DATAVERSE_FILE = "docs/m360_case_csv.csv"
dotnet run -- dbo.Customer account --sql-script sample_table.sql
```

Precedence order for offline file path:

1. `--dataverse-file` option
2. `CM_DATAVERSE_FILE` environment variable
3. `Dataverse:File` in configuration

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

### Dataverse Offline CSV Format

The offline provider attempts to be flexible with headers (case-insensitive). Recognized columns:

| Purpose | Accepted Headers |
|---------|------------------|
| Table logical name (optional if single table) | `Table`, `EntityLogicalName`, `Entity` |
| Column logical/schema name | `LogicalName`, `SchemaName`, `Name` |
| Data type | `AttributeType`, `Type`, `DataType`, `AttributeTypeName` |
| Display name (optional) | `DisplayName`, `Label` |
| Max length | `MaxLength`, `Length` |
| Required flag | `Required`, `IsRequired` |
| Option set values | `OptionSetValues`, `Options`, `PicklistValues` |
| Primary Id flag | `PrimaryId`, `IsPrimaryId` |
| Primary Name flag | `PrimaryName`, `IsPrimaryName` |

Boolean / required / primary flags accept: `true/false`, `yes/no`, `y/n`, or `1/0`.

Option set values can be separated by `;` or `|`.

If no table column is present the entire file is assumed to describe a single Dataverse table (whatever logical name you pass on the CLI).

System field inference: Common system columns (e.g. `createdon`, `createdby`, `modifiedon`, `ownerid`, `statecode`, `statuscode`, etc.) are auto-classified so weighting rules still apply.

See `docs/m360_case_csv.csv` and `docs/dataverse_sample.csv` for examples.

### Environment Variables (PowerShell)

PowerShell uses `setx` for persistent or `$Env:` for the current session. Examples below use temporary session variables. Sensitive values (passwords, API keys) should preferably be stored in **user secrets** for local dev and in secure variable stores (Key Vault, pipeline secrets) for CI/CD. The environment variable examples show placeholders only:

```powershell
$Env:Ai__Enabled = "true"
$Env:Ai__Endpoint = "https://your-openai-resource.openai.azure.com/"
# (Prefer: dotnet user-secrets set Ai:ApiKey "<KEY>")
# $Env:Ai__ApiKey = "<KEY>"  # fallback if you must
$Env:Ai__Deployment = "gpt-4o-mini"
$Env:Ai__Temperature = "0.2"
$Env:Ai__RetryCount = "2"
$Env:Ai__LogRaw = "false"
```

(In Bash, use `export` or the earlier `set` examples. Remember the double underscore `__` represents the `:` hierarchy separator.)

Security note: DO NOT commit real API keys. Use `dotnet user-secrets` locally and secure variables / Key Vault in CI/CD. The sample `appsettings.sample.json` shows placeholders only.

### User Secrets Quick Start (local only)

Initialize (first time):

```powershell
dotnet user-secrets init
dotnet user-secrets set Ai:ApiKey "<KEY>"
dotnet user-secrets list
```

Remove / rotate:

```powershell
dotnet user-secrets remove Ai:ApiKey
```

Never commit real keys to `appsettings.json`. In CI/CD use secure pipeline variables or Azure Key Vault (future enhancement).

### Azure OpenAI Deployment Notes

Ensure your deployment model supports sufficient context tokens for both table schemas. If schemas are large, consider pruning non-essential columns or future enhancement: streaming / chunking (not yet implemented).

### Authentication

No direct Dataverse or SQL authentication is performed; all metadata is supplied via files.

### Non‑AI Mode (Fallback Stub)

If AI configuration is missing, a no‑op mapper is injected and you'll get empty suggestions—useful only to validate pipeline wiring. Provide endpoint + key to obtain real mappings.

### Troubleshooting

- Empty mapping: Verify `Ai__Endpoint`, `Ai__ApiKey`, and `Ai__Deployment` are set and deployment name matches Azure OpenAI resource.
- 401/403 in logs: Incorrect key or resource endpoint region mismatch.
- Many system fields accepted: Adjust code (future enhancement) or manually filter; current heuristic already down-weights system fields.
- Script parse failures: Confirm the file contains exactly one `CREATE TABLE` statement and uses standard SQL types.

### Example End-to-End (PowerShell)

```powershell
$Env:Ai__Endpoint = "https://contoso-openai.openai.azure.com/"
$Env:Ai__ApiKey = "<KEY>"
$Env:Ai__Deployment = "gpt-4o-mini"
dotnet run --project CreateMapping -- dbo.Customer account --sql-script sample_table.sql --dataverse-file docs/m360_case_csv.csv --output mappings
```

## Current Limitations

- Pure AI approach: no deterministic fallback if the LLM yields poor or empty output.
- Limited transformation logic; AI may propose transformations but they are not executed—only documented.
- Requires accurate CREATE TABLE script (one table) & a matching Dataverse metadata CSV.

## Exit Codes

- 0 success (even if some columns unresolved)
- 1 unexpected failure

## Potential Future Enhancements

- Optional deterministic baseline (re‑introduce previous engine) for validation & fallback.
- Semantic domain & transformation library execution (apply suggested transformations).
- Optional deterministic baseline scoring.
- Rich transformation execution.
- Chunking for very wide tables.
