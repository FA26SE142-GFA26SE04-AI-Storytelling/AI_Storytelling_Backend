using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.BusinessReports.Services;

public class UserAccountStatisticsService : IUserAccountStatisticsService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserAccountStatisticsService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<UserAccountStatisticsDto> GetAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);

        // Phụ huynh = role User. Tài khoản đã xoá bị ẩn bởi query filter nên không được đếm.
        var parents = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => user.Role == UserRole.User && user.CreatedAt < toExclusive,
            cancellationToken: cancellationToken);
        var parentIds = parents.Select(user => user.Id).ToHashSet();

        var children = await _unitOfWork.Repository<ChildProfile>().FindAsync(
            child => child.CreatedAt < toExclusive && child.Status != ChildProfileStatus.Deleted,
            cancellationToken: cancellationToken);
        var ownerByChild = children.ToDictionary(child => child.Id, child => child.OwnerUserId);

        // Hoạt động trong kỳ: có đăng nhập (refresh token được cấp, hoặc lần đăng nhập gần nhất rơi vào kỳ)
        // hoặc có phiên đọc của một hồ sơ trẻ thuộc tài khoản.
        var signedIn = (await _unitOfWork.Repository<RefreshToken>().FindAsync(
                token => token.IssuedAt >= from && token.IssuedAt < toExclusive,
                cancellationToken: cancellationToken))
            .Select(token => token.UserAccountId);
        var lastLoginInPeriod = parents
            .Where(user => user.LastLoginAt >= from && user.LastLoginAt < toExclusive)
            .Select(user => user.Id);
        var hadReadingSession = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => session.StartedAt >= from && session.StartedAt < toExclusive,
                cancellationToken: cancellationToken))
            .Where(session => ownerByChild.ContainsKey(session.ChildProfileId))
            .Select(session => ownerByChild[session.ChildProfileId]);
        var active = signedIn.Concat(lastLoginInPeriod).Concat(hadReadingSession)
            .Where(parentIds.Contains)
            .Distinct()
            .Count();

        return new UserAccountStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            TotalParentAccounts = parents.Count,
            NewAccounts = parents.Count(user => user.CreatedAt >= from),
            ActiveAccounts = active,
            // Khoá 15 phút do nhập sai mật khẩu không phải trạng thái tài khoản; "bị khoá hoặc vô hiệu" là Suspended.
            LockedOrDisabledAccounts = parents.Count(user => user.Status == AccountStatus.Suspended),
            ChildProfiles = children.Count
        };
    }
}
