using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Common;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AIStoryInput.Services;

public sealed partial class AIStoryInputService
{
    private async Task<StoryWish> RequireSourceWishAsync(int userId, SubmitAIStoryInputRequestDto input, CancellationToken ct)
    {
        var wish = await _unitOfWork.Repository<StoryWish>().FirstOrDefaultAsync(x => x.Id == input.SourceWishId && !x.IsDeleted,
            cancellationToken: ct) ?? throw new NotFoundException("StoryWish", input.SourceWishId!);
        if (wish.ChildProfileId != input.ChildProfileId) throw new BadRequestException("WISH_CHILD_MISMATCH");
        if (!await ChildOwnership.IsOwnerAsync(_unitOfWork, wish.ChildProfileId, userId, ct))
            throw new ForbiddenException("Bạn không phải người quản lý Child Profile này.");
        return wish;
    }

    private async Task ValidateWishReplayAsync(int userId, SubmitAIStoryInputRequestDto input, StoryGenerationRequest request, CancellationToken ct)
    {
        var wish = await RequireSourceWishAsync(userId, input, ct);
        if (wish.Status is not (StoryWishStatus.Accepted or StoryWishStatus.Fulfilled) ||
            wish.GeneratedStoryId != request.StoryId || wish.GenerationRequestId != request.Id ||
            wish.ResolvedByUserId != userId || request.SubmittedByUserId != userId ||
            request.Story?.ChildProfileId != wish.ChildProfileId)
            throw new ConflictException("WISH_GENERATION_LINK_CONFLICT");
    }

    private async Task AcceptWishAsync(int userId, StoryWish wish, Story story, StoryGenerationRequest request, CancellationToken ct)
    {
        // Database-generated IDs are needed before the FK links are written, still inside the same transaction.
        await _unitOfWork.SaveChangesAsync(ct);
        wish.Status = StoryWishStatus.Accepted;
        wish.GeneratedStoryId = story.Id;
        wish.GenerationRequestId = request.Id;
        wish.ResolvedByUserId = userId;
        wish.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<StoryWish>().Update(wish);
        if (_wishAudit is not null)
            await _wishAudit.LogAsync(userId, "StoryWishAccepted", nameof(StoryWish), wish.Id,
                new { status = StoryWishStatus.Pending.ToString() },
                new { status = wish.Status.ToString(), generatedStoryId = story.Id, requestId = request.Id }, ct);
    }
}
