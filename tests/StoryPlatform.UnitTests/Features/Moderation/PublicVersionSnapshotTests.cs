using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class PublicVersionSnapshotTests
{
    private readonly PublicStoryReviewHarness _h = new();

    private Task<PublicVersionSnapshot> Load(int versionId) =>
        PublicVersionSnapshot.LoadAsync(_h.UnitOfWork.Object, _h.Versions.Single(version => version.Id == versionId));

    private void GivenRichVersion()
    {
        _h.AddScene(1, 1);
        _h.AddScene(2, 2);
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId, Type = QuizType.MultipleChoice,
            Question = "Ai thắng?", CorrectAnswer = "Rùa", Choices = "[\"Rùa\",\"Thỏ\"]"
        });
        _h.Vocabulary.Add(new StoryVocabulary
        {
            Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId, Term = "kiên nhẫn", Definition = "bền bỉ"
        });
    }

    private async Task<(string Original, string Copy)> FingerprintsAfter(Action<int> change)
    {
        GivenRichVersion();
        var copy = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 51);
        change(copy.Id);
        return ((await Load(PublicStoryReviewHarness.VersionId)).Fingerprint(), (await Load(copy.Id)).Fingerprint());
    }

    [Fact]
    public void Snapshot_HasNoDiscussionPart() =>
        Assert.Empty(typeof(PublicVersionSnapshot).GetProperties()
            .Where(property => property.Name.Contains("Discussion", StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public async Task LoadAsync_ReturnsOnlyTheRequestedVersion()
    {
        GivenRichVersion();
        _h.CloneVersion(PublicStoryReviewHarness.VersionId, 51);

        var snapshot = await Load(PublicStoryReviewHarness.VersionId);

        Assert.Equal(2, snapshot.Scenes.Count);
        Assert.Equal(2, snapshot.Segments.Count);
        Assert.Equal(2, snapshot.Beats.Count);
        Assert.Single(snapshot.Quizzes);
        Assert.Single(snapshot.Vocabulary);
        Assert.All(snapshot.Assets, asset => Assert.Equal(PublicStoryReviewHarness.VersionId, asset.StoryVersionId));
    }

    [Fact]
    public async Task MediaKeys_ListReadyUrlsInOrderAndHideFailedAssetUrl()
    {
        GivenRichVersion();
        _h.Assets.Single(asset => asset.Type == MediaType.Illustration && asset.StorySceneId == 2).Status = MediaStatus.Failed;

        var snapshot = await Load(PublicStoryReviewHarness.VersionId);

        Assert.Equal("1=https://cdn.test/img1.png", snapshot.ImageKey(1));
        Assert.Equal("1=https://cdn.test/audio1.mp3", snapshot.AudioKey(1));
        Assert.Equal("1=", snapshot.ImageKey(2));
    }

    [Fact]
    public async Task Fingerprint_IdenticalClone_IsEqualAndSha256Hex()
    {
        var (original, copy) = await FingerprintsAfter(_ => { });

        Assert.Equal(original, copy);
        Assert.Equal(64, original.Length);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("scene")]
    [InlineData("image")]
    [InlineData("audio")]
    [InlineData("quiz")]
    [InlineData("vocabulary")]
    public async Task Fingerprint_AnyRelevantContentChange_Differs(string changeKind)
    {
        var (original, copy) = await FingerprintsAfter(id =>
        {
            switch (changeKind)
            {
                case "content": _h.Versions.Single(version => version.Id == id).Content += " Thêm."; break;
                case "scene": _h.Scenes.First(scene => scene.StoryVersionId == id).SceneText = "Đã sửa"; break;
                case "image": _h.Assets.First(asset => asset.StoryVersionId == id && asset.Type == MediaType.Illustration).Url = "new-image"; break;
                case "audio": _h.Assets.First(asset => asset.StoryVersionId == id && asset.Type == MediaType.TtsAudio).Url = "new-audio"; break;
                case "quiz": _h.Quizzes.Single(quiz => quiz.StoryVersionId == id).CorrectAnswer = "Thỏ"; break;
                case "vocabulary": _h.Vocabulary.Add(new StoryVocabulary { Id = 900, StoryVersionId = id, Term = "mới", Definition = "thêm" }); break;
            }
        });

        Assert.NotEqual(original, copy);
    }

    [Fact]
    public async Task LoadAsync_IncludesListeningOfThisVersionOnly_SortedByAnchor_WithoutDeletedRows()
    {
        const int version = PublicStoryReviewHarness.VersionId;
        _h.AddListening(2, version, ListeningInteractionType.RolePlay, 20, null);
        _h.AddListening(1, version, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        _h.AddListening(3, version, ListeningInteractionType.PicturePrediction, 30, "Ai chờ?", "Mẹ").IsDeleted = true;
        _h.ListeningOptions.Single(x => x.Id == 12).IsDeleted = true;
        _h.CloneVersion(version, 51);

        var snapshot = await Load(version);

        Assert.Equal(new[] { 1, 2 }, snapshot.Listening.Select(x => x.Id));
        Assert.Equal(new[] { "Sợ" }, snapshot.OptionsOf(1).Select(x => x.Text));
        Assert.Equal("Checkpoint@10", PublicVersionSnapshot.ListeningKey(snapshot.Listening[0]));
    }

    [Fact]
    public async Task Fingerprint_SameListeningInCopiedVersion_IsIdentical()
    {
        _h.AddListening(1, PublicStoryReviewHarness.VersionId, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        var (original, copy) = await FingerprintsAfter(_ => { });
        Assert.Equal(original, copy);
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("optionFeedback")]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("media")]
    public async Task Fingerprint_ChangesWhenListeningChanges(string change)
    {
        _h.AddListening(1, PublicStoryReviewHarness.VersionId, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        var (original, copy) = await FingerprintsAfter(copyId =>
        {
            var copied = _h.ListeningInteractions.Single(x => x.StoryVersionId == copyId);
            switch (change)
            {
                case "prompt": copied.Prompt = "Thỏ nghĩ gì?"; break;
                case "optionFeedback": _h.ListeningOptions.First(x => x.ListeningInteractionId == copied.Id).Feedback = "Khác"; break;
                case "added": _h.AddListening(9, copyId, ListeningInteractionType.RolePlay, 20, null); break;
                case "removed": copied.IsDeleted = true; break;
                case "media":
                    _h.Assets.Add(new MediaAsset
                    {
                        Id = 990, StoryVersionId = copyId, ListeningInteractionId = copied.Id, Type = MediaType.ListeningAudio,
                        AudioPartKey = "prompt", Status = MediaStatus.Ready, Url = "https://cdn.test/listen.mp3", IsCurrent = true
                    });
                    break;
            }
        });
        Assert.NotEqual(original, copy);
    }
}
