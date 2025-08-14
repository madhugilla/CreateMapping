using System.Text.Json;
using System.Text.Json.Serialization;
using CreateMapping.Models;

namespace CreateMapping.Export;

public interface IJsonMappingExporter
{
    Task WriteAsync(MappingResult result, string path, CancellationToken ct = default);
}

public sealed class JsonMappingExporter : IJsonMappingExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(MappingResult result, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, result, Options, ct);
    }
}
