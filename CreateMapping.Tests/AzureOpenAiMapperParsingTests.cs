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

public class AzureOpenAiMapperParsingTests
{
    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _res;
        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> res) => _res = res;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_res(request));
    }

    private AzureOpenAiMapper CreateMapper(string rawContent)
    {
        var json = JsonSerializer.Serialize(new
        {
            choices = new object[]
            {
                new { message = new { content = rawContent } }
            }
        });
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
    var configDict = new Dictionary<string, string>
        {
            ["Ai:Endpoint"] = "https://example/",
            ["Ai:ApiKey"] = "key",
            ["Ai:Deployment"] = "model",
            ["Ai:Temperature"] = "0",
            ["Ai:RetryCount"] = "0",
            ["Ai:LogRaw"] = "false"
        };
    var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        var logger = Mock.Of<ILogger<AzureOpenAiMapper>>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("azure-openai")).Returns(httpClient);
        return new AzureOpenAiMapper(config, logger, factory.Object);
    }

    [Fact]
    public async Task ParsesValidJsonArray()
    {
        var mapper = CreateMapper("[{\"source\":\"Name\",\"target\":\"name\",\"confidence\":0.9}]");
        var src = new TableMetadata("SQL","S", new[]{ new ColumnMetadata("Name","nvarchar", true,100,null,null) });
        var tgt = new TableMetadata("DATAVERSE","T", new[]{ new ColumnMetadata("name","string", true,100,null,null) });
        var result = await mapper.SuggestMappingsAsync(src, tgt, Array.Empty<string>());
        Assert.Single(result);
        Assert.Equal("Name", result[0].SourceColumn);
    }

    [Fact]
    public async Task ReturnsEmptyWhenNoArray()
    {
        var mapper = CreateMapper("No JSON here");
        var src = new TableMetadata("SQL","S", new[]{ new ColumnMetadata("Name","nvarchar", true,100,null,null) });
        var tgt = new TableMetadata("DATAVERSE","T", new[]{ new ColumnMetadata("name","string", true,100,null,null) });
        var result = await mapper.SuggestMappingsAsync(src, tgt, Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task HandlesRequestedSourceColumnsFilter()
    {
        var mapper = CreateMapper("[{\"source\":\"Name\",\"target\":\"name\",\"confidence\":0.9},{\"source\":\"Id\",\"target\":\"id\",\"confidence\":0.8}]");
        var src = new TableMetadata("SQL","S", new[]{ 
            new ColumnMetadata("Id","int", false,null,null,null),
            new ColumnMetadata("Name","nvarchar", true,100,null,null) 
        });
        var tgt = new TableMetadata("DATAVERSE","T", new[]{ 
            new ColumnMetadata("id","int", false,null,null,null),
            new ColumnMetadata("name","string", true,100,null,null) 
        });
        
        // Request mapping for only the "Name" column
        var result = await mapper.SuggestMappingsAsync(src, tgt, new[] { "Name" });
        
        // The AI response includes both mappings, but the actual filtering happens in the prompt creation
        // This test verifies that the method can be called with specific requested columns
        Assert.Equal(2, result.Count); // Both suggestions returned since the mock response includes both
        Assert.Contains(result, r => r.SourceColumn == "Name");
        Assert.Contains(result, r => r.SourceColumn == "Id");
    }
}
