# SQL to Dataverse Mapping Tool - Technical Specification

## 1. Purpose & Scope

A command-line (extensible to service) .NET 9 utility that:

1. Accepts two inputs: (a) SQL Server table name, (b) Dataverse table (entity / logical name).  
2. Fetches column (attribute) metadata for both sources.  
3. Applies a multi-stage (rules + AI-assisted) analysis to propose a mapping of SQL columns -> Dataverse columns.  
4. Outputs a CSV (Excel friendly) mapping document plus a JSON metadata file for downstream automation.  
5. (Optional future) Persist mappings and feedback loop to improve AI suggestions.

Out of scope (initial version): Data migration execution, delta detection, transformation execution, error retry orchestration.

## 2. High-Level Architecture

```text
+----------------------+        +---------------------+
|  CLI / Host (dotnet) |        |  Config & Secrets   |
+----------+-----------+        +----------+----------+
           |                                |
           v                                v
+----------------------+        +---------------------+
|   Metadata Service   |<------>|  Credential Manager |
+----------+-----------+        +----------+----------+
           |                                
           v                                
   +-------+--------+                 +-------------------+
   | SQL Introspector|                | Dataverse Adapter |
   +-------+--------+                 +---------+---------+
           |                                    |
           v                                    v
   [sys.columns, etc.]                [EntityDefinitions, Attributes]

           +---------------------------------------------+
           | Mapping Engine (Rules + AI + Scoring)       |
           +-----------+-----------------+---------------+
                       |                 |
                       v                 v
               CSV Writer           JSON Writer
```

## 3. Key Components

| Component | Responsibility | Tech | Notes |
|-----------|----------------|------|-------|
| CLI Host | Parse args, invoke orchestration | System.CommandLine | Minimal UX, later add interactive mode |
| Config Provider | Load connection strings, Dataverse info | appsettings.json + env overrides | Support Azure Key Vault future |
| SQL Introspector | Retrieve column metadata | System.Data.SqlClient / Microsoft.Data.SqlClient | Use INFORMATION_SCHEMA & sys catalog fallback |
| Dataverse Adapter | Retrieve attribute metadata | Microsoft.PowerPlatform.Dataverse.Client | Uses Dataverse Web API under hood |
| Mapping Engine | Generate candidate mappings | Internal library + optional AI | Pluggable steps |
| Rules Module | Deterministic heuristics | C# | Name, data type, length, semantic patterns |
| AI Module | LLM based similarity & suggestions | Azure OpenAI / OpenAI (abstract) | Interface `IAiMapper` so optional |
| Scoring & Resolver | Aggregate scores, pick best | C# | Weighted linear combination |
| Exporters | Produce CSV + JSON | CsvHelper / manual | Ensure Excel compatible (UTF-8 BOM) |
| Logging | Structured logs | Microsoft.Extensions.Logging | Optional Serilog sink |

## 4. Inputs & Outputs

### Inputs

- SQL Table Name (string) (required)
- Dataverse Table Logical Name (string) (required)
- Optional: Output directory (default `./output`)
- Optional: Config path, AI enable flag, AI model name, temperature, weight overrides

### Outputs

1. CSV Mapping (filename pattern: `mapping_{sqlTable}_{dataverseTable}_{timestamp}.csv`)
2. JSON Mapping metadata (`mapping_{...}.json`) containing:
   - sourceColumns (name, type, nullable, length, sampleValues?)
   - targetColumns (logicalName, displayName, type, maxLength, required, optionSet values)
   - mappings: array objects { source, target, confidence, rationale, transformations[] }
   - unresolvedSourceColumns
   - unusedTargetColumns
   - generationMeta (versions, weights, AI model, runId, timestamp)

### CSV Schema (Columns)

| SourceColumn | SourceType | SourceNullable | SourceLength | TargetColumn | TargetType | TargetRequired | MatchType (Rule|AI|Manual) | Confidence (0-1) | Transformation | Rationale |

## 5. Metadata Retrieval Detail

### SQL

Primary query (INFORMATION_SCHEMA):

```sql
SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME = @TableName AND (c.TABLE_SCHEMA = @Schema OR @Schema IS NULL)
ORDER BY c.ORDINAL_POSITION;
```

Fallback (sys.*) for richer info (computed, identity, default):

```sql
SELECT col.name AS ColumnName, t.name AS DataType, col.is_nullable, col.max_length, col.precision, col.scale,
       col.is_identity, col.is_computed, dc.definition AS DefaultDefinition
FROM sys.columns col
JOIN sys.types t ON col.user_type_id = t.user_type_id
LEFT JOIN sys.default_constraints dc ON col.default_object_id = dc.object_id
WHERE col.object_id = OBJECT_ID(@FullTableName)
ORDER BY col.column_id;
```

