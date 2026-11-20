using StoryPlatform.AI.Api.Diagnostics;
using Xunit;

namespace StoryPlatform.AI.UnitTests.Diagnostics;

public sealed class AiPayloadPreviewTests
{
    [Fact]
    public void FormatAllowlisted_OmitsReasonAndDecisionAndFreeText()
    {
        var dto = new
        {
            traceId = "trace-123",
            status = "Failed",
            statusCode = 400,
            errorCode = "VALIDATION_FAILED",
            durationMs = 42,
            provider = "Gemini",
            model = "gemini-3.8-flash",
            reason = "Sensitive story excerpt: Bé Na lives at 123 Secret St",
            decision = "Reject because secret@example.com",
            storyText = "Ngày xửa ngày xưa có bạn nhỏ tên Bình.",
            prompt = "Illustrate the secret scene"
        };

        var preview = AiPayloadPreview.FormatAllowlisted(dto, 4096);

        Assert.Contains("\"traceId\":\"trace-123\"", preview, StringComparison.Ordinal);
        Assert.Contains("\"errorCode\":\"VALIDATION_FAILED\"", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Bé Na", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.com", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Ngày xửa ngày xưa", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Illustrate the secret scene", preview, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"[OMITTED len=", preview, StringComparison.Ordinal);
        Assert.Contains("\"decision\":\"[OMITTED len=", preview, StringComparison.Ordinal);
        Assert.Contains("\"storyText\":\"[OMITTED len=", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_RedactsBeforeTruncating_WhenSecretSpansBoundary()
    {
        var prefix = new string('a', 8185);
        var secretSpanningBoundary = "AIzaSyBoundarySecretKey1234567890";
        var payload = new { text = prefix + secretSpanningBoundary + " tail" };

        var preview = AiPayloadPreview.Format(payload, 32768);

        Assert.DoesNotContain("AIzaSy", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundarySecretKey", preview, StringComparison.Ordinal);
        Assert.Contains("[TRUNCATED]", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_BudgetExceeded_NeverLeavesUnscrubbedSubtree()
    {
        var items = Enumerable.Range(0, 30)
            .Select(i => new
            {
                index = i,
                nested = new
                {
                    a = "v1",
                    b = "v2",
                    c = "v3",
                    d = "v4",
                    e = "v5",
                    f = "v6",
                    g = "v7",
                    h = "v8",
                    email = $"child{i}@example.com",
                    secretValue = $"AIzaSySubtreeSecret{i:D20}"
                }
            })
            .ToArray();

        var preview = AiPayloadPreview.Format(new { items }, 32768);

        Assert.DoesNotContain("@example.com", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("AIzaSySubtreeSecret", preview, StringComparison.Ordinal);
        Assert.Contains("[OMITTED]", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DeepNestingBeyondMaxDepth_NeverLeavesUnscrubbedSubtree()
    {
        object current = new { email = "deep-child@example.com", token = "sk-DeepSecretToken1234567890" };
        for (var i = 0; i < 15; i++)
        {
            current = new { child = current };
        }

        var preview = AiPayloadPreview.Format(current, 32768);

        Assert.DoesNotContain("deep-child@example.com", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-DeepSecretToken", preview, StringComparison.Ordinal);
        Assert.Contains("[OMITTED]", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_OversizedString_ReturnsOmittedWithoutExposingRawPrefix()
    {
        var huge = "AIzaSyHugePrefixSecret1234567890 " + new string('x', 70000);
        var preview = AiPayloadPreview.Format(new { text = huge }, 32768);

        Assert.DoesNotContain("AIzaSyHugePrefixSecret", preview, StringComparison.Ordinal);
        Assert.Contains("[OMITTED len=", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatAllowlisted_ScrubsCredentialsAndEmailsInAllowlistedFields()
    {
        var dto = new
        {
            requestId = "req-user@example.com-AIzaSyLeak123456789012345",
            status = "OK"
        };

        var preview = AiPayloadPreview.FormatAllowlisted(dto, 2048);

        Assert.DoesNotContain("user@example.com", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("AIzaSyLeak", preview, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_EMAIL]", preview, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", preview, StringComparison.Ordinal);
    }
}
