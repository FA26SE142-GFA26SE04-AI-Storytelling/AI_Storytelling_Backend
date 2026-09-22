using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using StoryPlatform.AI.Api.Controllers;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Domain.Generation;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

/// <summary>
/// Validates the Dev-only guards on <see cref="DevVertexController"/>.
/// The actual Vertex AI round-trip is exercised by manual curl/swagger
/// runs because the <c>Google.GenAI</c> SDK requires live ADC credentials
/// and cannot be mocked via <c>HttpMessageHandler</c>.
/// </summary>
public sealed class DevVertexControllerTests
{
    [Fact]
    public async Task VertexTest_NonDevelopmentEnvironment_Returns404()
    {
        var controller = new DevVertexController(new HostingEnvironmentStub("Production"));

        var result = await controller.VertexTest(new StubLlmClient(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task VertexTest_DevelopmentEnvironment_ReturnsOkWithProviderPayload()
    {
        var controller = new DevVertexController(new HostingEnvironmentStub("Development"));

        var result = await controller.VertexTest(new StubLlmClient("\"VERTEX_AI_OK\""), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("VertexAI", root.GetProperty("provider").GetString());
        Assert.Contains("VERTEX_AI_OK", root.GetProperty("response").GetString());
    }

    private sealed class StubLlmClient : ILlmClient
    {
        private readonly string _response;
        public StubLlmClient(string response = "\"VERTEX_AI_OK\"") => _response = response;
        public Task<LlmGenerationResult> GenerateStructuredAsync(
            string prompt,
            string schemaName,
            JsonElement schema,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmGenerationResult(
                _response, "VertexAI", "gemini-1.5-flash-002", "gemini-1.5-flash-002", 1, 1, 10));
    }

    private sealed class HostingEnvironmentStub : IWebHostEnvironment
    {
        public HostingEnvironmentStub(string environmentName)
        {
            EnvironmentName = environmentName;
            ApplicationName = "StoryPlatform.AI.Api";
            ContentRootPath = AppContext.BaseDirectory;
            ContentRootFileProvider = new NullFileProvider();
            WebRootPath = AppContext.BaseDirectory;
            WebRootFileProvider = new NullFileProvider();
        }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
