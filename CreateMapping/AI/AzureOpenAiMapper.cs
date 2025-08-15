using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using CreateMapping.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CreateMapping.AI;

public sealed class AzureOpenAiMapper : IAiMapper
{
    private readonly OpenAIClient _client;
    private readonly HttpClient? _http; // used when constructed with IHttpClientFactory (test/back-compat/raw mode)
    private readonly ILogger<AzureOpenAiMapper> _logger;
    private readonly string _deployment;
    private readonly string _endpoint;
    private readonly string? _apiVersion;
    private readonly string _apiKeyMasked;
    private readonly string _apiKey; // only for raw http usage; do not log
    private readonly double _temperature;
    private readonly double _maxWeight;
    private readonly int _retryCount;
    private readonly bool _logRaw;
    private readonly bool _logRequest;
    private readonly bool _isReasoning;
    private readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(400);

    public AzureOpenAiMapper(IConfiguration config, ILogger<AzureOpenAiMapper> logger)
    {
        _logger = logger;
        var section = config.GetSection("Ai");
        _endpoint = section["Endpoint"] ?? throw new InvalidOperationException("Ai:Endpoint missing");
        _apiKey = section["ApiKey"] ?? throw new InvalidOperationException("Ai:ApiKey missing");
    _deployment = section["Deployment"] ?? section["Model"] ?? throw new InvalidOperationException("Ai:Deployment (or Model) missing");
    _apiVersion = section["ApiVersion"]; // optional preview override, e.g. 2024-12-01-preview
        _temperature = double.TryParse(section["Temperature"], out var t) ? t : 0.2;
        _maxWeight = double.TryParse(section["AiSimilarityWeight"], out var mw) ? mw : 0.30;
        _retryCount = int.TryParse(section["RetryCount"], out var rc) ? Math.Clamp(rc, 0, 5) : 2;
    _logRaw = bool.TryParse(section["LogRaw"], out var lr) && lr;
        // Default to logging request/response unless explicitly disabled (user request to log request and response)
        var logRequestSetting = section["LogRequest"]; // if null -> default true
        _logRequest = logRequestSetting == null || (bool.TryParse(logRequestSetting, out var lreq) && lreq);
        // Determine if deployment represents a reasoning model (o1 family) to adjust prompt format & temperature handling
        _isReasoning = true; // _deployment.Contains("o1", StringComparison.OrdinalIgnoreCase);
        _apiKeyMasked = MaskKey(_apiKey);
        if (!string.IsNullOrWhiteSpace(_apiVersion))
        {
            // Best-effort: attempt to map known versions, else use generic options (future-proof)
            var options = new OpenAIClientOptions();
            // Newer SDK may allow setting default API version through options.AdvancedOptions, else rely on header negotiation.
            // We just record it for logging; actual version selection is handled by SDK.
            _client = new OpenAIClient(new Uri(_endpoint), new AzureKeyCredential(_apiKey), options);
            _logger.LogInformation("AzureOpenAI configured endpoint={Endpoint} deployment={Deployment} apiVersion={ApiVersion} reasoning={Reasoning} apiKey={ApiKeyMasked}",
                _endpoint, _deployment, _apiVersion, _isReasoning, _apiKeyMasked);
        }
        else
        {
            _client = new OpenAIClient(new Uri(_endpoint), new AzureKeyCredential(_apiKey));
            _logger.LogInformation("AzureOpenAI configured endpoint={Endpoint} deployment={Deployment} reasoning={Reasoning} apiKey={ApiKeyMasked}",
                _endpoint, _deployment, _isReasoning, _apiKeyMasked);
        }
    }

