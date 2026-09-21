using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Services;

public class SafetyPolicyService : ISafetyPolicyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;

    public SafetyPolicyService(IUnitOfWork unitOfWork, ISupervisionAccessGuard accessGuard)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
    }

    public async Task<SafetyPolicyDto> SetSafetyPolicyAsync(
        int childProfileId, int currentUserId, SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsurePermissionAsync(
            childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);
        ValidateRequest(request);

        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        if (policy == null)
        {
            policy = new SafetyPolicy { ChildProfileId = childProfileId };
            ApplyRequest(policy, request);
            await policyRepo.AddAsync(policy, cancellationToken);
        }
        else
        {
            ApplyRequest(policy, request);
            policy.UpdatedAt = DateTime.UtcNow;
            policyRepo.Update(policy);

            var existingCategories = await _unitOfWork.Repository<SafetyPolicyCategory>()
                .FindAsync(
                    value => value.SafetyPolicyId == policy.Id,
                    cancellationToken: cancellationToken);
            if (existingCategories.Count > 0)
            {
                _unitOfWork.Repository<SafetyPolicyCategory>().DeleteRange(existingCategories);
            }
        }

        var categoryRepo = _unitOfWork.Repository<SafetyPolicyCategory>();
        foreach (var category in request.Categories)
        {
            await categoryRepo.AddAsync(new SafetyPolicyCategory
            {
                SafetyPolicy = policy,
                ContentCategoryId = category.ContentCategoryId,
                Rule = category.Rule
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SafetyPolicyDto
        {
            Id = policy.Id,
            ChildProfileId = policy.ChildProfileId,
            MaxStoryLength = policy.MaxStoryLength,
            RequiredApprovalMode = policy.RequiredApprovalMode.ToString(),
            ParentalGateEnabled = policy.ParentalGateEnabled,
            ConsentRecorded = policy.ConsentRecorded,
            ConsentRecordedAt = policy.ConsentRecordedAt,
            ConsentPolicyVersion = policy.ConsentPolicyVersion,
            SafetyScoreThreshold = policy.SafetyScoreThreshold,
            ReadabilityScoreThreshold = policy.ReadabilityScoreThreshold,
            ComprehensionThresholdPercent = policy.ComprehensionThresholdPercent,
            ComprehensionWindowSize = policy.ComprehensionWindowSize,
            Categories = request.Categories.Select(value => new SafetyPolicyCategoryDto
            {
                ContentCategoryId = value.ContentCategoryId,
                Rule = value.Rule.ToString()
            }).ToList()
        };
    }

    public async Task<SafetyPolicyDto> GetSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (policy == null)
        {
            throw new NotFoundException("Safety Policy của hồ sơ trẻ", childProfileId);
        }

        var categories = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
            value => value.SafetyPolicyId == policy.Id, cancellationToken: cancellationToken);

        return new SafetyPolicyDto
        {
            Id = policy.Id,
            ChildProfileId = policy.ChildProfileId,
            MaxStoryLength = policy.MaxStoryLength,
            RequiredApprovalMode = policy.RequiredApprovalMode.ToString(),
            ParentalGateEnabled = policy.ParentalGateEnabled,
            ConsentRecorded = policy.ConsentRecorded,
            ConsentRecordedAt = policy.ConsentRecordedAt,
            ConsentPolicyVersion = policy.ConsentPolicyVersion,
            SafetyScoreThreshold = policy.SafetyScoreThreshold,
            ReadabilityScoreThreshold = policy.ReadabilityScoreThreshold,
            ComprehensionThresholdPercent = policy.ComprehensionThresholdPercent,
            ComprehensionWindowSize = policy.ComprehensionWindowSize,
            Categories = categories.Select(c => new SafetyPolicyCategoryDto { ContentCategoryId = c.ContentCategoryId, Rule = c.Rule.ToString() }).ToList()
        };
    }

    public async Task DeleteSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsurePermissionAsync(childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);

        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (policy == null)
        {
            throw new NotFoundException("Safety Policy của hồ sơ trẻ", childProfileId);
        }

        var categoryRepo = _unitOfWork.Repository<SafetyPolicyCategory>();
        var categories = await categoryRepo.FindAsync(
            value => value.SafetyPolicyId == policy.Id, cancellationToken: cancellationToken);
        if (categories.Count > 0)
        {
            categoryRepo.DeleteRange(categories);
        }

        policyRepo.Delete(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyRequest(SafetyPolicy policy, SetSafetyPolicyRequestDto request)
    {
        policy.MaxStoryLength = request.MaxStoryLength;
        policy.RequiredApprovalMode = request.RequiredApprovalMode;
        policy.ParentalGateEnabled = request.ParentalGateEnabled;
        policy.ReadabilityScoreThreshold = request.ReadabilityScoreThreshold;
        policy.SafetyScoreThreshold = request.SafetyScoreThreshold;
        policy.ComprehensionThresholdPercent = request.ComprehensionThresholdPercent;
        policy.ComprehensionWindowSize = request.ComprehensionWindowSize;
        policy.ConsentRecorded = true;
        policy.ConsentRecordedAt = DateTime.UtcNow;
        policy.ConsentPolicyVersion = request.ConsentPolicyVersion;
    }

    private static void ValidateRequest(SetSafetyPolicyRequestDto request)
    {
        if (!request.ConsentRecorded)
        {
            throw new BadRequestException(
                "Phải xác nhận đồng thuận (consent) trước khi lưu quy tắc an toàn.");
        }

        if (request.MaxStoryLength is < 100 or > 20000)
        {
            throw new BadRequestException(
                "Độ dài truyện tối đa phải từ 100 đến 20000 ký tự.");
        }

        if (!Enum.IsDefined(request.RequiredApprovalMode))
        {
            throw new BadRequestException("Chế độ phê duyệt không hợp lệ.");
        }

        if (request.ReadabilityScoreThreshold is < 0m or > 100m)
        {
            throw new BadRequestException("Ngưỡng readability phải nằm trong khoảng 0 đến 100.");
        }

        if (request.SafetyScoreThreshold is < 0m or > 100m)
        {
            throw new BadRequestException("Ngưỡng safety phải nằm trong khoảng 0 đến 100.");
        }

        if (request.ComprehensionThresholdPercent is < 0m or > 100m || request.ComprehensionWindowSize is < 1 or > 20)
        {
            throw new BadRequestException("Cấu hình comprehension không hợp lệ.");
        }

        if (request.ConsentPolicyVersion <= 0)
        {
            throw new BadRequestException("Consent policy version phải lớn hơn 0.");
        }

        request.Categories ??= new List<SetSafetyPolicyCategoryRequestDto>();
        if (request.Categories.Any(value => value.ContentCategoryId <= 0
                                            || !Enum.IsDefined(value.Rule)))
        {
            throw new BadRequestException("Quy tắc danh mục nội dung không hợp lệ.");
        }

        if (request.Categories.GroupBy(value => value.ContentCategoryId).Any(group => group.Count() > 1))
        {
            throw new BadRequestException("Mỗi Content Category chỉ được khai báo một lần.");
        }
    }
}
