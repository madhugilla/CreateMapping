using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CreateMapping.AI;
using CreateMapping.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CreateMapping.Tests;

public class AzureOpenAiMapperRetryTests
{
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;
        public SequenceHandler(IEnumerable<Func<HttpResponseMessage>> responses) => _responses = new Queue<Func<HttpResponseMessage>>(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var next = _responses.Count > 0 ? _responses.Dequeue() : (() => new HttpResponseMessage(HttpStatusCode.OK));
            return Task.FromResult(next());
        }
    }

    private static HttpResponseMessage ChatResponse(string content)
    {
        var json = JsonSerializer.Serialize(new { choices = new object[]{ new { message = new { content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK){ Content = new StringContent(json, Encoding.UTF8, "application/json")};
    }

    private AzureOpenAiMapper CreateMapper(HttpClient client, int retryCount, bool logRaw = false)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>
        {
            ["Ai:Endpoint"] = "https://example/",
            ["Ai:ApiKey"] = "key",
            ["Ai:Deployment"] = "model",
            ["Ai:RetryCount"] = retryCount.ToString(),
            ["Ai:LogRaw"] = logRaw ? "true" : "false"
        }).Build();
        var logger = Mock.Of<ILogger<AzureOpenAiMapper>>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("azure-openai")).Returns(client);
        return new AzureOpenAiMapper(cfg, logger, factory.Object);
    }

    [Fact]
    public async Task RetriesOn429ThenSucceeds()
    {
        var handler = new SequenceHandler(new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage((HttpStatusCode)429){ Content = new StringContent("{}")},
            () => ChatResponse("[{\"source\":\"Name\",\"target\":\"name\",\"confidence\":0.9}]")
        });
        var client = new HttpClient(handler);
        var mapper = CreateMapper(client, retryCount: 2);
        var src = new TableMetadata("SQL","S", new[]{ new ColumnMetadata("Name","nvarchar", true,100,null,null) });
        var tgt = new TableMetadata("DATAVERSE","T", new[]{ new ColumnMetadata("name","string", true,100,null,null) });
        var suggestions = await mapper.SuggestMappingsAsync(src, tgt, Array.Empty<string>());
        Assert.Single(suggestions);
    }

    [Fact]
    public async Task ExhaustsRetriesReturnsEmpty()
    {
        var handler = new SequenceHandler(new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){ Content = new StringContent("{}")},
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){ Content = new StringContent("{}")},
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){ Content = new StringContent("{}")}
        });
        var client = new HttpClient(handler);
        var mapper = CreateMapper(client, retryCount: 2);
        var src = new TableMetadata("SQL","S", new[]{ new ColumnMetadata("Name","nvarchar", true,100,null,null) });
        var tgt = new TableMetadata("DATAVERSE","T", new[]{ new ColumnMetadata("name","string", true,100,null,null) });
        var suggestions = await mapper.SuggestMappingsAsync(src, tgt, Array.Empty<string>());
        Assert.Empty(suggestions);
    }
}
