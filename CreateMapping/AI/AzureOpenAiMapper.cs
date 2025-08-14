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
                c.Scale
            });
        var targetCols = target.Columns.Select(c => new {
            c.Name,
            c.DataType,
            c.Length,
            c.IsRequired
        });

        var system = "You map full SQL table metadata to Dataverse attributes. Return ONLY a JSON array where each element is {\"source\":<sqlCol>,\"target\":<dataverseAttr>,\"confidence\":0-1,\"transformation\":optional,\"rationale\":optional}. Do not repeat a target. Include only plausible one-to-one mappings. No markdown, no extra text.";
        var userObj = new {
            sourceTable = source.Name,
            targetTable = target.Name,
            sourceColumns = sourceCols, // renamed for clarity (was unresolvedSource)
            targetColumns = targetCols
        };
        var user = JsonSerializer.Serialize(userObj);

        var payload = new {
            messages = new object[] {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = _temperature,
            max_tokens = 800
        };

    var json = JsonSerializer.Serialize(payload);
    HttpResponseMessage? resp = null;
        for (var attempt = 0; attempt <= _retryCount; attempt++)
        {
            try
            {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"openai/deployments/{_deployment}/chat/completions?api-version=2024-05-01-preview")
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

    private sealed record AiMappingSuggestionInternal(string Source, string Target, double Confidence, string? Transformation, string? Rationale);
}
