using Microsoft.Extensions.DependencyInjection;
using Moq;
using StoryPlatform.Application;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.DTOs;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class StoryArchiveConsequencesTests
{
    private sealed class RecordingHandler : IStoryArchiveHandler
    {
        private readonly string _name;
        private readonly List<string> _log;
        private readonly bool _fails;

        public RecordingHandler(string name, List<string> log, bool fails = false)
        {
            _name = name;
            _log = log;
            _fails = fails;
        }

        public StoryArchiveContext? Received { get; private set; }

        public Task HandleAsync(StoryArchiveContext context, CancellationToken cancellationToken = default)
        {
            Received = context;
            _log.Add(_name);
            return _fails ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }

    private static StoryArchiveContext Context() =>
        new(new Story { Id = 10 }, ArchivedReason.SafetyConcern, 11, DateTime.UtcNow);

    [Fact]
    public async Task ApplyAsync_CallsHandlersInOrderWithSameContext()
    {
        var log = new List<string>();
        var first = new RecordingHandler("first", log);
        var second = new RecordingHandler("second", log);
        var context = Context();

        await new StoryArchiveConsequences([first, second]).ApplyAsync(context);

        Assert.Equal(new[] { "first", "second" }, log);
        Assert.Same(context, first.Received);
        Assert.Same(context, second.Received);
    }

    [Fact]
    public async Task ApplyAsync_HandlerFailureStopsAndPropagates()
    {
        var log = new List<string>();
        var sut = new StoryArchiveConsequences([
            new RecordingHandler("first", log),
            new RecordingHandler("broken", log, true),
            new RecordingHandler("never", log)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ApplyAsync(Context()));

        Assert.Equal(new[] { "first", "broken" }, log);
    }

    [Fact]
    public void DependencyInjection_RegistersHandlersInRequiredOrder()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddScoped(_ => new Mock<IAuditLogWriter>().Object);
        services.AddScoped(_ => new Mock<INotificationService>().Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IStoryArchiveHandler>()
            .Select(handler => handler.GetType()).ToArray();

        Assert.Equal(
            new[]
            {
                typeof(PendingSubmissionArchiveHandler), typeof(CommunitySubmissionArchiveHandler),
                typeof(ReadingSessionArchiveHandler), typeof(ChildRoadmapArchiveHandler)
            },
            handlers);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStoryArchiveConsequences>());
    }
}

public class ContentReportArchiveConsequencesIntegrationTests
{
    private readonly ContentReportHarness _h = new();
    private readonly Mock<IStoryArchiveConsequences> _consequences = new();
    private readonly ContentReportReviewService _sut;

    public ContentReportArchiveConsequencesIntegrationTests()
    {
        _sut = new ContentReportReviewService(
            _h.UnitOfWork.Object, _h.Audit.Object, _h.Notifications.Object, _consequences.Object);
    }

    [Fact]
    public async Task Archive_AppliesConsequencesOnceWithArchivedStoryAndDecisionData()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        StoryStatus? observedStatus = null;
        _consequences.Setup(service => service.ApplyAsync(
                It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()))
            .Callback<StoryArchiveContext, CancellationToken>((context, _) =>
                observedStatus = context.Story.Status)
            .Returns(Task.CompletedTask);

        await _sut.ArchiveAsync(ContentReportHarness.ModeratorId, item.Id, new ArchiveReportRequestDto
        {
            ArchiveReason = ArchivedReason.SafetyConcern,
            Reason = "Không an toàn"
        });

        Assert.Equal(StoryStatus.Archived, observedStatus);
        _consequences.Verify(service => service.ApplyAsync(
            It.Is<StoryArchiveContext>(context =>
                context.Story.Id == ContentReportHarness.PublicStoryId
                && context.Reason == ArchivedReason.SafetyConcern
                && context.ActorUserId == ContentReportHarness.ModeratorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissOrAlreadyArchivedStory_DoesNotApplyConsequences()
    {
        var dismissed = _h.AddReportItem(ContentReportReason.Cultural);
        await _sut.DismissAsync(ContentReportHarness.ModeratorId, dismissed.Id,
            new DismissReportRequestDto { Reason = "Không vi phạm" });

        var archived = _h.AddReportItem(ContentReportReason.Copyright);
        _h.Stories.Single(story => story.Id == ContentReportHarness.PublicStoryId).Status = StoryStatus.Archived;
        await _sut.ArchiveAsync(ContentReportHarness.ModeratorId, archived.Id,
            new ArchiveReportRequestDto
            {
                ArchiveReason = ArchivedReason.Copyright,
                Reason = "Vi phạm bản quyền"
            });

        _consequences.Verify(service => service.ApplyAsync(
            It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_ConsequenceFailureRollsBackDecision()
    {
        var item = _h.AddReportItem(ContentReportReason.SafetyConcern);
        _consequences.Setup(service => service.ApplyAsync(
                It.IsAny<StoryArchiveContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ArchiveAsync(ContentReportHarness.ModeratorId, item.Id, new ArchiveReportRequestDto
            {
                ArchiveReason = ArchivedReason.SafetyConcern,
                Reason = "Không an toàn"
            }));

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
