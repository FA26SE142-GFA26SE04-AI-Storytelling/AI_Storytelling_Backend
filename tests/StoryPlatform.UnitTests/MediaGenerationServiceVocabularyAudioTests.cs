using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    [Fact]
    public async Task VocabularyAudio_IsGeneratedInTheTtsBranch_ForEachWord()
    {
        var uow = Seed();
        uow.Seed(new StoryVocabulary { Id = 70, StoryVersionId = 10, Term = "cà rốt", Definition = "củ màu cam" });
        uow.Seed(new StoryVocabulary { Id = 71, StoryVersionId = 10, Term = "dũng cảm", Definition = "không sợ" });
        var tts = new RecordingTtsProvider();
        var service = Create(uow, tts: tts);

        Assert.True((await service.RunPackageAsync()).Success);
        Assert.Contains("cà rốt", tts.Inputs);
        Assert.Contains("dũng cảm", tts.Inputs);
        var assets = uow.Items<MediaAsset>().Where(x => x.Type == MediaType.VocabularyAudio)
            .OrderBy(x => x.StoryVocabularyId).ToList();
        Assert.Equal(new int?[] { 70, 71 }, assets.Select(x => x.StoryVocabularyId));
        Assert.All(assets, asset =>
        {
            Assert.Equal(MediaStatus.Ready, asset.Status);
            Assert.Null(asset.StorySceneId);
            Assert.StartsWith("1/versions/10/v", asset.Url);
            Assert.Contains($"/vocab-{asset.StoryVocabularyId}-asset-{asset.Id}-", asset.Url);
        });
    }

    [Fact]
    public async Task VocabularyAudioFailure_DoesNotBlockThePackage()
    {
        var uow = Seed();
        uow.Seed(new StoryVocabulary { Id = 70, StoryVersionId = 10, Term = "cà rốt", Definition = "củ màu cam" });
        var service = Create(uow, tts: new FailingTermTtsProvider("cà rốt"));

        Assert.True((await service.RunPackageAsync()).Success);
        var vocabularyAudio = Assert.Single(uow.Items<MediaAsset>(), x => x.Type == MediaType.VocabularyAudio);
        Assert.Equal(MediaStatus.Failed, vocabularyAudio.Status);
        Assert.Null(vocabularyAudio.Url);
        Assert.True((await service.GetProgressAsync(1, 1)).IsGenerated);
    }

    [Fact]
    public async Task VocabularyAudio_PackageListsItWithSignedUrlForReview()
    {
        var uow = Seed();
        uow.Seed(new StoryVocabulary { Id = 70, StoryVersionId = 10, Term = "cà rốt", Definition = "củ màu cam" });
        var service = Create(uow);
        Assert.True((await service.RunPackageAsync()).Success);

        var item = Assert.Single((await service.GetPackageAsync(1, 1)).Vocabulary);
        Assert.Equal(70, item.VocabularyId);
        Assert.Equal("cà rốt", item.Term);
        Assert.Equal("Ready", item.Status);
        Assert.StartsWith("https://media.test/1/versions/10/", item.Url);
    }

    [Fact]
    public async Task VocabularyAudio_PackageFailedItemHasNoUrl()
    {
        var uow = Seed();
        uow.Seed(new StoryVocabulary { Id = 70, StoryVersionId = 10, Term = "cà rốt", Definition = "củ màu cam" });
        var service = Create(uow, tts: new FailingTermTtsProvider("cà rốt"));
        Assert.True((await service.RunPackageAsync()).Success);

        var item = Assert.Single((await service.GetPackageAsync(1, 1)).Vocabulary);
        Assert.Equal("Failed", item.Status);
        Assert.Null(item.Url);
    }

    private sealed class FailingTermTtsProvider(string failingText) : ITtsProvider
    {
        public Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default) =>
            exactSceneText == failingText
                ? throw new PermanentMediaGenerationException("TTS_TEST_FAILURE")
                : Task.FromResult(new GeneratedMedia([4, 5, 6], "audio/mpeg"));
    }
}
