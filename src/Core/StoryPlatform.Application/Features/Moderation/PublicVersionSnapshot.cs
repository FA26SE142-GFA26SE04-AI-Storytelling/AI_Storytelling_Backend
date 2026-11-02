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
    IReadOnlyList<ListeningInteraction> Listening,
    IReadOnlyList<ListeningInteractionOption> ListeningOptions)
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
            asset => asset.IsCurrent && asset.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var quizzes = (await unitOfWork.Repository<QuizItem>().FindAsync(
                quiz => quiz.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(quiz => quiz.Id).ToList();
        var vocabulary = (await unitOfWork.Repository<StoryVocabulary>().FindAsync(
                word => word.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(word => word.Id).ToList();
        // Bỏ các dòng đã xoá mềm khi dựng nội dung để duyệt.
        var listening = (await unitOfWork.Repository<ListeningInteraction>().FindAsync(
                interaction => interaction.StoryVersionId == versionId && !interaction.IsDeleted,
                cancellationToken: cancellationToken))
            .OrderBy(interaction => interaction.AnchorOffset).ToList();
        var listeningIds = listening.Select(interaction => interaction.Id).ToList();
        var listeningOptions = (await unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                option => listeningIds.Contains(option.ListeningInteractionId) && !option.IsDeleted,
                cancellationToken: cancellationToken))
            .OrderBy(option => option.ListeningInteractionId).ThenBy(option => option.SortOrder).ToList();
        return new PublicVersionSnapshot(
            version, scenes, segments, beats, assets, quizzes, vocabulary, listening, listeningOptions);
    }

    /// <summary>URL ảnh sẵn sàng của một scene theo thứ tự beat; bỏ qua beat chưa có ảnh.</summary>
    public IReadOnlyList<string> ImageUrls(int sceneId) => Beats
        .Where(beat => beat.StorySceneId == sceneId)
        .OrderBy(beat => beat.BeatOrder)
        .Select(beat => ReadyUrl(asset =>
            asset.IllustrationBeatId == beat.Id && asset.Type == MediaType.Illustration))
        .Where(url => url.Length > 0)
        .ToList();

    /// <summary>URL audio sẵn sàng của một scene theo thứ tự đoạn; bỏ qua đoạn chưa có audio.</summary>
    public IReadOnlyList<string> AudioUrls(int sceneId) => Segments
        .Where(segment => segment.StorySceneId == sceneId)
        .OrderBy(segment => segment.SegmentOrder)
        .Select(segment => ReadyUrl(asset =>
            asset.StorySegmentId == segment.Id && asset.Type == MediaType.TtsAudio))
        .Where(url => url.Length > 0)
        .ToList();

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

    public IReadOnlyList<ListeningInteractionOption> OptionsOf(int interactionId) => ListeningOptions
        .Where(option => option.ListeningInteractionId == interactionId)
        .OrderBy(option => option.SortOrder).ToList();

    public static string ListeningKey(ListeningInteraction interaction) => $"{interaction.Type}@{interaction.AnchorOffset}";

    public string ListeningValues(ListeningInteraction interaction) => string.Join("|", new[]
    {
        interaction.Kind?.ToString() ?? string.Empty, interaction.Prompt ?? string.Empty,
        interaction.SourceQuote ?? string.Empty, interaction.CharacterName ?? string.Empty,
        interaction.HiddenWord ?? string.Empty, interaction.Hint ?? string.Empty,
        interaction.RevealOffset?.ToString() ?? string.Empty, interaction.Explanation ?? string.Empty,
        interaction.MatchFeedback ?? string.Empty, interaction.NoMatchFeedback ?? string.Empty,
        interaction.WaitFeedback ?? string.Empty
    }.Concat(OptionsOf(interaction.Id).Select(option => string.Join("~",
        option.SortOrder, option.Text, option.Feedback ?? string.Empty, option.IsMatch,
        option.CorrectOrder?.ToString() ?? string.Empty, option.SourceQuote ?? string.Empty,
        option.ImageDescription ?? string.Empty))));

    public string ListeningMediaKey(ListeningInteraction interaction) => string.Join(";", Assets
        .Where(asset => asset.ListeningInteractionId == interaction.Id && asset.Type == MediaType.ListeningAudio
                        && asset.Status == MediaStatus.Ready)
        .OrderBy(asset => asset.AudioPartKey, StringComparer.Ordinal)
        .Select(asset => $"{asset.AudioPartKey}={asset.Url}")
        .Concat(OptionsOf(interaction.Id).Select(option => $"{option.SortOrder}={ListeningImageUrl(option.Id)}")));

    public string? ListeningImageUrl(int optionId) => ReadyUrl(asset =>
        asset.ListeningInteractionOptionId == optionId && asset.Type == MediaType.ListeningImage) is { Length: > 0 } url ? url : null;

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
            if (new[] { quiz.SourceQuote, quiz.Explanation, quiz.Hint, quiz.LearningGoal, quiz.OptionFeedbackJson }.Any(x => !string.IsNullOrEmpty(x)))
                rows.Add(new[] { "quiz-learning", quiz.Question, quiz.SourceQuote ?? "", quiz.Explanation ?? "", quiz.Hint ?? "", quiz.LearningGoal ?? "", quiz.OptionFeedbackJson ?? "" });
        }

        foreach (var word in Vocabulary)
        {
            rows.Add(new[] { "vocabulary", word.Term, word.Definition });
            if (!string.IsNullOrEmpty(word.SourceQuote) || !string.IsNullOrEmpty(word.Example))
                rows.Add(new[] { "vocabulary-learning", word.Term, word.SourceQuote ?? "", word.Example ?? "" });
        }

        foreach (var interaction in Listening)
            rows.Add(new[] { "listening", ListeningKey(interaction), ListeningValues(interaction), ListeningMediaKey(interaction) });

        var json = JsonSerializer.Serialize(rows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private string ReadyUrl(Func<MediaAsset, bool> predicate) =>
        Assets.FirstOrDefault(asset => asset.Status == MediaStatus.Ready && predicate(asset))?.Url
        ?? string.Empty;
}
