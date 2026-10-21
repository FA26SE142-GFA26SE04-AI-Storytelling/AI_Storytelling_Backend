using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Contracts.AI.Moderation;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class VertexGrayZoneProviderTests
{
    private static readonly GrayZoneScan Scan = new("phase3", "6-8", "vi", [new("paragraph", "p0", "Na sửa xe.")], []) { Rubric = RubricTestData.Snapshot() };
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return respond(request); }
    }
    private static VertexGrayZoneModerationProvider Provider(Transport transport) => new(new HttpClient(transport),
        Options.Create(new AIServiceOptions { BaseUrl = "http://ai.test", InternalApiKey = "test-only-internal-key" }));

    private static async Task<EvaluateGrayZoneResponse> Result(HttpRequestMessage message)
    {
        Assert.Equal("/api/ai/moderation/gray-zone/evaluate", message.RequestUri!.AbsolutePath);
        Assert.True(message.Headers.Contains("X-Internal-Api-Key"));
        var request = (await message.Content!.ReadFromJsonAsync<EvaluateGrayZoneRequest>())!;
        return new()
        {
            RequestId = request.RequestId, SourceFingerprint = request.SourceFingerprint, Model = "vertex-test-model",
            RubricVersion = request.RubricVersion, RubricId = request.Rubric?.Id, RubricHash = request.Rubric?.ContentHash,
            Scores = GrayZoneRules.CriterionCodes.Select(x => new GrayZoneCriterionScore(x, x == "GZ02" ? 2 : 0)).ToArray(),
            RiskScore = 50, Findings = [new("GZ02", "paragraph", "p0", "Na sửa xe.", "Cần đối chiếu ngữ cảnh", "Xem toàn truyện")]
        };
    }

    [Fact]
    public async Task MapsVerifiedEvidenceWithoutUsingSafetyEndpoint()
    {
        var transport = new Transport(async message => new(HttpStatusCode.OK) { Content = JsonContent.Create(await Result(message)) });
        var result = await Provider(transport).EvaluateAsync(Scan, default);
        Assert.Equal(50, result.RiskScore); Assert.Equal("GZ02", Assert.Single(result.Findings).Category);
        Assert.Equal("Na sửa xe.", result.Findings[0].EvidenceQuote);
        Assert.Equal(GrayZoneRules.RubricVersion, result.RubricVersion); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task MissingSnapshotNeverCallsAiService()
    {
        var transport = new Transport(_ => throw new Exception("No HTTP expected"));
        await Assert.ThrowsAsync<GrayZoneProviderException>(() => Provider(transport).EvaluateAsync(Scan with { Rubric = null }, default));
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransportsPinnedRubricAndRejectsDifferentResponseHash(bool corruptHash)
    {
        var definition = new RubricDefinition("Assess child safety.", GrayZoneRules.CriterionCodes.Select(code =>
            new RubricCriterion(code, code, "Check", "Context", Enumerable.Range(0, 5).ToDictionary(x => x.ToString(), x => "Level " + x), [])).ToArray());
        var rubric = new RubricSnapshot(12, "managed-v2", RubricRules.SchemaVersion, RubricRules.Hash(definition), definition);
        var transport = new Transport(async message =>
        {
            var response = await Result(message);
            Assert.Equal(rubric.ContentHash, response.RubricHash);
            if (corruptHash) response = response with { RubricHash = new string('0', 64) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        });
        var scan = Scan with { Rubric = rubric, RubricVersion = rubric.Version };
        if (corruptHash) await Assert.ThrowsAsync<GrayZoneProviderException>(() => Provider(transport).EvaluateAsync(scan, default));
        else Assert.Equal(rubric.Version, (await Provider(transport).EvaluateAsync(scan, default)).RubricVersion);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("fingerprint")] [InlineData("quote")] [InlineData("score")] [InlineData("rubric")]
    public async Task RejectsUnverifiableResponse(string field)
    {
        var transport = new Transport(async message =>
        {
            var result = await Result(message);
            result = field switch
            {
                "fingerprint" => result with { SourceFingerprint = new string('B', 64) },
                "score" => result with { RiskScore = 99 },
                "rubric" => result with { RubricVersion = "wrong" },
                _ => result with { Findings = [result.Findings[0] with { EvidenceQuote = "invented" }] }
            };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
        });
        await Assert.ThrowsAsync<GrayZoneProviderException>(() => Provider(transport).EvaluateAsync(Scan, default));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)] [InlineData(HttpStatusCode.TooManyRequests)] [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task UpstreamErrorsAreNotSafeAndNotRetried(HttpStatusCode status)
    {
        var transport = new Transport(_ => Task.FromResult(new HttpResponseMessage(status)
        { Content = JsonContent.Create(new { errorCode = "GRAY_ZONE_TIMEOUT", error = "private body" }) }));
        var error = await Assert.ThrowsAsync<GrayZoneProviderException>(() => Provider(transport).EvaluateAsync(Scan, default));
        Assert.Equal(504, error.StatusCode); Assert.DoesNotContain("private", error.Message); Assert.Equal(1, transport.Calls);
    }
}
