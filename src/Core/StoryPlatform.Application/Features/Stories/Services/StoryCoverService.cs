using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Stories.Services;

/// <summary>
/// Chọn ảnh bìa từ chính các tranh minh họa của truyện. Bìa được lưu dạng tham chiếu <c>scene:{id}#crop=...</c>
/// (xem <see cref="StoryCover"/>) vì link ảnh ký tạm sẽ hết hạn.
/// </summary>
public sealed class StoryCoverService : IStoryCoverService
{
    private const double Tolerance = 1e-6;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard? _supervision;

    public StoryCoverService(IUnitOfWork unitOfWork, IChildProfileAccessGuard? supervision = null)
    {
        _unitOfWork = unitOfWork;
        _supervision = supervision;
    }

    public async Task SetCoverAsync(int userId, int storyId, SetStoryCoverRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        await MediaStoryAccess.AuthorizeAuthorAsync(_unitOfWork, _supervision, story, userId, cancellationToken);

        async Task ApplyAsync()
        {
            story.CoverImageUrl = request.MediaAssetId is { } assetId
                ? await BuildCoverAsync(story, assetId, request, cancellationToken)
                : null;
            _unitOfWork.Repository<Story>().Update(story);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (story.Scope != StoryScope.Creator)
        {
            await ApplyAsync();
            return;
        }

        // Truyện Creator theo quy tắc khóa chung của backend: đã công khai, đang chờ duyệt, bị từ chối, đã lưu trữ hoặc đã
        // nộp duyệt thì không sửa được (409 CREATOR_PUBLICATION_LOCKED / SUBMITTED_VERSION_IMMUTABLE_CREATE_REVISION).
        // Muốn đổi thì Creator gửi yêu cầu gỡ rồi tạo phiên bản chỉnh sửa. Ảnh bìa không phải sửa nội dung nên không xét gói đã chốt.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);
            await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, story.Id, null, cancellationToken);
            await ApplyAsync();
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string> BuildCoverAsync(Story story, int assetId, SetStoryCoverRequestDto request, CancellationToken ct)
    {
        var version = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            x => x.StoryId == story.Id && x.IsCurrent, cancellationToken: ct)).FirstOrDefault()
            ?? throw new NotFoundException("Current story version", story.Id);
        var asset = await _unitOfWork.Repository<MediaAsset>().FirstOrDefaultAsync(
            x => x.Id == assetId && x.StoryVersionId == version.Id && x.ParentStoryPackageId == null
                 && x.IsCurrent && x.Type == MediaType.Illustration && x.Status == MediaStatus.Ready
                 && x.IllustrationBeatId != null && x.Url != null,
            cancellationToken: ct)
            ?? throw new BadRequestException("Ảnh này không thuộc truyện hoặc chưa sẵn sàng để làm ảnh bìa.");

        if (request.X is null && request.Y is null && request.W is null && request.H is null)
            return StoryCover.Build(asset.Id);
        if (request.X is not { } x || request.Y is not { } y || request.W is not { } w || request.H is not { } h)
            throw new BadRequestException("Vùng hiển thị ảnh bìa cần đủ x, y, w, h.");
        if (!(w > 0 && w <= 1 && h > 0 && h <= 1 && x >= -Tolerance && y >= -Tolerance
              && x + w <= 1 + Tolerance && y + h <= 1 + Tolerance))
            throw new BadRequestException("Vùng hiển thị ảnh bìa không hợp lệ.");
        return StoryCover.Build(asset.Id, Math.Max(x, 0), Math.Max(y, 0), w, h);
    }
}
