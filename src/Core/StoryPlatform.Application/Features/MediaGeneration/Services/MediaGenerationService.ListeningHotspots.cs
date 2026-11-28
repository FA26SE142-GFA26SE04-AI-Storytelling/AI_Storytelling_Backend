using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed partial class MediaGenerationService
{
    // Nhà cung cấp lỗi hoặc không thấy đồ vật: thử lại sau một khoảng, không gọi dồn mỗi vòng rảnh của worker.
    private static readonly TimeSpan HotspotRetryAfter = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, DateTime> HotspotAttempts = new();

    private async Task InvalidateListeningHotspotsAsync(int versionId, int retiredAssetId, CancellationToken ct)
    {
        var interactions = await _unitOfWork.Repository<ListeningInteraction>().FindAsync(x =>
            x.StoryVersionId == versionId && (x.Type == ListeningInteractionType.PictureSearch ||
                x.Type == ListeningInteractionType.TrueFalse) && !x.IsDeleted, cancellationToken: ct);
        foreach (var interaction in interactions)
        {
            if (interaction.HotspotJson is not null && HotspotBox.TryParse(interaction.HotspotJson)?.AssetId == retiredAssetId)
            {
                interaction.HotspotJson = null;
                _unitOfWork.Repository<ListeningInteraction>().Update(interaction);
            }
        }
        var ids = interactions.Select(x => x.Id).ToList();
        var options = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(x =>
            ids.Contains(x.ListeningInteractionId) && x.HotspotJson != null && !x.IsDeleted, cancellationToken: ct);
        foreach (var option in options)
            if (HotspotBox.TryParse(option.HotspotJson)?.AssetId == retiredAssetId)
            {
                option.HotspotJson = null;
                _unitOfWork.Repository<ListeningInteractionOption>().Update(option);
            }
    }

    /// <summary>
    /// Lúc rảnh: định vị bù khung đồ vật cho truyện đã có tranh nhưng chưa có khung (người duyệt vừa sửa tên đồ vật hoặc
    /// mô tả, lần định vị trước bị lỗi, hoặc truyện có từ trước khi có tính năng). Bỏ qua gói phụ huynh vì manifest đã
    /// chốt danh sách media. Mỗi mục chỉ thử lại sau <see cref="HotspotRetryAfter"/>. True nếu đã làm một truyện.
    /// </summary>
    public async Task<bool> BackfillListeningHotspotsAsync(CancellationToken ct = default)
    {
        if (_pictureObjectLocator is null) return false;
        var now = _clock.GetUtcNow().UtcDateTime;
        var searches = _unitOfWork.Repository<ListeningInteraction>().Query()
            .Where(x => !x.IsDeleted && x.ParentStoryPackageId == null && x.StorySceneId != null &&
                x.Type == ListeningInteractionType.PictureSearch && x.TargetLabel != null && x.HotspotJson == null)
            .OrderBy(x => x.Id).Take(50).Select(x => new { x.Id, x.TargetLabel, x.SourceStart, x.StoryVersionId }).ToList()
            // Khóa gồm tên đồ vật và vị trí câu trích: người duyệt sửa thì được định vị ngay, không phải chờ hết thời gian chờ.
            .Select(x => (Key: $"i{x.Id}:{x.TargetLabel}:{x.SourceStart}", x.StoryVersionId)).ToList();
        var trueFalse = _unitOfWork.Repository<ListeningInteraction>().Query()
            .Where(x => !x.IsDeleted && x.ParentStoryPackageId == null && x.StorySceneId != null &&
                x.Type == ListeningInteractionType.TrueFalse)
            .Select(x => new { x.Id, x.StoryVersionId }).ToList();
        var trueFalseIds = trueFalse.Select(x => x.Id).ToList();
        var versionOf = trueFalse.ToDictionary(x => x.Id, x => x.StoryVersionId);
        var evidence = _unitOfWork.Repository<ListeningInteractionOption>().Query()
            .Where(x => !x.IsDeleted && x.HotspotJson == null && x.ImageDescription != null &&
                trueFalseIds.Contains(x.ListeningInteractionId))
            .OrderBy(x => x.Id).Take(50).Select(x => new { x.Id, x.ImageDescription, x.ListeningInteractionId }).ToList()
            .Select(x => (Key: $"o{x.Id}:{x.ImageDescription}", StoryVersionId: versionOf[x.ListeningInteractionId])).ToList();
        var due = searches.Concat(evidence)
            .Where(x => !HotspotAttempts.TryGetValue(x.Key, out var last) || now - last >= HotspotRetryAfter).ToList();
        if (due.Count == 0) return false;
        var versionId = due[0].StoryVersionId;
        foreach (var item in due.Where(x => x.StoryVersionId == versionId)) HotspotAttempts[item.Key] = now;
        await LocateListeningHotspotsAsync(versionId, null, ct);
        return true;
    }

    private async Task LocateListeningHotspotsAsync(int versionId, int? parentPackageId, CancellationToken ct)
    {
        if (_pictureObjectLocator is null) return;
        var interactions = await _unitOfWork.Repository<ListeningInteraction>().FindAsync(x =>
            x.StoryVersionId == versionId && x.ParentStoryPackageId == parentPackageId &&
            (x.Type == ListeningInteractionType.PictureSearch || x.Type == ListeningInteractionType.TrueFalse) &&
            !x.IsDeleted, cancellationToken: ct);
        if (interactions.Count == 0) return;
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(x =>
            x.StoryVersionId == versionId && !x.IsDeleted, cancellationToken: ct);
        var sceneIds = scenes.Select(x => x.Id).ToList();
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(x =>
            sceneIds.Contains(x.StorySceneId) && !x.IsDeleted, cancellationToken: ct);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(x =>
            x.StoryVersionId == versionId && (x.ParentStoryPackageId == parentPackageId ||
                parentPackageId != null && x.ParentStoryPackageId == null) &&
            x.Type == MediaType.Illustration && x.IsCurrent && !x.IsDeleted &&
            x.Status == MediaStatus.Ready && x.ValidationStatus == ValidationStatus.Passed,
            cancellationToken: ct);
        assets = assets.OrderByDescending(x => x.ParentStoryPackageId == parentPackageId).ToArray();

        foreach (var interaction in interactions.Where(x => x.Type == ListeningInteractionType.PictureSearch))
        {
            var picture = ChildListeningContent.StoryPicture(interaction.SourceStart, scenes, beats, assets, true);
            var old = HotspotBox.TryParse(interaction.HotspotJson);
            if (picture is null || string.IsNullOrWhiteSpace(picture.Url))
            {
                if (interaction.HotspotJson is not null)
                {
                    interaction.HotspotJson = null;
                    _unitOfWork.Repository<ListeningInteraction>().Update(interaction);
                }
                continue;
            }
            if (old?.AssetId == picture.Id) continue;
            interaction.HotspotJson = null;
            if (!string.IsNullOrWhiteSpace(interaction.TargetLabel))
            {
                var box = await TryLocateAsync(picture, interaction.TargetLabel, interaction.Id, ct);
                if (box is not null && PictureSearchUnchanged(interaction)) interaction.HotspotJson = box.ToJson();
            }
            _unitOfWork.Repository<ListeningInteraction>().Update(interaction);
        }

        // Đúng/sai: tên chi tiết bằng chứng nằm ở ImageDescription của câu; định vị trên tranh của đoạn neo.
        // Không tìm thấy thì chỉ mất phần chỉ vào tranh, không chặn tương tác.
        var trueFalse = interactions.Where(x => x.Type == ListeningInteractionType.TrueFalse).ToList();
        if (trueFalse.Count > 0)
        {
            var ids = trueFalse.Select(x => x.Id).ToList();
            var options = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(x =>
                ids.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
            foreach (var option in options)
            {
                var owner = trueFalse.First(x => x.Id == option.ListeningInteractionId);
                var picture = ChildListeningContent.StoryPicture(owner.AnchorOffset - 1, scenes, beats, assets, true);
                var old = HotspotBox.TryParse(option.HotspotJson);
                if (string.IsNullOrWhiteSpace(option.ImageDescription) || picture is null || string.IsNullOrWhiteSpace(picture.Url))
                {
                    if (option.HotspotJson is not null)
                    {
                        option.HotspotJson = null;
                        _unitOfWork.Repository<ListeningInteractionOption>().Update(option);
                    }
                    continue;
                }
                if (old?.AssetId == picture.Id) continue;
                option.HotspotJson = null;
                var box = await TryLocateAsync(picture, option.ImageDescription, owner.Id, ct);
                if (box is not null && EvidenceUnchanged(option)) option.HotspotJson = box.ToJson();
                _unitOfWork.Repository<ListeningInteractionOption>().Update(option);
            }
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<HotspotBox?> TryLocateAsync(MediaAsset picture, string label, int interactionId, CancellationToken ct)
    {
        try
        {
            var image = await _mediaStorage.ReadAsync(picture.Url!, ct);
            var located = await _pictureObjectLocator!.LocateAsync(image, label, ct);
            if (located is null) return null;
            var hotspot = new HotspotBox(picture.Id, located.X, located.Y, located.W, located.H);
            return HotspotBox.IsValid(hotspot) ? hotspot : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Picture hotspot localization failed for interaction {InteractionId}", interactionId);
            return null;
        }
    }

    // Người duyệt có thể sửa trong lúc chờ nhà cung cấp trả lời: so lại với DB trước khi ghi khung.
    private bool PictureSearchUnchanged(ListeningInteraction interaction)
    {
        var now = _unitOfWork.Repository<ListeningInteraction>().Query().Where(x => x.Id == interaction.Id && !x.IsDeleted)
            .Select(x => new { x.TargetLabel, x.SourceStart }).FirstOrDefault();
        return now is not null && now.TargetLabel == interaction.TargetLabel && now.SourceStart == interaction.SourceStart;
    }

    private bool EvidenceUnchanged(ListeningInteractionOption option)
    {
        var now = _unitOfWork.Repository<ListeningInteractionOption>().Query().Where(x => x.Id == option.Id && !x.IsDeleted)
            .Select(x => x.ImageDescription).FirstOrDefault();
        return now == option.ImageDescription;
    }
}
