using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.WishCards.Services;

public class ChildWishCardService : IChildWishCardService
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildWishCardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ChildWishCardDto>> ListAsync(
        int childProfileId, string? group, CancellationToken ct = default)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildWishCardPolicy.ProfileUnavailableMessage);
        }

        if (!WishCardRules.TryParseGroup(group, out var parsed) || !WishCardRules.IsWishGroup(parsed))
        {
            throw new BadRequestException(ChildWishCardPolicy.UnknownGroupMessage);
        }

        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is { StoryWishesEnabled: false })
        {
            throw new ForbiddenException(ChildWishCardPolicy.WishesOffMessage);
        }

        IReadOnlyList<SafetyPolicyCategory> rules = new List<SafetyPolicyCategory>();
        if (policy != null)
        {
            var policyId = policy.Id;
            rules = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
                rule => rule.SafetyPolicyId == policyId, cancellationToken: ct);
        }

        var hasAllowedList = rules.Any(rule => rule.Rule == PolicyRule.Allowed);
        // Biến cục bộ để EF dịch được phép so sánh enum nullable.
        AgeBand? age = child.AgeBand;
        var cards = (await _unitOfWork.Repository<WishCard>().FindAsync(
                card => !card.IsDeleted
                        && card.IsActive
                        && card.Group == parsed
                        && (card.AgeBand == null || card.AgeBand == age),
                cancellationToken: ct))
            .OrderBy(card => card.SortOrder).ThenBy(card => card.Id)
            .Where(card => card.ContentCategoryId.HasValue
                ? SafetyPolicyEvaluator.IsCategoryPermitted(card.ContentCategoryId.Value, rules)
                : !hasAllowedList)
            .Take(WishCardRules.MaxCardsPerWishScreen)
            .Select(card => new ChildWishCardDto
            {
                Id = card.Id,
                Group = card.Group.ToString(),
                Label = card.Label,
                ImageUrl = card.ImageUrl,
                AudioUrl = card.AudioUrl
            }).ToList();
        return cards;
    }
}
