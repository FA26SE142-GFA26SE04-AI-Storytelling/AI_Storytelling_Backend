using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildMediaUrlSignerTests
{
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ChildMediaUrlSigner _sut;

    public ChildMediaUrlSignerTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed/{path}");
        _sut = new ChildMediaUrlSigner(_storage.Object, NullLogger<ChildMediaUrlSigner>.Instance);
    }

    [Fact]
    public async Task Signs_storage_paths_once_and_keeps_https_links()
    {
        var url = await _sut.SignAsync(new[] { "908/a.png", "908/a.png", "https://cdn/b.png", null, "http://plain/c.png" });

        Assert.Equal("https://signed/908/a.png", url("908/a.png"));
        Assert.Equal("https://cdn/b.png", url("https://cdn/b.png"));
        Assert.Null(url("http://plain/c.png"));
        Assert.Null(url(null));
        _storage.Verify(s => s.GetSignedUrlAsync("908/a.png", ChildMediaUrlSigner.Expiry, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_signature_returns_null_instead_of_failing_the_whole_read()
    {
        _storage.Setup(s => s.GetSignedUrlAsync("908/broken.png", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage down"));

        var url = await _sut.SignAsync(new[] { "908/ok.png", "908/broken.png" });

        Assert.Equal("https://signed/908/ok.png", url("908/ok.png"));
        Assert.Null(url("908/broken.png"));
    }

    [Fact]
    public async Task Signs_every_media_link_in_reading_content()
    {
        var content = new ReadingContentDto
        {
            Scenes =
            [
                new ReadingSceneDto
                {
                    IllustrationUrl = "908/scene.png",
                    Segments = [new ReadingSegmentDto { AudioUrl = "908/seg.wav" }],
                    Interaction = new ReadingInteractionDto
                    {
                        ModelAudioUrl = "908/model.wav",
                        AudioUrls = new Dictionary<string, string> { ["prompt"] = "908/prompt.wav" },
                        Options = [new ReadingInteractionOptionDto { ImageUrl = "908/opt.png", AudioUrl = "908/opt.wav", FeedbackAudioUrl = "908/fb.wav" }]
                    }
                }
            ],
            Vocabulary = [new ReadingVocabularyDto { AudioUrl = "908/word.wav" }]
        };

        await ((Application.Features.ReadingSessions.Interfaces.IChildMediaUrlSigner)_sut).SignAsync(content, CancellationToken.None);

        var scene = content.Scenes[0];
        Assert.Equal("https://signed/908/scene.png", scene.IllustrationUrl);
        Assert.Equal("https://signed/908/seg.wav", scene.Segments[0].AudioUrl);
        Assert.Equal("https://signed/908/model.wav", scene.Interaction!.ModelAudioUrl);
        Assert.Equal("https://signed/908/prompt.wav", scene.Interaction.AudioUrls["prompt"]);
        Assert.Equal("https://signed/908/opt.png", scene.Interaction.Options[0].ImageUrl);
        Assert.Equal("https://signed/908/opt.wav", scene.Interaction.Options[0].AudioUrl);
        Assert.Equal("https://signed/908/fb.wav", scene.Interaction.Options[0].FeedbackAudioUrl);
        Assert.Equal("https://signed/908/word.wav", content.Vocabulary[0].AudioUrl);
    }
}
