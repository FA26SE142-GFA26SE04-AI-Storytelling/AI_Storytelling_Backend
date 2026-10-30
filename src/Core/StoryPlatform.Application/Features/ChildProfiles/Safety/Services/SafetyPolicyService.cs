using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Services;

public class SafetyPolicyService : ISafetyPolicyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _accessGuard;
    private readonly IAuditLogWriter _auditLogWriter;

    public SafetyPolicyService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard accessGuard,
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
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);
        var isOwner = true;
        ValidateRequest(request);

        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        var needsConsent = policy == null || !policy.ConsentRecorded;
        if (needsConsent)
        {
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
            // Hồ sơ mới mặc định nhận danh sách Blocked theo age_band, trừ khi phụ huynh tắt preset.
            var applyPreset = request.ApplyAgePreset ?? request.Categories.Count == 0;
            if (applyPreset)
            {
                var child = await _unitOfWork.Repository<ChildProfile>()
                    .GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
                var presetIds = await GetBlockedPresetIdsAsync(child.AgeBand, cancellationToken);
                foreach (var presetId in presetIds
                             .Where(id => request.Categories.All(category => category.ContentCategoryId != id)))
                {
                    request.Categories.Add(new SetSafetyPolicyCategoryRequestDto
                    {
                        ContentCategoryId = presetId,
                        Rule = PolicyRule.Blocked
                    });
                }
            }

            policy = new SafetyPolicy { ChildProfileId = childProfileId };
            ApplyRequest(policy, request, isOwner);
            await policyRepo.AddAsync(policy, cancellationToken);
        }
        else
        {
            var existingCategories = await categoryRepo.FindAsync(
                value => value.SafetyPolicyId == policy.Id,
                cancellationToken: cancellationToken);

            beforeState = Snapshot(policy, existingCategories.Select(ToCategoryDto));

            ApplyRequest(policy, request, isOwner);
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
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

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

    public async Task<SafetyPolicyDto> ReconsentAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Safety Policy của hồ sơ trẻ", childProfileId);
        if (!policy.ConsentRecorded)
        {
            throw new BadRequestException(
                "Hồ sơ chưa có consent ban đầu — hãy lưu Safety Policy kèm xác nhận đồng thuận.");
        }

        var categories = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
            value => value.SafetyPolicyId == policy.Id, cancellationToken: cancellationToken);
        var categoryDtos = categories.Select(ToCategoryDto).ToList();
        if (!ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion))
        {
            return MapToDto(policy, categoryDtos);
        }

        var previousVersion = policy.ConsentPolicyVersion;
        RecordConsent(policy, currentUserId);
        policy.UpdatedAt = DateTime.UtcNow;
        policyRepo.Update(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            currentUserId, "RECONSENT_SAFETY_POLICY", nameof(SafetyPolicy), policy.Id,
            new { consentPolicyVersion = previousVersion },
            new { consentPolicyVersion = policy.ConsentPolicyVersion },
            cancellationToken);

        return MapToDto(policy, categoryDtos);
    }

    public async Task<AgePresetDto> GetAgePresetAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        var child = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);

        return new AgePresetDto
        {
            AgeBand = child.AgeBand.ToString(),
            BlockedCategoryIds = await GetBlockedPresetIdsAsync(child.AgeBand, cancellationToken)
        };
    }

    public async Task<SafetyPolicyDto> ApplyAgePresetAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);
        var child = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Safety Policy của hồ sơ trẻ", childProfileId);

        var presetIds = await GetBlockedPresetIdsAsync(child.AgeBand, cancellationToken);
        var categoryRepo = _unitOfWork.Repository<SafetyPolicyCategory>();
        var existing = await categoryRepo.FindAsync(
            value => value.SafetyPolicyId == policy.Id, cancellationToken: cancellationToken);
        var before = Snapshot(policy, existing.Select(ToCategoryDto));

        // Thay toàn bộ Blocked bằng preset; giữ Allowed trừ danh mục nay bị Blocked.
        var toRemove = existing
            .Where(category => category.Rule == PolicyRule.Blocked
                               || presetIds.Contains(category.ContentCategoryId))
            .ToList();
        if (toRemove.Count > 0)
        {
            categoryRepo.DeleteRange(toRemove);
        }

        foreach (var presetId in presetIds)
        {
            await categoryRepo.AddAsync(new SafetyPolicyCategory
            {
                SafetyPolicyId = policy.Id,
                ContentCategoryId = presetId,
                Rule = PolicyRule.Blocked
            }, cancellationToken);
        }

        policy.UpdatedAt = DateTime.UtcNow;
        policyRepo.Update(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var resultCategories = existing.Except(toRemove).Select(ToCategoryDto)
            .Concat(presetIds.Select(id => new SafetyPolicyCategoryDto
            {
                ContentCategoryId = id,
                Rule = PolicyRule.Blocked.ToString()
            })).ToList();
        await _auditLogWriter.LogAsync(
            currentUserId, "APPLY_AGE_PRESET", nameof(SafetyPolicy), policy.Id,
            before, Snapshot(policy, resultCategories), cancellationToken);

        return MapToDto(policy, resultCategories);
    }

    public async Task DeleteSafetyPolicyAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

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

    private static void ApplyRequest(
        SafetyPolicy policy, SetSafetyPolicyRequestDto request, bool isOwner)
    {
        policy.MaxStoryLength = request.MaxStoryLength;
        policy.RequiredApprovalMode = request.RequiredApprovalMode;
        policy.ParentalGateEnabled = request.ParentalGateEnabled;
        policy.ReadabilityScoreThreshold = request.ReadabilityScoreThreshold;
        policy.SafetyScoreThreshold = request.SafetyScoreThreshold;
        policy.ComprehensionThresholdPercent = request.ComprehensionThresholdPercent;
        policy.ComprehensionWindowSize = request.ComprehensionWindowSize;
        policy.StoryWishesEnabled = request.StoryWishesEnabled;
        policy.AllowInteractiveStories = request.AllowInteractiveStories;
        policy.ChildCreationEnabled = request.ChildCreationEnabled;
        policy.ChildCreationMode = request.ChildCreationMode;
        ApplyVoiceSetting(policy, request, isOwner);
    }

    private static void ApplyVoiceSetting(
        SafetyPolicy policy, SetSafetyPolicyRequestDto request, bool isOwner)
    {
        if (!request.VoiceAnswersEnabled)
        {
            policy.VoiceAnswersEnabled = false;
            policy.VoiceEnableRequestedAt = null;
            return;
        }

        if (policy.VoiceConsentAt.HasValue)
        {
            policy.VoiceAnswersEnabled = true;
            policy.VoiceEnableRequestedAt = null;
            return;
        }

        if (isOwner)
        {
            if (!request.VoiceConsentAccepted)
            {
                throw new BadRequestException(
                    "Owner cần đồng ý mục (e) về nhận dạng giọng nói trước khi bật.");
            }

            policy.VoiceConsentAt = DateTime.UtcNow;
            policy.VoiceAnswersEnabled = true;
            policy.VoiceEnableRequestedAt = null;
            return;
        }

        policy.VoiceAnswersEnabled = false;
        policy.VoiceEnableRequestedAt = DateTime.UtcNow;
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
        policy.StoryWishesEnabled,
        policy.AllowInteractiveStories,
        policy.ChildCreationEnabled,
        ChildCreationMode = policy.ChildCreationMode.ToString(),
        policy.VoiceAnswersEnabled,
        policy.VoiceConsentAt,
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
        StoryWishesEnabled = policy.StoryWishesEnabled,
        AllowInteractiveStories = policy.AllowInteractiveStories,
        ChildCreationEnabled = policy.ChildCreationEnabled,
        ChildCreationMode = policy.ChildCreationMode.ToString(),
        VoiceAnswersEnabled = policy.VoiceAnswersEnabled,
        VoiceConsentAt = policy.VoiceConsentAt,
        VoiceConsentRequired = policy.VoiceEnableRequestedAt.HasValue,
        ConsentOutdated = ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion),
        EffectiveVoiceAnswersEnabled = policy.VoiceAnswersEnabled
                                       && !ConsentPolicy.IsOutdated(policy.ConsentPolicyVersion),
        Categories = categories
    };

    private async Task<List<int>> GetBlockedPresetIdsAsync(
        AgeBand ageBand, CancellationToken cancellationToken)
    {
        var bandName = ageBand.ToString();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            value => value.IsActive, cancellationToken: cancellationToken);
        return categories
            .Where(value => value.DefaultBlockedAgeBands.Contains(bandName))
            .Select(value => value.Id)
            .ToList();
    }

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

        if (!Enum.IsDefined(request.ChildCreationMode))
        {
            throw new BadRequestException("Chế độ duyệt truyện bé sáng tác không hợp lệ.");
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

        if (request.Categories.Any(value => value.Rule == PolicyRule.Restricted))
        {
            throw new BadRequestException(
                "Quy tắc danh mục chỉ gồm Allowed hoặc Blocked (không có mức Restricted).");
        }

        if (request.Categories.GroupBy(value => value.ContentCategoryId).Any(group => group.Count() > 1))
        {
            throw new BadRequestException("Mỗi Content Category chỉ được khai báo một lần.");
        }
    }
}
