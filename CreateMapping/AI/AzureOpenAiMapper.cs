using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CreateMapping.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CreateMapping.AI;

public sealed class AzureOpenAiMapper : IAiMapper
{
    private readonly HttpClient _http;
    private readonly ILogger<AzureOpenAiMapper> _logger;
    private readonly string _deployment;
    private readonly double _temperature;
    private readonly double _maxWeight;
    private readonly int _retryCount;
    private readonly bool _logRaw;
    private readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(400);

    public AzureOpenAiMapper(IConfiguration config, ILogger<AzureOpenAiMapper> logger, IHttpClientFactory factory)
    {
        _logger = logger;
        _http = factory.CreateClient("azure-openai");
        var section = config.GetSection("Ai");
        var endpoint = section["Endpoint"] ?? throw new InvalidOperationException("Ai:Endpoint missing");
        var apiKey = section["ApiKey"] ?? throw new InvalidOperationException("Ai:ApiKey missing");
        _deployment = section["Deployment"] ?? section["Model"] ?? throw new InvalidOperationException("Ai:Deployment (or Model) missing");
        _temperature = double.TryParse(section["Temperature"], out var t) ? t : 0.2;
        _maxWeight = double.TryParse(section["AiSimilarityWeight"], out var mw) ? mw : 0.30;
    _retryCount = int.TryParse(section["RetryCount"], out var rc) ? Math.Clamp(rc, 0, 5) : 2;
    _logRaw = bool.TryParse(section["LogRaw"], out var lr) && lr;
        if (!endpoint.EndsWith("/")) endpoint += "/";
        _http.BaseAddress = new Uri(endpoint);
        _http.DefaultRequestHeaders.Add("api-key", apiKey);
    }

