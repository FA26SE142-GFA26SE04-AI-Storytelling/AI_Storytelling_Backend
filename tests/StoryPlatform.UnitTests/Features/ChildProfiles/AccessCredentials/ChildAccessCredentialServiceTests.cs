using System.Buffers.Text;
using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.AccessCredentials;

public class ChildAccessCredentialServiceTests
{
    private const string ClientKey = "203.0.113.7";

    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepo = new();
    private readonly Mock<IGenericRepository<ChildSession>> _sessionRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<RefreshToken>> _refreshTokenRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGenerator = new();
    private readonly Mock<IClientAttemptLimiter> _limiter = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly List<(int? Actor, string Action, string EntityType, int EntityId, string? Before, string? After)> _audits = new();
    private readonly ChildAccessCredentialService _sut;

    public ChildAccessCredentialServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildAccessCredential>()).Returns(_credentialRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildSession>()).Returns(_sessionRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<RefreshToken>()).Returns(_refreshTokenRepo.Object);
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Status = ChildProfileStatus.Active });
        _jwtTokenGenerator.Setup(generator => generator.ChildTokenExpiresInSeconds).Returns(14400);
        _auditLogWriter.Setup(w => w.LogAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, string, int, object?, object?, CancellationToken>(
                (actor, action, entityType, entityId, before, after, _) => _audits.Add((
                    actor, action, entityType, entityId,
                    before == null ? null : JsonSerializer.Serialize(before),
                    after == null ? null : JsonSerializer.Serialize(after))))
            .Returns(Task.CompletedTask);
        _sut = new ChildAccessCredentialService(
            _unitOfWork.Object, _guard.Object, _jwtTokenGenerator.Object, _limiter.Object,
            _auditLogWriter.Object);
    }

    // ---------- CreateOrRegenerateEasyLoginAsync ----------

    [Fact]
    public async Task CreateOrRegenerateEasyLoginAsync_WithoutManageSafetyPermission_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(
                1, 3, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateOrRegenerateEasyLoginAsync(1, 3));

        _credentialRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildAccessCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task CreateOrRegenerateEasyLoginAsync_NoCredential_CreatesCredentialAndStoresOnlyHash()
    {
        AllowManageSafety();
        SetupCredentials();
        ChildAccessCredential? added = null;
        _credentialRepo.Setup(r => r.AddAsync(
                It.IsAny<ChildAccessCredential>(), It.IsAny<CancellationToken>()))
            .Callback<ChildAccessCredential, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((ChildAccessCredential value, CancellationToken _) => value);

        var result = await _sut.CreateOrRegenerateEasyLoginAsync(1, 2);

        Assert.NotNull(added);
        Assert.Equal(1, added!.ChildProfileId);
        Assert.Equal(2, added.CreatedByUserId);
        Assert.Equal(ChildAccessCredentialService.DefaultAvatarId, added.AvatarId);
        Assert.Equal(TokenHasher.Hash(result.Secret), added.EasyLoginSecretHash);
        Assert.NotEqual(result.Secret, added.EasyLoginSecretHash);
        Assert.NotNull(added.EasyLoginCreatedAt);
        Assert.Equal(added.EasyLoginCreatedAt, result.CreatedAt);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var createAudit = Assert.Single(_audits);
        Assert.Equal(2, createAudit.Actor);
        Assert.Equal("CREATE_EASY_LOGIN", createAudit.Action);
        Assert.Equal(nameof(ChildAccessCredential), createAudit.EntityType);
        Assert.Contains("\"childProfileId\":1", createAudit.After);
        Assert.DoesNotContain(result.Secret, createAudit.After);
        Assert.DoesNotContain(added.EasyLoginSecretHash!, createAudit.After);
    }

    [Fact]
    public async Task CreateOrRegenerateEasyLoginAsync_SecretIsAtLeast128BitAndDiffersEachCall()
    {
        AllowManageSafety();
        SetupCredentials();

        var first = await _sut.CreateOrRegenerateEasyLoginAsync(1, 2);
        var second = await _sut.CreateOrRegenerateEasyLoginAsync(1, 2);

        Assert.True(Base64Url.DecodeFromChars(first.Secret).Length >= 16);
        Assert.NotEqual(first.Secret, second.Secret);
    }

    [Fact]
    public async Task CreateOrRegenerateEasyLoginAsync_ExistingCredential_ReplacesSecretAndEndsItsSessions()
    {
        AllowManageSafety();
        var credential = Credential("old-secret");
        var oldHash = credential.EasyLoginSecretHash;
        SetupCredentials(credential);
        var session = new ChildSession { Id = 9, ChildProfileId = 1, ChildAccessCredentialId = 1 };
        _sessionRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ChildSession, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChildSession> { session });

        var result = await _sut.CreateOrRegenerateEasyLoginAsync(1, 2);

        Assert.NotEqual(oldHash, credential.EasyLoginSecretHash);
        Assert.Equal(TokenHasher.Hash(result.Secret), credential.EasyLoginSecretHash);
        Assert.True(session.IsDeleted);
        _credentialRepo.Verify(r => r.Update(credential), Times.Once);
        _credentialRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildAccessCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionRepo.Verify(r => r.Update(session), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var regenerateAudit = Assert.Single(_audits);
        Assert.Equal("REGENERATE_EASY_LOGIN", regenerateAudit.Action);
        Assert.Equal(1, regenerateAudit.EntityId);
        Assert.Contains("\"endedChildSessions\":1", regenerateAudit.After);
        Assert.DoesNotContain(result.Secret, regenerateAudit.After);
    }

    // ---------- RevokeCredentialAsync ----------

    [Fact]
    public async Task RevokeCredentialAsync_WithoutManageSafetyPermission_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(
                1, 3, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RevokeCredentialAsync(1, 3));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_audits);
    }

    [Fact]
    public async Task RevokeCredentialAsync_Existing_SoftDeletesClearsSecretAndEndsSessions()
    {
        AllowManageSafety();
        var credential = Credential("qr-secret");
        SetupCredentials(credential);
        var session = new ChildSession { Id = 9, ChildProfileId = 1, ChildAccessCredentialId = 1 };
        _sessionRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<ChildSession, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChildSession> { session });

        await _sut.RevokeCredentialAsync(1, 2);

        Assert.True(credential.IsDeleted);
        Assert.Null(credential.EasyLoginSecretHash);
        Assert.True(session.IsDeleted);
        _credentialRepo.Verify(r => r.Delete(It.IsAny<ChildAccessCredential>()), Times.Never);
        _credentialRepo.Verify(r => r.Update(credential), Times.Once);
        _sessionRepo.Verify(r => r.Update(session), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var revokeAudit = Assert.Single(_audits);
        Assert.Equal(2, revokeAudit.Actor);
        Assert.Equal("REVOKE_EASY_LOGIN", revokeAudit.Action);
        Assert.Equal(1, revokeAudit.EntityId);
        Assert.Contains("\"endedChildSessions\":1", revokeAudit.After);
    }

    [Fact]
    public async Task RevokeCredentialAsync_NoCredential_IsNoOp()
    {
        AllowManageSafety();
        SetupCredentials();

        await _sut.RevokeCredentialAsync(1, 2);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_audits);
    }

    // ---------- GetCredentialAsync ----------

    [Fact]
    public async Task GetCredentialAsync_NoCredential_ReturnsHasEasyLoginFalse()
    {
        AllowSupervision();
        SetupCredentials();

        var result = await _sut.GetCredentialAsync(1, 2);

        Assert.Equal(1, result.ChildProfileId);
        Assert.False(result.HasEasyLogin);
        Assert.Null(result.EasyLoginCreatedAt);
    }

    [Fact]
    public async Task GetCredentialAsync_WithSecret_ReturnsFlagAndCreatedAt()
    {
        AllowSupervision();
        var credential = Credential("qr-secret");
        SetupCredentials(credential);

        var result = await _sut.GetCredentialAsync(1, 2);

        Assert.True(result.HasEasyLogin);
        Assert.Equal(credential.EasyLoginCreatedAt, result.EasyLoginCreatedAt);
    }

    [Fact]
    public async Task GetCredentialAsync_PendingMigrationRowWithoutSecret_ReturnsHasEasyLoginFalse()
    {
        AllowSupervision();
        SetupCredentials(Credential(secret: null));

        var result = await _sut.GetCredentialAsync(1, 2);

        Assert.False(result.HasEasyLogin);
    }

    // ---------- LoginWithEasyLoginAsync ----------

    [Fact]
    public async Task LoginWithEasyLoginAsync_ValidSecret_CreatesSessionBoundToCredentialAndResetsFailures()
    {
        SetupCredentials(Credential("qr-secret"));
        _jwtTokenGenerator.Setup(g => g.GenerateChildAccessToken(1, It.IsAny<string>())).Returns("child-jwt");
        var addedSession = CaptureAddedSession();

        var result = await _sut.LoginWithEasyLoginAsync("  qr-secret  ", ClientKey);

        Assert.Equal(1, result.ChildProfileId);
        Assert.Equal("avatar-fox", result.AvatarId);
        Assert.Equal("child-jwt", result.AccessToken);
        Assert.Equal(14400, result.ExpiresInSeconds);
        Assert.Equal(1, addedSession()!.ChildAccessCredentialId);
        Assert.Null(addedSession()!.SupervisorSessionId);
        _limiter.Verify(l => l.Reset(ClientKey), Times.Once);
        _limiter.Verify(l => l.RegisterFailure(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_SecretIsReusable_EachScanStartsAnotherSession()
    {
        SetupCredentials(Credential("qr-secret"));
        var addedSessions = new List<ChildSession>();
        _sessionRepo.Setup(r => r.AddAsync(It.IsAny<ChildSession>(), It.IsAny<CancellationToken>()))
            .Callback<ChildSession, CancellationToken>((value, _) => addedSessions.Add(value))
            .ReturnsAsync((ChildSession value, CancellationToken _) => value);

        await _sut.LoginWithEasyLoginAsync("qr-secret", ClientKey);
        await _sut.LoginWithEasyLoginAsync("qr-secret", ClientKey);

        Assert.Equal(2, addedSessions.Count);
        Assert.NotEqual(addedSessions[0].SessionKey, addedSessions[1].SessionKey);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_LooksUpByHashNotPlainText()
    {
        var credential = Credential("secret-A");
        SetupCredentials(credential);

        await _sut.LoginWithEasyLoginAsync("secret-A", ClientKey);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync(credential.EasyLoginSecretHash!, ClientKey));
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_UnknownSecret_RegistersFailureAndCreatesNoSession()
    {
        SetupCredentials(Credential("secret-A"));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync("secret-B", ClientKey));

        _limiter.Verify(l => l.RegisterFailure(ClientKey), Times.Once);
        _limiter.Verify(l => l.Reset(It.IsAny<string>()), Times.Never);
        _sessionRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _jwtTokenGenerator.Verify(g => g.GenerateChildAccessToken(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_RevokedSecret_IsRejected()
    {
        var credential = Credential("qr-secret");
        SetupCredentials(credential);
        credential.EasyLoginSecretHash = null; // sau khi thu hồi

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync("qr-secret", ClientKey));

        _limiter.Verify(l => l.RegisterFailure(ClientKey), Times.Once);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_BlockedClient_ThrowsForbiddenWithoutQueryingCredentials()
    {
        _limiter.Setup(l => l.IsBlocked(ClientKey)).Returns(true);
        SetupCredentials(Credential("qr-secret"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginWithEasyLoginAsync("qr-secret", ClientKey));

        Assert.Equal(ChildAccessCredentialService.TooManyFailedScansMessage, ex.Message);
        _credentialRepo.Verify(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
            It.IsAny<CancellationToken>()), Times.Never);
        _limiter.Verify(l => l.RegisterFailure(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LoginWithEasyLoginAsync_BlankSecret_ThrowsBadRequestWithoutCountingFailure(string secret)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync(secret, ClientKey));

        _limiter.Verify(l => l.RegisterFailure(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Draft)]
    [InlineData(ChildProfileStatus.Archived)]
    public async Task LoginWithEasyLoginAsync_ProfileNotActive_BlocksWithFriendlyMessageAndNoFailureCount(
        ChildProfileStatus status)
    {
        SetupCredentials(Credential("qr-secret"));
        SetupProfileStatus(status);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginWithEasyLoginAsync("qr-secret", ClientKey));

        Assert.Equal(ChildAccessCredentialService.ChildEntryBlockedMessage, ex.Message);
        _limiter.Verify(l => l.RegisterFailure(It.IsAny<string>()), Times.Never);
        _sessionRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- GetMySessionProfileAsync ----------

    [Fact]
    public async Task GetMySessionProfileAsync_NotFound_ThrowsNotFound()
    {
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildProfile?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetMySessionProfileAsync(1));
    }

    [Fact]
    public async Task GetMySessionProfileAsync_Exists_ReturnsNicknameAndAgeBand()
    {
        _profileRepo.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 1, Nickname = "Bé An", AgeBand = AgeBand.Age_6_8 });

        var result = await _sut.GetMySessionProfileAsync(1);

        Assert.Equal(1, result.ChildProfileId);
        Assert.Equal("Bé An", result.Nickname);
        Assert.Equal("Age_6_8", result.AgeBand);
    }

    // ---------- StartSupervisedSessionAsync ----------

    [Fact]
    public async Task StartSupervisedSessionAsync_Valid_RecordsSupervisorSessionAsEntrySource()
    {
        AllowSupervision();
        SetupSupervisorSession(SupervisorSession());
        SetupCredentials(Credential("qr-secret"));
        _jwtTokenGenerator.Setup(g => g.GenerateChildAccessToken(1, It.IsAny<string>()))
            .Returns("child-jwt");
        var addedSession = CaptureAddedSession();

        var result = await _sut.StartSupervisedSessionAsync(1, 2, "supervisor-refresh");

        Assert.Equal("child-jwt", result.AccessToken);
        Assert.Equal("avatar-fox", result.AvatarId);
        Assert.Equal(77, addedSession()!.SupervisorSessionId);
        Assert.Null(addedSession()!.ChildAccessCredentialId);
    }

    [Fact]
    public async Task StartSupervisedSessionAsync_ChildWithoutCredential_StillStartsSession()
    {
        AllowSupervision();
        SetupSupervisorSession(SupervisorSession());
        SetupCredentials();
        var addedSession = CaptureAddedSession();

        var result = await _sut.StartSupervisedSessionAsync(1, 2, "supervisor-refresh");

        Assert.Equal(string.Empty, result.AvatarId);
        Assert.Equal(77, addedSession()!.SupervisorSessionId);
    }

    [Fact]
    public async Task StartSupervisedSessionAsync_NotSupervising_ThrowsBeforeCheckingToken()
    {
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartSupervisedSessionAsync(1, 2, "supervisor-refresh"));

        _refreshTokenRepo.Verify(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<RefreshToken, bool>>>(), null,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<RefreshToken?> InvalidSupervisorSessions() => new()
    {
        null,
        new RefreshToken { Id = 77, UserAccountId = 2, SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddDays(1), RevokedAt = DateTime.UtcNow.AddMinutes(-1) },
        new RefreshToken { Id = 77, UserAccountId = 2, SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
        new RefreshToken { Id = 77, UserAccountId = 2, SessionScope = SessionScope.Admin,
            ExpiresAt = DateTime.UtcNow.AddDays(1) },
        new RefreshToken { Id = 77, UserAccountId = 99, SessionScope = SessionScope.Supervisor,
            ExpiresAt = DateTime.UtcNow.AddDays(1) },
        new RefreshToken { Id = 77, UserAccountId = 2, TokenHash = "wrong-hash",
            SessionScope = SessionScope.Supervisor, ExpiresAt = DateTime.UtcNow.AddDays(1) }
    };

    [Theory]
    [MemberData(nameof(InvalidSupervisorSessions))]
    public async Task StartSupervisedSessionAsync_InvalidSupervisorSession_ThrowsUnauthorized(
        RefreshToken? token)
    {
        AllowSupervision();
        SetupSupervisorSession(token);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.StartSupervisedSessionAsync(1, 2, "supervisor-refresh"));

        _sessionRepo.Verify(r => r.AddAsync(
            It.IsAny<ChildSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Draft)]
    [InlineData(ChildProfileStatus.Archived)]
    public async Task StartSupervisedSessionAsync_ProfileNotActive_BlocksWithFriendlyMessage(
        ChildProfileStatus status)
    {
        AllowSupervision();
        SetupSupervisorSession(SupervisorSession());
        SetupProfileStatus(status);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartSupervisedSessionAsync(1, 2, "supervisor-refresh"));

        Assert.Equal(ChildAccessCredentialService.ChildEntryBlockedMessage, ex.Message);
    }

    // ---------- helpers ----------

    private void AllowSupervision() => _guard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship());

    private void AllowManageSafety() => _guard
        .Setup(g => g.EnsurePermissionAsync(
            1, 2, Permission.ManageSafetySettings, It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    private void SetupCredentials(params ChildAccessCredential[] credentials) => _credentialRepo
        .Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((Expression<Func<ChildAccessCredential, bool>> predicate, string? _, CancellationToken _) =>
            credentials.FirstOrDefault(predicate.Compile()));

    private void SetupProfileStatus(ChildProfileStatus status) => _profileRepo
        .Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ChildProfile { Id = 1, Status = status });

    private void SetupSupervisorSession(RefreshToken? token) => _refreshTokenRepo
        .Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<RefreshToken, bool>>>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(token);

    private static RefreshToken SupervisorSession() => new()
    {
        Id = 77,
        UserAccountId = 2,
        TokenHash = TokenHasher.Hash("supervisor-refresh"),
        SessionScope = SessionScope.Supervisor,
        ExpiresAt = DateTime.UtcNow.AddDays(1)
    };

    private Func<ChildSession?> CaptureAddedSession()
    {
        ChildSession? added = null;
        _sessionRepo.Setup(r => r.AddAsync(It.IsAny<ChildSession>(), It.IsAny<CancellationToken>()))
            .Callback<ChildSession, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((ChildSession value, CancellationToken _) => value);
        return () => added;
    }

    private static ChildAccessCredential Credential(string? secret = null) => new()
    {
        Id = 1,
        ChildProfileId = 1,
        AvatarId = "avatar-fox",
        EasyLoginSecretHash = secret == null ? null : TokenHasher.Hash(secret),
        EasyLoginCreatedAt = secret == null ? null : new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
