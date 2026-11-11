using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.BusinessReports.Services;

public class BusinessReportService : IBusinessReportService
{
    private static readonly StoryStatus[] ApprovedOrPastStatuses =
        [StoryStatus.Approved, StoryStatus.MediaProcessing, StoryStatus.Ready];

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IRevenueStatisticsService _revenueStatisticsService;
    private readonly IUserAccountStatisticsService _userAccountStatisticsService;

    public BusinessReportService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, IRevenueStatisticsService revenueStatisticsService,
        IUserAccountStatisticsService userAccountStatisticsService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _revenueStatisticsService = revenueStatisticsService;
        _userAccountStatisticsService = userAccountStatisticsService;
    }

    public async Task<AdminBusinessReportDto> GenerateAsync(
        int adminUserId, GenerateBusinessReportRequestDto request, CancellationToken cancellationToken = default)
    {
        // Kỳ tính trọn ngày, gồm cả ngày PeriodEnd, nên hai kỳ liền nhau là kỳ sau bắt đầu vào ngày kế tiếp.
        var (from, toExclusive) = ReportPeriod.Resolve(request.PeriodStart, request.PeriodEnd);
        var lastDay = toExclusive.AddDays(-1);

        var overlapping = await _unitOfWork.Repository<BusinessReport>().ExistsAsync(
            item => item.PeriodStart < toExclusive && item.PeriodEnd >= from,
            cancellationToken);
        if (overlapping)
        {
            throw new ConflictException("Kỳ báo cáo này trùng hoặc chồng lên một Business Report đã có.");
        }

        var storiesInPeriod = await _unitOfWork.Repository<Story>().FindAsync(
            item => item.Source == StorySource.Ai
                    && item.CreatedAt >= from
                    && item.CreatedAt < toExclusive,
            cancellationToken: cancellationToken);

        var completedSessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            item => item.Status == SessionStatus.Completed
                    && item.CompletedAt != null
                    && item.CompletedAt >= from
                    && item.CompletedAt < toExclusive,
            cancellationToken: cancellationToken);

        // Chụp số liệu doanh thu và tài khoản tại thời điểm tạo: báo cáo đã có không đổi khi dữ liệu về sau thay đổi.
        var revenue = await _revenueStatisticsService.GetAsync(from, lastDay, cancellationToken);
        var accounts = await _userAccountStatisticsService.GetAsync(from, lastDay, cancellationToken);

        var report = new BusinessReport
        {
            PeriodStart = from,
            PeriodEnd = lastDay,
            StoriesGenerated = storiesInPeriod.Count,
            StoriesApproved = storiesInPeriod.Count(item => ApprovedOrPastStatuses.Contains(item.Status)),
            StoriesRejected = storiesInPeriod.Count(item => item.Status == StoryStatus.Rejected),
            ReadingSessionsCompleted = completedSessions.Count,
            RevenueSnapshotJson = JsonSerializer.Serialize(revenue, SnapshotJsonOptions),
            AccountSnapshotJson = JsonSerializer.Serialize(accounts, SnapshotJsonOptions),
            Status = ReportStatus.Compiling
        };

        await _unitOfWork.Repository<BusinessReport>().AddAsync(report, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "BusinessReportGenerated", nameof(BusinessReport), report.Id,
            null,
            new { periodStart = from, periodEnd = lastDay, revenue.TotalRevenueVnd, revenue.PaidTransactionCount },
            cancellationToken);

        return MapToAdminDto(report);
    }

    public async Task<AdminBusinessReportDto> PublishAsync(
        int adminUserId, int reportId, CancellationToken cancellationToken = default)
    {
        var report = await _unitOfWork.Repository<BusinessReport>().GetByIdAsync(reportId, cancellationToken)
                     ?? throw new NotFoundException("Business Report", reportId);

        if (report.Status == ReportStatus.Published)
        {
            throw new ConflictException("Business Report đã được publish trước đó.");
        }

        var beforeState = new { status = report.Status.ToString() };
        report.Status = ReportStatus.Published;
        report.PublishedAt = DateTime.UtcNow;
        _unitOfWork.Repository<BusinessReport>().Update(report);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "BusinessReportPublished", nameof(BusinessReport), report.Id,
            beforeState, new { status = report.Status.ToString() }, cancellationToken);

        return MapToAdminDto(report);
    }

    public async Task<BusinessReportDto> GetLatestPublishedAsync(CancellationToken cancellationToken = default)
    {
        var published = await _unitOfWork.Repository<BusinessReport>().FindAsync(
            item => item.Status == ReportStatus.Published, cancellationToken: cancellationToken);

        var latest = published.OrderByDescending(item => item.PublishedAt).FirstOrDefault()
                     ?? throw new NotFoundException("Chưa có Business Report nào được publish.");

        // Endpoint này mở cho mọi tài khoản đã đăng nhập: không trả số liệu doanh thu.
        return MapToDto(latest, new BusinessReportDto());
    }

    public async Task<PagedResult<AdminBusinessReportDto>> ListAsync(
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, totalCount) = await _unitOfWork.Repository<BusinessReport>().GetPagedAsync(
            pageIndex, pageSize,
            orderBy: source => source.OrderByDescending(item => item.PeriodStart).ThenByDescending(item => item.Id),
            cancellationToken: cancellationToken);

        return new PagedResult<AdminBusinessReportDto>(
            items.Select(MapToAdminDto).ToList(), totalCount, pageIndex, pageSize);
    }

    public async Task<AdminBusinessReportDto> GetByIdAsync(int reportId, CancellationToken cancellationToken = default)
    {
        var report = await _unitOfWork.Repository<BusinessReport>().FirstOrDefaultAsync(
                         item => item.Id == reportId, cancellationToken: cancellationToken)
                     ?? throw new NotFoundException("Business Report", reportId);
        return MapToAdminDto(report);
    }

    private static AdminBusinessReportDto MapToAdminDto(BusinessReport report)
    {
        var dto = MapToDto(report, new AdminBusinessReportDto());
        dto.Revenue = string.IsNullOrWhiteSpace(report.RevenueSnapshotJson)
            ? null
            : JsonSerializer.Deserialize<RevenueStatisticsDto>(report.RevenueSnapshotJson, SnapshotJsonOptions);
        dto.Accounts = string.IsNullOrWhiteSpace(report.AccountSnapshotJson)
            ? null
            : JsonSerializer.Deserialize<UserAccountStatisticsDto>(report.AccountSnapshotJson, SnapshotJsonOptions);
        return dto;
    }

    private static T MapToDto<T>(BusinessReport report, T dto) where T : BusinessReportDto
    {
        dto.Id = report.Id;
        dto.PeriodStart = report.PeriodStart;
        dto.PeriodEnd = report.PeriodEnd;
        dto.StoriesGenerated = report.StoriesGenerated;
        dto.StoriesApproved = report.StoriesApproved;
        dto.StoriesRejected = report.StoriesRejected;
        dto.ReadingSessionsCompleted = report.ReadingSessionsCompleted;
        dto.Status = report.Status.ToString();
        dto.PublishedAt = report.PublishedAt;
        return dto;
    }
}
