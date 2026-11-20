using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AiActivity.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.BackgroundServices;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AiActivity;

public sealed class AiActivityPersistenceRegressionTests
{
    private static CreatorManualMediaPersistenceTests.Db Db(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    [Theory]
    [InlineData(AiActivityMode.None)]
    [InlineData(AiActivityMode.TaskOnly)]
    public async Task Admission_survives_detached_caller_update_and_blocks_third_story(AiActivityMode originalMode)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Db(connection);
        await db.Database.EnsureCreatedAsync();
        db.Stories.AddRange(
            new Story { Id = 1, AuthorUserId = 10, AiActivityMode = originalMode, AiActivityStartedAt = originalMode == AiActivityMode.None ? null : DateTime.UtcNow.AddMinutes(-5) },
            new Story { Id = 2, AuthorUserId = 10, AiActivityMode = AiActivityMode.FullCreation },
            new Story { Id = 3, AuthorUserId = 10 });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var uow = new UnitOfWork(db);
        var service = new AccountAiActivityService(uow);
        var caller = (await uow.Repository<Story>().FirstOrDefaultAsync(s => s.Id == 1))!;
        var previousStart = caller.AiActivityStartedAt;
        await service.AdmitStoryAsync(10, caller, AiActivityMode.FullCreation);
        caller.Status = StoryStatus.MediaProcessing;
        uow.Repository<Story>().Update(caller);
        await uow.SaveChangesAsync(); db.ChangeTracker.Clear();
        var saved = await db.Stories.AsNoTracking().SingleAsync(s => s.Id == 1);
        Assert.Equal(AiActivityMode.FullCreation, saved.AiActivityMode);
        Assert.NotNull(saved.AiActivityStartedAt);
        if (previousStart.HasValue) Assert.Equal(previousStart, saved.AiActivityStartedAt);
        var third = (await uow.Repository<Story>().FirstOrDefaultAsync(s => s.Id == 3))!;
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.AdmitStoryAsync(10, third, AiActivityMode.FullCreation));
        Assert.Equal("AI_STORY_CONCURRENCY_LIMIT", error.ErrorCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Finalizer_releases_terminal_failure_but_preserves_newer_successful_review(bool outline, bool newerSuccess)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("pg_advisory_xact_lock", (long _) => 0L);
        var started = DateTime.UtcNow.AddMinutes(-5);
        await using (var db = Db(connection))
        {
            await db.Database.EnsureCreatedAsync();
            db.Stories.Add(new Story { Id = 1, AuthorUserId = 10, Status = StoryStatus.OutlineReview,
                AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = started });
            db.StoryVersions.Add(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true });
            db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 1, StoryId = 1, BaseStoryVersionId = 10,
                Operation = outline ? GenerationJobOperation.RegenerateOutline : GenerationJobOperation.GenerateContent,
                Status = GenerationJobStatus.Processing, ConcurrencyToken = "owner", StartedAt = started });
            if (newerSuccess) db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 2, StoryId = 1,
                StoryVersionId = 10, Status = GenerationJobStatus.Completed, StartedAt = started.AddMinutes(1) });
            await db.SaveChangesAsync();
        }
        using var provider = new ServiceCollection()
            .AddScoped<ApplicationDbContext>(_ => Db(connection))
            .AddScoped<IUnitOfWork, UnitOfWork>().BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        if (outline) await new OutlineJobFailureFinalizer(scopes).MarkFailedAsync(1, "owner", "TERMINAL");
        else await new ContentGenerationJobFailureFinalizer(scopes).MarkFailedAsync(1, "owner", "TERMINAL");
        await using var verify = Db(connection);
        Assert.Equal(GenerationJobStatus.Failed, (await verify.StoryGenerationJobs.SingleAsync(j => j.Id == 1)).Status);
        Assert.Equal(newerSuccess ? AiActivityMode.FullCreation : AiActivityMode.None, (await verify.Stories.SingleAsync()).AiActivityMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_terminalizes_abandoned_check_and_keeps_slot_for_another_live_check(bool otherLiveCheck)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("pg_advisory_xact_lock", (long _) => 0L);
        await using var db = Db(connection);
        await db.Database.EnsureCreatedAsync();
        db.Stories.Add(new Story { Id = 1, AuthorUserId = 10, AiActivityMode = AiActivityMode.TaskOnly });
        db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 1, StoryId = 1,
            Operation = GenerationJobOperation.GrayZoneCheck, Status = GenerationJobStatus.Processing,
            ConcurrencyToken = "abandoned", LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5) });
        if (otherLiveCheck) db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 2, StoryId = 1,
            Operation = GenerationJobOperation.GrayZoneCheck, Status = GenerationJobStatus.Processing,
            ConcurrencyToken = "live", LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1) });
        // Expired generation jobs belong to their own worker, never to this recovery path.
        db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 3, StoryId = 99,
            Operation = GenerationJobOperation.GenerateContent, Status = GenerationJobStatus.Processing,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var uow = new UnitOfWork(db);
        var recovery = new GrayZoneCheckRecoveryService(uow, new AccountAiActivityService(uow));
        await recovery.RecoverAsync();
        await recovery.RecoverAsync(); // Idempotent, no provider dispatch.
        db.ChangeTracker.Clear();
        var abandoned = await db.StoryGenerationJobs.SingleAsync(j => j.Id == 1);
        Assert.Equal(GenerationJobStatus.Failed, abandoned.Status);
        Assert.Equal("AI_OPERATION_INTERRUPTED", abandoned.ErrorCode);
        Assert.NotEqual("abandoned", abandoned.ConcurrencyToken);
        Assert.Null(abandoned.LeaseExpiresAt);
        Assert.Equal(otherLiveCheck ? AiActivityMode.TaskOnly : AiActivityMode.None, (await db.Stories.SingleAsync()).AiActivityMode);
        Assert.Equal(GenerationJobStatus.Processing, (await db.StoryGenerationJobs.SingleAsync(j => j.Id == 3)).Status);
        if (otherLiveCheck) Assert.Equal("live", (await db.StoryGenerationJobs.SingleAsync(j => j.Id == 2)).ConcurrencyToken);
    }

    [Fact]
    public async Task Terminal_content_failure_refunds_held_credit_and_releases_activity_in_the_same_finalization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("pg_advisory_xact_lock", (long _) => 0L);
        await using (var db = Db(connection))
        {
            await db.Database.EnsureCreatedAsync();
            db.Stories.Add(new Story { Id = 1, AuthorUserId = 10, Status = StoryStatus.OutlineReview,
                AiActivityMode = AiActivityMode.FullCreation, AiActivityStartedAt = DateTime.UtcNow.AddMinutes(-1) });
            db.StoryVersions.Add(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true });
            db.StoryGenerationJobs.Add(new StoryGenerationJob { Id = 1, StoryId = 1, BaseStoryVersionId = 10,
                Operation = GenerationJobOperation.GenerateContent, Status = GenerationJobStatus.Processing,
                ConcurrencyToken = "owner", StartedAt = DateTime.UtcNow });
            db.Set<CreditLot>().Add(new CreditLot { Id = 20, UserId = 10, Total = 1, Remaining = 0 });
            db.Set<CreditUsage>().Add(new CreditUsage { Id = 30, UserId = 10, LotId = 20, StoryId = 1,
                Amount = 1, Reason = CreditUsageReason.StoryRequest });
            await db.SaveChangesAsync();
        }
        using var provider = new ServiceCollection().AddScoped<ApplicationDbContext>(_ => Db(connection))
            .AddScoped<IUnitOfWork, UnitOfWork>().BuildServiceProvider();
        await new ContentGenerationJobFailureFinalizer(provider.GetRequiredService<IServiceScopeFactory>())
            .MarkFailedAsync(1, "owner", "TERMINAL");
        await using var verify = Db(connection);
        Assert.Equal(AiActivityMode.None, (await verify.Stories.SingleAsync()).AiActivityMode);
        Assert.Equal(1, (await verify.Set<CreditLot>().SingleAsync()).Remaining);
        Assert.NotNull((await verify.Set<CreditUsage>().SingleAsync()).RefundedAt);
        Assert.Equal(GenerationJobStatus.Failed, (await verify.StoryGenerationJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Recovered_check_cannot_record_a_late_provider_result()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("pg_advisory_xact_lock", (long _) => 0L);
        await using var db = Db(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserAccount { Id = 10, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        db.Stories.Add(new Story { Id = 1, AuthorUserId = 10, Scope = StoryScope.Creator, Source = StorySource.Manual });
        db.StoryVersions.Add(new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Story", Content = "Safe story." });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var uow = new UnitOfWork(db);
        var activity = new AccountAiActivityService(uow);
        var recovery = new GrayZoneCheckRecoveryService(uow, activity);
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(e => e.IsConfigured).Returns(true);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .Returns(async (GrayZoneScan scan, CancellationToken _) =>
            {
                var job = await db.StoryGenerationJobs.SingleAsync();
                job.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5);
                await db.SaveChangesAsync();
                await recovery.RecoverAsync();
                return new GrayZoneAssessment(ModerationZone.Safe, GrayZoneEvaluator.Fingerprint(scan), scan.Phase, "p1", "fake", "fake", 0, []);
            });
        var check = new GrayZoneContentCheckService(uow, evaluator.Object,
            new GrayZoneWorkflowService(uow, new Mock<IAuditLogWriter>().Object), new Mock<IChildProfileAccessGuard>().Object, activity);
        var error = await Assert.ThrowsAsync<ConflictException>(() => check.CheckAsync(10, 1, 10, default));
        Assert.Equal("STALE_JOB_RESULT", error.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(GenerationJobStatus.Failed, (await db.StoryGenerationJobs.SingleAsync()).Status);
        Assert.Null(GrayZoneWorkflowService.ReadAssessment(await db.StoryVersions.SingleAsync()));
    }
}
