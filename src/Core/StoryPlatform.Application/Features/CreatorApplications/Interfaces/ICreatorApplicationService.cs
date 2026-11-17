using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorApplications.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorApplications.Interfaces;

/// <summary>Đơn đăng ký Content Creator (Luồng 5, M5): Creator xem / nộp lại đơn, Admin duyệt hoặc từ chối.</summary>
public interface ICreatorApplicationService
{
    /// <summary>Đơn mới nhất của Creator đang đăng nhập (trạng thái, lý do từ chối).</summary>
    Task<CreatorApplicationDto> GetMineAsync(int applicantUserId, CancellationToken cancellationToken = default);

    /// <summary>Nộp lại sau khi bị từ chối: tạo đơn Pending mới. 409 nếu đang có đơn chờ duyệt.</summary>
    Task<CreatorApplicationDto> ResubmitAsync(
        int applicantUserId, ResubmitCreatorApplicationRequestDto request, CancellationToken cancellationToken = default);

    Task<PagedResult<CreatorApplicationDto>> ListAsync(
        CreatorApplicationStatus? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    Task<CreatorApplicationDto> GetAsync(int applicationId, CancellationToken cancellationToken = default);

    /// <summary>Admin duyệt đơn Pending: tài khoản Creator thành Active. 409 nếu đơn không còn chờ.</summary>
    Task<CreatorApplicationDto> ApproveAsync(int adminUserId, int applicationId, CancellationToken cancellationToken = default);

    /// <summary>Admin từ chối đơn Pending kèm lý do bắt buộc. 409 nếu đơn không còn chờ.</summary>
    Task<CreatorApplicationDto> RejectAsync(
        int adminUserId, int applicationId, RejectCreatorApplicationRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creator tự đăng ký nhưng chưa được duyệt nhận role tạm <see cref="ApplicantRole"/> trong access token, nên mọi API
/// [Authorize(Roles = "ContentCreator")] trả 403. Khi Admin duyệt, role thật khác role trong token cũ và middleware thu hồi
/// token đó; làm mới phiên sẽ nhận role ContentCreator.
/// </summary>
public static class CreatorApplicantAccess
{
    public const string ApplicantRole = "CreatorApplicant";

    /// <summary>Giá trị claim role của access token cho tài khoản này.</summary>
    public static string RoleClaimFor(UserAccount user) =>
        user is { Role: UserRole.ContentCreator, Status: AccountStatus.AwaitingApproval }
            ? ApplicantRole
            : user.Role.ToString();
}
