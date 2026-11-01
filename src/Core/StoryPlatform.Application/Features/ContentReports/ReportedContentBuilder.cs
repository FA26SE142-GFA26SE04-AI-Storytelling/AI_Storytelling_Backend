using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentReports;

/// <summary>Nguồn dùng chung để dựng nội dung truyện trả cho nhân sự kiểm duyệt.</summary>
internal static class ReportedContentBuilder
{
    public static ReportedStoryDto BuildStory(
        Story story, List<ReportedLabelDto> labels, Func<string?, string?> mask) => new()
        {
            StoryId = story.Id,
            Title = mask(story.Title),
            Description = mask(story.Description),
            Genre = story.Genre,
            AgeBand = story.AgeBand,
            ReadingLevel = story.ReadingLevel,
            Language = story.Language,
            Scope = story.Scope.ToString(),
            IsArchived = story.Status == StoryStatus.Archived || story.ArchivedAt != null,
            Labels = labels
        };

    public static ReportedVersionDto BuildVersion(StoryVersion version, Func<string?, string?> mask) => new()
    {
        VersionId = version.Id,
        VersionNo = version.VersionNo,
        Title = mask(version.Title) ?? string.Empty,
        Content = mask(version.Content),
        Lesson = mask(version.Lesson)
    };

    public static List<ReportedSceneDto> BuildScenes(
        PublicVersionSnapshot snapshot, Func<string?, string?> mask) =>
        snapshot.Scenes.Select(scene => new ReportedSceneDto
        {
            SceneIndex = scene.SceneIndex,
            Text = mask(scene.SceneText) ?? string.Empty,
            ImageUrls = snapshot.ImageUrls(scene.Id).ToList(),
            AudioUrls = snapshot.AudioUrls(scene.Id).ToList()
        }).ToList();

    public static List<ReportedVocabularyDto> BuildVocabulary(
        PublicVersionSnapshot snapshot, Func<string?, string?> mask) =>
        snapshot.Vocabulary.Select(word => new ReportedVocabularyDto
        {
            Term = mask(word.Term) ?? string.Empty,
            Definition = mask(word.Definition) ?? string.Empty
        }).ToList();

    public static List<ReportedQuizDto> BuildQuizzes(
        PublicVersionSnapshot snapshot, Func<string?, string?> mask) =>
        snapshot.Quizzes.Select(quiz => new ReportedQuizDto
        {
            Type = quiz.Type.ToString(),
            Question = mask(quiz.Question) ?? string.Empty,
            CorrectAnswer = mask(quiz.CorrectAnswer),
            Choices = mask(quiz.Choices)
        }).ToList();

    public static async Task<List<ReportedLabelDto>> LoadLabelsAsync(
        IUnitOfWork unitOfWork, int storyId, CancellationToken cancellationToken)
    {
        var links = await unitOfWork.Repository<StoryCategory>().FindAsync(
            link => link.StoryId == storyId, cancellationToken: cancellationToken);
        var categoryIds = links.Select(link => link.ContentCategoryId).ToList();
        var categories = await unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);

        return categories.OrderBy(category => category.Id)
            .Select(category => new ReportedLabelDto
            {
                Code = category.Code,
                DisplayName = category.DisplayName
            })
            .ToList();
    }

    /// <summary>Truyện riêng dùng bản IsCurrent; Public Library dùng bản Approved mới nhất.</summary>
    public static async Task<StoryVersion?> FindDisplayedVersionAsync(
        IUnitOfWork unitOfWork, Story story, CancellationToken cancellationToken)
    {
        var versions = await unitOfWork.Repository<StoryVersion>().FindAsync(
            version => version.StoryId == story.Id, cancellationToken: cancellationToken);

        if (story.Scope == StoryScope.Parent)
        {
            return versions.FirstOrDefault(version => version.IsCurrent);
        }

        return versions
            .Where(version => version.Moderation.ReviewStatus == VersionReviewStatus.Approved)
            .OrderByDescending(version => version.Moderation.ReviewedAt)
            .ThenByDescending(version => version.VersionNo)
            .FirstOrDefault();
    }
}
