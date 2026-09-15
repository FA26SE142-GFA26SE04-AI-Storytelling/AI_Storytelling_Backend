using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.BusinessReports;

public class BusinessReportServiceTests
{
    private readonly Mock<IGenericRepository<BusinessReport>> _businessReportRepository = new();
    private readonly Mock<IGenericRepository<Story>> _storyRepository = new();
    private readonly Mock<IGenericRepository<ReadingSession>> _readingSessionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly BusinessReportService _sut;

    private static readonly DateTime PeriodStart = new(2026, 1, 1);
    private static readonly DateTime PeriodEnd = new(2026, 1, 31);

    public BusinessReportServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<BusinessReport>()).Returns(_businessReportRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Story>()).Returns(_storyRepository.Object);
        _unitOfWork.Setup(work => work.Repository<ReadingSession>()).Returns(_readingSessionRepository.Object);

        _businessReportRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<BusinessReport, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sut = new BusinessReportService(_unitOfWork.Object, _auditLogWriter.Object);
    }

    private static Story MakeStory(int id, StorySource source, StoryStatus status, DateTime createdAt) => new()
    {
        Id = id,
        Source = source,
        Status = status,
        CreatedAt = createdAt,
        AuthorUserId = 1,
        ChildProfileId = 1
    };

    // ---------- GenerateAsync ----------

    [Fact]
    public async Task GenerateAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GenerateAsync(
            10, new GenerateBusinessReportRequestDto { PeriodStart = PeriodEnd, PeriodEnd = PeriodStart }));
    }

    [Fact]
    public async Task GenerateAsync_DuplicatePeriod_ThrowsConflict()
    {
        _businessReportRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<BusinessReport, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.GenerateAsync(
            10, new GenerateBusinessReportRequestDto { PeriodStart = PeriodStart, PeriodEnd = PeriodEnd }));
    }

    [Fact]
    public async Task GenerateAsync_ComputesMetricsFromStoriesAndReadingSessions()
    {
        var storiesInPeriod = new[]
        {
            MakeStory(1, StorySource.Ai, StoryStatus.Approved, new DateTime(2026, 1, 10)),
            MakeStory(2, StorySource.Ai, StoryStatus.Ready, new DateTime(2026, 1, 15)),
            MakeStory(3, StorySource.Ai, StoryStatus.Rejected, new DateTime(2026, 1, 20)),
            MakeStory(4, StorySource.Ai, StoryStatus.Draft, new DateTime(2026, 1, 5)),
            MakeStory(5, StorySource.Manual, StoryStatus.Approved, new DateTime(2026, 1, 10)),
            MakeStory(6, StorySource.Ai, StoryStatus.Approved, new DateTime(2026, 2, 1))
        };
        _storyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Story, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Story, bool>> predicate, string? _, CancellationToken _) =>
                storiesInPeriod.Where(predicate.Compile()).ToList());

        var completedSessions = new[]
        {
            new ReadingSession
            {
                Id = 1, ChildProfileId = 1, StoryId = 1,
                Status = SessionStatus.Completed, StartedAt = new DateTime(2026, 1, 9),
                CompletedAt = new DateTime(2026, 1, 10)
            },
            new ReadingSession
            {
                Id = 2, ChildProfileId = 1, StoryId = 1,
                Status = SessionStatus.Completed, StartedAt = new DateTime(2026, 2, 1),
                CompletedAt = new DateTime(2026, 2, 2)
            },
            new ReadingSession
            {
                Id = 3, ChildProfileId = 1, StoryId = 1,
                Status = SessionStatus.Abandoned, StartedAt = new DateTime(2026, 1, 12),
                CompletedAt = null
            }
        };
        _readingSessionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<ReadingSession, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ReadingSession, bool>> predicate, string? _, CancellationToken _) =>
                completedSessions.Where(predicate.Compile()).ToList());

        BusinessReport? added = null;
        _businessReportRepository.Setup(repo => repo.AddAsync(It.IsAny<BusinessReport>(), It.IsAny<CancellationToken>()))
            .Callback<BusinessReport, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((BusinessReport entity, CancellationToken _) => entity);

        var result = await _sut.GenerateAsync(
            10, new GenerateBusinessReportRequestDto { PeriodStart = PeriodStart, PeriodEnd = PeriodEnd });

        Assert.NotNull(added);
        Assert.Equal(4, added!.StoriesGenerated); // stories 1,2,3,4 (Ai, in period) — 5 is Manual, 6 is out of period
        Assert.Equal(2, added.StoriesApproved); // story 1 (Approved), story 2 (Ready)
        Assert.Equal(1, added.StoriesRejected); // story 3
        Assert.Equal(1, added.ReadingSessionsCompleted); // session 1 only
        Assert.Equal(ReportStatus.Compiling, added.Status);
        Assert.Equal("Compiling", result.Status);
        Assert.Equal(4, result.StoriesGenerated);
    }

    // ---------- PublishAsync ----------

    private static BusinessReport MakeReport(int id, ReportStatus status) => new()
    {
        Id = id,
        PeriodStart = PeriodStart,
        PeriodEnd = PeriodEnd,
        Status = status
    };

    [Fact]
    public async Task PublishAsync_NotFound_ThrowsNotFound()
    {
        _businessReportRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessReport?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PublishAsync(10, 1));
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublished_ThrowsConflict()
    {
        var report = MakeReport(1, ReportStatus.Published);
        _businessReportRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.PublishAsync(10, 1));
    }

    [Fact]
    public async Task PublishAsync_Valid_SetsPublishedStatusAndAudits()
    {
        var report = MakeReport(1, ReportStatus.Compiling);
        _businessReportRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var result = await _sut.PublishAsync(10, 1);

        Assert.Equal(ReportStatus.Published, report.Status);
        Assert.NotNull(report.PublishedAt);
        Assert.Equal("Published", result.Status);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            10, "BusinessReportPublished", nameof(BusinessReport), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- GetLatestPublishedAsync ----------

    [Fact]
    public async Task GetLatestPublishedAsync_NonePublished_ThrowsNotFound()
    {
        _businessReportRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<BusinessReport, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BusinessReport>());

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetLatestPublishedAsync());
    }

    [Fact]
    public async Task GetLatestPublishedAsync_ReturnsMostRecentlyPublished()
    {
        var older = MakeReport(1, ReportStatus.Published);
        older.PublishedAt = new DateTime(2026, 1, 1);
        var newer = MakeReport(2, ReportStatus.Published);
        newer.PublishedAt = new DateTime(2026, 2, 1);
        _businessReportRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<BusinessReport, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { older, newer });

        var result = await _sut.GetLatestPublishedAsync();

        Assert.Equal(2, result.Id);
    }
}
