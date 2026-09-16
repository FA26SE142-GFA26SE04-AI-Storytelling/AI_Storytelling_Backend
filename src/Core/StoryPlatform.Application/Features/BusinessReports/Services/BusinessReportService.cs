using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
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

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public BusinessReportService(IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<BusinessReportDto> GenerateAsync(
        int adminUserId, GenerateBusinessReportRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.PeriodEnd <= request.PeriodStart)
        {
            throw new BadRequestException("PeriodEnd phải sau PeriodStart.");
        }

        var duplicatePeriod = await _unitOfWork.Repository<BusinessReport>().ExistsAsync(
            item => item.PeriodStart == request.PeriodStart && item.PeriodEnd == request.PeriodEnd,
            cancellationToken);
        if (duplicatePeriod)
        {
            throw new ConflictException("Đã có Business Report cho đúng kỳ báo cáo này.");
        }

        var storiesInPeriod = await _unitOfWork.Repository<Story>().FindAsync(
            item => item.Source == StorySource.Ai
                    && item.CreatedAt >= request.PeriodStart
                    && item.CreatedAt <= request.PeriodEnd,
            cancellationToken: cancellationToken);

        var completedSessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            item => item.Status == SessionStatus.Completed
                    && item.CompletedAt != null
                    && item.CompletedAt >= request.PeriodStart
                    && item.CompletedAt <= request.PeriodEnd,
            cancellationToken: cancellationToken);

        var report = new BusinessReport
        {
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            StoriesGenerated = storiesInPeriod.Count,
            StoriesApproved = storiesInPeriod.Count(item => ApprovedOrPastStatuses.Contains(item.Status)),
            StoriesRejected = storiesInPeriod.Count(item => item.Status == StoryStatus.Rejected),
            ReadingSessionsCompleted = completedSessions.Count,
            Status = ReportStatus.Compiling
        };

        await _unitOfWork.Repository<BusinessReport>().AddAsync(report, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(report);
    }

    public async Task<BusinessReportDto> PublishAsync(
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

        return MapToDto(report);
    }

    public async Task<BusinessReportDto> GetLatestPublishedAsync(CancellationToken cancellationToken = default)
    {
        var published = await _unitOfWork.Repository<BusinessReport>().FindAsync(
            item => item.Status == ReportStatus.Published, cancellationToken: cancellationToken);

        var latest = published.OrderByDescending(item => item.PublishedAt).FirstOrDefault()
                     ?? throw new NotFoundException("Chưa có Business Report nào được publish.");

        return MapToDto(latest);
    }

    private static BusinessReportDto MapToDto(BusinessReport report) => new()
    {
        Id = report.Id,
        PeriodStart = report.PeriodStart,
        PeriodEnd = report.PeriodEnd,
        StoriesGenerated = report.StoriesGenerated,
        StoriesApproved = report.StoriesApproved,
        StoriesRejected = report.StoriesRejected,
        ReadingSessionsCompleted = report.ReadingSessionsCompleted,
        Status = report.Status.ToString(),
        PublishedAt = report.PublishedAt
    };
}
