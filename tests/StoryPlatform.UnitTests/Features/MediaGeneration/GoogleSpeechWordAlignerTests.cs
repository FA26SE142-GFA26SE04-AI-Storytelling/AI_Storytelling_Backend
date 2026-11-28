using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests.Features.MediaGeneration;

public sealed class GoogleSpeechWordAlignerTests
{
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static GoogleSpeechWordAligner Create(Transport transport) =>
        new(new HttpClient(transport),
            Microsoft.Extensions.Options.Options.Create(new SpeechToTextOptions { ProjectId = "demo", Location = "global", Model = "long", LanguageCode = "vi-VN" }),
            NullLogger<GoogleSpeechWordAligner>.Instance);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("1.200s", 1.2)]
    [InlineData("0s", 0.0)]
    [InlineData("12.5s", 12.5)]
    [InlineData(null, 0.0)]
    [InlineData("bad", 0.0)]
    public void ParseOffsetSeconds_ReadsProtoDurations(string? input, double expected) =>
        Assert.Equal(expected, GoogleSpeechWordAligner.ParseOffsetSeconds(input), 3);

    [Fact]
    public void ParseTimedWords_ReadsWordsAndOffsetsAcrossResults()
    {
        var json = """
            {"results":[
              {"alternatives":[{"words":[{"word":"thỏ","startOffset":"0s","endOffset":"0.400s"},{"word":"con","startOffset":"0.400s","endOffset":"0.700s"}]}]},
              {"alternatives":[{"words":[{"word":"đi","startOffset":"0.900s","endOffset":"1.100s"}]}]}
            ]}
            """;
        var words = GoogleSpeechWordAligner.ParseTimedWords(json);
        Assert.Equal(["thỏ", "con", "đi"], words.Select(w => w.Text));
        Assert.Equal([0.0, 0.4, 0.9], words.Select(w => w.StartSeconds));
        Assert.Equal(1.1, words[2].EndSeconds, 3);
    }

    [Fact]
    public void ParseTimedWords_NoResultsOrNoWords_ReturnsEmpty()
    {
        Assert.Empty(GoogleSpeechWordAligner.ParseTimedWords("{}"));
        Assert.Empty(GoogleSpeechWordAligner.ParseTimedWords("""{"results":[{"alternatives":[{"transcript":"x"}]}]}"""));
    }

    [Fact]
    public async Task RecognizeWordsAsync_RequestsWordTimeOffsets()
    {
        var transport = new Transport(_ => Json("""{"results":[{"alternatives":[{"words":[{"word":"a","startOffset":"0s","endOffset":"1s"}]}]}]}"""));
        var words = await Create(transport).RecognizeWordsAsync([1, 2, 3]);

        Assert.Single(words);
        Assert.Contains("\"enableWordTimeOffsets\":true", transport.Bodies.Single());
        Assert.Contains("\"languageCodes\":[\"vi-VN\"]", transport.Bodies.Single());
    }

    [Fact]
    public async Task RecognizeWordsAsync_HttpFailure_ThrowsSpeechRecognitionUnavailable()
    {
        var transport = new Transport(_ => Json("""{"error":{"status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<SpeechRecognitionUnavailableException>(() => Create(transport).RecognizeWordsAsync([1]));
    }
}
