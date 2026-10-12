using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;

public class ChildProfileService : IChildProfileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IChildSessionRevoker _childSessionRevoker;

    public ChildProfileService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IChildSessionRevoker childSessionRevoker)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _childSessionRevoker = childSessionRevoker;
    }

    public async Task<ChildProfileDto> CreateChildProfileAsync(
        int ownerUserId,
        CreateChildProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var owner = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(ownerUserId, cancellationToken);
        if (owner == null)
        {
            throw new NotFoundException("Tài khoản", ownerUserId);
        }

        ValidateRequest(request);

        var childProfile = new ChildProfile
        {
            OwnerUserId = ownerUserId,
            Nickname = request.Nickname.Trim(),
            AgeBand = request.AgeBand,
            Language = NormalizeLanguage(request.Language),
            Status = ChildProfileStatus.Draft
        };

        await _unitOfWork.Repository<ChildProfile>().AddAsync(childProfile, cancellationToken);

        // Bước 1.2b: quan hệ Owner phải được thêm trước cùng lần SaveChangesAsync.
        await _unitOfWork.Repository<SupervisionRelationship>().AddAsync(
            new SupervisionRelationship
            {
                ChildProfile = childProfile,
                SupervisorUserId = ownerUserId,
                SupervisorRole = SupervisorRole.Owner,
                RevokedAt = null
            },
            cancellationToken);

        // Lưu profile và quyền Owner cùng nhau trước để EF cấp ChildProfile.Id.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // AuditLog dùng EntityId thường, không có navigation tới ChildProfile, nên cần lưu riêng sau khi có Id.
        await WriteChildProfileAuditAsync(
            ownerUserId, "CREATE_CHILD_PROFILE", childProfile.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToChildProfileDto(childProfile);
    }

    public async Task<ChildProfileDto> ActivateChildProfileAsync(
        int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var profileRepo = _unitOfWork.Repository<ChildProfile>();
        var profile = await profileRepo.GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);

        if (profile.Status is ChildProfileStatus.Suspended or ChildProfileStatus.Archived)
        {
            throw new BadRequestException(
                "Không thể kích hoạt hồ sơ trẻ đang bị đình chỉ hoặc đã lưu trữ.");
        }

        var hasLearningProfile = await _unitOfWork.Repository<LearningProfile>()
            .ExistsAsync(value => value.ChildProfileId == childProfileId, cancellationToken);
        if (!hasLearningProfile)
        {
            throw new BadRequestException(
                "Hồ sơ trẻ chưa có Learning Profile không thể kích hoạt.");
        }

        var safetyPolicy = await _unitOfWork.Repository<SafetyPolicy>()
            .FirstOrDefaultAsync(
                value => value.ChildProfileId == childProfileId,
                cancellationToken: cancellationToken);
        if (safetyPolicy == null)
        {
            throw new BadRequestException(
                "Hồ sơ trẻ chưa có Safety Policy — không thể kích hoạt.");
        }

        if (!safetyPolicy.ConsentRecorded)
        {
            throw new BadRequestException(
                "Safety Policy chưa có consent hợp lệ theo phiên bản hiện tại — không thể kích hoạt.");
        }

        if (safetyPolicy.ConsentPolicyVersion < ConsentPolicy.CurrentVersion)
        {
            throw new BadRequestException(
                "Điều khoản đồng thuận đã thay đổi — Owner cần đồng ý bản mới trước khi kích hoạt.");
        }

        profile.Status = ChildProfileStatus.Active;
        profile.UpdatedAt = DateTime.UtcNow;
        profileRepo.Update(profile);
        await WriteChildProfileAuditAsync(
            currentUserId, "ACTIVATE_CHILD_PROFILE", childProfileId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToChildProfileDto(profile);
    }

    public async Task<List<ChildProfileDto>> ListMyChildProfilesAsync(
        int currentUserId, CancellationToken cancellationToken = default)
    {
        var relationships = await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            value => value.SupervisorUserId == currentUserId
                     && value.RevokedAt == null
                     && value.ChildProfile != null
                     && value.ChildProfile.Status != ChildProfileStatus.Archived
                     && value.ChildProfile.Status != ChildProfileStatus.Deleted,
            includeProperties: nameof(SupervisionRelationship.ChildProfile),
            cancellationToken: cancellationToken);

        return relationships
            .Select(value => MapToChildProfileDto(value.ChildProfile!))
            .ToList();
    }

    public async Task<ChildProfileDto> GetChildProfileByIdAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        return MapToChildProfileDto(profile);
    }

    public async Task<ChildProfileDto> UpdateChildProfileAsync(
        int childProfileId, int currentUserId, UpdateChildProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var profileRepo = _unitOfWork.Repository<ChildProfile>();
        var profile = await profileRepo.GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Nickname))
        {
            throw new BadRequestException("Biệt danh của trẻ không được để trống.");
        }

        if (!Enum.IsDefined(request.AgeBand))
        {
            throw new BadRequestException("Nhóm tuổi nhận thức không hợp lệ.");
        }

        var language = NormalizeLanguage(request.Language);
        var previousAgeBand = profile.AgeBand;
        var ageBandChanged = previousAgeBand != request.AgeBand;

        profile.Nickname = request.Nickname.Trim();
        profile.AgeBand = request.AgeBand;
        profile.Language = language;
        if (!string.IsNullOrWhiteSpace(request.AvatarId))
        {
            profile.AvatarId = request.AvatarId.Trim();
        }

        profile.UpdatedAt = DateTime.UtcNow;
        profileRepo.Update(profile);

        if (ageBandChanged)
        {
            await _unitOfWork.Repository<ChildProfileVersionHistory>().AddAsync(
                new ChildProfileVersionHistory
                {
                    ChildProfileId = childProfileId,
                    RecommendationId = null,
                    Source = VersionHistorySource.Manual,
                    PreviousConfig = JsonSerializer.Serialize(new { ageBand = previousAgeBand.ToString() }),
                    NewConfig = JsonSerializer.Serialize(new { ageBand = profile.AgeBand.ToString() }),
                    AppliedByUserId = currentUserId,
                    VersionStatus = VersionHistoryStatus.ActiveVersion,
                    AppliedAt = DateTime.UtcNow
                }, cancellationToken);
        }

        await WriteChildProfileAuditAsync(
            currentUserId, "UPDATE_CHILD_PROFILE", childProfileId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToChildProfileDto(profile);
        if (ageBandChanged)
        {
            dto.ShouldReapplyBlockedPreset = true;
            dto.SuggestedReadingMode = ReadingModeDefaults.ForAgeBand(profile.AgeBand).ToString();
        }

        return dto;
    }

    public async Task ArchiveChildProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var profile = await LoadOwnedProfileAsync(childProfileId, currentUserId, cancellationToken);
        if (profile.Status == ChildProfileStatus.Archived)
        {
            return;
        }

        // Bước 1.8b: lưu trữ từ Active / Suspended; thu hồi Child Session và Easy Login.
        if (profile.Status is not (ChildProfileStatus.Active or ChildProfileStatus.Suspended))
        {
            throw new BadRequestException("Chỉ có thể lưu trữ hồ sơ đang Active hoặc đã tạm dừng.");
        }

        await ChangeStatusAndRevokeAccessAsync(
            profile, ChildProfileStatus.Archived, "ARCHIVE_CHILD_PROFILE", currentUserId, cancellationToken);
    }

    public async Task<ChildProfileDto> SuspendChildProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var profile = await LoadOwnedProfileAsync(childProfileId, currentUserId, cancellationToken);
        if (profile.Status == ChildProfileStatus.Suspended)
        {
            return MapToChildProfileDto(profile);
        }

        if (profile.Status != ChildProfileStatus.Active)
        {
            throw new BadRequestException("Chỉ có thể tạm dừng hồ sơ đang Active.");
        }

        await ChangeStatusAndRevokeAccessAsync(
            profile, ChildProfileStatus.Suspended, "SUSPEND_CHILD_PROFILE", currentUserId, cancellationToken);
        return MapToChildProfileDto(profile);
    }

    public Task<ChildProfileDto> ResumeChildProfileAsync(
        int childProfileId, int currentUserId, ResumeChildProfileRequestDto request,
        CancellationToken cancellationToken = default) =>
        ReactivateAsync(
            childProfileId, currentUserId, request, ChildProfileStatus.Suspended,
            "Chỉ có thể kích hoạt lại hồ sơ đang tạm dừng.", "RESUME_CHILD_PROFILE", cancellationToken);

    public Task<ChildProfileDto> RestoreChildProfileAsync(
        int childProfileId, int currentUserId, ResumeChildProfileRequestDto request,
        CancellationToken cancellationToken = default) =>
        ReactivateAsync(
            childProfileId, currentUserId, request, ChildProfileStatus.Archived,
            "Chỉ có thể khôi phục hồ sơ đã lưu trữ.", "RESTORE_CHILD_PROFILE", cancellationToken);

    private async Task<ChildProfileDto> ReactivateAsync(
        int childProfileId, int currentUserId, ResumeChildProfileRequestDto request,
        ChildProfileStatus requiredStatus, string wrongStatusMessage, string auditAction,
        CancellationToken cancellationToken)
    {
        var profile = await LoadOwnedProfileAsync(childProfileId, currentUserId, cancellationToken);
        if (profile.Status != requiredStatus)
        {
            throw new BadRequestException(wrongStatusMessage);
        }

        // Easy Login đã bị thu hồi hẳn khi tạm dừng / lưu trữ — Owner phải tạo QR mới (Bước 1.10).
        await EnsureConsentCurrentAsync(
            profile.Id, currentUserId, request?.AcceptCurrentConsent ?? false, cancellationToken);

        profile.Status = ChildProfileStatus.Active;
        profile.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<ChildProfile>().Update(profile);
        await WriteChildProfileAuditAsync(currentUserId, auditAction, profile.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToChildProfileDto(profile);
    }

    private async Task<ChildProfile> LoadOwnedProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null || profile.Status == ChildProfileStatus.Deleted)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        // Chỉ Owner được tạm dừng / kích hoạt lại / lưu trữ / khôi phục hồ sơ (Bước 1.8b).
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        return profile;
    }

    private async Task ChangeStatusAndRevokeAccessAsync(
        ChildProfile profile, ChildProfileStatus newStatus, string auditAction, int currentUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        profile.Status = newStatus;
        profile.UpdatedAt = now;
        _unitOfWork.Repository<ChildProfile>().Update(profile);

        await _childSessionRevoker.RevokeByChildProfileAsync(profile.Id, cancellationToken);

        // Soft-revoke giống ChildAccessCredentialService.RevokeCredentialAsync: giữ dòng cho audit,
        // chỉ mục unique lọc IsDeleted=false nên Owner tạo được QR mới sau khi kích hoạt lại.
        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == profile.Id, cancellationToken: cancellationToken);
        if (credential != null)
        {
            credential.IsDeleted = true;
            credential.EasyLoginSecretHash = null;
            credential.UpdatedAt = now;
            credentialRepo.Update(credential);
        }

        await WriteChildProfileAuditAsync(currentUserId, auditAction, profile.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureConsentCurrentAsync(
        int childProfileId, int currentUserId, bool acceptCurrentConsent,
        CancellationToken cancellationToken)
    {
        var policyRepo = _unitOfWork.Repository<SafetyPolicy>();
        var policy = await policyRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (policy == null || !policy.ConsentRecorded)
        {
            throw new BadRequestException("Hồ sơ trẻ chưa có consent hợp lệ — không thể kích hoạt lại.");
        }

        if (policy.ConsentPolicyVersion >= ConsentPolicy.CurrentVersion)
        {
            return;
        }

        if (!acceptCurrentConsent)
        {
            throw new BadRequestException(
                "Điều khoản đồng thuận đã thay đổi — Owner cần đồng ý bản mới để tiếp tục.");
        }

        policy.ConsentPolicyVersion = ConsentPolicy.CurrentVersion;
        policy.ConsentRecordedAt = DateTime.UtcNow;
        policy.ConsentedByUserId = currentUserId;
        policy.UpdatedAt = DateTime.UtcNow;
        policyRepo.Update(policy);
    }

    private async Task WriteChildProfileAuditAsync(
        int actorUserId, string action, int childProfileId, CancellationToken cancellationToken)
    {
        await _unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(ChildProfile),
            EntityId = childProfileId,
            OccurredAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private static void ValidateRequest(CreateChildProfileRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Nickname))
        {
            throw new BadRequestException("Biệt danh của trẻ không được để trống.");
        }

        if (!Enum.IsDefined(request.AgeBand))
        {
            throw new BadRequestException("Nhóm tuổi nhận thức không hợp lệ.");
        }

        var language = request.Language?.Trim();
        if (language is { Length: > 10 })
        {
            throw new BadRequestException("Mã ngôn ngữ tối đa 10 ký tự.");
        }
    }

    private const string SupportedLanguage = "vi";

    private static string NormalizeLanguage(string? language)
    {
        var value = string.IsNullOrWhiteSpace(language)
            ? SupportedLanguage
            : language.Trim().ToLowerInvariant();
        if (value != SupportedLanguage)
        {
            throw new BadRequestException("Nền tảng hiện chỉ hỗ trợ tiếng Việt.");
        }

        return value;
    }

    private static ChildProfileDto MapToChildProfileDto(ChildProfile childProfile)
    {
        return new ChildProfileDto
        {
            Id = childProfile.Id,
            OwnerUserId = childProfile.OwnerUserId,
            Nickname = childProfile.Nickname,
            AvatarId = childProfile.AvatarId,
            AgeBand = childProfile.AgeBand.ToString(),
            Language = childProfile.Language,
            Status = childProfile.Status.ToString(),
            CreatedAt = childProfile.CreatedAt
        };
    }
}