    // Back-compat/test constructor supporting raw HttpClient injection via factory. When provided, we bypass SDK and use REST for determinism in tests.
    public AzureOpenAiMapper(IConfiguration config, ILogger<AzureOpenAiMapper> logger, IHttpClientFactory httpClientFactory)
        : this(config, logger)
    {
        try
        {
            _http = httpClientFactory.CreateClient("azure-openai");
            _logger.LogInformation("AzureOpenAI raw HTTP mode enabled (test/back-compat). endpoint={Endpoint} deployment={Deployment}", _endpoint, _deployment);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire named HttpClient 'azure-openai'. Falling back to SDK client only.");
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "<empty>";
        // Avoid logging full secret: show only first 4 and last 4 characters when long enough
        if (key.Length <= 8) return new string('*', key.Length);
        var prefix = key.Substring(0, 4);
        var suffix = key.Substring(key.Length - 4, 4);
        return prefix + new string('*', key.Length - 8) + suffix;
    }

    public async Task<IReadOnlyList<AiMappingSuggestion>> SuggestMappingsAsync(TableMetadata source, TableMetadata target, IReadOnlyCollection<string> requestedSourceColumns, CancellationToken ct = default)
    {
        if (target.Columns.Count == 0)
            return Array.Empty<AiMappingSuggestion>();

        var requestedSet = new HashSet<string>(requestedSourceColumns, StringComparer.OrdinalIgnoreCase);
        var systemPrompt = BuildReasoningSystemPrompt();
        var payload = BuildUserPayload(source, target, requestedSet);
        var userJson = JsonSerializer.Serialize(payload);
        if (_logRequest)
        {
            var preview = userJson.Length > 1500 ? userJson[..1500] + "...<truncated>" : userJson;
            _logger.LogInformation("AI request prepared deployment={Deployment} reasoning={Reasoning} temperature={Temperature} retryCount={RetryCount} sourceCols={SourceCount} targetCols={TargetCount} requestedFilter={RequestedFilter} payloadBytes={PayloadBytes} payloadPreview={Preview}",
                _deployment, _isReasoning, _temperature, _retryCount, source.Columns.Count, target.Columns.Count, requestedSet.Count > 0 ? requestedSet.Count : 0, System.Text.Encoding.UTF8.GetByteCount(userJson), preview);
        }
        else
        {
            _logger.LogInformation("AI invoke start: deployment={Deployment} reasoning={Reasoning} sourceCols={SourceCount} targetCols={TargetCount} requestedFilter={RequestedFilter}", _deployment, _isReasoning, source.Columns.Count, target.Columns.Count, requestedSet.Count > 0 ? requestedSet.Count : 0);
        }

        string? content = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var startedAt = DateTime.UtcNow;
        if (_http != null)
        {
            // Raw REST mode
            var url = _endpoint.TrimEnd('/') + $"/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion ?? "2024-02-15-preview"}";
            object body = _isReasoning ? BuildReasoningModelPayload(systemPrompt, userJson) : BuildStandardModelPayload(systemPrompt, userJson);
            var json = JsonSerializer.Serialize(body);
            for (var attempt = 0; attempt <= _retryCount; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.Add("api-key", _apiKey);
                    req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var respHttp = await _http.SendAsync(req, ct);
                    if ((int)respHttp.StatusCode >= 500 || (int)respHttp.StatusCode == 429 || (int)respHttp.StatusCode == 408)
                    {
                        if (attempt < _retryCount)
                        {
                            var delay = Backoff(attempt);
                            _logger.LogWarning("AI transient HTTP status {Status}. Retry in {Delay} (attempt {Attempt}/{Max})", (int)respHttp.StatusCode, delay, attempt + 1, _retryCount + 1);
                            await Task.Delay(delay, ct);
                            continue;
                        }
                    }
                    if (!respHttp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("AI raw HTTP failed status={Status}", (int)respHttp.StatusCode);
                        break;
                    }
                    var rawText = await respHttp.Content.ReadAsStringAsync(ct);
                    try
                    {
                        using var doc = JsonDocument.Parse(rawText);
                        content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                        if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                        {
                            if (usageEl.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var pti)) promptTokens = pti;
                            if (usageEl.TryGetProperty("completion_tokens", out var ctok) && ctok.TryGetInt32(out var cti)) completionTokens = cti;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "AI raw HTTP parse error");
                    }
                    break;
                }
                catch (Exception ex) when (attempt < _retryCount)
                {
                    var delay = Backoff(attempt);
                    _logger.LogWarning(ex, "AI raw HTTP exception. Retry in {Delay} (attempt {Attempt}/{Max})", delay, attempt + 1, _retryCount + 1);
                    await Task.Delay(delay, ct);
                }
            }
        }
        else
        {
            // SDK path
            var messages = new List<ChatRequestMessage>();
            if (!_isReasoning)
            {
                messages.Add(new ChatRequestSystemMessage(systemPrompt));
                messages.Add(new ChatRequestUserMessage(userJson));
            }
            else
            {
                messages.Add(new ChatRequestUserMessage(systemPrompt + "\n\n" + userJson));
            }
            var temperature = _isReasoning ? (float?)null : (float?)_temperature;
            var opts = new ChatCompletionsOptions { DeploymentName = _deployment, Temperature = temperature };
            foreach (var m in messages) opts.Messages.Add(m);
            Response<ChatCompletions>? resp = null;
            for (var attempt = 0; attempt <= _retryCount; attempt++)
            {
                try
                {
                    resp = await _client.GetChatCompletionsAsync(opts, ct);
                    break;
                }
                catch (RequestFailedException rfe) when (attempt < _retryCount && IsTransient(rfe.Status))
                {
                    var delay = Backoff(attempt);
                    _logger.LogWarning(rfe, "AI transient error {Status}. Retry in {Delay} (attempt {Attempt}/{Max})", rfe.Status, delay, attempt + 1, _retryCount + 1);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex) when (attempt < _retryCount)
                {
                    var delay = Backoff(attempt);
                    _logger.LogWarning(ex, "AI exception. Retry in {Delay} (attempt {Attempt}/{Max})", delay, attempt + 1, _retryCount + 1);
                    await Task.Delay(delay, ct);
                }
            }
            if (resp == null)
            {
                _logger.LogInformation("AI invoke failed after {Attempts} attempts (no response)", _retryCount + 1);
                return Array.Empty<AiMappingSuggestion>();
            }
            var choice = resp.Value.Choices.FirstOrDefault();
            content = choice?.Message?.Content;
            promptTokens = resp.Value.Usage?.PromptTokens;
            completionTokens = resp.Value.Usage?.CompletionTokens;
        }
        if (content == null)
        {
            _logger.LogInformation("AI returned no content");
            return Array.Empty<AiMappingSuggestion>();
        }
        if (string.IsNullOrWhiteSpace(content)) return Array.Empty<AiMappingSuggestion>();
        if (_logRaw || _logRequest)
        {
            var raw = content;
            if (!_logRaw) // only request logging enabled -> truncate aggressive
            {
                raw = raw.Length > 1500 ? raw.Substring(0, 1500) + "...<truncated>" : raw;
            }
            _logger.LogInformation("AI response raw: {Raw}", raw);
        }
        _logger.LogInformation("AI invoke success in {ElapsedMs} ms; promptTokens={PromptTokens} completionTokens={CompletionTokens}", (DateTime.UtcNow-startedAt).TotalMilliseconds, promptTokens, completionTokens);

