using System.Text.Json;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.CreatorApplications.DTOs;
using StoryPlatform.Application.Features.CreatorApplications.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorApplications.Services;

/// <summary>
/// Đơn đăng ký Content Creator (Luồng 5, M5). Creator tự đăng ký (AuthService.RegisterCreatorAsync), xác thực email rồi chờ
/// ở AwaitingApproval; Admin duyệt → tài khoản Active, hoặc từ chối kèm lý do → Creator nộp lại thành đơn mới.
/// </summary>
public class CreatorApplicationService : ICreatorApplicationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;

    public CreatorApplicationService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _emailSender = emailSender;
    }

    public async Task<CreatorApplicationDto> GetMineAsync(int applicantUserId, CancellationToken cancellationToken = default)
    {
        var applicant = await RequireApplicantAsync(applicantUserId, cancellationToken);
        var latest = await LatestOfAsync(applicantUserId, cancellationToken)
                     ?? throw new NotFoundException("Không tìm thấy đơn đăng ký nhà sáng tạo nội dung của tài khoản này.");
        // Creator không thấy Admin nào đã xử lý đơn.
        var dto = MapToDto(latest, applicant, reviewer: null);
        return dto;
    }

    public async Task<CreatorApplicationDto> ResubmitAsync(
        int applicantUserId, ResubmitCreatorApplicationRequestDto request, CancellationToken cancellationToken = default)
    {
        var introduction = request.Introduction?.Trim() ?? string.Empty;
        if (introduction.Length < 20 || introduction.Length > 1000)
        {
            throw new BadRequestException("Giới thiệu bản thân từ 20 đến 1000 ký tự.");
        }

        var applicant = await RequireApplicantAsync(applicantUserId, cancellationToken);
        if (applicant.Status != AccountStatus.AwaitingApproval)
        {
            throw new ForbiddenException(applicant.Status == AccountStatus.Active
                ? "Tài khoản đã được duyệt, không cần nộp lại đơn."
                : "Tài khoản chưa thể nộp lại đơn đăng ký nhà sáng tạo nội dung.");
        }

        var application = new CreatorApplication
        {
            ApplicantUserId = applicant.Id,
            Introduction = introduction,
            PortfolioUrl = string.IsNullOrWhiteSpace(request.PortfolioUrl) ? null : request.PortfolioUrl.Trim(),
            Status = CreatorApplicationStatus.Pending
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Hai lần nộp lại cùng lúc: chỉ một đơn Pending (index unique có điều kiện chặn thêm ở database).
            await _unitOfWork.AcquireTransactionLockAsync(applicant.Id, cancellationToken);
            var latest = await LatestOfAsync(applicant.Id, cancellationToken);
            if (latest?.Status == CreatorApplicationStatus.Pending)
            {
                throw new ConflictException("Đơn đăng ký của bạn đang chờ duyệt.");
            }

            if (latest?.Status == CreatorApplicationStatus.Approved)
            {
                throw new ConflictException("Đơn đăng ký của bạn đã được duyệt.");
            }

            await _unitOfWork.Repository<CreatorApplication>().AddAsync(application, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }

        return MapToDto(application, applicant, reviewer: null);
    }

    public async Task<PagedResult<CreatorApplicationDto>> ListAsync(
        CreatorApplicationStatus? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        // Đơn chờ duyệt cũ nhất lên trước để Admin xử lý theo thứ tự nộp; các trạng thái khác mới nhất trước.
        var (items, totalCount) = await _unitOfWork.Repository<CreatorApplication>().GetPagedAsync(
            pageIndex, pageSize,
            filter: item => status == null || item.Status == status,
            orderBy: status == CreatorApplicationStatus.Pending
                ? source => source.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                : source => source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
            includeProperties: "Applicant,ReviewedBy",
            cancellationToken: cancellationToken);

        var result = items.Select(item => MapToDto(item, item.Applicant, item.ReviewedBy)).ToList();
        return new PagedResult<CreatorApplicationDto>(result, totalCount, pageIndex, pageSize);
    }

    public async Task<CreatorApplicationDto> GetAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _unitOfWork.Repository<CreatorApplication>().FirstOrDefaultAsync(
                              item => item.Id == applicationId, "Applicant,ReviewedBy", cancellationToken)
                          ?? throw new NotFoundException("Đơn đăng ký nhà sáng tạo nội dung", applicationId);
        return MapToDto(application, application.Applicant, application.ReviewedBy);
    }

    public Task<CreatorApplicationDto> ApproveAsync(int adminUserId, int applicationId, CancellationToken cancellationToken = default) =>
        DecideAsync(adminUserId, applicationId, rejectionReason: null, cancellationToken);

    public Task<CreatorApplicationDto> RejectAsync(
        int adminUserId, int applicationId, RejectCreatorApplicationRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Cần ghi lý do từ chối.");
        }

        if (reason.Length > 500)
        {
            throw new BadRequestException("Lý do từ chối tối đa 500 ký tự.");
        }

        return DecideAsync(adminUserId, applicationId, reason, cancellationToken);
    }

    /// <summary>Duyệt (<paramref name="rejectionReason"/> null) hoặc từ chối một đơn Pending.</summary>
    private async Task<CreatorApplicationDto> DecideAsync(
        int adminUserId, int applicationId, string? rejectionReason, CancellationToken cancellationToken)
    {
        var users = _unitOfWork.Repository<UserAccount>();
        var admin = await users.GetByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Administrator, Status: AccountStatus.Active } || admin.IsDeleted)
        {
            throw new ForbiddenException("Chỉ quản trị viên đang hoạt động mới xử lý được đơn đăng ký nhà sáng tạo nội dung.");
        }

        var applications = _unitOfWork.Repository<CreatorApplication>();
        var found = await applications.GetByIdAsync(applicationId, cancellationToken)
                    ?? throw new NotFoundException("Đơn đăng ký nhà sáng tạo nội dung", applicationId);
        var approve = rejectionReason == null;
        CreatorApplication application;
        UserAccount applicant;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Cùng khoá với lúc Creator nộp lại: hai Admin xử lý cùng lúc chỉ một người thành công.
            await _unitOfWork.AcquireTransactionLockAsync(found.ApplicantUserId, cancellationToken);
            // Đọc lại sau khi có khoá: bản đã track trước đó có thể cũ nếu Admin khác vừa xử lý xong.
            _unitOfWork.DiscardTrackedChanges();
            application = await applications.GetByIdAsync(applicationId, cancellationToken)
                          ?? throw new NotFoundException("Đơn đăng ký nhà sáng tạo nội dung", applicationId);
            if (application.Status != CreatorApplicationStatus.Pending)
            {
                throw new ConflictException("Đơn này đã được xử lý.");
            }

            applicant = await users.GetByIdAsync(application.ApplicantUserId, cancellationToken)
                        ?? throw new NotFoundException("Tài khoản", application.ApplicantUserId);
            if (applicant.IsDeleted || applicant.Role != UserRole.ContentCreator)
            {
                throw new ConflictException("Tài khoản nộp đơn không còn tồn tại.");
            }

            if (applicant.Status == AccountStatus.Registered)
            {
                throw new ConflictException("Nhà sáng tạo chưa xác thực email, chưa thể xử lý đơn.");
            }

            if (applicant.Status != AccountStatus.AwaitingApproval)
            {
                throw new ConflictException("Tài khoản không ở trạng thái chờ duyệt.");
            }

            var now = DateTime.UtcNow;
            application.Status = approve ? CreatorApplicationStatus.Approved : CreatorApplicationStatus.Rejected;
            application.RejectionReason = rejectionReason;
            application.ReviewedByUserId = admin.Id;
            application.ReviewedAt = now;
            application.UpdatedAt = now;
            applications.Update(application);

            if (approve)
            {
                // Token role tạm CreatorApplicant không còn khớp role thật nên bị thu hồi; làm mới phiên nhận role Creator.
                applicant.Status = AccountStatus.Active;
                applicant.UpdatedAt = now;
                users.Update(applicant);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }

        await _auditLogWriter.LogAsync(
            admin.Id, approve ? "CreatorApplicationApproved" : "CreatorApplicationRejected",
            nameof(CreatorApplication), application.Id,
            beforeState: new
            {
                status = CreatorApplicationStatus.Pending.ToString(),
                accountStatus = AccountStatus.AwaitingApproval.ToString()
            },
            afterState: new
            {
                status = application.Status.ToString(),
                accountStatus = applicant.Status.ToString(),
                applicantUserId = applicant.Id,
                reason = rejectionReason
            },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            applicant.Id,
            approve ? NotificationType.CreatorApplicationApproved : NotificationType.CreatorApplicationRejected,
            JsonSerializer.Serialize(new { applicationId = application.Id, reason = rejectionReason }),
            cancellationToken);
        await _emailSender.SendCreatorApplicationResultEmailAsync(
            applicant.Email, applicant.FullName, rejectionReason, cancellationToken);

        return MapToDto(application, applicant, admin);
    }

    private async Task<UserAccount> RequireApplicantAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Role != UserRole.ContentCreator)
        {
            throw new ForbiddenException("Chỉ tài khoản nhà sáng tạo nội dung mới có đơn đăng ký.");
        }

        return user;
    }

    private async Task<CreatorApplication?> LatestOfAsync(int applicantUserId, CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CreatorApplication>().FindAsync(
            item => item.ApplicantUserId == applicantUserId, cancellationToken: cancellationToken))
        .OrderByDescending(item => item.CreatedAt)
        .ThenByDescending(item => item.Id)
        .FirstOrDefault();

    private static CreatorApplicationDto MapToDto(
        CreatorApplication application, UserAccount? applicant, UserAccount? reviewer) => new()
    {
        Id = application.Id,
        ApplicantUserId = application.ApplicantUserId,
        ApplicantUsername = applicant?.Username ?? string.Empty,
        ApplicantFullName = applicant?.FullName ?? string.Empty,
        ApplicantEmail = applicant?.Email ?? string.Empty,
        ApplicantStatus = applicant?.Status.ToString() ?? string.Empty,
        Introduction = application.Introduction,
        PortfolioUrl = application.PortfolioUrl,
        Status = application.Status.ToString(),
        CreatedAt = application.CreatedAt,
        ReviewedAt = application.ReviewedAt,
        ReviewedByName = reviewer == null
            ? null
            : string.IsNullOrWhiteSpace(reviewer.FullName) ? reviewer.Username : reviewer.FullName,
        RejectionReason = application.RejectionReason
    };
}