### Dataverse

Use `IOrganizationService` or ServiceClient metadata requests:
- RetrieveEntityRequest (EntityFilters.Attributes)
Fields captured: LogicalName, DisplayName, AttributeType, RequiredLevel, MaxLength, Precision, IsPrimaryId, IsPrimaryName, OptionSet metadata.

Caching: Cache metadata locally (in-memory) keyed by table to reduce API calls in same run.

## 6. Mapping Algorithm
Stages:
1. Preprocessing
   - Normalize names (lowercase, strip underscores, camel/pascal to tokens)
   - Tokenize and stem
2. Deterministic Rule Matching (produce candidate pairs with initial scores)
   - Exact name match (weight 0.50)
   - Case-insensitive match (0.45)
   - Normalized token set equality (0.40)
   - Prefix/suffix handling ("_id" vs "id", etc.)
   - Data type compatibility (penalty if mismatch)
   - Length tolerance (varchar(50) -> nvarchar(60) OK)
3. Semantic Heuristics
   - Recognize common semantic domains: email, phone, date, address, amount, count, status, code, description, name, created/modified audit fields.
   - Apply domain-specific boosts (e.g., email->email column +0.15)
4. AI Similarity (optional)
   - For each unmapped source column, send batched prompt containing source column descriptor and list of candidate target attribute descriptors; get probability-like similarity scores.
   - Cap AI influence weight (e.g., 0.30) to avoid overshadowing deterministic evidence.
5. Aggregation & Conflict Resolution
   - Score = Σ(weight_i * signal_i) - Σ(penalties)
   - If multiple sources map to same target: keep highest score above threshold T_high; others unresolved or flagged multi-map.
6. Thresholding
   - T_high (>=0.70) auto-accept
   - T_review (>=0.40 and <0.70) mark as NeedsReview (Confidence column reflects)
   - <0.40 unresolved
7. Transformation Suggestions
   - Simple casts (string->nvarchar), trimming, uppercase, parse date, split fullName, map option sets (via value dictionary)
   - AI can suggest transformation text snippet (plain English)

Weights configurable in config JSON:

```json
"Weights": {
  "ExactName": 0.50,
  "CaseInsensitive": 0.45,
  "Normalized": 0.40,
  "SemanticDomain": 0.15,
  "TypeCompatibility": 0.20,
  "AiSimilarity": 0.30,
  "LengthPenaltyPer10Pct": -0.02
}
```

## 7. AI Module Interface

```csharp
public interface IAiMapper {
    Task<IReadOnlyList<AiMappingSuggestion>> SuggestMappingsAsync(
        TableMetadata source,
        TableMetadata target,
        IReadOnlyCollection<SourceColumn> unresolvedSource,
        CancellationToken ct = default);
}

public record AiMappingSuggestion(
    string SourceColumn,
    string TargetColumn,
    double Confidence,
    string? Transformation,
    string? Rationale);
```
Abstraction allows NoOp implementation if AI disabled.

Prompt Strategy (batched): Provide JSON array of unresolved source columns with name+type+length+sample tokens, plus target column catalog; ask model to respond with list of {source,target,confidence,transformation,rationale}. Guard rails: system message with constraints, ask for valid JSON only.

## 8. Configuration

`appsettings.json` example:

```json
{
  "Sql": { "ConnectionString": "Server=...;Database=...;Trusted_Connection=True;Encrypt=True;" },
  "Dataverse": { "Url": "https://org.crm.dynamics.com", "ClientId": "...", "TenantId": "...", "ClientSecret": "..." },
  "Ai": { "Enabled": true, "Provider": "AzureOpenAI", "Endpoint": "https://...", "Model": "gpt-4o", "ApiKey": "...", "Temperature": 0.2 },
  "Weights": { /* as above */ },
  "Output": { "Directory": "output" }
}
```
Secrets overridden via environment variables or user secrets.

## 9. Security & Compliance

- Use least privilege SQL user with metadata read (VIEW DEFINITION).
- Store secrets outside source (env vars, user secrets, Azure Key Vault future).
- TLS enforced for Dataverse and SQL (Encrypt=True, TrustServerCertificate=False).
- Avoid logging full connection strings or secrets.
- AI data minimization: send only column names, types, and generalized tokens (no PII sample values by default). Add flag `--includeSamples` if user explicitly allows.
- Optional classification heuristic to redact potential PII tokens before AI call (email addresses -> placeholder).

