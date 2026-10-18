using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>Nội dung đầy đủ của một phiên bản dùng cho dấu vân tay và so sánh.</summary>
public sealed record PublicVersionSnapshot(
    StoryVersion Version,
    IReadOnlyList<StoryScene> Scenes,
    IReadOnlyList<StorySegment> Segments,
    IReadOnlyList<IllustrationBeat> Beats,
    IReadOnlyList<MediaAsset> Assets,
    IReadOnlyList<QuizItem> Quizzes,
    IReadOnlyList<StoryVocabulary> Vocabulary,
    IReadOnlyList<DiscussionQuestion> Discussions)
{
    public static async Task<PublicVersionSnapshot> LoadAsync(
        IUnitOfWork unitOfWork,
        StoryVersion version,
        CancellationToken cancellationToken = default)
    {
        var versionId = version.Id;
        var scenes = (await unitOfWork.Repository<StoryScene>().FindAsync(
                scene => scene.StoryVersionId == versionId,
                cancellationToken: cancellationToken))
            .OrderBy(scene => scene.SceneIndex).ToList();
        var sceneIds = scenes.Select(scene => scene.Id).ToList();

        var segments = await unitOfWork.Repository<StorySegment>().FindAsync(
            segment => sceneIds.Contains(segment.StorySceneId), cancellationToken: cancellationToken);
        var beats = await unitOfWork.Repository<IllustrationBeat>().FindAsync(
            beat => sceneIds.Contains(beat.StorySceneId), cancellationToken: cancellationToken);
        var assets = await unitOfWork.Repository<MediaAsset>().FindAsync(
            asset => asset.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var quizzes = (await unitOfWork.Repository<QuizItem>().FindAsync(
                quiz => quiz.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(quiz => quiz.Id).ToList();
        var vocabulary = (await unitOfWork.Repository<StoryVocabulary>().FindAsync(
                word => word.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(word => word.Id).ToList();
        var discussions = (await unitOfWork.Repository<DiscussionQuestion>().FindAsync(
                question => question.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(question => question.Id).ToList();

        return new PublicVersionSnapshot(
            version, scenes, segments, beats, assets, quizzes, vocabulary, discussions);
    }

    public string ImageKey(int sceneId) => string.Join(";", Beats
        .Where(beat => beat.StorySceneId == sceneId)
        .OrderBy(beat => beat.BeatOrder)
        .Select(beat => $"{beat.BeatOrder}={ReadyUrl(asset =>
            asset.IllustrationBeatId == beat.Id && asset.Type == MediaType.Illustration)}"));

    public string AudioKey(int sceneId) => string.Join(";", Segments
        .Where(segment => segment.StorySceneId == sceneId)
        .OrderBy(segment => segment.SegmentOrder)
        .Select(segment => $"{segment.SegmentOrder}={ReadyUrl(asset =>
            asset.StorySegmentId == segment.Id && asset.Type == MediaType.TtsAudio)}"));

    public string Fingerprint()
    {
        var rows = new List<string[]>
        {
            new[]
            {
                "version",
                Version.Title,
                Version.Content ?? string.Empty,
                Version.Lesson ?? string.Empty,
                Version.OutlineOpening ?? string.Empty,
                Version.OutlineDevelopment ?? string.Empty,
                Version.OutlineEnding ?? string.Empty
            }
        };

        foreach (var scene in Scenes)
        {
            rows.Add(new[]
            {
                "scene",
                scene.SceneIndex.ToString(),
                scene.SceneText,
                scene.VisualDescription ?? string.Empty,
                ImageKey(scene.Id),
                AudioKey(scene.Id)
            });
        }

        foreach (var quiz in Quizzes)
        {
            rows.Add(new[]
            {
                "quiz",
                quiz.Type.ToString(),
                quiz.Question,
                quiz.CorrectAnswer ?? string.Empty,
                quiz.Choices ?? string.Empty
            });
        }

        foreach (var word in Vocabulary)
        {
            rows.Add(new[] { "vocabulary", word.Term, word.Definition });
        }

        foreach (var question in Discussions)
        {
            rows.Add(new[] { "discussion", question.Question, question.IsMoralLesson.ToString() });
        }

        var json = JsonSerializer.Serialize(rows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private string ReadyUrl(Func<MediaAsset, bool> predicate) =>
        Assets.FirstOrDefault(asset => asset.Status == MediaStatus.Ready && predicate(asset))?.Url
        ?? string.Empty;
}
