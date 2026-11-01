using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class GoogleSpeechToTextRecognizerTests
{
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request);
        }
    }

    private static SpeechToTextOptions Options(string location = "global") => new()
    {
        ProjectId = "demo-project", Location = location, Model = "long", LanguageCode = "vi-VN", TimeoutSeconds = 30
    };

    private static GoogleSpeechToTextRecognizer Create(Transport transport) =>
        new(new HttpClient(transport), Microsoft.Extensions.Options.Options.Create(Options()),
            NullLogger<GoogleSpeechToTextRecognizer>.Instance);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void RecognizeUri_UsesGlobalOrRegionalHost()
    {
        Assert.Equal("https://speech.googleapis.com/v2/projects/demo-project/locations/global/recognizers/_:recognize",
            GoogleSpeechToTextRecognizer.RecognizeUri(Options()).ToString());
        Assert.Equal("https://us-central1-speech.googleapis.com/v2/projects/demo-project/locations/us-central1/recognizers/_:recognize",
            GoogleSpeechToTextRecognizer.RecognizeUri(Options("us-central1")).ToString());
    }

    [Fact]
    public void ParseWords_ReadsWordsAndConfidence_AndTreatsZeroConfidenceAsUnknown()
    {
        var words = GoogleSpeechToTextRecognizer.ParseWords("""
            {"results":[{"alternatives":[{"transcript":"thỏ con","words":[
              {"word":"thỏ","confidence":0.92},{"word":"con","confidence":0}]}]}]}
            """);

        Assert.Equal(new[] { "thỏ", "con" }, words.Select(word => word.Text));
        Assert.Equal(0.92, words[0].Confidence);
        Assert.Null(words[1].Confidence);
    }

    [Fact]
    public void ParseWords_FallsBackToTranscript_AndHandlesEmptyResults()
    {
        var fromTranscript = GoogleSpeechToTextRecognizer.ParseWords(
            """{"results":[{"alternatives":[{"transcript":"ra vườn"}]}]}""");
        var empty = GoogleSpeechToTextRecognizer.ParseWords("{}");

        Assert.Equal(new[] { "ra", "vườn" }, fromTranscript.Select(word => word.Text));
        Assert.All(fromTranscript, word => Assert.Null(word.Confidence));
        Assert.Empty(empty);
    }

    [Fact]
    public async Task RecognizeAsync_PostsBase64AudioWithVietnameseConfig()
    {
        var transport = new Transport(_ => Task.FromResult(Json(
            """{"results":[{"alternatives":[{"words":[{"word":"thỏ","confidence":0.9}]}]}]}""")));

        var words = await Create(transport).RecognizeAsync([1, 2, 3]);

        Assert.Equal("thỏ", Assert.Single(words).Text);
        var (request, body) = Assert.Single(transport.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), json.RootElement.GetProperty("content").GetString());
        var config = json.RootElement.GetProperty("config");
        Assert.Equal("vi-VN", config.GetProperty("languageCodes")[0].GetString());
        Assert.Equal("long", config.GetProperty("model").GetString());
        Assert.True(config.GetProperty("features").GetProperty("enableWordConfidence").GetBoolean());
    }

    [Fact]
    public async Task RecognizeAsync_ProviderError_ThrowsUnavailable()
    {
        var transport = new Transport(_ => Task.FromResult(Json("""{"error":{"code":403}}""", HttpStatusCode.Forbidden)));

        var error = await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() => Create(transport).RecognizeAsync([1]));

        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task RecognizeAsync_NetworkFailure_ThrowsUnavailable()
    {
        var transport = new Transport(_ => throw new HttpRequestException("down"));

        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() => Create(transport).RecognizeAsync([1]));
    }

    [Fact]
    public async Task RecognizeAsync_MalformedProviderResponse_ThrowsUnavailable()
    {
        var transport = new Transport(_ => Task.FromResult(Json("{invalid")));

        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() => Create(transport).RecognizeAsync([1]));
    }
}
