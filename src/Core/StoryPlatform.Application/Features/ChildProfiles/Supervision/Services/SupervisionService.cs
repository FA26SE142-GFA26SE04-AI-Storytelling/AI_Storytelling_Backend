using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;

public class SupervisionService : ISupervisionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly INotificationService _notificationService;

    public SupervisionService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _notificationService = notificationService;
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

        if (email != null && !new EmailAddressAttribute().IsValid(email))
        {
            throw new BadRequestException("Email người được mời không đúng định dạng.");
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

        if (email != null)
        {
            var inviter = await _unitOfWork.Repository<UserAccount>()
                .GetByIdAsync(inviterUserId, cancellationToken);
            var inviterName = inviter?.FullName ?? "Một người dùng AI Storytelling Platform";
            await _emailSender.SendSupervisionInvitationEmailAsync(
                email, inviterName, invitation.InvitationCode!, cancellationToken);
        }

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

    public async Task<OwnershipTransferRequestDto> RequestOwnershipTransferAsync(
        int childProfileId, int requesterUserId, TransferOwnershipRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (requesterUserId == request.TargetSupervisorUserId)
        {
            throw new BadRequestException("Không thể chuyển nhượng quyền Owner cho chính mình.");
        }

        var requesterRelationship = await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, requesterUserId, cancellationToken);
        var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();

        int currentOwnerUserId;
        int targetSupervisorUserId;
        int responderUserId;
        OwnershipTransferRequestStatus status;

        switch (requesterRelationship.SupervisorRole)
        {
            case SupervisorRole.Owner:
                var targetExists = await relationshipRepo.ExistsAsync(
                    value => value.ChildProfileId == childProfileId
                             && value.SupervisorUserId == request.TargetSupervisorUserId
                             && value.SupervisorRole == SupervisorRole.AdditionalSupervisor
                             && value.RevokedAt == null,
                    cancellationToken);
                if (!targetExists)
                {
                    throw new BadRequestException(
                        "Người nhận phải là một Additional Supervisor đang hoạt động của hồ sơ trẻ này.");
                }

                currentOwnerUserId = requesterUserId;
                targetSupervisorUserId = request.TargetSupervisorUserId;
                responderUserId = targetSupervisorUserId;
                status = OwnershipTransferRequestStatus.Pending;
                break;

            case SupervisorRole.AdditionalSupervisor:
                var targetIsOwner = await relationshipRepo.ExistsAsync(
                    value => value.ChildProfileId == childProfileId
                             && value.SupervisorUserId == request.TargetSupervisorUserId
                             && value.SupervisorRole == SupervisorRole.Owner
                             && value.RevokedAt == null,
                    cancellationToken);
                if (!targetIsOwner)
                {
                    throw new BadRequestException(
                        "Người nhận phải là Owner đang hoạt động của hồ sơ trẻ này.");
                }

                currentOwnerUserId = request.TargetSupervisorUserId;
                targetSupervisorUserId = requesterUserId;
                responderUserId = currentOwnerUserId;
                status = OwnershipTransferRequestStatus.PendingOwnerResponse;
                break;

            // SupervisorRole chỉ có 2 giá trị hợp lệ; nhánh này chỉ còn lại phòng khi
            // enum bị mở rộng trong tương lai hoặc dữ liệu bị ép kiểu sai.
            default:
                throw new ForbiddenException(
                    "Chỉ Owner hoặc Additional Supervisor đang hoạt động mới có thể tạo yêu cầu đổi quyền Owner.");
        }

        var transferRequest = new OwnershipTransferRequest
        {
            ChildProfileId = childProfileId,
            CurrentOwnerUserId = currentOwnerUserId,
            TargetSupervisorUserId = targetSupervisorUserId,
            Status = status
        };

        await _unitOfWork.Repository<OwnershipTransferRequest>()
            .AddAsync(transferRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notificationService.CreateAsync(
            responderUserId, NotificationType.OwnershipTransferRequested,
            JsonSerializer.Serialize(new
            {
                ownershipTransferRequestId = transferRequest.Id,
                childProfileId,
                requesterUserId,
                responderUserId
            }), cancellationToken);

        return MapOwnershipTransferRequest(transferRequest);
    }

    public async Task<SupervisionRelationshipDto> AcceptOwnershipTransferAsync(
        int ownershipTransferRequestId, int accepterUserId,
        CancellationToken cancellationToken = default)
    {
        var requestRepo = _unitOfWork.Repository<OwnershipTransferRequest>();
        var transferRequest = await requestRepo.GetByIdAsync(
            ownershipTransferRequestId, cancellationToken);
        if (transferRequest == null)
        {
            throw new NotFoundException(
                "Yêu cầu chuyển nhượng quyền Owner", ownershipTransferRequestId);
        }

        if (!IsPendingOwnershipTransfer(transferRequest.Status))
        {
            throw new BadRequestException(
                "Yêu cầu chuyển nhượng quyền Owner đã được xử lý trước đó.");
        }

        var responderUserId = GetOwnershipTransferResponderUserId(transferRequest);
        if (accepterUserId != responderUserId)
        {
            throw new ForbiddenException(
                "Chỉ người nhận yêu cầu đổi quyền Owner mới có thể chấp nhận yêu cầu này.");
        }

        var requesterUserId = GetOwnershipTransferRequesterUserId(transferRequest);
        var ownerResponds = IsOwnerResponseOwnershipTransfer(transferRequest.Status);

        SupervisionRelationship? targetRelationship = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(
                transferRequest.ChildProfileId, cancellationToken);

            var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
            var ownerRelationship = await relationshipRepo.FirstOrDefaultAsync(
                value => value.ChildProfileId == transferRequest.ChildProfileId
                         && value.SupervisorUserId == transferRequest.CurrentOwnerUserId
                         && value.SupervisorRole == SupervisorRole.Owner
                         && value.RevokedAt == null,
                cancellationToken: cancellationToken);
            if (ownerRelationship == null)
            {
                throw new BadRequestException(
                    "Yêu cầu chuyển nhượng không còn hợp lệ vì Owner trong yêu cầu không còn giữ quyền Owner của hồ sơ này.");
            }

            targetRelationship = await relationshipRepo.FirstOrDefaultAsync(
                value => value.ChildProfileId == transferRequest.ChildProfileId
                         && value.SupervisorUserId == transferRequest.TargetSupervisorUserId
                         && value.SupervisorRole == SupervisorRole.AdditionalSupervisor
                         && value.RevokedAt == null,
                cancellationToken: cancellationToken);
            if (targetRelationship == null)
            {
                throw new BadRequestException(
                    "Additional Supervisor trong yêu cầu không còn hoạt động trên hồ sơ trẻ này.");
            }

            ownerRelationship.SupervisorRole = SupervisorRole.AdditionalSupervisor;
            targetRelationship.SupervisorRole = SupervisorRole.Owner;
            relationshipRepo.Update(ownerRelationship);
            relationshipRepo.Update(targetRelationship);

            var permissionRepo = _unitOfWork.Repository<SupervisionPermission>();
            var staleTargetPermissions = await permissionRepo.FindAsync(
                value => value.SupervisionRelationshipId == targetRelationship.Id,
                cancellationToken: cancellationToken);
            permissionRepo.DeleteRange(staleTargetPermissions);

            var ownerHasViewResults = await permissionRepo.ExistsAsync(
                value => value.SupervisionRelationshipId == ownerRelationship.Id
                         && value.Permission == Permission.ViewResults,
                cancellationToken);
            if (!ownerHasViewResults)
            {
                await permissionRepo.AddAsync(new SupervisionPermission
                {
                    SupervisionRelationshipId = ownerRelationship.Id,
                    Permission = Permission.ViewResults
                }, cancellationToken);
            }

            var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
            var childProfile = await childProfileRepo.GetByIdAsync(
                transferRequest.ChildProfileId, cancellationToken);
            if (childProfile == null)
            {
                throw new NotFoundException("Hồ sơ trẻ", transferRequest.ChildProfileId);
            }

            childProfile.OwnerUserId = transferRequest.TargetSupervisorUserId;
            childProfile.UpdatedAt = DateTime.UtcNow;
            childProfileRepo.Update(childProfile);

            transferRequest.Status = ownerResponds
                ? OwnershipTransferRequestStatus.AcceptedByOwner
                : OwnershipTransferRequestStatus.Accepted;
            transferRequest.RespondedAt = DateTime.UtcNow;
            requestRepo.Update(transferRequest);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _notificationService.CreateAsync(
            requesterUserId, NotificationType.OwnershipTransferAccepted,
            JsonSerializer.Serialize(new
            {
                ownershipTransferRequestId = transferRequest.Id,
                childProfileId = transferRequest.ChildProfileId,
                newOwnerUserId = transferRequest.TargetSupervisorUserId
            }), cancellationToken);

        return MapRelationship(targetRelationship!);
    }

    public async Task<OwnershipTransferRequestDto> RejectOwnershipTransferAsync(
        int ownershipTransferRequestId, int rejecterUserId,
        CancellationToken cancellationToken = default)
    {
        var requestRepo = _unitOfWork.Repository<OwnershipTransferRequest>();
        var transferRequest = await requestRepo.GetByIdAsync(
            ownershipTransferRequestId, cancellationToken);
        if (transferRequest == null)
        {
            throw new NotFoundException(
                "Yêu cầu chuyển nhượng quyền Owner", ownershipTransferRequestId);
        }

        if (!IsPendingOwnershipTransfer(transferRequest.Status))
        {
            throw new BadRequestException(
                "Yêu cầu chuyển nhượng quyền Owner đã được xử lý trước đó.");
        }

        var responderUserId = GetOwnershipTransferResponderUserId(transferRequest);
        if (rejecterUserId != responderUserId)
        {
            throw new ForbiddenException(
                "Chỉ người nhận yêu cầu đổi quyền Owner mới có thể từ chối yêu cầu này.");
        }

        var requesterUserId = GetOwnershipTransferRequesterUserId(transferRequest);
        transferRequest.Status = IsOwnerResponseOwnershipTransfer(transferRequest.Status)
            ? OwnershipTransferRequestStatus.RejectedByOwner
            : OwnershipTransferRequestStatus.Rejected;
        transferRequest.RespondedAt = DateTime.UtcNow;
        requestRepo.Update(transferRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notificationService.CreateAsync(
            requesterUserId, NotificationType.OwnershipTransferRejected,
            JsonSerializer.Serialize(new
            {
                ownershipTransferRequestId = transferRequest.Id,
                childProfileId = transferRequest.ChildProfileId
            }), cancellationToken);

        return MapOwnershipTransferRequest(transferRequest);
    }

    public async Task<List<OwnershipTransferRequestDto>> ListOwnershipTransferRequestsAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);

        var requests = await _unitOfWork.Repository<OwnershipTransferRequest>().FindAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        return requests.Select(MapOwnershipTransferRequest).ToList();
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

        if (target.SupervisorRole == SupervisorRole.Owner)
        {
            return Enum.GetValues<Permission>().Select(permission => permission.ToString()).ToList();
        }

        var permissions = await _unitOfWork.Repository<SupervisionPermission>().FindAsync(
            value => value.SupervisionRelationshipId == supervisionRelationshipId, cancellationToken: cancellationToken);

        return permissions.Select(p => p.Permission.ToString()).ToList();
    }

    public async Task<PermissionRequestDto> CreatePermissionRequestAsync(
        int supervisionRelationshipId, int requesterUserId, CreatePermissionRequestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>()
            .GetByIdAsync(supervisionRelationshipId, cancellationToken);
        if (relationship == null)
        {
            throw new NotFoundException("Quan hệ giám sát", supervisionRelationshipId);
        }

        if (relationship.SupervisorUserId != requesterUserId)
        {
            throw new ForbiddenException(
                "Bạn chỉ có thể tạo yêu cầu xin quyền cho chính quan hệ giám sát của mình.");
        }

        if (relationship.RevokedAt.HasValue)
        {
            throw new BadRequestException(
                "Không thể tạo yêu cầu xin quyền cho quan hệ giám sát đã bị thu hồi.");
        }

        if (relationship.SupervisorRole == SupervisorRole.Owner)
        {
            throw new BadRequestException(
                "Owner đã có toàn quyền nên không cần tạo yêu cầu xin quyền.");
        }

        var permissions = (request.Permissions ?? new List<Permission>()).Distinct().ToList();
        if (permissions.Count == 0)
        {
            throw new BadRequestException("Vui lòng chọn ít nhất 1 quyền cần xin.");
        }

        if (permissions.Any(permission => !Enum.IsDefined(permission)))
        {
            throw new BadRequestException("Danh sách quyền chứa giá trị không hợp lệ.");
        }

        var permissionRequest = new SupervisionPermissionRequest
        {
            SupervisionRelationshipId = supervisionRelationshipId,
            RequesterUserId = requesterUserId,
            Status = PermissionRequestStatus.Pending,
            Items = permissions.Select(permission => new SupervisionPermissionRequestItem
            {
                Permission = permission
            }).ToList()
        };

        await _unitOfWork.Repository<SupervisionPermissionRequest>()
            .AddAsync(permissionRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var childProfile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(relationship.ChildProfileId, cancellationToken);
        if (childProfile != null)
        {
            await _notificationService.CreateAsync(
                childProfile.OwnerUserId, NotificationType.PermissionRequestCreated,
                JsonSerializer.Serialize(new
                {
                    permissionRequestId = permissionRequest.Id,
                    supervisionRelationshipId,
                    permissions = permissions.Select(permission => permission.ToString())
                }), cancellationToken);
        }

        return MapPermissionRequest(permissionRequest);
    }

    public async Task<PermissionRequestDto> AcceptPermissionRequestAsync(
        int permissionRequestId, int ownerUserId, CancellationToken cancellationToken = default)
    {
        var permissionRequest = await LoadPermissionRequestOrThrowAsync(
            permissionRequestId, cancellationToken);

        var relationship = await _unitOfWork.Repository<SupervisionRelationship>()
            .GetByIdAsync(permissionRequest.SupervisionRelationshipId, cancellationToken);
        if (relationship == null)
        {
            throw new NotFoundException(
                "Quan hệ giám sát", permissionRequest.SupervisionRelationshipId);
        }

        await _accessGuard.EnsureOwnerAsync(
            relationship.ChildProfileId, ownerUserId, cancellationToken);

        if (permissionRequest.Status != PermissionRequestStatus.Pending)
        {
            throw new BadRequestException("Yêu cầu xin quyền đã được xử lý trước đó.");
        }

        var permissionRepo = _unitOfWork.Repository<SupervisionPermission>();
        foreach (var item in permissionRequest.Items)
        {
            var alreadyGranted = await permissionRepo.ExistsAsync(
                value => value.SupervisionRelationshipId == relationship.Id
                         && value.Permission == item.Permission,
                cancellationToken);
            if (!alreadyGranted)
            {
                await permissionRepo.AddAsync(new SupervisionPermission
                {
                    SupervisionRelationshipId = relationship.Id,
                    Permission = item.Permission
                }, cancellationToken);
            }
        }

        permissionRequest.Status = PermissionRequestStatus.Accepted;
        permissionRequest.RespondedAt = DateTime.UtcNow;
        permissionRequest.RespondedByUserId = ownerUserId;
        _unitOfWork.Repository<SupervisionPermissionRequest>().Update(permissionRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notificationService.CreateAsync(
            permissionRequest.RequesterUserId, NotificationType.PermissionRequestAccepted,
            JsonSerializer.Serialize(new
            {
                permissionRequestId = permissionRequest.Id,
                permissions = permissionRequest.Items.Select(item => item.Permission.ToString())
            }), cancellationToken);

        return MapPermissionRequest(permissionRequest);
    }

    public async Task<PermissionRequestDto> RejectPermissionRequestAsync(
        int permissionRequestId, int ownerUserId, CancellationToken cancellationToken = default)
    {
        var permissionRequest = await LoadPermissionRequestOrThrowAsync(
            permissionRequestId, cancellationToken);

        var relationship = await _unitOfWork.Repository<SupervisionRelationship>()
            .GetByIdAsync(permissionRequest.SupervisionRelationshipId, cancellationToken);
        if (relationship == null)
        {
            throw new NotFoundException(
                "Quan hệ giám sát", permissionRequest.SupervisionRelationshipId);
        }

        await _accessGuard.EnsureOwnerAsync(
            relationship.ChildProfileId, ownerUserId, cancellationToken);

        if (permissionRequest.Status != PermissionRequestStatus.Pending)
        {
            throw new BadRequestException("Yêu cầu xin quyền đã được xử lý trước đó.");
        }

        permissionRequest.Status = PermissionRequestStatus.Rejected;
        permissionRequest.RespondedAt = DateTime.UtcNow;
        permissionRequest.RespondedByUserId = ownerUserId;
        _unitOfWork.Repository<SupervisionPermissionRequest>().Update(permissionRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notificationService.CreateAsync(
            permissionRequest.RequesterUserId, NotificationType.PermissionRequestRejected,
            JsonSerializer.Serialize(new { permissionRequestId = permissionRequest.Id }),
            cancellationToken);

        return MapPermissionRequest(permissionRequest);
    }

    public async Task<List<PermissionRequestDto>> ListPermissionRequestsAsync(
        int supervisionRelationshipId, int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var relationship = await _unitOfWork.Repository<SupervisionRelationship>()
            .GetByIdAsync(supervisionRelationshipId, cancellationToken);
        if (relationship == null)
        {
            throw new NotFoundException("Quan hệ giám sát", supervisionRelationshipId);
        }

        await _accessGuard.EnsureActiveSupervisionAsync(
            relationship.ChildProfileId, currentUserId, cancellationToken);

        var requests = await _unitOfWork.Repository<SupervisionPermissionRequest>().FindAsync(
            value => value.SupervisionRelationshipId == supervisionRelationshipId,
            includeProperties: "Items",
            cancellationToken: cancellationToken);

        return requests.Select(MapPermissionRequest).ToList();
    }

    private async Task<SupervisionPermissionRequest> LoadPermissionRequestOrThrowAsync(
        int permissionRequestId, CancellationToken cancellationToken)
    {
        var permissionRequest = await _unitOfWork.Repository<SupervisionPermissionRequest>()
            .FirstOrDefaultAsync(
                value => value.Id == permissionRequestId,
                includeProperties: "Items",
                cancellationToken: cancellationToken);
        if (permissionRequest == null)
        {
            throw new NotFoundException("Yêu cầu xin quyền", permissionRequestId);
        }

        return permissionRequest;
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

    private static PermissionRequestDto MapPermissionRequest(
        SupervisionPermissionRequest request) => new()
    {
        Id = request.Id,
        SupervisionRelationshipId = request.SupervisionRelationshipId,
        RequesterUserId = request.RequesterUserId,
        Status = request.Status.ToString(),
        Permissions = request.Items.Select(item => item.Permission.ToString()).ToList(),
        CreatedAt = request.CreatedAt,
        RespondedAt = request.RespondedAt
    };

    private static bool IsPendingOwnershipTransfer(OwnershipTransferRequestStatus status) =>
        status is OwnershipTransferRequestStatus.Pending
            or OwnershipTransferRequestStatus.PendingOwnerResponse;

    private static bool IsOwnerResponseOwnershipTransfer(OwnershipTransferRequestStatus status) =>
        status is OwnershipTransferRequestStatus.PendingOwnerResponse
            or OwnershipTransferRequestStatus.AcceptedByOwner
            or OwnershipTransferRequestStatus.RejectedByOwner;

    private static int GetOwnershipTransferRequesterUserId(OwnershipTransferRequest request) =>
        IsOwnerResponseOwnershipTransfer(request.Status)
            ? request.TargetSupervisorUserId
            : request.CurrentOwnerUserId;

    private static int GetOwnershipTransferResponderUserId(OwnershipTransferRequest request) =>
        IsOwnerResponseOwnershipTransfer(request.Status)
            ? request.CurrentOwnerUserId
            : request.TargetSupervisorUserId;

    private static string GetOwnershipTransferPublicStatus(OwnershipTransferRequestStatus status) =>
        status switch
        {
            OwnershipTransferRequestStatus.PendingOwnerResponse => "Pending",
            OwnershipTransferRequestStatus.AcceptedByOwner => "Accepted",
            OwnershipTransferRequestStatus.RejectedByOwner => "Rejected",
            _ => status.ToString()
        };

    private static OwnershipTransferRequestDto MapOwnershipTransferRequest(
        OwnershipTransferRequest request) => new()
    {
        Id = request.Id,
        ChildProfileId = request.ChildProfileId,
        CurrentOwnerUserId = request.CurrentOwnerUserId,
        TargetSupervisorUserId = request.TargetSupervisorUserId,
        RequesterUserId = GetOwnershipTransferRequesterUserId(request),
        ResponderUserId = GetOwnershipTransferResponderUserId(request),
        Status = GetOwnershipTransferPublicStatus(request.Status),
        CreatedAt = request.CreatedAt,
        RespondedAt = request.RespondedAt
    };
}
