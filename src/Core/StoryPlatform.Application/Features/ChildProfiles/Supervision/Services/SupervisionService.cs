using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
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
    private readonly IAuditLogWriter _auditLogWriter;

    public SupervisionService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        INotificationService notificationService,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _notificationService = notificationService;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<InvitationDto> CreateInvitationAsync(
        int childProfileId, int inviterUserId, CreateInvitationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, inviterUserId, cancellationToken);

        if (request.ExpiresInDays is < 1 or > SupervisionDefaults.InvitationMaxExpiryDays)
        {
            throw new BadRequestException(
                $"Số ngày hết hạn phải từ 1 đến {SupervisionDefaults.InvitationMaxExpiryDays}.");
        }

        var email = request.InviteeEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BadRequestException("Email người được mời là bắt buộc.");
        }

        if (email.Length > 150)
        {
            throw new BadRequestException("Email người được mời tối đa 150 ký tự.");
        }

        if (!new EmailAddressAttribute().IsValid(email))
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

        await _auditLogWriter.LogAsync(
            inviterUserId, "CREATE_SUPERVISION_INVITATION", nameof(SupervisionInvitation), invitation.Id,
            null,
            new { childProfileId, inviteeEmail = MaskEmail(email), expiresAt = invitation.ExpiresAt },
            cancellationToken);

        var inviter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(inviterUserId, cancellationToken);
        var inviterName = inviter?.FullName ?? "Một người dùng AI Storytelling Platform";
        await _emailSender.SendSupervisionInvitationEmailAsync(
            email, inviterName, invitation.InvitationCode!, cancellationToken);

        return MapInvitation(invitation);
    }

    public async Task<ClaimInvitationResultDto> ClaimInvitationAsync(
        string invitationCode, int claimerUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await LoadPendingInvitationAsync(invitationCode, cancellationToken);
        if (string.IsNullOrWhiteSpace(invitation.InviteeEmail))
        {
            throw new BadRequestException("Lời mời không có email người nhận hợp lệ.");
        }

        var alreadySupervising = await _unitOfWork.Repository<SupervisionRelationship>()
            .ExistsAsync(
                value => value.ChildProfileId == invitation.ChildProfileId
                         && value.SupervisorUserId == claimerUserId
                         && value.RevokedAt == null,
                cancellationToken);
        if (alreadySupervising)
        {
            throw new BadRequestException("Bạn đã giám sát hồ sơ trẻ này.");
        }

        var now = DateTime.UtcNow;
        if (invitation.OtpExpiresAt.HasValue)
        {
            var lastSentAt = invitation.OtpExpiresAt.Value - SupervisionDefaults.InvitationOtpTtl;
            if (lastSentAt + SupervisionDefaults.InvitationOtpResendCooldown > now)
            {
                throw new BadRequestException("Vui lòng chờ trước khi yêu cầu gửi lại OTP.");
            }
        }

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        invitation.OtpHash = TokenHasher.Hash(OtpPayload(invitation.Id, otp));
        invitation.OtpExpiresAt = now + SupervisionDefaults.InvitationOtpTtl;
        invitation.OtpFailedAttempts = 0;
        invitation.OtpVerifiedAt = null;
        _unitOfWork.Repository<SupervisionInvitation>().Update(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            claimerUserId, "CLAIM_SUPERVISION_INVITATION", nameof(SupervisionInvitation), invitation.Id,
            null, new { childProfileId = invitation.ChildProfileId, channel = "email" }, cancellationToken);

        await _emailSender.SendSupervisionInvitationOtpEmailAsync(
            invitation.InviteeEmail, otp, cancellationToken);

        return new ClaimInvitationResultDto
        {
            MaskedEmail = MaskEmail(invitation.InviteeEmail),
            OtpExpiresAt = invitation.OtpExpiresAt.Value
        };
    }

    public async Task<InvitationPreviewDto> VerifyInvitationOtpAsync(
        string invitationCode, string otp, int verifierUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await LoadPendingInvitationAsync(invitationCode, cancellationToken);
        var now = DateTime.UtcNow;
        if (invitation.OtpHash == null
            || !invitation.OtpExpiresAt.HasValue
            || invitation.OtpExpiresAt.Value <= now)
        {
            throw new BadRequestException(
                "Mã OTP không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu gửi lại.");
        }

        var submittedHash = TokenHasher.Hash(
            OtpPayload(invitation.Id, (otp ?? string.Empty).Trim()));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(submittedHash),
                Encoding.UTF8.GetBytes(invitation.OtpHash)))
        {
            invitation.OtpFailedAttempts++;
            var locked = invitation.OtpFailedAttempts >= SupervisionDefaults.InvitationOtpMaxAttempts;
            if (locked)
            {
                invitation.OtpHash = null;
                invitation.OtpExpiresAt = null;
            }

            _unitOfWork.Repository<SupervisionInvitation>().Update(invitation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (locked)
            {
                await _auditLogWriter.LogAsync(
                    verifierUserId, "SUPERVISION_INVITATION_OTP_LOCKED", nameof(SupervisionInvitation), invitation.Id,
                    null,
                    new
                    {
                        childProfileId = invitation.ChildProfileId,
                        failedAttempts = invitation.OtpFailedAttempts
                    },
                    cancellationToken);
            }

            throw new BadRequestException("Mã OTP không chính xác.");
        }

        invitation.OtpHash = null;
        invitation.OtpExpiresAt = null;
        invitation.OtpFailedAttempts = 0;
        invitation.OtpVerifiedAt = now;
        invitation.InviteeUserId = verifierUserId;
        _unitOfWork.Repository<SupervisionInvitation>().Update(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            verifierUserId, "VERIFY_SUPERVISION_INVITATION_OTP", nameof(SupervisionInvitation), invitation.Id,
            null, new { childProfileId = invitation.ChildProfileId }, cancellationToken);

        var child = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(invitation.ChildProfileId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ trẻ", invitation.ChildProfileId);
        var inviter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(invitation.InviterUserId, cancellationToken);

        return new InvitationPreviewDto
        {
            InvitationId = invitation.Id,
            InviterName = inviter?.FullName ?? "Người dùng AI Storytelling Platform",
            ChildNickname = child.Nickname,
            ChildAgeBand = child.AgeBand.ToString()
        };
    }

    public async Task<SupervisionRelationshipDto> AcceptInvitationAsync(
        string invitationCode, int accepterUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await LoadPendingInvitationAsync(invitationCode, cancellationToken);
        RequireVerifiedInvitation(invitation, accepterUserId);
        var invitationRepo = _unitOfWork.Repository<SupervisionInvitation>();

        var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
        var childProfile = await childProfileRepo.GetByIdAsync(
            invitation.ChildProfileId, cancellationToken);
        if (childProfile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", invitation.ChildProfileId);
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

        foreach (var permission in SupervisionDefaults.DefaultPreset)
        {
            await _unitOfWork.Repository<SupervisionPermission>().AddAsync(
                new SupervisionPermission
                {
                    SupervisionRelationship = relationship,
                    Permission = permission
                }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            accepterUserId, "ACCEPT_SUPERVISION_INVITATION", nameof(SupervisionInvitation), invitation.Id,
            new { status = InvitationStatus.Pending.ToString() },
            new
            {
                status = InvitationStatus.Accepted.ToString(),
                childProfileId = invitation.ChildProfileId,
                relationshipId = relationship.Id,
                role = relationship.SupervisorRole.ToString(),
                permissions = SupervisionDefaults.DefaultPreset.Select(permission => permission.ToString()).ToList()
            },
            cancellationToken);
        await NotifyOwnerAsync(
            invitation.ChildProfileId, NotificationType.SupervisionAccepted, invitation.Id,
            accepterUserId, cancellationToken);

        return MapRelationship(relationship);
    }

    public async Task RejectInvitationAsync(
        string invitationCode, int rejecterUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await LoadPendingInvitationAsync(invitationCode, cancellationToken);
        RequireVerifiedInvitation(invitation, rejecterUserId);

        invitation.Status = InvitationStatus.Rejected;
        invitation.RespondedAt = DateTime.UtcNow;
        _unitOfWork.Repository<SupervisionInvitation>().Update(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            rejecterUserId, "REJECT_SUPERVISION_INVITATION", nameof(SupervisionInvitation), invitation.Id,
            new { status = InvitationStatus.Pending.ToString() },
            new { status = InvitationStatus.Rejected.ToString(), childProfileId = invitation.ChildProfileId },
            cancellationToken);
        await NotifyOwnerAsync(
            invitation.ChildProfileId, NotificationType.SupervisionRejected, invitation.Id,
            rejecterUserId, cancellationToken);
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

        // Owner luôn là Parent và không bị thu hồi qua đây, nên hồ sơ không đổi trạng thái (Bước 1.6b).
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            revokerUserId, "REVOKE_SUPERVISION", nameof(SupervisionRelationship), target.Id,
            new
            {
                childProfileId = target.ChildProfileId,
                supervisorUserId = target.SupervisorUserId,
                role = target.SupervisorRole.ToString()
            },
            new { revokedByUserId = revokerUserId, target.RevokedAt },
            cancellationToken);
    }

    public async Task<OwnershipTransferRequestDto> RequestOwnershipTransferAsync(
        int childProfileId, int requesterUserId, TransferOwnershipRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (requesterUserId == request.TargetSupervisorUserId)
        {
            throw new BadRequestException("Không thể chuyển nhượng quyền Owner cho chính mình.");
        }

        await _accessGuard.EnsureOwnerAsync(childProfileId, requesterUserId, cancellationToken);

        var relationshipRepo = _unitOfWork.Repository<SupervisionRelationship>();
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

        var now = DateTime.UtcNow;
        var requestRepo = _unitOfWork.Repository<OwnershipTransferRequest>();
        var hasPendingRequest = await requestRepo.ExistsAsync(
            value => value.ChildProfileId == childProfileId
                     && value.Status == OwnershipTransferRequestStatus.Pending
                     && (value.ExpiresAt == null || value.ExpiresAt > now),
            cancellationToken);
        if (hasPendingRequest)
        {
            throw new ConflictException(
                "Hồ sơ trẻ đã có một yêu cầu chuyển quyền sở hữu đang chờ xử lý.");
        }

        var transferRequest = new OwnershipTransferRequest
        {
            ChildProfileId = childProfileId,
            CurrentOwnerUserId = requesterUserId,
            TargetSupervisorUserId = request.TargetSupervisorUserId,
            Status = OwnershipTransferRequestStatus.Pending,
            ExpiresAt = now + SupervisionDefaults.OwnershipTransferTtl
        };

        await requestRepo.AddAsync(transferRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            requesterUserId, "REQUEST_OWNERSHIP_TRANSFER", nameof(OwnershipTransferRequest), transferRequest.Id,
            null,
            new
            {
                childProfileId,
                targetUserId = request.TargetSupervisorUserId,
                expiresAt = transferRequest.ExpiresAt
            },
            cancellationToken);

        await _notificationService.CreateAsync(
            transferRequest.TargetSupervisorUserId, NotificationType.OwnershipTransferRequested,
            JsonSerializer.Serialize(new
            {
                ownershipTransferRequestId = transferRequest.Id,
                childProfileId,
                requesterUserId,
                responderUserId = transferRequest.TargetSupervisorUserId
            }), cancellationToken);

        return MapOwnershipTransferRequest(transferRequest);
    }

    public async Task<SupervisionRelationshipDto> AcceptOwnershipTransferAsync(
        int ownershipTransferRequestId, int accepterUserId,
        CancellationToken cancellationToken = default)
    {
        var requestRepo = _unitOfWork.Repository<OwnershipTransferRequest>();
        var transferRequest = await requestRepo.GetByIdAsync(
            ownershipTransferRequestId, cancellationToken)
            ?? throw new NotFoundException(
                "Yêu cầu chuyển nhượng quyền Owner", ownershipTransferRequestId);

        if (transferRequest.Status != OwnershipTransferRequestStatus.Pending)
        {
            throw new BadRequestException(
                "Yêu cầu chuyển nhượng quyền Owner đã được xử lý trước đó.");
        }

        if (accepterUserId != transferRequest.TargetSupervisorUserId)
        {
            throw new ForbiddenException(
                "Chỉ Additional Supervisor được chỉ định mới có thể chấp nhận yêu cầu này.");
        }

        if (IsExpired(transferRequest, DateTime.UtcNow))
        {
            transferRequest.Status = OwnershipTransferRequestStatus.Expired;
            requestRepo.Update(transferRequest);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.LogAsync(
                null, "EXPIRE_OWNERSHIP_TRANSFER", nameof(OwnershipTransferRequest), transferRequest.Id,
                new { status = OwnershipTransferRequestStatus.Pending.ToString() },
                new { status = OwnershipTransferRequestStatus.Expired.ToString() }, cancellationToken);
            throw new BadRequestException("Yêu cầu chuyển nhượng quyền Owner đã hết hạn.");
        }

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
                    "Yêu cầu chuyển nhượng không còn hợp lệ vì Owner trong yêu cầu không còn giữ quyền Owner.");
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
            var targetPermissions = await permissionRepo.FindAsync(
                value => value.SupervisionRelationshipId == targetRelationship.Id,
                cancellationToken: cancellationToken);
            if (targetPermissions.Count > 0)
            {
                permissionRepo.DeleteRange(targetPermissions);
            }

            foreach (var permission in SupervisionDefaults.DefaultPreset)
            {
                await permissionRepo.AddAsync(new SupervisionPermission
                {
                    SupervisionRelationshipId = ownerRelationship.Id,
                    Permission = permission
                }, cancellationToken);
            }

            var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
            var childProfile = await childProfileRepo.GetByIdAsync(
                transferRequest.ChildProfileId, cancellationToken)
                ?? throw new NotFoundException("Hồ sơ trẻ", transferRequest.ChildProfileId);
            childProfile.OwnerUserId = transferRequest.TargetSupervisorUserId;
            childProfile.UpdatedAt = DateTime.UtcNow;
            childProfileRepo.Update(childProfile);

            transferRequest.Status = OwnershipTransferRequestStatus.Accepted;
            transferRequest.RespondedAt = DateTime.UtcNow;
            requestRepo.Update(transferRequest);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLogWriter.LogAsync(
            accepterUserId, "ACCEPT_OWNERSHIP_TRANSFER", nameof(OwnershipTransferRequest), transferRequest.Id,
            new { ownerUserId = transferRequest.CurrentOwnerUserId },
            new { ownerUserId = transferRequest.TargetSupervisorUserId },
            cancellationToken);

        await _notificationService.CreateAsync(
            transferRequest.CurrentOwnerUserId, NotificationType.OwnershipTransferAccepted,
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
            ownershipTransferRequestId, cancellationToken)
            ?? throw new NotFoundException(
                "Yêu cầu chuyển nhượng quyền Owner", ownershipTransferRequestId);

        if (transferRequest.Status != OwnershipTransferRequestStatus.Pending)
        {
            throw new BadRequestException(
                "Yêu cầu chuyển nhượng quyền Owner đã được xử lý trước đó.");
        }

        if (rejecterUserId != transferRequest.TargetSupervisorUserId)
        {
            throw new ForbiddenException(
                "Chỉ Additional Supervisor được chỉ định mới có thể từ chối yêu cầu này.");
        }

        transferRequest.Status = OwnershipTransferRequestStatus.Rejected;
        transferRequest.RespondedAt = DateTime.UtcNow;
        requestRepo.Update(transferRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            rejecterUserId, "REJECT_OWNERSHIP_TRANSFER", nameof(OwnershipTransferRequest), transferRequest.Id,
            new { status = OwnershipTransferRequestStatus.Pending.ToString() },
            new { status = OwnershipTransferRequestStatus.Rejected.ToString() }, cancellationToken);

        await _notificationService.CreateAsync(
            transferRequest.CurrentOwnerUserId, NotificationType.OwnershipTransferRejected,
            JsonSerializer.Serialize(new
            {
                ownershipTransferRequestId = transferRequest.Id,
                childProfileId = transferRequest.ChildProfileId
            }), cancellationToken);

        return MapOwnershipTransferRequest(transferRequest);
    }

    public async Task<OwnershipTransferRequestDto> CancelOwnershipTransferAsync(
        int ownershipTransferRequestId, int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var requestRepo = _unitOfWork.Repository<OwnershipTransferRequest>();
        var transferRequest = await requestRepo.GetByIdAsync(
            ownershipTransferRequestId, cancellationToken)
            ?? throw new NotFoundException(
                "Yêu cầu chuyển nhượng quyền Owner", ownershipTransferRequestId);

        if (transferRequest.CurrentOwnerUserId != ownerUserId)
        {
            throw new ForbiddenException("Chỉ Owner đã tạo yêu cầu mới có thể huỷ yêu cầu này.");
        }

        if (transferRequest.Status != OwnershipTransferRequestStatus.Pending)
        {
            throw new BadRequestException("Chỉ có thể huỷ yêu cầu chuyển quyền đang chờ xử lý.");
        }

        transferRequest.Status = OwnershipTransferRequestStatus.Cancelled;
        transferRequest.RespondedAt = DateTime.UtcNow;
        requestRepo.Update(transferRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            ownerUserId, "CANCEL_OWNERSHIP_TRANSFER", nameof(OwnershipTransferRequest), transferRequest.Id,
            new { status = OwnershipTransferRequestStatus.Pending.ToString() },
            new { status = OwnershipTransferRequestStatus.Cancelled.ToString() }, cancellationToken);

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
            await _auditLogWriter.LogAsync(
                ownerUserId, "GRANT_SUPERVISION_PERMISSION", nameof(SupervisionRelationship), target.Id,
                null,
                new
                {
                    childProfileId = target.ChildProfileId,
                    supervisorUserId = target.SupervisorUserId,
                    permission = permission.ToString()
                },
                cancellationToken);
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
            await _auditLogWriter.LogAsync(
                ownerUserId, "REVOKE_SUPERVISION_PERMISSION", nameof(SupervisionRelationship), target.Id,
                new
                {
                    childProfileId = target.ChildProfileId,
                    supervisorUserId = target.SupervisorUserId,
                    permission = permission.ToString()
                },
                null, cancellationToken);
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

        await _accessGuard.EnsureOwnerAsync(invitation.ChildProfileId, currentUserId, cancellationToken);

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new BadRequestException("Chỉ có thể huỷ lời mời đang ở trạng thái Pending.");
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.RespondedAt = DateTime.UtcNow;
        invitationRepo.Update(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            currentUserId, "CANCEL_SUPERVISION_INVITATION", nameof(SupervisionInvitation), invitation.Id,
            new { status = InvitationStatus.Pending.ToString() },
            new { status = InvitationStatus.Revoked.ToString(), childProfileId = invitation.ChildProfileId },
            cancellationToken);
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

        await _auditLogWriter.LogAsync(
            ownerUserId, "ACCEPT_PERMISSION_REQUEST", nameof(SupervisionPermissionRequest), permissionRequest.Id,
            null,
            new
            {
                supervisionRelationshipId = relationship.Id,
                permissions = permissionRequest.Items.Select(item => item.Permission.ToString()).ToList()
            },
            cancellationToken);

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

    public async Task<PermissionRequestDto> CancelPermissionRequestAsync(
        int permissionRequestId, int requesterUserId, CancellationToken cancellationToken = default)
    {
        var permissionRequest = await LoadPermissionRequestOrThrowAsync(
            permissionRequestId, cancellationToken);

        // BR-1.14 — chỉ Additional Supervisor đã tạo yêu cầu mới được tự huỷ.
        if (permissionRequest.RequesterUserId != requesterUserId)
        {
            throw new ForbiddenException("Bạn chỉ có thể huỷ yêu cầu xin quyền do chính mình tạo.");
        }

        if (permissionRequest.Status != PermissionRequestStatus.Pending)
        {
            throw new BadRequestException("Chỉ có thể huỷ yêu cầu xin quyền đang chờ xử lý.");
        }

        permissionRequest.Status = PermissionRequestStatus.Cancelled;
        permissionRequest.RespondedAt = DateTime.UtcNow;
        permissionRequest.RespondedByUserId = requesterUserId;
        _unitOfWork.Repository<SupervisionPermissionRequest>().Update(permissionRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

    private async Task<SupervisionInvitation> LoadPendingInvitationAsync(
        string invitationCode, CancellationToken cancellationToken)
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
            invitation.Status = InvitationStatus.Expired;
            invitation.RespondedAt = DateTime.UtcNow;
            invitationRepo.Update(invitation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("Mã mời đã hết hạn.");
        }

        return invitation;
    }

    private static void RequireVerifiedInvitation(
        SupervisionInvitation invitation, int currentUserId)
    {
        if (invitation.InviteeUserId != currentUserId
            || !invitation.OtpVerifiedAt.HasValue
            || invitation.OtpVerifiedAt.Value + SupervisionDefaults.InvitationVerifiedWindow <= DateTime.UtcNow)
        {
            throw new BadRequestException(
                "Bạn phải xác thực OTP hợp lệ trước khi phản hồi lời mời.");
        }
    }

    private async Task NotifyOwnerAsync(
        int childProfileId, NotificationType notificationType, int invitationId,
        int respondingUserId, CancellationToken cancellationToken)
    {
        var childProfile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var responder = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(respondingUserId, cancellationToken);

        await _notificationService.CreateAsync(
            childProfile.OwnerUserId, notificationType,
            JsonSerializer.Serialize(new
            {
                invitationId,
                childProfileId,
                supervisorUserId = respondingUserId,
                supervisorName = responder?.FullName ?? string.Empty
            }), cancellationToken);
    }

    private static string OtpPayload(int invitationId, string otp) =>
        $"{invitationId}:{otp}";

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "*****";
        }

        return $"{email[0]}*****{email[at..]}";
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

    private static OwnershipTransferRequestDto MapOwnershipTransferRequest(
        OwnershipTransferRequest request) => new()
    {
        Id = request.Id,
        ChildProfileId = request.ChildProfileId,
        CurrentOwnerUserId = request.CurrentOwnerUserId,
        TargetSupervisorUserId = request.TargetSupervisorUserId,
        RequesterUserId = request.CurrentOwnerUserId,
        ResponderUserId = request.TargetSupervisorUserId,
        Status = request.Status == OwnershipTransferRequestStatus.Pending
                 && request.ExpiresAt <= DateTime.UtcNow
            ? OwnershipTransferRequestStatus.Expired.ToString()
            : request.Status.ToString(),
        CreatedAt = request.CreatedAt,
        RespondedAt = request.RespondedAt,
        ExpiresAt = request.ExpiresAt
    };

    private static bool IsExpired(OwnershipTransferRequest request, DateTime now) =>
        request.ExpiresAt.HasValue && request.ExpiresAt.Value <= now;
}
