using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Profiles;

public class ChildProfileEraserTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepo = new();
    private readonly Mock<IChildSessionRevoker> _revoker = new();
    private readonly ChildProfileEraser _sut;

    public ChildProfileEraserTests()
    {
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildAccessCredential>()).Returns(_credentialRepo.Object);
        _sut = new ChildProfileEraser(_unitOfWork.Object, _revoker.Object);
    }

    [Fact]
    public async Task EraseAsync_AnonymizesAndRevokesAccess()
    {
        var credential = new ChildAccessCredential { ChildProfileId = 5, EasyLoginSecretHash = "x" };
        _credentialRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        var child = new ChildProfile { Id = 5, OwnerUserId = 7, Nickname = "Bin", Status = ChildProfileStatus.Active };

        await _sut.EraseAsync(child, 7, Now);

        Assert.Equal(ChildProfileEraser.AnonymizedNickname, child.Nickname);
        Assert.Equal(ChildProfileStatus.Deleted, child.Status);
        // Giữ dòng cho Admin / audit: chỉ đổi trạng thái, không xóa mềm (bộ lọc !IsDeleted sẽ ẩn hồ sơ).
        Assert.False(child.IsDeleted);
        Assert.Equal(Now, credential.RevokedAt);
        Assert.Null(credential.EasyLoginSecretHash);
        _revoker.Verify(r => r.RevokeByChildProfileAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _profileRepo.Verify(r => r.Update(child), Times.Once);
    }
}
