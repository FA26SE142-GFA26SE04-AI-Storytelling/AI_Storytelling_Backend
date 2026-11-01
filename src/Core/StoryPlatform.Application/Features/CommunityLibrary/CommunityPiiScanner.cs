using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.CommunityLibrary;

/// <summary>Dò PII trên toàn bộ văn bản của một phiên bản truyện (dùng lúc nộp và lúc Moderator duyệt).</summary>
public static class CommunityPiiScanner
{
    public static async Task<List<PiiFinding>> ScanAsync(
        IUnitOfWork unitOfWork, Story story, StoryVersion version, string? childNickname,
        CancellationToken cancellationToken)
    {
        var snapshot = await PublicVersionSnapshot.LoadAsync(unitOfWork, version, cancellationToken);
        var parts = new List<(string Location, string? Text)>
        {
            ("title", story.Title),
            ("description", story.Description),
            ("title", version.Title),
            ("content", version.Content),
            ("lesson", version.Lesson)
        };
        parts.AddRange(snapshot.Scenes.Select(scene => ($"scene {scene.SceneIndex}", (string?)scene.SceneText)));
        parts.AddRange(snapshot.Vocabulary.Select((word, index) =>
            ($"vocabulary {index + 1}", (string?)(word.Term + " " + word.Definition))));
        parts.AddRange(snapshot.Quizzes.Select((quiz, index) =>
            ($"quiz {index + 1}", (string?)(quiz.Question + " " + quiz.CorrectAnswer + " " + quiz.Choices))));

        return PiiDetector.Scan(parts, childNickname);
    }
}
