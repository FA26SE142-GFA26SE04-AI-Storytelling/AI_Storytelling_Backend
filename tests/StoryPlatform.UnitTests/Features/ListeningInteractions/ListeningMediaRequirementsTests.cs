using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningMediaRequirementsTests
{
    private static readonly ListeningInteraction Picture = new()
    {
        Id = 5, Type = ListeningInteractionType.PicturePrediction, Prompt = "Ai đến?", WaitFeedback = "Nghe tiếp nhé!"
    };
    private static readonly IReadOnlyList<ListeningInteractionOption> Options =
    [
        new() { Id = 50, ListeningInteractionId = 5, SortOrder = 1, Text = "Bác Gấu", Feedback = "Đúng là bác Gấu." },
        new() { Id = 51, ListeningInteractionId = 5, SortOrder = 2, Text = "Chim Sẻ", IsDeleted = true }
    ];
    private static MediaAsset Audio(int? optionId, string key) => new()
    {
        Type = MediaType.ListeningAudio, ListeningInteractionId = 5, ListeningInteractionOptionId = optionId,
        AudioPartKey = key, Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed, Url = $"{optionId}-{key}.mp3"
    };
    private static MediaAsset Image(ValidationStatus validation = ValidationStatus.Passed) => new()
    {
        Type = MediaType.ListeningImage, ListeningInteractionId = 5, ListeningInteractionOptionId = 50,
        Status = MediaStatus.Ready, ValidationStatus = validation, Url = "50.png"
    };
    private static List<MediaAsset> Complete() =>
        [Audio(null, "prompt"), Audio(null, "wait"), Audio(50, "text"), Audio(50, "feedback"), Image()];

    [Fact]
    public void CompleteMedia_HasNothingMissing() =>
        Assert.Empty(ListeningMediaRequirements.Missing(Picture, Options, Complete()));

    [Fact]
    public void UnusableAudio_IsMissing()
    {
        var assets = Complete();
        assets[0].Status = MediaStatus.ManualReview;
        assets[1].IsCurrent = false;
        assets[2].Url = null;
        Assert.Equal(["5:i:prompt", "5:i:wait", "5:o50:text"],
            ListeningMediaRequirements.Missing(Picture, Options, assets));
    }

    [Fact]
    public void ImageMustHavePassedValidation()
    {
        var assets = Complete();
        assets[4] = Image(ValidationStatus.Failed);
        Assert.Equal(["5:o50:image"], ListeningMediaRequirements.Missing(Picture, Options, assets));
        Assert.Null(ListeningMediaRequirements.FindImage(assets, 50));
    }

    [Fact]
    public void FindAudio_MatchesOwnerAndPart()
    {
        var assets = Complete();
        Assert.Equal("-prompt.mp3", ListeningMediaRequirements.FindAudio(assets, 5, null, "prompt")!.Url);
        Assert.Equal("50-feedback.mp3", ListeningMediaRequirements.FindAudio(assets, 5, 50, "feedback")!.Url);
        Assert.Null(ListeningMediaRequirements.FindAudio(assets, 6, null, "prompt"));
    }

    [Fact]
    public void AudioMustHavePassedValidation()
    {
        var assets = Complete();
        assets[0].ValidationStatus = ValidationStatus.Pending;
        Assert.Equal(["5:i:prompt"], ListeningMediaRequirements.Missing(Picture, Options, assets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionUsesNewestValidAssetWithIdTieBreaker(bool image)
    {
        var older = image ? Image() : Audio(null, "prompt");
        older.Id = 1; older.CompletedAt = DateTime.UtcNow.AddMinutes(-1);
        var newer = image ? Image() : Audio(null, "prompt");
        newer.Id = 2; newer.CompletedAt = DateTime.UtcNow;
        var tied = image ? Image() : Audio(null, "prompt");
        tied.Id = 3; tied.CompletedAt = newer.CompletedAt;
        var invalid = image ? Image() : Audio(null, "prompt");
        invalid.Id = 4; invalid.CompletedAt = newer.CompletedAt.Value.AddMinutes(1); invalid.ValidationStatus = ValidationStatus.Failed;
        MediaAsset[] assets = [invalid, older, newer, tied];
        var selected = image ? ListeningMediaRequirements.FindImage(assets, 50)
            : ListeningMediaRequirements.FindAudio(assets, 5, null, "prompt");
        Assert.Same(tied, selected);
    }
}
