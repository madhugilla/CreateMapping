# AI Mapping Enhancement Guide

This document describes the enhanced AI mapping functionality with reasoning models and system field prioritization.

## Key Features

### 1. Reasoning-Based AI Prompts
The AI mapper now uses sophisticated prompts that guide the model through systematic reasoning:

- **Source Analysis**: Examines SQL columns for business purpose, constraints, and patterns
- **Target Classification**: Identifies custom vs system Dataverse fields
- **Mapping Strategy**: Prioritizes custom fields, applies system field patterns
- **Confidence Scoring**: Uses structured 5-tier confidence system (0.9-0.5+)

### 2. System Field Detection
Automatically identifies and categorizes common Dataverse system fields:

**Audit Fields**: `createdon`, `createdby`, `modifiedon`, `modifiedby`
**Ownership**: `ownerid`, `owningbusinessunit`, `owningteam`, `owninguser`  
**State Management**: `statecode`, `statuscode`
**System Tracking**: `versionnumber`, `importsequencenumber`
**Time Zone**: `timezoneruleversionnumber`, `utcconversiontimezonecode`

### 3. Priority-Based Mapping
- **Custom Fields**: Priority 1 - Mapped first with 5% confidence boost
- **System Fields**: Priority 2-10 - Mapped by importance with 5% confidence reduction
- **Duplicate Prevention**: No duplicate target mappings allowed

### 4. Reasoning Model Support
Supports both standard and reasoning models:

**Standard Models** (GPT-4, GPT-3.5):
```json
{
  "Deployment": "gpt-4",
  "Temperature": 0.2
}
```

**Reasoning Models** (o1-preview, o1-mini):
```json
{
  "Deployment": "o1-preview"
}
```

## Configuration

### Environment Variables
For security, set sensitive values as environment variables:
```bash
export Ai__ApiKey="your-api-key"
export Dataverse__Password="your-password"
```

### appsettings.json
```json
{
  "Ai": {
    "Enabled": true,
    "Endpoint": "https://your-endpoint.openai.azure.com/",
    "Deployment": "o1-preview",
    "RetryCount": 2,
    "LogRaw": false
  }
}
```

## Usage Examples

### Basic Mapping
```bash
dotnet run SqlTable DataverseTable --output ./mappings
```

### With Script Input
```bash
dotnet run SqlTable DataverseTable --sql-script ./create_table.sql
```

## AI Prompt Structure

The reasoning prompt guides the AI through:

1. **Analyze Source Columns** - Identify business purpose and constraints
2. **Analyze Target Columns** - Classify custom vs system fields  
3. **Apply Mapping Strategy** - Prioritize custom fields, use system patterns
4. **Score Confidence** - Apply structured scoring (0.9-0.5+)
5. **Generate Output** - Return JSON with rationale

## System Field Patterns

The AI is trained to recognize these mapping patterns:

```
SQL Pattern          → Dataverse Field    → Confidence
created_date         → createdon          → 0.75-0.85
created_by          → createdby          → 0.75-0.85  
modified_date       → modifiedon         → 0.75-0.85
updated_by          → modifiedby         → 0.75-0.85
owner_id            → ownerid            → 0.70-0.80
status              → statecode          → 0.65-0.75
detailed_status     → statuscode         → 0.65-0.75
```

## Output Enhancement

Mapping results now include:
- **Match Type**: `AI-Custom` or `AI-System-{SystemFieldType}`
- **Priority Information**: Visual indication of mapping order
- **Enhanced Rationale**: Detailed reasoning for each mapping
- **System Field Classification**: Clear identification of field types

## Testing

Run the comprehensive test suite:
```bash
dotnet test
```

Key test categories:
- **SystemFieldClassifierTests**: Validates field detection and prioritization
- **PrioritizedMappingTests**: Verifies custom-first ordering
- **OrchestratorTests**: Tests enhanced orchestration logic
- **AzureOpenAiMapperParsingTests**: Validates AI response parsing