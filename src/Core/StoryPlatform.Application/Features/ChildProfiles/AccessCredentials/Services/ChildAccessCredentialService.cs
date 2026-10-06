using System.Buffers.Text;
using System.Security.Cryptography;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Services;

public class ChildAccessCredentialService : IChildAccessCredentialService
{
    public const string DefaultAvatarId = "avatar-default";

    // BR-1.15: secret ≥128 bit — dùng 256 bit (32 byte).
    private const int SecretByteLength = 32;

    public const string TooManyFailedScansMessage =
        "Quét sai quá nhiều lần trên thiết bị này. Vui lòng thử lại sau 5 phút.";

    // Bước 3.0 — thông điệp duy nhất cho mọi lý do bị chặn; không lộ trạng thái nghiệp vụ cho trẻ.
    public const string ChildEntryBlockedMessage =
        "Bạn chưa vào đọc truyện được lúc này. Hãy nhờ bố mẹ hoặc thầy cô giúp nhé!";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IClientAttemptLimiter _attemptLimiter;
    private readonly IAuditLogWriter _auditLogWriter;

    public ChildAccessCredentialService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IJwtTokenGenerator jwtTokenGenerator,
        IClientAttemptLimiter attemptLimiter,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _jwtTokenGenerator = jwtTokenGenerator;
        _attemptLimiter = attemptLimiter;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<ChildAccessCredentialDto> GetCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var credential = await _unitOfWork.Repository<ChildAccessCredential>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        var hasEasyLogin = credential?.EasyLoginSecretHash != null;
        return new ChildAccessCredentialDto
        {
            ChildProfileId = childProfileId,
            HasEasyLogin = hasEasyLogin,
            EasyLoginCreatedAt = hasEasyLogin ? credential!.EasyLoginCreatedAt : null
        };
    }

    public async Task<EasyLoginSecretDto> CreateOrRegenerateEasyLoginAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        // BR-1.11: Owner hoặc Additional Supervisor có manage_safety_settings (Owner luôn qua).
        await _accessGuard.EnsurePermissionAsync(
            childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        var secret = GenerateSecret();
        var now = DateTime.UtcNow;
        var isRegeneration = credential != null;
        object? beforeState = null;
        var endedChildSessions = 0;

        if (credential == null)
        {
            credential = new ChildAccessCredential
            {
                ChildProfileId = childProfileId,
                AvatarId = DefaultAvatarId,
                EasyLoginSecretHash = TokenHasher.Hash(secret),
                EasyLoginCreatedAt = now,
                CreatedByUserId = currentUserId
            };
            await credentialRepo.AddAsync(credential, cancellationToken);
        }
        else
        {
            beforeState = new { childProfileId, easyLoginCreatedAt = credential.EasyLoginCreatedAt };

            // Tạo lại: secret cũ vô hiệu ngay và mọi Child Session sinh từ credential bị kết thúc (Mục 7c).
            credential.EasyLoginSecretHash = TokenHasher.Hash(secret);
            credential.EasyLoginCreatedAt = now;
            credential.UpdatedAt = now;
            credentialRepo.Update(credential);
            endedChildSessions = await EndChildSessionsAsync(credential.Id, now, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Không bao giờ ghi secret hoặc hash vào audit.
        await _auditLogWriter.LogAsync(
            currentUserId,
            isRegeneration ? "REGENERATE_EASY_LOGIN" : "CREATE_EASY_LOGIN",
            nameof(ChildAccessCredential), credential.Id,
            beforeState,
            new { childProfileId, easyLoginCreatedAt = now, endedChildSessions },
            cancellationToken);

        return new EasyLoginSecretDto { Secret = secret, CreatedAt = now };
    }

    public async Task RevokeCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsurePermissionAsync(
            childProfileId, currentUserId, Permission.ManageSafetySettings, cancellationToken);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (credential == null)
        {
            return;
        }

        // Soft-revoke: giữ dòng cho audit "trẻ tự vào" (child_sessions/reading_sessions trỏ tới nó)
        // và kết thúc ngay mọi Child Session sinh ra từ credential này.
        var now = DateTime.UtcNow;
        var easyLoginCreatedAt = credential.EasyLoginCreatedAt;
        credential.IsDeleted = true;
        credential.EasyLoginSecretHash = null;
        credential.UpdatedAt = now;
        credentialRepo.Update(credential);
        var endedChildSessions = await EndChildSessionsAsync(credential.Id, now, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            currentUserId, "REVOKE_EASY_LOGIN", nameof(ChildAccessCredential), credential.Id,
            new { childProfileId, easyLoginCreatedAt },
            new { childProfileId, endedChildSessions },
            cancellationToken);
    }

    public async Task<ChildSessionDto> LoginWithEasyLoginAsync(
        string secret, string clientKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new BadRequestException("Mã EasyLogin không hợp lệ hoặc đã bị thu hồi.");
        }

        if (_attemptLimiter.IsBlocked(clientKey))
        {
            throw new ForbiddenException(TooManyFailedScansMessage);
        }

        var secretHash = TokenHasher.Hash(secret.Trim());
        var credential = await _unitOfWork.Repository<ChildAccessCredential>().FirstOrDefaultAsync(
            value => value.EasyLoginSecretHash == secretHash,
            cancellationToken: cancellationToken);
        if (credential == null)
        {
            _attemptLimiter.RegisterFailure(clientKey);
            throw new BadRequestException("Mã EasyLogin không hợp lệ hoặc đã bị thu hồi.");
        }

        // Hồ sơ không Active chặn bằng thông điệp chung, KHÔNG tính là quét sai.
        await EnsureChildCanEnterAsync(credential.ChildProfileId, cancellationToken);
        _attemptLimiter.Reset(clientKey);

        return await StartChildSessionAsync(
            credential.ChildProfileId, credential.AvatarId, credential.Id, null, cancellationToken);
    }

    public async Task<ChildSessionDto> StartSupervisedSessionAsync(
        int childProfileId, int currentUserId, string supervisorRefreshToken,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);
        var supervisorSession = await ResolveSupervisorSessionAsync(
            currentUserId, supervisorRefreshToken, cancellationToken);
        await EnsureChildCanEnterAsync(childProfileId, cancellationToken);

        // Avatar chỉ để hiển thị; trẻ chưa có credential vẫn vào được qua lối Supervisor.
        var credential = await _unitOfWork.Repository<ChildAccessCredential>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        return await StartChildSessionAsync(
            childProfileId, credential?.AvatarId ?? string.Empty, null, supervisorSession.Id,
            cancellationToken);
    }

    public async Task<ChildSessionProfileDto> GetMySessionProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        return new ChildSessionProfileDto
        {
            ChildProfileId = profile.Id,
            Nickname = profile.Nickname,
            AgeBand = profile.AgeBand.ToString()
        };
    }

    // Hold Mode (Luồng 4) cố ý không chặn ở đây — trẻ cần vào đọc để được tự mở khoá khi cải thiện.
    private async Task EnsureChildCanEnterAsync(
        int childProfileId, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null || profile.IsDeleted || profile.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildEntryBlockedMessage);
        }
    }

    private async Task<RefreshToken> ResolveSupervisorSessionAsync(
        int currentUserId, string supervisorRefreshToken, CancellationToken cancellationToken)
    {
        const string invalidSessionMessage =
            "Phiên Supervisor không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.";
        if (string.IsNullOrWhiteSpace(supervisorRefreshToken))
        {
            throw new UnauthorizedException(invalidSessionMessage);
        }

        var tokenHash = TokenHasher.Hash(supervisorRefreshToken.Trim());
        var session = await _unitOfWork.Repository<RefreshToken>().FirstOrDefaultAsync(
            value => value.TokenHash == tokenHash && value.UserAccountId == currentUserId,
            cancellationToken: cancellationToken);
        if (session == null
            || session.UserAccountId != currentUserId
            || session.TokenHash != tokenHash
            || session.RevokedAt != null
            || session.ExpiresAt <= DateTime.UtcNow
            || session.SessionScope != SessionScope.Supervisor)
        {
            throw new UnauthorizedException(invalidSessionMessage);
        }

        return session;
    }

    private async Task<ChildSessionDto> StartChildSessionAsync(
        int childProfileId, string avatarId, int? childAccessCredentialId, int? supervisorSessionId,
        CancellationToken cancellationToken)
    {
        var session = new ChildSession
        {
            ChildProfileId = childProfileId,
            ChildAccessCredentialId = childAccessCredentialId,
            SupervisorSessionId = supervisorSessionId,
            SessionKey = Guid.NewGuid().ToString("N"),
            LastActivityAt = DateTime.UtcNow
        };
        await _unitOfWork.Repository<ChildSession>().AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ChildSessionDto
        {
            ChildProfileId = childProfileId,
            AvatarId = avatarId,
            AccessToken = _jwtTokenGenerator.GenerateChildAccessToken(childProfileId, session.SessionKey),
            ExpiresInSeconds = _jwtTokenGenerator.ChildTokenExpiresInSeconds
        };
    }

    private async Task<int> EndChildSessionsAsync(
        int credentialId, DateTime now, CancellationToken cancellationToken)
    {
        var sessionRepo = _unitOfWork.Repository<ChildSession>();
        var sessions = await sessionRepo.FindAsync(
            session => session.ChildAccessCredentialId == credentialId,
            cancellationToken: cancellationToken);
        foreach (var session in sessions)
        {
            session.IsDeleted = true;
            session.UpdatedAt = now;
            sessionRepo.Update(session);
        }

        return sessions.Count;
    }

    private static string GenerateSecret() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretByteLength));
}
