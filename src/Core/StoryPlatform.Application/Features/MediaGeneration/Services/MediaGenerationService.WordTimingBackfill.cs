using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    // Audio Gemini đã tạo trước khi có tính năng căn mốc: nghe lại bằng STT rồi bù mốc, không tạo lại audio.
    private const string GeminiTtsProviderName = "GeminiTTS";
    private const double WordTimingBackfillMinCoverage = 0.8;
    // STT đồng bộ nhận tối đa ~1 phút; WAV 24 kHz 16-bit mono ≈ 58 giây.
    private const int WordTimingBackfillMaxBytes = 2_800_000;
    private static readonly TimeSpan WordTimingRetryAfter = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, DateTime> WordTimingAttempts = new();

    /// <summary>
    /// Lúc rảnh: bù mốc từng chữ cho audio Gemini đã tạo (lời kể và các câu bé nhìn chữ theo giọng đọc) còn thiếu mốc.
    /// Chỉ chạy khi đã cấu hình căn mốc bằng STT. Bỏ qua gói phụ huynh vì manifest đã chốt media. Mỗi audio chỉ thử lại
    /// sau <see cref="WordTimingRetryAfter"/>. Mỗi vòng một audio; True nếu đã xử lý một audio.
    /// </summary>
    public async Task<bool> BackfillWordTimingsAsync(CancellationToken ct = default)
    {
        if (_audioWordAligner is null) return false;
        var now = _clock.GetUtcNow().UtcDateTime;
        var interactions = _unitOfWork.Repository<ListeningInteraction>().Query();
        var candidates = _unitOfWork.Repository<MediaAsset>().Query()
            .Where(a => !a.IsDeleted && a.IsCurrent && a.ParentStoryPackageId == null && a.Status == MediaStatus.Ready &&
                a.Url != null && (a.WordTimings == null || a.WordTimings == "") && a.Provider == GeminiTtsProviderName &&
                ((a.Type == MediaType.TtsAudio && a.StorySegmentId != null) ||
                 (a.Type == MediaType.ListeningAudio && a.ListeningInteractionId != null && a.ListeningInteractionOptionId == null &&
                  (a.AudioPartKey == ListeningAudioParts.Text || a.AudioPartKey == ListeningAudioParts.Name ||
                   a.AudioPartKey == ListeningAudioParts.Prompt || a.AudioPartKey == ListeningAudioParts.Sentence ||
                   (a.AudioPartKey != null && a.AudioPartKey.StartsWith("sentence:")))) ||
                 // Lượt thoại của Đọc luân phiên: audio mức option, bé nhìn chữ sáng theo giọng đọc.
                 (a.Type == MediaType.ListeningAudio && a.ListeningInteractionOptionId != null && a.AudioPartKey == ListeningAudioParts.Text &&
                  interactions.Any(i => i.Id == a.ListeningInteractionId && i.Type == ListeningInteractionType.AlternateReading))))
            .OrderBy(a => a.Id).Take(100).ToList();
        var asset = candidates.FirstOrDefault(a =>
            !WordTimingAttempts.TryGetValue($"w{a.Id}:{a.Url}", out var last) || now - last >= WordTimingRetryAfter);
        if (asset is null) return false;
        WordTimingAttempts[$"w{asset.Id}:{asset.Url}"] = now;

        try
        {
            var text = ExactTextOfAudio(asset);
            if (string.IsNullOrWhiteSpace(text)) return true;
            var tokens = SsmlTokenizer.Tokenize(text);
            var audio = await _mediaStorage.ReadAsync(asset.Url!, ct);
            if (audio.Content.Length == 0 || audio.Content.Length > WordTimingBackfillMaxBytes) return true;

            var heard = await _audioWordAligner.RecognizeWordsAsync(audio.Content, ct);
            var json = WordTimingAligner.BuildWordTimingsJson(tokens, heard, WordTimingBackfillMinCoverage);
            if (json is null) return true;

            // Audio có thể vừa được tạo lại trong lúc chờ STT: chỉ ghi khi vẫn là đúng audio đã nghe.
            var current = _unitOfWork.Repository<MediaAsset>().Query()
                .Where(a => a.Id == asset.Id && !a.IsDeleted).Select(a => new { a.Url, a.WordTimings }).FirstOrDefault();
            if (current is null || current.Url != asset.Url || !string.IsNullOrEmpty(current.WordTimings)) return true;
            asset.WordTimings = json;
            _unitOfWork.Repository<MediaAsset>().Update(asset);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Word timings backfilled Asset={AssetId} Type={Type} Part={Part}", asset.Id, asset.Type, asset.AudioPartKey);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogWarning("Word timing backfill failed Asset={AssetId} Error={Error}", asset.Id, exception.GetType().Name);
        }
        return true;
    }

    /// <summary>Đúng văn bản đã dùng để tạo audio này; null nếu không xác định được (nguồn đã đổi hoặc bị xóa).</summary>
    private string? ExactTextOfAudio(MediaAsset asset)
    {
        if (asset.Type == MediaType.TtsAudio)
            return _unitOfWork.Repository<StorySegment>().Query()
                .Where(x => x.Id == asset.StorySegmentId && !x.IsDeleted).Select(x => x.TextContent).FirstOrDefault();

        var interaction = _unitOfWork.Repository<ListeningInteraction>().Query()
            .FirstOrDefault(x => x.Id == asset.ListeningInteractionId && !x.IsDeleted);
        if (interaction is null) return null;
        var options = _unitOfWork.Repository<ListeningInteractionOption>().Query()
            .Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted).ToList();
        return ListeningAudioParts.For(interaction, options)
            .FirstOrDefault(p => p.OptionId == asset.ListeningInteractionOptionId && p.PartKey == asset.AudioPartKey)?.Text;
    }
}