    public async Task<IReadOnlyList<AiMappingSuggestion>> SuggestMappingsAsync(TableMetadata source, TableMetadata target, IReadOnlyCollection<string> requestedSourceColumns, CancellationToken ct = default)
    {
        if (target.Columns.Count == 0)
            return Array.Empty<AiMappingSuggestion>();

        // We now always provide ALL (requested) source columns so the model can propose the full mapping.
        var requestedSet = new HashSet<string>(requestedSourceColumns, StringComparer.OrdinalIgnoreCase);
        var sourceCols = source.Columns
            .Where(c => requestedSet.Count == 0 || requestedSet.Contains(c.Name))
            .Select(c => new {
                c.Name,
                c.DataType,
                c.Length,
                c.Precision,
                c.Scale,
                c.IsIdentity,
                c.IsPrimaryId,
                c.IsPrimaryName,
                c.IsRequired
            });
        
        var customTargetCols = target.Columns
            .Where(c => !c.IsSystemField)
            .Select(c => new {
                c.Name,
                c.DataType,
                c.Length,
                c.IsRequired,
                c.IsPrimaryId,
                c.IsPrimaryName,
                IsSystemField = false,
                SystemFieldType = "None"
            });
            
        var systemTargetCols = target.Columns
            .Where(c => c.IsSystemField)
            .Select(c => new {
                c.Name,
                c.DataType,
                c.Length,
                c.IsRequired,
                c.IsPrimaryId,
                c.IsPrimaryName,
                IsSystemField = true,
                SystemFieldType = c.SystemFieldType.ToString()
            });

        var system = BuildReasoningSystemPrompt();
        var userObj = new {
            sourceTable = source.Name,
            targetTable = target.Name,
            sourceColumns = sourceCols,
            customTargetColumns = customTargetCols,
            systemTargetColumns = systemTargetCols,
            mappingInstructions = new {
                priorityOrder = "Map custom fields first (higher priority), then system fields (lower priority)",
                customFieldsPriority = "Custom business fields should be mapped with higher confidence",
                systemFieldsGuidance = new {
                    createdOn = "Map from SQL audit columns like created_date, create_time, date_created",
                    createdBy = "Map from SQL audit columns like created_by, creator_id, created_user",
                    modifiedOn = "Map from SQL audit columns like modified_date, update_time, last_modified",
                    modifiedBy = "Map from SQL audit columns like modified_by, updater_id, last_user",
                    owner = "Map from SQL user/owner columns like owner_id, assigned_to, user_id",
                    state = "Map from SQL status/state columns like status, state, is_active",
                    status = "Map from SQL detailed status columns like status_code, detailed_status"
                }
            }
        };
        var user = JsonSerializer.Serialize(userObj);

        // Check if this is a reasoning model (o1-preview, o1-mini)
        var isReasoningModel = _deployment.Contains("o1", StringComparison.OrdinalIgnoreCase);
        
        var payload = isReasoningModel 
            ? BuildReasoningModelPayload(system, user)
            : BuildStandardModelPayload(system, user);

        var json = JsonSerializer.Serialize(payload);
        HttpResponseMessage? resp = null;
        for (var attempt = 0; attempt <= _retryCount; attempt++)
        {
            try
            {
                var apiVersion = isReasoningModel ? "2024-09-01-preview" : "2024-05-01-preview";
                using var req = new HttpRequestMessage(HttpMethod.Post, $"openai/deployments/{_deployment}/chat/completions?api-version={apiVersion}")
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) break;
                var status = (int)resp.StatusCode;
                if (status is 429 or 500 or 502 or 503 or 504)
                {
                    if (attempt < _retryCount)
                    {
                        var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                        _logger.LogWarning("AI request transient failure {Status}. Retrying in {Delay} (attempt {Attempt}/{Max})", status, delay, attempt + 1, _retryCount + 1);
                        await Task.Delay(delay, ct);
                        continue;
                    }
                }
                // Non-retriable or exhausted
                _logger.LogWarning("Azure OpenAI request failed: {Status} {Reason}", status, resp.ReasonPhrase);
                return Array.Empty<AiMappingSuggestion>();
            }
            catch (Exception ex) when (attempt < _retryCount)
            {
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                _logger.LogWarning(ex, "AI request exception, retrying in {Delay} (attempt {Attempt}/{Max})", delay, attempt + 1, _retryCount + 1);
                await Task.Delay(delay, ct);
            }
        }
        if (resp == null || !resp.IsSuccessStatusCode)
        {
            return Array.Empty<AiMappingSuggestion>();
        }
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        if (_logRaw)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using var copyReader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var raw = await copyReader.ReadToEndAsync(ct);
            stream.Seek(0, SeekOrigin.Begin);
            _logger.LogInformation("Raw AI response: {Raw}", raw);
        }
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        // Navigate to first choice message content
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) return Array.Empty<AiMappingSuggestion>();

        // Attempt to extract JSON (in case model returns surrounding text)
        var jsonStart = content.IndexOf('[');
        var jsonEnd = content.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < jsonStart)
            return Array.Empty<AiMappingSuggestion>();
        var jsonSlice = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        try
        {
            var parsed = JsonSerializer.Deserialize<List<AiMappingSuggestionInternal>>(jsonSlice, new JsonSerializerOptions{PropertyNameCaseInsensitive = true}) ?? new();
            return parsed.Where(p => !string.IsNullOrWhiteSpace(p.Source) && !string.IsNullOrWhiteSpace(p.Target))
                .Select(p => new AiMappingSuggestion(p.Source, p.Target, Clamp(p.Confidence), p.Transformation, p.Rationale))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI suggestions JSON");
            return Array.Empty<AiMappingSuggestion>();
        }
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static string BuildReasoningSystemPrompt()
    {
        return """
            You are an expert data mapping specialist tasked with mapping SQL table columns to Dataverse entity attributes.

            Follow this systematic reasoning approach:

            1. ANALYZE SOURCE COLUMNS:
               - Examine each SQL column's name, data type, constraints
               - Identify business purpose (ID, name, description, date, amount, etc.)
               - Note primary keys, foreign keys, audit fields

            2. ANALYZE TARGET COLUMNS:
               - Custom fields: Business-specific attributes (higher mapping priority)
               - System fields: Standard Dataverse fields (lower mapping priority)
               - Note required fields, primary keys, data types

            3. MAPPING STRATEGY:
               - Prioritize custom field mappings (confidence 0.7-0.95)
               - Map system fields with appropriate patterns (confidence 0.6-0.85)
               - Exact name matches get highest confidence
               - Semantic/business logic matches get medium-high confidence
               - Data type compatibility is crucial

            4. SYSTEM FIELD PATTERNS:
               - created*/date_created → createdon
               - created_by/creator → createdby  
               - modified*/last_modified → modifiedon
               - modified_by/updater → modifiedby
               - owner*/assigned_to → ownerid
               - status/state/active → statecode/statuscode

            5. CONFIDENCE SCORING:
               - 0.9-0.95: Exact name match with compatible type
               - 0.8-0.89: Strong semantic match (e.g., customer_name → name)
               - 0.7-0.79: Good business logic match
               - 0.6-0.69: System field pattern match
               - 0.5-0.59: Reasonable inference
               - Below 0.5: Don't include

            Return ONLY a JSON array with this exact format:
            [{"source":"<sqlColumn>","target":"<dataverseAttribute>","confidence":0.0-1.0,"transformation":"<optional>","rationale":"<reasoning>"}]

            Requirements:
            - No duplicate target mappings
            - Focus on one-to-one mappings
            - Include transformation if data conversion needed
            - Provide clear rationale for each mapping
            - No markdown, no extra text outside JSON
            """;
    }

    private object BuildStandardModelPayload(string system, string user)
    {
        return new {
            messages = new object[] {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = _temperature,
            max_tokens = 1500 // Increased for detailed reasoning
        };
    }

    private object BuildReasoningModelPayload(string system, string user)
    {
        // For reasoning models (o1-preview, o1-mini), combine system and user messages
        var combinedPrompt = $"{system}\n\nUser Request:\n{user}";
        
        return new {
            messages = new object[] {
                new { role = "user", content = combinedPrompt }
            },
            max_completion_tokens = 2000 // Reasoning models use max_completion_tokens
        };
    }

    private sealed record AiMappingSuggestionInternal(string Source, string Target, double Confidence, string? Transformation, string? Rationale);
}
