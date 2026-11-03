using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed partial class StoryReviewService
{
    public async Task<ConfirmStoryPackageResponseDto> ConfirmPackageAsync(int userId, int storyId,
        ConfirmStoryPackageRequestDto input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        await AuthorizeCreatorOperationAsync(userId, storyId, cancellationToken);
        var story = await _storyRepo.FirstOrDefaultAsync(x => x.Id == storyId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Story", storyId);
        await EnsureReviewPermissionAsync(story, userId, cancellationToken);
        await QueueReviewedMediaAsync(userId, storyId, story.Scope == StoryScope.Creator, input.VersionId, cancellationToken, input);
        var receipt = await FindPackageReceiptAsync(userId, storyId, input, cancellationToken)
            ?? throw new ConflictException("PACKAGE_CONFIRMATION_RECEIPT_MISSING");
        return JsonSerializer.Deserialize<ConfirmStoryPackageResponseDto>(receipt.OperationResponseJson, ProposalJsonOptions)
            ?? throw new ConflictException("PACKAGE_CONFIRMATION_RECEIPT_INVALID");
    }

    private async Task<StoryAuthoringOperation?> FindPackageReceiptAsync(int userId, int storyId,
        ConfirmStoryPackageRequestDto input, CancellationToken ct)
    {
        var receipt = await _unitOfWork.Repository<StoryAuthoringOperation>().FirstOrDefaultAsync(
            x => x.StoryId == storyId && x.OperationId == input.OperationId.Trim(), cancellationToken: ct);
        if (receipt is not null && (receipt.Kind != "package-confirm" || receipt.ActorUserId != userId ||
            receipt.StoryVersionId != input.VersionId || receipt.RequestHash != PackageHash(storyId, input.VersionId)))
            throw new ConflictException("PACKAGE_OPERATION_KEY_CONFLICT");
        return receipt;
    }

    private static string PackageHash(int storyId, int versionId) => ReadabilityEvaluationEvidence.Hash($"package-confirm:{storyId}:{versionId}");
}
