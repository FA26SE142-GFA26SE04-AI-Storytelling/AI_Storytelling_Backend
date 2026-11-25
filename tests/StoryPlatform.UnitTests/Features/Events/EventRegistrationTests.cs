using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Events;

public class EventRegistrationTests
{
    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly EventParticipationService _service;

    public EventRegistrationTests()
    {
        _store.Seed(new UserAccount { Id = 50, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        _store.Seed(new Event { Id = 1, Status = EventStatus.Published });
        _store.Seed(new EventTrack { Id = 2, EventId = 1, TrackType = EventTrackType.Creator,
            RegistrationStartAt = DateTime.UtcNow.AddDays(-1), RegistrationEndAt = DateTime.UtcNow.AddDays(1),
            GrantAiCredits = 3, GrantSubmitCredits = 2, ExecutionEndAt = DateTime.UtcNow.AddDays(2) });
        _store.Seed(new EventRuleVersion { Id = 3, EventId = 1, IsActive = true });
        _service = new EventParticipationService(_store, _audit.Object);
    }

    private static RegisterEventRequestDto Request() => new() { TrackType = EventTrackType.Creator, AcceptRules = true };

    [Fact]
    public async Task RepeatRegistrationReusesEnrollmentAndDoesNotGrantAgain()
    {
        await _service.RegisterAsync(50, 1, Request());
        await _service.RegisterAsync(50, 1, Request());
        Assert.Single(_store.Items<EventParticipation>());
        Assert.Equal(2, _store.Items<CreditLot>().Count);
        Assert.Equal(3, _store.Items<EventParticipation>().Single().AcceptedRuleVersionId);
        Assert.Equal(new[] { "begin", "event-lock:1", "commit", "begin", "event-lock:1", "commit" }, _store.Calls);
    }

    [Fact]
    public async Task AuditFailureRollsBackInsteadOfCommittingEnrollmentAndCredits()
    {
        _audit.Setup(x => x.LogAsync(50, "EventRegistered", nameof(EventParticipation), It.IsAny<int>(), null,
            It.IsAny<object>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("audit unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegisterAsync(50, 1, Request()));
        Assert.Equal(new[] { "begin", "event-lock:1", "rollback" }, _store.Calls);
    }

    [Fact]
    public async Task RejectedRulesDoNotGrantCredits()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _service.RegisterAsync(50, 1,
            new() { TrackType = EventTrackType.Creator, AcceptRules = false }));
        Assert.Empty(_store.Items<CreditLot>());
        Assert.Empty(_store.Items<EventParticipation>());
        Assert.DoesNotContain("commit", _store.Calls);
    }
}
