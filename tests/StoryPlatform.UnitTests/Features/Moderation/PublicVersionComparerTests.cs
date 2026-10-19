using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class PublicVersionComparerTests
{
    private readonly PublicStoryReviewHarness _h = new();

    private async Task<PublicVersionComparisonDto> CompareAsync(Action<StoryVersion>? change = null)
    {
        _h.AddScene(1, 1);
        _h.AddScene(2, 2);
        _h.Quizzes.Add(new QuizItem { Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId, Type = QuizType.TrueFalse, Question = "Rùa thắng?", CorrectAnswer = "Đúng" });
        _h.Vocabulary.Add(new StoryVocabulary { Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId, Term = "kiên nhẫn", Definition = "bền bỉ" });
        _h.Discussions.Add(new DiscussionQuestion { Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId, Question = "Vì sao rùa thắng?" });
        var current = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 51);
        change?.Invoke(current);
        var before = await PublicVersionSnapshot.LoadAsync(_h.UnitOfWork.Object, _h.Versions.Single(version => version.Id == PublicStoryReviewHarness.VersionId));
        var after = await PublicVersionSnapshot.LoadAsync(_h.UnitOfWork.Object, current);
        return PublicVersionComparer.Compare(before, after);
    }

    [Fact]
    public async Task Compare_IdenticalVersions_ReportsNoChange()
    {
        var result = await CompareAsync();

        Assert.True(result.IsIdentical);
        Assert.All(result.TextDiff, entry => Assert.Equal("unchanged", entry.Type));
        Assert.Empty(result.ChangedFields);
        Assert.Empty(result.SceneChanges);
        Assert.Empty(result.ArtifactChanges);
    }

    [Fact]
    public async Task Compare_ContentAndFields_ReturnsDiffAndMetrics()
    {
        var result = await CompareAsync(version =>
        {
            version.Content = "Mở đầu. Giữa truyện đã đổi. Kết thúc. Câu mới thêm.";
            version.Title = "Tên mới";
            version.OutlineEnding = "Kết mới";
        });

        Assert.False(result.IsIdentical);
        Assert.Equal(new[] { "unchanged", "modified", "unchanged", "added" }, result.TextDiff.Select(entry => entry.Type));
        Assert.Equal(new[] { "title", "outlineEnding" }, result.ChangedFields);
        Assert.Equal(3, result.BaseMetrics.SentenceCount);
        Assert.Equal(4, result.CurrentMetrics.SentenceCount);
        Assert.True(result.CurrentMetrics.WordCount > result.BaseMetrics.WordCount);
    }

    [Fact]
    public async Task Compare_SceneTextImageAndAudioChanges_AreReportedPerScene()
    {
        var result = await CompareAsync(version =>
        {
            _h.Scenes.First(scene => scene.StoryVersionId == version.Id && scene.SceneIndex == 1).SceneText = "Đã sửa";
            var sceneId = 2 + version.Id * 100000;
            _h.Assets.First(asset => asset.StoryVersionId == version.Id && asset.Type == MediaType.Illustration && asset.StorySceneId == sceneId).Url = "redrawn";
            _h.Assets.First(asset => asset.StoryVersionId == version.Id && asset.Type == MediaType.TtsAudio && asset.StorySceneId == sceneId).Url = "redone";
        });

        var first = result.SceneChanges.Single(scene => scene.SceneIndex == 1);
        Assert.True(first.TextChanged);
        Assert.False(first.ImageChanged);
        var second = result.SceneChanges.Single(scene => scene.SceneIndex == 2);
        Assert.True(second.ImageChanged);
        Assert.True(second.AudioChanged);
    }

    [Fact]
    public async Task Compare_ArtifactChanges_AreReportedByKind()
    {
        var result = await CompareAsync(version =>
        {
            _h.Quizzes.Single(quiz => quiz.StoryVersionId == version.Id).CorrectAnswer = "Sai";
            _h.Vocabulary.RemoveAll(word => word.StoryVersionId == version.Id);
            _h.Vocabulary.Add(new StoryVocabulary { Id = 801, StoryVersionId = version.Id, Term = "mới", Definition = "vừa thêm" });
            _h.Discussions.RemoveAll(question => question.StoryVersionId == version.Id);
        });

        Assert.Contains(result.ArtifactChanges, change => change.Kind == "quiz" && change.Change == "modified");
        Assert.Contains(result.ArtifactChanges, change => change.Kind == "vocabulary" && change.Change == "removed");
        Assert.Contains(result.ArtifactChanges, change => change.Kind == "vocabulary" && change.Change == "added");
        Assert.Contains(result.ArtifactChanges, change => change.Kind == "discussion" && change.Change == "removed");
    }
}
