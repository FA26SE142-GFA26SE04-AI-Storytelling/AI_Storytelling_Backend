using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;

public class ChildProfileService : IChildProfileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;

    public ChildProfileService(IUnitOfWork unitOfWork, ISupervisionAccessGuard accessGuard)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
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
            Language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language.Trim(),
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

        var hasSafetyPolicy = await _unitOfWork.Repository<SafetyPolicy>()
            .ExistsAsync(value => value.ChildProfileId == childProfileId, cancellationToken);
        if (!hasSafetyPolicy)
        {
            throw new BadRequestException(
                "Hồ sơ trẻ chưa có Safety Policy — không thể kích hoạt.");
        }

        // BR-1.9 áp dụng thống nhất cho mọi scope: phải có ít nhất một Parent đang giám sát.
        var hasActiveParentSupervisor = await _unitOfWork.Repository<SupervisionRelationship>()
            .ExistsAsync(
                value => value.ChildProfileId == childProfileId
                         && value.RevokedAt == null
                         && value.SupervisorUser != null
                         && value.SupervisorUser.Role == UserRole.Parent,
                cancellationToken);

        profile.Status = hasActiveParentSupervisor
            ? ChildProfileStatus.Active
            : ChildProfileStatus.PendingParentConsent;
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
                     && value.ChildProfile.Status != ChildProfileStatus.Archived,
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

        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Nickname))
        {
            throw new BadRequestException("Biệt danh của trẻ không được để trống.");
        }

        if (!Enum.IsDefined(request.AgeBand))
        {
            throw new BadRequestException("Nhóm tuổi nhận thức không hợp lệ.");
        }

        profile.Nickname = request.Nickname.Trim();
        profile.AgeBand = request.AgeBand;
        profile.Language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language.Trim();
        profile.UpdatedAt = DateTime.UtcNow;
        profileRepo.Update(profile);
        await WriteChildProfileAuditAsync(
            currentUserId, "UPDATE_CHILD_PROFILE", childProfileId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToChildProfileDto(profile);
    }

    public async Task ArchiveChildProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var profileRepo = _unitOfWork.Repository<ChildProfile>();
        var profile = await profileRepo.GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        // Chỉ Owner được quyền lưu trữ hồ sơ.
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        if (profile.Status == ChildProfileStatus.Archived)
        {
            return;
        }

        // Archived KHÔNG xoá dữ liệu lịch sử — chỉ dừng truy cập mới (đặc tả Luồng 1, Mục 4).
        profile.Status = ChildProfileStatus.Archived;
        profile.UpdatedAt = DateTime.UtcNow;
        profileRepo.Update(profile);
        await WriteChildProfileAuditAsync(
            currentUserId, "ARCHIVE_CHILD_PROFILE", childProfileId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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

    private static ChildProfileDto MapToChildProfileDto(ChildProfile childProfile)
    {
        return new ChildProfileDto
        {
            Id = childProfile.Id,
            OwnerUserId = childProfile.OwnerUserId,
            Nickname = childProfile.Nickname,
            AgeBand = childProfile.AgeBand.ToString(),
            Language = childProfile.Language,
            Status = childProfile.Status.ToString(),
            CreatedAt = childProfile.CreatedAt
        };
    }
}
