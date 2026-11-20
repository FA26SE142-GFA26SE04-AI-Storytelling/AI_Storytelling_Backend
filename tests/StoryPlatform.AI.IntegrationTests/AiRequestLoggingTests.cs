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
    public async Task Payload_logging_is_development_only_allowlisted_omits_freetext_and_does_not_change_response(
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
            requestId = "content-job-42",
            content = "Synthetic story preview with child@example.com",
            nested = new { apiKey = "body-private-key", description = "configured-private-key" },
            jwt = "Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("body-private-key", body); // Logging must not mutate DTOs or responses.
        var text = string.Join('\n', logs.Messages);
        Assert.Contains("AI request received", text);
        Assert.Contains("AI action response", text);
        Assert.DoesNotContain("Synthetic story preview", text, StringComparison.Ordinal);
        Assert.DoesNotContain("child@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("body-private-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("header-private-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("configured-private-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", text, StringComparison.Ordinal);
        Assert.Equal(expectPayload, text.Contains("[OMITTED len=", StringComparison.Ordinal));
        if (expectPayload)
        {
            Assert.Contains("content-job-42", text, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Preview_redacts_before_truncation_and_handles_nested_arrays()
    {
        var text = AiPayloadPreview.Format(new
        {
            rows = new[] { new { password = "do-not-log", text = "secret-value", email = "kid@example.com", phone = "0912345678" } },
            content = new string('x', 5000)
        }, 256, ["secret-value"]);
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("[TRUNCATED]", text);
        Assert.DoesNotContain("do-not-log", text);
        Assert.DoesNotContain("secret-value", text);
        Assert.DoesNotContain("kid@example.com", text);
        Assert.DoesNotContain("0912345678", text);
        Assert.True(text.Length < 300);
    }

    [Fact]
    public void Allowlisted_preview_omits_freetext_even_without_regex_match_and_redacts_pii_in_allowlisted_fields()
    {
        var preview = AiPayloadPreview.FormatAllowlisted(new
        {
            requestId = "req-1-kid@example.com",
            status = "ok",
            storyText = "Bé An đi học về qua cánh đồng xanh.",
            nested = new { prompt = "Tell a story", errorCode = "SAFE_CODE", phone = "+84 912 345 678" }
        }, 2048);

        Assert.Contains("[REDACTED_EMAIL]", preview);
        Assert.Contains("SAFE_CODE", preview);
        Assert.Contains("[OMITTED len=", preview);
        Assert.DoesNotContain("kid@example.com", preview);
        Assert.DoesNotContain("Bé An đi học", preview);
        Assert.DoesNotContain("Tell a story", preview);
        Assert.DoesNotContain("912 345 678", preview);
    }

    [Fact]
    public void Scalar_preview_redacts_credentials_emails_phones_and_keeps_Vietnamese_readable()
    {
        var preview = AiPayloadPreview.Format(
            "Câu chuyện có secret-value\nBearer hidden-token-12345\nemail kid@example.com\nphone 0912345678\nhttps://user:pass@example.com/a",
            1024,
            ["secret-value"]);
        Assert.Contains("Câu chuyện", preview);
        Assert.DoesNotContain("secret-value", preview);
        Assert.DoesNotContain("hidden-token-12345", preview);
        Assert.DoesNotContain("kid@example.com", preview);
        Assert.DoesNotContain("0912345678", preview);
        Assert.DoesNotContain("user:pass", preview);
        Assert.DoesNotContain('\n', preview); // Embedded line breaks stay escaped, preventing forged log lines.
    }

    [Fact]
    public void Preview_bounds_depth_and_arrays_and_falls_back_to_safe_marker_on_error()
    {
        object deep = "leaf";
        for (var i = 0; i < 20; i++) deep = new { child = deep };
        var deepPreview = AiPayloadPreview.Format(deep, 4096);
        Assert.Contains("[OMITTED]", deepPreview);
        Assert.DoesNotContain("leaf", deepPreview);

        var many = AiPayloadPreview.Format(Enumerable.Range(0, 50).ToArray(), 4096);
        Assert.DoesNotContain("49", many);

        var unserializable = new UnserializablePayload();
        Assert.Equal("[PAYLOAD_UNAVAILABLE]", AiPayloadPreview.Format(unserializable, 512));
    }

    private sealed class UnserializablePayload
    {
        public string Boom => throw new InvalidOperationException("raw-secret-must-not-leak");
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
