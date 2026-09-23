using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.AccessCredentials;

public class ChildAccessCredentialServiceTests
{
    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGenerator = new();
    private readonly ChildAccessCredentialService _sut;

    public ChildAccessCredentialServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildAccessCredential>()).Returns(_credentialRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _jwtTokenGenerator.Setup(generator => generator.ChildTokenExpiresInSeconds).Returns(14400);
        _sut = new ChildAccessCredentialService(
            _unitOfWork.Object, _guard.Object, _passwordHasher.Object, _jwtTokenGenerator.Object);
    }

    [Fact]
    public async Task SetPinAsync_WithoutSupervision_StopsBeforeHashing()
    {
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SetPinAsync(1, 2, ValidCredentialRequest()));

        _passwordHasher.Verify(p => p.HashPassword(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetPinAsync_NewCredential_StoresHashInsteadOfRawPin()
    {
        AllowSupervision();
        SetupCredential(null);
        _passwordHasher.Setup(p => p.HashPassword("1234")).Returns("hashed-pin");
        ChildAccessCredential? added = null;
        _credentialRepo.Setup(r => r.AddAsync(
                It.IsAny<ChildAccessCredential>(), It.IsAny<CancellationToken>()))
            .Callback<ChildAccessCredential, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((ChildAccessCredential value, CancellationToken _) => value);

        await _sut.SetPinAsync(1, 2, ValidCredentialRequest());

        Assert.Equal("avatar-fox", added!.AvatarId);
        Assert.Equal("hashed-pin", added.PinHash);
        Assert.NotEqual("1234", added.PinHash);
        Assert.Equal(2, added.CreatedByUserId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPinAsync_ExistingCredential_ReplacesPinAndClearsLock()
    {
        AllowSupervision();
        var credential = Credential();
        credential.FailedAttempts = 3;
        credential.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        SetupCredential(credential);
        _passwordHasher.Setup(p => p.HashPassword("1234")).Returns("new-hash");

        await _sut.SetPinAsync(1, 2, ValidCredentialRequest());

        Assert.Equal("new-hash", credential.PinHash);
        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockedUntil);
        _credentialRepo.Verify(r => r.Update(credential), Times.Once);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12ab")]
    public async Task SetPinAsync_InvalidPin_ThrowsBadRequest(string pin)
    {
        AllowSupervision();
        var request = ValidCredentialRequest();
        request.Pin = pin;

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetPinAsync(1, 2, request));
    }

    [Fact]
    public async Task LoginWithPinAsync_CorrectPin_ResetsFailuresAndReturnsSession()
    {
        var credential = Credential();
        credential.FailedAttempts = 2;
        SetupCredential(credential);
        _passwordHasher.Setup(p => p.VerifyPassword("1234", "hashed-pin")).Returns(true);
        _jwtTokenGenerator.Setup(generator => generator.GenerateChildAccessToken(1))
            .Returns("child-jwt-token");

        var result = await _sut.LoginWithPinAsync(1, "1234");

        Assert.Equal(1, result.ChildProfileId);
        Assert.Equal("avatar-fox", result.AvatarId);
        Assert.Equal("child-jwt-token", result.AccessToken);
        Assert.Equal(14400, result.ExpiresInSeconds);
        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockedUntil);
    }

    [Fact]
    public async Task LoginWithPinAsync_WrongPin_DoesNotGenerateToken()
    {
        var credential = Credential();
        SetupCredential(credential);
        _passwordHasher.Setup(hasher => hasher.VerifyPassword("0000", "hashed-pin"))
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.LoginWithPinAsync(1, "0000"));

        _jwtTokenGenerator.Verify(
            generator => generator.GenerateChildAccessToken(It.IsAny<int>()), Times.Never);
    }

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

    [Fact]
    public async Task LoginWithPinAsync_FifthWrongAttempt_LocksFor15Minutes()
    {
        var credential = Credential();
        credential.FailedAttempts = 4;
        SetupCredential(credential);
        _passwordHasher.Setup(p => p.VerifyPassword("0000", "hashed-pin")).Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithPinAsync(1, "0000"));

        Assert.Equal(0, credential.FailedAttempts);
        Assert.True(credential.LockedUntil > DateTime.UtcNow.AddMinutes(14));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginWithPinAsync_Locked_DoesNotVerifyPin()
    {
        var credential = Credential();
        credential.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        SetupCredential(credential);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.LoginWithPinAsync(1, "1234"));

        _passwordHasher.Verify(p => p.VerifyPassword(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithPinAsync_ExpiredLock_AllowsCorrectPin()
    {
        var credential = Credential();
        credential.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        SetupCredential(credential);
        _passwordHasher.Setup(p => p.VerifyPassword("1234", "hashed-pin")).Returns(true);

        await _sut.LoginWithPinAsync(1, "1234");

        Assert.Null(credential.LockedUntil);
    }

    [Fact]
    public async Task GetCredentialAsync_NotSet_ThrowsNotFound()
    {
        AllowSupervision();
        SetupCredential(null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCredentialAsync(1, 2));
    }

    [Fact]
    public async Task GetCredentialAsync_Exists_ReturnsDtoWithoutPinHash()
    {
        AllowSupervision();
        var credential = Credential();
        credential.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        SetupCredential(credential);

        var result = await _sut.GetCredentialAsync(1, 2);

        Assert.Equal("avatar-fox", result.AvatarId);
        Assert.True(result.HasPin);
        Assert.True(result.IsLocked);
    }

    [Fact]
    public async Task RevokeCredentialAsync_Existing_DeletesRowHard()
    {
        AllowSupervision();
        var credential = Credential();
        SetupCredential(credential);

        await _sut.RevokeCredentialAsync(1, 2);

        _credentialRepo.Verify(r => r.Delete(credential), Times.Once);
    }

    [Fact]
    public async Task GenerateEasyLoginCodeAsync_NoExistingCredential_ThrowsNotFound()
    {
        AllowSupervision();
        SetupCredential(null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GenerateEasyLoginCodeAsync(1, 2));
    }

    [Fact]
    public async Task GenerateEasyLoginCodeAsync_ExistingCredential_SetsCodeWith5MinuteTtl()
    {
        AllowSupervision();
        var credential = Credential();
        SetupCredential(credential);
        _jwtTokenGenerator.Setup(g => g.GenerateRefreshToken()).Returns("new-easylogin-code");

        var before = DateTime.UtcNow;
        var result = await _sut.GenerateEasyLoginCodeAsync(1, 2);

        Assert.Equal("new-easylogin-code", result.Code);
        Assert.Equal("new-easylogin-code", credential.EasyLoginCode);
        Assert.Null(credential.EasyLoginUsedAt);
        Assert.True(credential.EasyLoginExpiresAt > before.AddMinutes(4)
            && credential.EasyLoginExpiresAt <= before.AddMinutes(5).AddSeconds(1));
        _credentialRepo.Verify(r => r.Update(credential), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateEasyLoginCodeAsync_PreviousCodePending_IsInvalidatedByNewOne()
    {
        AllowSupervision();
        var credential = Credential();
        credential.EasyLoginCode = "old-code";
        credential.EasyLoginExpiresAt = DateTime.UtcNow.AddMinutes(3);
        SetupCredential(credential);
        _jwtTokenGenerator.Setup(g => g.GenerateRefreshToken()).Returns("new-code");

        await _sut.GenerateEasyLoginCodeAsync(1, 2);

        Assert.Equal("new-code", credential.EasyLoginCode);
        Assert.NotEqual("old-code", credential.EasyLoginCode);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_ValidUnusedCode_CreatesSessionAndMarksUsed()
    {
        var credential = Credential();
        credential.EasyLoginCode = "valid-code";
        credential.EasyLoginExpiresAt = DateTime.UtcNow.AddMinutes(2);
        _credentialRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        _jwtTokenGenerator.Setup(g => g.GenerateChildAccessToken(1)).Returns("child-jwt");

        var result = await _sut.LoginWithEasyLoginAsync("valid-code");

        Assert.Equal(1, result.ChildProfileId);
        Assert.Equal("child-jwt", result.AccessToken);
        Assert.NotNull(credential.EasyLoginUsedAt);
        Assert.Null(credential.EasyLoginCode);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_ExpiredCode_ThrowsBadRequest()
    {
        var credential = Credential();
        credential.EasyLoginCode = "expired-code";
        credential.EasyLoginExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        _credentialRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync("expired-code"));

        _jwtTokenGenerator.Verify(g => g.GenerateChildAccessToken(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_AlreadyUsedOrUnknownCode_ThrowsBadRequest()
    {
        _credentialRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildAccessCredential?)null);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync("already-used-or-unknown"));
    }

    [Fact]
    public async Task LoginWithEasyLoginAsync_CodeMarkedUsed_ThrowsBadRequest()
    {
        var credential = Credential();
        credential.EasyLoginCode = "used-code";
        credential.EasyLoginExpiresAt = DateTime.UtcNow.AddMinutes(2);
        credential.EasyLoginUsedAt = DateTime.UtcNow.AddMinutes(-1);
        SetupCredential(credential);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LoginWithEasyLoginAsync("used-code"));

        _jwtTokenGenerator.Verify(g => g.GenerateChildAccessToken(It.IsAny<int>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AllowSupervision() => _guard
        .Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SupervisionRelationship());

    private void SetupCredential(ChildAccessCredential? credential) => _credentialRepo
        .Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(credential);

    private static SetChildAccessCredentialRequestDto ValidCredentialRequest() => new()
    {
        AvatarId = "avatar-fox",
        Pin = "1234"
    };

    private static ChildAccessCredential Credential() => new()
    {
        Id = 1,
        ChildProfileId = 1,
        AvatarId = "avatar-fox",
        PinHash = "hashed-pin"
    };
}