## 10. Performance Considerations

- Metadata calls are small; main cost is AI round-trips. Batch unresolved columns (e.g., <=20 per prompt) to keep token count reasonable.
- Parallelization: Deterministic scoring per source column can run in parallel (Parallel.ForEach) but preserve order in final output.
- Caching: In-memory dictionary for Dataverse entity metadata. Optionally persist to disk cache with hash of entity logical name + timestamp.

## 11. Error Handling Strategy

| Scenario | Handling |
|----------|----------|
| SQL table not found | Return exit code 2, friendly message |
| Dataverse entity not found | Exit code 3 |
| Auth failure | Exit code 10 |
| AI provider unreachable | Log warning, proceed with rules only |
| CSV write failure | Exit code 20 |
| Partial mapping | Still output files; unresolved section populated |

Retry transient errors (SQL, HTTP 429/5xx) with exponential backoff (Polly).

## 12. Logging & Telemetry

- Log levels: Information (progress), Warning (skipped), Error (fatal), Debug (score breakdown when --verbose).
- Structured fields: sourceColumn, targetColumn, stage, score, confidence.
- Optionally produce a score audit JSON file when `--audit` flag used.

## 13. Extensibility Points

- Add new semantic domain recognizers via `ISemanticEnricher` interface.
- Plug different AI providers by implementing `IAiMapper`.
- Add new exporters (e.g., Excel .xlsx) by implementing `IMappingExporter`.

## 14. CLI Usage Examples

```text
CreateMapping --sql-table Customer --dataverse-table account
CreateMapping --sql-table dbo.Customer --dataverse-table contact --output .\mappings --ai-disabled
CreateMapping --sql-table OrderHeader --dataverse-table salesorder --weights weights.json --verbose --audit
```

## 15. Acceptance Criteria

- Given valid table names, tool produces both CSV & JSON with at least all exact name matches and semantic matches above threshold.
- CSV opens in Excel with correct column headers and UTF-8 encoding.
- AI disabled still yields deterministic mappings.
- Unresolved columns listed for any source columns below threshold.
- All secrets configurable via environment variables (no hard-coded secrets committed).
- Exit codes documented and returned as specified.

## 16. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| AI cost overruns | $$ | Batch + limit usage; config toggle |
| Poor AI suggestions | Low accuracy | Weight cap + threshold review |
| Datatype mismatches missed | Data migration failures later | Strict compatibility matrix + penalties |
| Schema drift | Stale mappings | Include run timestamp + re-run before migration |
| PII leakage to AI | Compliance | Redaction + opt-in sample flag |

## 17. Future Enhancements

- Feedback loop storing accepted/rejected mappings to fine-tune heuristic weights.
- Direct export to Azure Data Factory or SSIS mapping schema.
- Web UI for manual review and approval workflow.
- Integration tests against mock Dataverse and local SQL container.
- Transformation DSL to formalize transformation steps.

## 18. Data Type Compatibility Matrix (Excerpt)

| SQL | Dataverse Primary Compatible | Notes |
|-----|------------------------------|-------|
| int | Integer, BigInt (if upcast) | Upcast allowed int->BigInt |
| bigint | BigInt | |
| uniqueidentifier | UniqueIdentifier | |
| varchar/nvarchar | SingleLine.Text / Memo | Use length to decide |
| decimal/numeric | Decimal, Money | Precision/scale must fit |
| bit | Boolean | |
| datetime/datetime2 | DateTime | |
| float | Double | |

Full matrix to be encoded in code with penalties for narrowing conversions.

## 19. Directory & File Layout (Proposed)

```text
CreateMapping/
  Program.cs
  /Services
    MetadataService.cs
    Sql/SqlIntrospector.cs
    Dataverse/DataverseMetadataProvider.cs
  /Mapping
    MappingEngine.cs
    Rules/*
    Ai/* (optional)
  /Models
    TableMetadata.cs
    ColumnMetadata.cs
    MappingResult.cs
  /Export
    CsvMappingExporter.cs
    JsonMappingExporter.cs
  appsettings.json (local dev only - exclude secrets)
  README.md
  docs/MappingTool_TechSpec.md
```

## 20. Open Questions

- Need to confirm authentication method for Dataverse (client secret vs certificate). (Assume client secret first version.)
- Should we allow multi-target mapping (1 source -> 2 target columns)? (Initial: no, mark as NeedsReview.)
- Do we include sample values from SQL for better AI mapping? (Initial: disabled by default.)

---
Version: 0.1.0-draft  
Owner: TBD  
Last Updated: (auto-generated timestamp at runtime)
