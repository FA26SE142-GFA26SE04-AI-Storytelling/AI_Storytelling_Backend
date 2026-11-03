using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using StoryPlatform.AI.Api.Diagnostics;
using Xunit;

namespace StoryPlatform.AI.IntegrationTests;

public sealed class AiRequestLoggingTests
{
    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    public async Task Payload_logging_is_development_only_redacted_and_does_not_change_response(
        string environment, bool enabled, bool expectPayload)
    {
        var logs = new CaptureLogs();
        var builder = new HostBuilder().UseEnvironment(environment)
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Diagnostics:LogPayloads"] = enabled.ToString(),
                ["AI:InternalApiKey"] = "configured-private-key"
            }))
            .ConfigureLogging(l => { l.ClearProviders(); l.AddProvider(logs); })
            .ConfigureWebHost(w => w.UseTestServer()
                .ConfigureServices(s => s.AddControllers(o => o.Filters.Add<AiRequestLoggingFilter>())
                    .AddApplicationPart(typeof(DiagnosticProbeController).Assembly))
                .Configure(a => { a.UseRouting(); a.UseEndpoints(e => e.MapControllers()); }));
        using var server = await builder.StartAsync();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", "header-private-key");
        var response = await client.PostAsJsonAsync("/diagnostic-probe", new
        {
            requestId = "content-job-42", content = "Synthetic story preview",
            nested = new { apiKey = "body-private-key", description = "configured-private-key" },
            jwt = "Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("body-private-key", body); // Logging must not mutate DTOs or responses.
        var text = string.Join('\n', logs.Messages);
        Assert.Contains("AI request received", text);
        Assert.Contains("AI action response", text);
        Assert.Equal(expectPayload, text.Contains("Synthetic story preview", StringComparison.Ordinal));
        Assert.DoesNotContain("body-private-key", text);
        Assert.DoesNotContain("header-private-key", text);
        Assert.DoesNotContain("configured-private-key", text);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", text);
        if (expectPayload) Assert.Contains("[REDACTED]", text);
    }

    [Fact]
    public void Preview_redacts_before_truncation_and_handles_nested_arrays()
    {
        var text = AiPayloadPreview.Format(new
        {
            rows = new[] { new { password = "do-not-log", text = "secret-value" } },
            content = new string('x', 5000)
        }, 256, ["secret-value"]);
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("[TRUNCATED]", text);
        Assert.DoesNotContain("do-not-log", text);
        Assert.DoesNotContain("secret-value", text);
        Assert.True(text.Length < 300);
    }

    [Fact]
    public void Scalar_preview_redacts_credentials_and_keeps_Vietnamese_readable()
    {
        var preview = AiPayloadPreview.Format("Câu chuyện có secret-value\nBearer hidden", 1024, ["secret-value"]);
        Assert.Contains("Câu chuyện", preview);
        Assert.DoesNotContain("secret-value", preview);
        Assert.DoesNotContain("Bearer hidden", preview);
        Assert.DoesNotContain('\n', preview); // Embedded line breaks stay escaped, preventing forged log lines.
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Messages);
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}

[ApiController]
[Route("diagnostic-probe")]
public sealed class DiagnosticProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Echo([FromBody] System.Text.Json.JsonElement body) => Ok(body);
}
