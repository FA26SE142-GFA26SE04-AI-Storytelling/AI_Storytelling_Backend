using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
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
    private readonly IAuditLogWriter _auditLogWriter;

    public SafetyPolicyService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<SafetyPolicyDto> SetSafetyPolicyAsync(
        int childProfileId, int currentUserId, SetSafetyPolicyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsurePermissionAsync(
            childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);
        var relationship = await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);
        var isOwner = relationship.SupervisorRole == SupervisorRole.Owner;
        ValidateRequest(request);

        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        var needsConsent = policy == null || !policy.ConsentRecorded;
        if (needsConsent)
        {
            if (!isOwner)
            {
                throw new ForbiddenException(
                    "Chỉ Owner mới được ghi nhận đồng thuận (consent) cho hồ sơ trẻ.");
            }

            if (!request.ConsentRecorded)
            {
                throw new BadRequestException(
                    "Phải xác nhận đồng thuận (consent) trước khi lưu quy tắc an toàn.");
            }
        }

        var categoryRepo = _unitOfWork.Repository<SafetyPolicyCategory>();
        object? beforeState = null;
        if (policy == null)
        {
            policy = new SafetyPolicy { ChildProfileId = childProfileId };
            ApplyRequest(policy, request);
            await policyRepo.AddAsync(policy, cancellationToken);
        }
        else
        {
            var existingCategories = await categoryRepo.FindAsync(
                value => value.SafetyPolicyId == policy.Id,
                cancellationToken: cancellationToken);

            beforeState = Snapshot(policy, existingCategories.Select(ToCategoryDto));

            ApplyRequest(policy, request);
            policy.UpdatedAt = DateTime.UtcNow;
            policyRepo.Update(policy);

            if (existingCategories.Count > 0)
            {
                categoryRepo.DeleteRange(existingCategories);
            }
        }

        if (needsConsent)
        {
            RecordConsent(policy, currentUserId);
        }

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

        var categoryDtos = request.Categories.Select(value => new SafetyPolicyCategoryDto
        {
            ContentCategoryId = value.ContentCategoryId,
            Rule = value.Rule.ToString()
        }).ToList();

        await _auditLogWriter.LogAsync(
            currentUserId, "SET_SAFETY_POLICY", nameof(SafetyPolicy), policy.Id,
            beforeState, Snapshot(policy, categoryDtos), cancellationToken);
        if (needsConsent)
        {
            await _auditLogWriter.LogAsync(
                currentUserId, "RECORD_SAFETY_CONSENT", nameof(SafetyPolicy), policy.Id,
                null,
                new { policy.ChildProfileId, policy.ConsentPolicyVersion, policy.ConsentedByUserId },
                cancellationToken);
        }

        return MapToDto(policy, categoryDtos);
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

        return MapToDto(policy, categories.Select(ToCategoryDto).ToList());
    }

    public async Task DeleteSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsurePermissionAsync(childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);

        var profile = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                      ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        if (profile.Status == ChildProfileStatus.Active)
        {
            throw new BadRequestException(
                "Không thể xóa Safety Policy của hồ sơ đang Active — trẻ đang dùng quy tắc này.");
        }

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

        await _auditLogWriter.LogAsync(
            currentUserId, "DELETE_SAFETY_POLICY", nameof(SafetyPolicy), policy.Id,
            Snapshot(policy, categories.Select(ToCategoryDto)), null, cancellationToken);
    }

    private static void RecordConsent(SafetyPolicy policy, int consentedByUserId)
    {
        policy.ConsentRecorded = true;
        policy.ConsentRecordedAt = DateTime.UtcNow;
        policy.ConsentPolicyVersion = ConsentPolicy.CurrentVersion;
        policy.ConsentedByUserId = consentedByUserId;
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
    }

    private static SafetyPolicyCategoryDto ToCategoryDto(SafetyPolicyCategory category) => new()
    {
        ContentCategoryId = category.ContentCategoryId,
        Rule = category.Rule.ToString()
    };

    private static object Snapshot(SafetyPolicy policy, IEnumerable<SafetyPolicyCategoryDto> categories) => new
    {
        policy.MaxStoryLength,
        RequiredApprovalMode = policy.RequiredApprovalMode.ToString(),
        policy.ParentalGateEnabled,
        policy.SafetyScoreThreshold,
        policy.ReadabilityScoreThreshold,
        policy.ComprehensionThresholdPercent,
        policy.ComprehensionWindowSize,
        Categories = categories.Select(c => new { c.ContentCategoryId, c.Rule }).ToList()
    };

    private static SafetyPolicyDto MapToDto(SafetyPolicy policy, List<SafetyPolicyCategoryDto> categories) => new()
    {
        Id = policy.Id,
        ChildProfileId = policy.ChildProfileId,
        MaxStoryLength = policy.MaxStoryLength,
        RequiredApprovalMode = policy.RequiredApprovalMode.ToString(),
        ParentalGateEnabled = policy.ParentalGateEnabled,
        ConsentRecorded = policy.ConsentRecorded,
        ConsentRecordedAt = policy.ConsentRecordedAt,
        ConsentPolicyVersion = policy.ConsentPolicyVersion,
        ConsentedByUserId = policy.ConsentedByUserId,
        SafetyScoreThreshold = policy.SafetyScoreThreshold,
        ReadabilityScoreThreshold = policy.ReadabilityScoreThreshold,
        ComprehensionThresholdPercent = policy.ComprehensionThresholdPercent,
        ComprehensionWindowSize = policy.ComprehensionWindowSize,
        Categories = categories
    };

    private static void ValidateRequest(SetSafetyPolicyRequestDto request)
    {
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