        // Extract JSON array
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end < start) return Array.Empty<AiMappingSuggestion>();
        var slice = content.Substring(start, end - start + 1);
        try
        {
            var parsed = JsonSerializer.Deserialize<List<AiMappingSuggestionInternal>>(slice, new JsonSerializerOptions{PropertyNameCaseInsensitive = true}) ?? new();
            var materialized = parsed.Where(p => !string.IsNullOrWhiteSpace(p.Source) && !string.IsNullOrWhiteSpace(p.Target))
                .Select(p => new AiMappingSuggestion(p.Source, p.Target, Clamp(p.Confidence), p.Transformation, p.Rationale))
                .ToList();
            if (_logRequest)
            {
                // Log concise summary of suggestions (avoid flooding logs)
                var summary = materialized
                    .Take(20) // cap summary
                    .Select(s => $"{s.SourceColumn}->{s.TargetColumn}({s.Confidence:0.00})")
                    .ToArray();
                _logger.LogInformation("AI parsed {Count} suggestions (showing {Shown}) suggestionsSummary={Summary}", materialized.Count, summary.Length, string.Join(", ", summary));
            }
            return materialized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI suggestions JSON");
            return Array.Empty<AiMappingSuggestion>();
        }
    }

    private static bool IsTransient(int status) => status is 408 or 429 or 500 or 502 or 503 or 504;
    private TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));

    private object BuildUserPayload(TableMetadata source, TableMetadata target, HashSet<string> requestedSet)
    {
        var sourceCols = source.Columns
            .Where(c => requestedSet.Count == 0 || requestedSet.Contains(c.Name))
            .Select(c => new { c.Name, c.DataType, c.Length, c.Precision, c.Scale, c.IsIdentity, c.IsPrimaryId, c.IsPrimaryName, c.IsRequired });
        var customTargetCols = target.Columns
            .Where(c => !c.IsSystemField)
            .Select(c => new { c.Name, c.DataType, c.Length, c.IsRequired, c.IsPrimaryId, c.IsPrimaryName, IsSystemField = false, SystemFieldType = "None" });
        var systemTargetCols = target.Columns
            .Where(c => c.IsSystemField)
            .Select(c => new { c.Name, c.DataType, c.Length, c.IsRequired, c.IsPrimaryId, c.IsPrimaryName, IsSystemField = true, SystemFieldType = c.SystemFieldType.ToString() });

        return new {
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
