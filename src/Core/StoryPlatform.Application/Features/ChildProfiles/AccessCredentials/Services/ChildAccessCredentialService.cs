using System.Buffers.Text;
using System.Security.Cryptography;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
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
    private readonly IChildProfileAccessGuard _accessGuard;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IClientAttemptLimiter _attemptLimiter;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IChildRoadmapEligibility _eligibility;

    public ChildAccessCredentialService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard accessGuard,
        IJwtTokenGenerator jwtTokenGenerator,
        IClientAttemptLimiter attemptLimiter,
        IAuditLogWriter auditLogWriter,
        IChildRoadmapEligibility eligibility)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _jwtTokenGenerator = jwtTokenGenerator;
        _attemptLimiter = attemptLimiter;
        _auditLogWriter = auditLogWriter;
        _eligibility = eligibility;
    }

    public async Task<ChildAccessCredentialDto> GetCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        var credential = await FindActiveCredentialAsync(childProfileId, cancellationToken);

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
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var secret = GenerateSecret();
        var now = DateTime.UtcNow;
        var active = await FindActiveCredentialAsync(childProfileId, cancellationToken);
        var isRegeneration = active != null;
        var avatarId = active?.AvatarId ?? DefaultAvatarId;
        object? beforeState = null;
        var endedChildSessions = 0;

        ChildAccessCredential credential;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (active != null)
            {
                beforeState = new
                {
                    childProfileId,
                    easyLoginCreatedAt = active.EasyLoginCreatedAt
                };
                MarkRevoked(active, now);
                credentialRepo.Update(active);
                endedChildSessions = await EndChildSessionsAsync(
                    active.Id, now, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            credential = new ChildAccessCredential
            {
                ChildProfileId = childProfileId,
                AvatarId = avatarId,
                EasyLoginSecretHash = TokenHasher.Hash(secret),
                EasyLoginCreatedAt = now,
                CreatedByUserId = currentUserId
            };
            await credentialRepo.AddAsync(credential, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

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
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await FindActiveCredentialAsync(childProfileId, cancellationToken);
        if (credential == null)
        {
            return;
        }

        // Soft-revoke: giữ dòng cho audit "trẻ tự vào" (child_sessions/reading_sessions trỏ tới nó)
        // và kết thúc ngay mọi Child Session sinh ra từ credential này.
        var now = DateTime.UtcNow;
        var easyLoginCreatedAt = credential.EasyLoginCreatedAt;
        MarkRevoked(credential, now);
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
            value => value.EasyLoginSecretHash == secretHash && value.RevokedAt == null,
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
            credential.ChildProfileId, credential.AvatarId, credential.Id, null,
            ChildEntryMethod.EasyLogin, cancellationToken);
    }

    public async Task<ChildSessionDto> StartSupervisedSessionAsync(
        int childProfileId, int currentUserId, string supervisorRefreshToken,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        var supervisorSession = await ResolveSupervisorSessionAsync(
            currentUserId, supervisorRefreshToken, cancellationToken);
        await EnsureChildCanEnterAsync(childProfileId, cancellationToken);

        // Avatar chỉ để hiển thị; trẻ chưa có credential vẫn vào được qua lối Supervisor.
        var credential = await FindActiveCredentialAsync(childProfileId, cancellationToken);

        return await StartChildSessionAsync(
            childProfileId, credential?.AvatarId ?? string.Empty, null, supervisorSession.Id,
            ChildEntryMethod.ProfileSwitcher, cancellationToken);
    }

    public async Task<StoryHandoverOptionsDto> GetStoryHandoverOptionsAsync(
        int storyId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var children = await _unitOfWork.Repository<ChildProfile>().FindAsync(
            value => value.OwnerUserId == currentUserId
                     && !value.IsDeleted
                     && value.Status != ChildProfileStatus.Archived
                     && value.Status != ChildProfileStatus.Deleted,
            cancellationToken: cancellationToken);

        var options = new List<StoryHandoverChildDto>();
        int? storyOwnerChildId = null;
        foreach (var child in children.OrderBy(value => value.Id))
        {
            var (skipCode, story) = await EvaluateStoryForChildAsync(child, storyId, cancellationToken);
            if (story is { Scope: StoryScope.Parent })
            {
                storyOwnerChildId = story.ChildProfileId;
            }

            options.Add(new StoryHandoverChildDto
            {
                ChildProfileId = child.Id,
                Nickname = child.Nickname,
                CanRead = skipCode == null,
                SkipCode = skipCode,
                Message = skipCode == null ? null : HandoverMessageFor(skipCode)
            });
        }

        var readable = options.Where(option => option.CanRead).ToList();
        int? suggested = readable.Any(option => option.ChildProfileId == storyOwnerChildId)
            ? storyOwnerChildId
            : readable.Count == 1 ? readable[0].ChildProfileId : null;

        return new StoryHandoverOptionsDto
        {
            StoryId = storyId,
            SuggestedChildProfileId = suggested,
            Children = options
        };
    }

    // "Cho bé đọc truyện này" — cùng luật mở truyện với ChildReadingService.StartAsync; Hold Mode chưa áp ở luồng này.
    public async Task<ChildStoryHandoverDto> StartStoryHandoverAsync(
        int childProfileId, int currentUserId, int storyId, string supervisorRefreshToken,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);
        var supervisorSession = await ResolveSupervisorSessionAsync(
            currentUserId, supervisorRefreshToken, cancellationToken);
        var child = await EnsureChildCanEnterAsync(childProfileId, cancellationToken);

        var (skipCode, _) = await EvaluateStoryForChildAsync(child, storyId, cancellationToken);
        if (skipCode != null)
        {
            throw new ForbiddenException(HandoverMessageFor(skipCode));
        }

        var credential = await FindActiveCredentialAsync(childProfileId, cancellationToken);
        var session = await StartChildSessionAsync(
            childProfileId, credential?.AvatarId ?? string.Empty, null, supervisorSession.Id,
            ChildEntryMethod.SupervisorPick, cancellationToken);

        return new ChildStoryHandoverDto
        {
            ChildProfileId = session.ChildProfileId,
            AvatarId = session.AvatarId,
            AccessToken = session.AccessToken,
            ExpiresInSeconds = session.ExpiresInSeconds,
            StoryId = storyId
        };
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
    private async Task<ChildProfile> EnsureChildCanEnterAsync(
        int childProfileId, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null || profile.IsDeleted || profile.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildEntryBlockedMessage);
        }

        return profile;
    }

    private async Task<(string? SkipCode, Story? Story)> EvaluateStoryForChildAsync(
        ChildProfile child, int storyId, CancellationToken cancellationToken)
    {
        if (child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            return (ChildRoadmapSkipCodes.ProfileInactive, null);
        }

        var found = await ChildStoryLookup.FindAsync(_unitOfWork, child.Id, storyId, cancellationToken);
        if (found == null)
        {
            return (ChildRoadmapSkipCodes.StoryUnavailable, null);
        }

        var reason = await _eligibility.EvaluateAsync(child, found.Value.Story, cancellationToken);
        return (reason == null ? null : SkipCodeFor(reason.Value), found.Value.Story);
    }

    private static string SkipCodeFor(ChildRoadmapSkipReason reason) => reason switch
    {
        ChildRoadmapSkipReason.BlockedTopic => ChildRoadmapSkipCodes.BlockedTopic,
        ChildRoadmapSkipReason.WrongAge => ChildRoadmapSkipCodes.WrongAge,
        ChildRoadmapSkipReason.TooLong => ChildRoadmapSkipCodes.TooLong,
        _ => ChildRoadmapSkipCodes.StoryUnavailable
    };

    // Cùng câu báo với lộ trình (ChildRoadmapService.MessageFor) — phụ huynh thấy lý do, trẻ thì không.
    private static string HandoverMessageFor(string skipCode) => skipCode switch
    {
        ChildRoadmapSkipCodes.ProfileInactive => "Hồ sơ của bé đang không hoạt động.",
        ChildRoadmapSkipCodes.BlockedTopic => "Truyện thuộc chủ đề đã bị chặn với bé này.",
        ChildRoadmapSkipCodes.WrongAge => "Truyện không đúng độ tuổi của bé này.",
        ChildRoadmapSkipCodes.TooLong => "Truyện dài hơn mức cho phép của bé này.",
        _ => "Truyện hiện không còn đọc được."
    };

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
        ChildEntryMethod entryMethod, CancellationToken cancellationToken)
    {
        var session = new ChildSession
        {
            ChildProfileId = childProfileId,
            ChildAccessCredentialId = childAccessCredentialId,
            SupervisorSessionId = supervisorSessionId,
            EntryMethod = entryMethod,
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

    private Task<ChildAccessCredential?> FindActiveCredentialAsync(
        int childProfileId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<ChildAccessCredential>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId && value.RevokedAt == null,
            cancellationToken: cancellationToken);

    private static void MarkRevoked(ChildAccessCredential credential, DateTime now)
    {
        credential.RevokedAt = now;
        credential.EasyLoginSecretHash = null;
        credential.UpdatedAt = now;
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
