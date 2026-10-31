using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class AIStoryInputServiceTests
{
    // Real EF transaction, generated IDs, FK and uniqueness checks. PostgreSQL advisory locks/provider are not exercised.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WishCreation_PersistsLinksAndQuotaTogether_OrRollsBackWhenAuditFails(bool failAudit)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WishDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var seed = CreateEligibleUnitOfWork();
        void Seed<T>() where T : class => db.Set<T>().AddRange(seed.Items<T>());
        Seed<UserAccount>(); Seed<ChildProfile>(); Seed<LearningProfile>(); Seed<SafetyPolicy>();

        db.Set<StoryWish>().Add(PendingWish());
        db.Set<TokenQuotaConfig>().Add(new()
        {
            Scope = TokenQuotaScope.Child, ChildProfileId = 1, QuotaLimit = 10,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var uow = new WishTestUnitOfWork(db);
        var audit = new Mock<IAuditLogWriter>();
        if (failAudit)
            audit.Setup(x => x.LogAsync(It.IsAny<int?>(), "StoryWishAccepted", It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("simulated wish audit failure"));
        var service = new AIStoryInputService(uow, new RuleBasedInputGuardrail(),
            new TokenQuotaService(uow, new NoopAuditLogWriter()), wishAudit: audit.Object);
        if (failAudit)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(1, WishInput()));
            Assert.Equal("simulated wish audit failure", error.Message);
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<Story>().ToListAsync());
            Assert.Empty(await db.Set<StoryGenerationRequest>().ToListAsync());
            Assert.Empty(await db.Set<StoryGenerationJob>().ToListAsync());
            Assert.Equal(0, (await db.Set<TokenQuotaConfig>().SingleAsync()).QuotaUsed);
            var wish = await db.Set<StoryWish>().SingleAsync();
            Assert.Equal(StoryWishStatus.Pending, wish.Status);
            Assert.Null(wish.GeneratedStoryId);
            Assert.Null(wish.GenerationRequestId);
        }
        else
        {
            var result = await service.SubmitAsync(1, WishInput());
            db.ChangeTracker.Clear();
            var replay = await service.SubmitAsync(1, WishInput());
            db.ChangeTracker.Clear();
            var wish = await db.Set<StoryWish>().SingleAsync();
            Assert.Equal(result.StoryId, wish.GeneratedStoryId);
            Assert.Equal(result.RequestId, wish.GenerationRequestId);
            Assert.Equal(result.RequestId, replay.RequestId);
            Assert.Single(await db.Set<Story>().ToListAsync());
            Assert.Single(await db.Set<StoryGenerationRequest>().ToListAsync());
            Assert.Single(await db.Set<StoryGenerationJob>().ToListAsync());
            Assert.Equal(1, (await db.Set<TokenQuotaConfig>().SingleAsync()).QuotaUsed);
        }
        Assert.Contains(-1, uow.Locks);
    }

    private sealed class WishTestUnitOfWork(ApplicationDbContext db) : IUnitOfWork
    {
        private readonly UnitOfWork _inner = new(db);
        public List<int> Locks { get; } = [];
        public IGenericRepository<T> Repository<T>() where T : class => _inner.Repository<T>();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _inner.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => _inner.BeginTransactionAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => _inner.CommitTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
        { Locks.Add(resourceId); return Task.CompletedTask; }
        public void DiscardTrackedChanges() => _inner.DiscardTrackedChanges();
        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class WishDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            Type[] types = [typeof(UserAccount), typeof(ChildProfile), typeof(LearningProfile), typeof(SafetyPolicy),
                typeof(SafetyPolicyCategory), typeof(ContentCategory), typeof(LearningProfileTopic),
                typeof(StoryWish), typeof(Story), typeof(StoryGenerationRequest), typeof(StoryGenerationJob), typeof(TokenQuotaConfig)];
            foreach (var type in types)
                foreach (var property in type.GetProperties().Where(p => typeof(BaseEntity).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))))
                    b.Entity(type).Ignore(property.Name);
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (!types.Contains(type)) b.Ignore(type);
            foreach (var type in types) b.Entity(type).HasKey("Id");
            new StoryGenerationRequestConfiguration().Configure(b.Entity<StoryGenerationRequest>());
            b.Entity<SafetyPolicyCategory>().HasOne(x => x.ContentCategory).WithMany().HasForeignKey(x => x.ContentCategoryId);
            b.Entity<StoryWish>().HasOne<Story>().WithMany().HasForeignKey(x => x.GeneratedStoryId);
            b.Entity<StoryWish>().HasOne<StoryGenerationRequest>().WithMany().HasForeignKey(x => x.GenerationRequestId);
        }
    }
}
