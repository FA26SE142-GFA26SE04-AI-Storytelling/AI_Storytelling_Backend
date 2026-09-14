using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;

public class SupervisionService : ISupervisionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public SupervisionService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IJwtTokenGenerator tokenGenerator)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<InvitationDto> CreateInvitationAsync(
        int childProfileId, int inviterUserId, CreateInvitationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, inviterUserId, cancellationToken);

        if (request.ExpiresInDays is < 1 or > 365)
        {
            throw new BadRequestException("Số ngày hết hạn phải từ 1 đến 365.");
        }

        var email = string.IsNullOrWhiteSpace(request.InviteeEmail)
            ? null
            : request.InviteeEmail.Trim();
        if (email?.Length > 150)
        {
            throw new BadRequestException("Email người được mời tối đa 150 ký tự.");
        }

        var invitation = new SupervisionInvitation
        {
            ChildProfileId = childProfileId,
            InviterUserId = inviterUserId,
            InvitationCode = _tokenGenerator.GenerateRefreshToken(),
            InviteeEmail = email,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(request.ExpiresInDays)
        };

        await _unitOfWork.Repository<SupervisionInvitation>()
            .AddAsync(invitation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapInvitation(invitation);
    }

    public async Task<SupervisionRelationshipDto> AcceptInvitationAsync(
        string invitationCode, int accepterUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invitationCode))
        {
            throw new BadRequestException("Mã mời không hợp lệ hoặc đã được sử dụng.");
        }

        var invitationRepo = _unitOfWork.Repository<SupervisionInvitation>();
        var invitation = await invitationRepo.FirstOrDefaultAsync(
            value => value.InvitationCode == invitationCode.Trim()
                     && value.Status == InvitationStatus.Pending,
            cancellationToken: cancellationToken);
        if (invitation == null)
        {
            throw new BadRequestException("Mã mời không hợp lệ hoặc đã được sử dụng.");
        }

        if (!invitation.ExpiresAt.HasValue || invitation.ExpiresAt.Value <= DateTime.UtcNow)
        {
            throw new BadRequestException("Mã mời đã hết hạn.");
        }

        var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
        var childProfile = await childProfileRepo.GetByIdAsync(
            invitation.ChildProfileId, cancellationToken);
        if (childProfile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", invitation.ChildProfileId);
        }

        var accepter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(accepterUserId, cancellationToken);
        if (accepter == null)
        {
            throw new NotFoundException("Tài khoản", accepterUserId);
        }

        var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
        var alreadySupervising = await relationshipRepo.ExistsAsync(
            value => value.ChildProfileId == invitation.ChildProfileId
                     && value.SupervisorUserId == accepterUserId
                     && value.RevokedAt == null,
            cancellationToken);
        if (alreadySupervising)
        {
            throw new BadRequestException("Bạn đã giám sát hồ sơ trẻ này.");
        }

        var now = DateTime.UtcNow;
        invitation.Status = InvitationStatus.Accepted;
        invitation.UsedAt = now;
        invitation.RespondedAt = now;
        invitation.InviteeUserId = accepterUserId;
        invitationRepo.Update(invitation);

        var relationship = new SupervisionRelationship
        {
            ChildProfileId = invitation.ChildProfileId,
            SupervisorUserId = accepterUserId,
            SupervisionInvitationId = invitation.Id,
            SupervisorRole = SupervisorRole.AdditionalSupervisor
        };
        await relationshipRepo.AddAsync(relationship, cancellationToken);

        // Parent vừa chấp nhận làm hồ sơ đang chờ consent thỏa BR-1.9 ngay trong transaction này.
        if (childProfile.Status == ChildProfileStatus.PendingParentConsent
            && accepter.Role == UserRole.Parent)
        {
            childProfile.Status = ChildProfileStatus.Active;
            childProfile.UpdatedAt = now;
            childProfileRepo.Update(childProfile);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapRelationship(relationship);
    }

    public async Task RevokeSupervisionAsync(
        int supervisionRelationshipId, int revokerUserId,
        CancellationToken cancellationToken = default)
    {
        var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
        var target = await relationshipRepo.GetByIdAsync(
            supervisionRelationshipId, cancellationToken);
        if (target == null)
        {
            throw new NotFoundException("Quan hệ giám sát", supervisionRelationshipId);
        }

        await _accessGuard.EnsureOwnerAsync(
            target.ChildProfileId, revokerUserId, cancellationToken);

        if (target.SupervisorRole == SupervisorRole.Owner)
        {
            throw new BadRequestException(
                "Không thể thu hồi quan hệ Owner; hãy dùng luồng chuyển quyền sở hữu.");
        }

        if (target.RevokedAt.HasValue)
        {
            return;
        }

        target.RevokedAt = DateTime.UtcNow;
        target.RevokedByUserId = revokerUserId;
        relationshipRepo.Update(target);

        var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
        var childProfile = await childProfileRepo.GetByIdAsync(
            target.ChildProfileId, cancellationToken);
        if (childProfile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", target.ChildProfileId);
        }

        var owner = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(childProfile.OwnerUserId, cancellationToken);
        if (owner == null)
        {
            throw new NotFoundException("Tài khoản", childProfile.OwnerUserId);
        }

        if (owner.Role == UserRole.Teacher)
        {
            var stillHasParent = await relationshipRepo.ExistsAsync(
                value => value.ChildProfileId == target.ChildProfileId
                         && value.Id != target.Id
                         && value.RevokedAt == null
                         && value.SupervisorUser != null
                         && value.SupervisorUser.Role == UserRole.Parent,
                cancellationToken);
            if (!stillHasParent)
            {
                childProfile.Status = ChildProfileStatus.PendingParentConsent;
                childProfile.UpdatedAt = DateTime.UtcNow;
                childProfileRepo.Update(childProfile);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task GrantPermissionAsync(
        int supervisionRelationshipId, int ownerUserId, Permission permission,
        CancellationToken cancellationToken = default)
    {
        var target = await GetPermissionTargetAsync(
            supervisionRelationshipId, ownerUserId, permission, cancellationToken);
        var permissionRepo = _unitOfWork.Repository<SupervisionPermission>();
        var alreadyGranted = await permissionRepo.ExistsAsync(
            value => value.SupervisionRelationshipId == target.Id
                     && value.Permission == permission,
            cancellationToken);

        if (!alreadyGranted)
        {
            await permissionRepo.AddAsync(new SupervisionPermission
            {
                SupervisionRelationshipId = target.Id,
                Permission = permission
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokePermissionAsync(
        int supervisionRelationshipId, int ownerUserId, Permission permission,
        CancellationToken cancellationToken = default)
    {
        var target = await GetPermissionTargetAsync(
            supervisionRelationshipId, ownerUserId, permission, cancellationToken);
        var permissionRepo = _unitOfWork.Repository<SupervisionPermission>();
        var existing = await permissionRepo.FirstOrDefaultAsync(
            value => value.SupervisionRelationshipId == target.Id
                     && value.Permission == permission,
            cancellationToken: cancellationToken);

        if (existing != null)
        {
            permissionRepo.Delete(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<InvitationDto>> ListInvitationsAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var invitations = await _unitOfWork.Repository<SupervisionInvitation>().FindAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        return invitations.Select(MapInvitation).ToList();
    }

    public async Task CancelInvitationAsync(
        int invitationId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var invitationRepo = _unitOfWork.Repository<SupervisionInvitation>();
        var invitation = await invitationRepo.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null)
        {
            throw new NotFoundException("Lời mời giám sát", invitationId);
        }

        await _accessGuard.EnsureActiveSupervisionAsync(invitation.ChildProfileId, currentUserId, cancellationToken);

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new BadRequestException("Chỉ có thể huỷ lời mời đang ở trạng thái Pending.");
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.RespondedAt = DateTime.UtcNow;
        invitationRepo.Update(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SupervisionRelationshipDto>> ListSupervisorsAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var relationships = await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            value => value.ChildProfileId == childProfileId && value.RevokedAt == null,
            cancellationToken: cancellationToken);

        return relationships.Select(MapRelationship).ToList();
    }

    public async Task<List<string>> ListPermissionsAsync(
        int supervisionRelationshipId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
        var target = await relationshipRepo.GetByIdAsync(supervisionRelationshipId, cancellationToken);
        if (target == null)
        {
            throw new NotFoundException("Quan hệ giám sát", supervisionRelationshipId);
        }

        await _accessGuard.EnsureOwnerAsync(target.ChildProfileId, currentUserId, cancellationToken);

        var permissions = await _unitOfWork.Repository<SupervisionPermission>().FindAsync(
            value => value.SupervisionRelationshipId == supervisionRelationshipId, cancellationToken: cancellationToken);

        return permissions.Select(p => p.Permission.ToString()).ToList();
    }

    private async Task<SupervisionRelationship> GetPermissionTargetAsync(
        int relationshipId, int ownerUserId, Permission permission,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(permission))
        {
            throw new BadRequestException("Permission không hợp lệ.");
        }

        var target = await _unitOfWork.Repository<SupervisionRelationship>()
            .GetByIdAsync(relationshipId, cancellationToken);
        if (target == null)
        {
            throw new NotFoundException("Quan hệ giám sát", relationshipId);
        }

        await _accessGuard.EnsureOwnerAsync(
            target.ChildProfileId, ownerUserId, cancellationToken);

        if (target.SupervisorRole == SupervisorRole.Owner)
        {
            throw new BadRequestException(
                "Không thể cấu hình permission cho Owner vì Owner mặc định toàn quyền.");
        }

        if (target.RevokedAt.HasValue)
        {
            throw new BadRequestException(
                "Không thể cấu hình permission cho quan hệ giám sát đã bị thu hồi.");
        }

        return target;
    }

    private static InvitationDto MapInvitation(SupervisionInvitation invitation) => new()
    {
        Id = invitation.Id,
        ChildProfileId = invitation.ChildProfileId,
        InvitationCode = invitation.InvitationCode ?? string.Empty,
        Status = invitation.Status.ToString(),
        ExpiresAt = invitation.ExpiresAt
    };

    private static SupervisionRelationshipDto MapRelationship(
        SupervisionRelationship relationship) => new()
    {
        Id = relationship.Id,
        ChildProfileId = relationship.ChildProfileId,
        SupervisorUserId = relationship.SupervisorUserId,
        SupervisorRole = relationship.SupervisorRole.ToString()
    };
}
