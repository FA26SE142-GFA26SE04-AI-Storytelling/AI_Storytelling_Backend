using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Application.Features.CreatorStatistics.Services;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

// Opt-in only (STORYPLATFORM_TEST_PG). Chạy trên database tạm: migration AddSubscriptionCyclesAndCreditLots và
// khoá advisory theo tài khoản là hai thứ SQLite không kiểm được.
public sealed class SubscriptionPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    private const string Migration = "AddSubscriptionCyclesAndCreditLots";
    private const int CreativePlanId = 3;

    [PostgresFact]
    public async Task Migration_ConvertsLeftoverPersonalQuotaToLegacyLots_AndSeedsNewPlans()
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable))
        {
            Pooling = false
        };
        var name = "storyplatform_it_sub_" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE \"{name}\"");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString)
                .Options;
            int withLeftover, usedUp;
            await using (var before = new ApplicationDbContext(options))
            {
                var migrations = before.Database.GetMigrations().ToList();
                var previous = migrations[migrations.FindIndex(id => id.EndsWith("_" + Migration)) - 1];
                await before.GetService<IMigrator>().MigrateAsync(previous);

                var parentA = NewParent("legacy_a");
                var parentB = NewParent("legacy_b");
                before.Users.AddRange(parentA, parentB);
                await before.SaveChangesAsync();
                (withLeftover, usedUp) = (parentA.Id, parentB.Id);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                before.TokenQuotaConfigs.AddRange(
                    new TokenQuotaConfig { Scope = TokenQuotaScope.System, QuotaLimit = 1000, QuotaUsed = 10, PeriodStart = today, PeriodEnd = today.AddMonths(1) },
                    new TokenQuotaConfig { Scope = TokenQuotaScope.Personal, UserId = withLeftover, QuotaLimit = 50, QuotaUsed = 18, PeriodStart = today, PeriodEnd = today.AddMonths(1) },
                    new TokenQuotaConfig { Scope = TokenQuotaScope.Personal, UserId = usedUp, QuotaLimit = 50, QuotaUsed = 50, PeriodStart = today, PeriodEnd = today.AddMonths(1) });
                await before.SaveChangesAsync();
            }

            await using var db = new ApplicationDbContext(options);
            await db.Database.MigrateAsync();

            var lot = await db.CreditLots.SingleAsync();
            Assert.Equal(
                (withLeftover, CreditType.AI, CreditSource.Legacy, 32, 32, (DateTime?)null, (int?)null),
                (lot.UserId, lot.CreditType, lot.Source, lot.Total, lot.Remaining, lot.ExpiresAt, lot.PaymentId));
            Assert.Equal(3, await db.TokenQuotaConfigs.CountAsync()); // bảng cũ giữ nguyên dữ liệu
            Assert.Equal(
                ["1:Legacy:User:49000:50:0:False", "2:Reading:User:49000:0:0:True", "3:Creative:User:99000:4:0:True",
                    "4:AddOn:User:29000:2:0:True", "5:CreatorPack:Creator:99000:6:20:True"],
                (await db.SubscriptionPlans.OrderBy(plan => plan.Id).ToListAsync())
                    .Select(plan => $"{plan.Id}:{plan.PlanType}:{plan.Audience}:{plan.PriceVnd}:{plan.AiCredits}:{plan.SubmitCredits}:{plan.IsActive}"));

            // Sequence đã được đẩy qua các Id chèn cố định: gói tạo sau này không trùng khoá.
            var added = new SubscriptionPlan { Name = "Gói thử", PlanType = PlanType.Reading, PriceVnd = 1000 };
            db.SubscriptionPlans.Add(added);
            await db.SaveChangesAsync();
            Assert.Equal(6, added.Id);
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
    }

    [PostgresFact]
    public async Task WebhookAndMarkPaidRacingOnOneTransaction_ApplyThePlanOnce()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        PaymentTransaction transaction;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            transaction = new PaymentTransaction
            {
                PlanId = CreativePlanId, PayerUserId = database.ParentUserId, TransactionCode = "SEPAYRACE000001",
                Amount = 99000, Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            seed.PaymentTransactions.Add(transaction);
            await seed.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = new ApplicationDbContext(database.Options);
            await using var uow = new UnitOfWork(context);
            var plan = await context.SubscriptionPlans.SingleAsync(item => item.Id == CreativePlanId);
            await NewSubscriptionService(uow).ApplyPaidTransactionAsync(transaction, plan, now);
        }));

        await using var db = new ApplicationDbContext(database.Options);
        var subscription = await db.UserSubscriptions.SingleAsync();
        Assert.Equal((SubscriptionStatus.Active, transaction.Id), (subscription.Status, subscription.PaymentId));
        var lot = await db.CreditLots.SingleAsync();
        Assert.Equal((CreditSource.Cycle, 4, (int?)transaction.Id), (lot.Source, lot.Remaining, lot.PaymentId));
    }

    [PostgresFact]
    public async Task TwoStoryRequestsRacingForTheLastCredit_OnlyOneConsumesIt()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        int childId;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            var child = new ChildProfile
            {
                OwnerUserId = database.ParentUserId, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi",
                Status = ChildProfileStatus.Active
            };
            seed.ChildProfiles.Add(child);
            seed.CreditLots.Add(new CreditLot
            {
                UserId = database.ParentUserId, Source = CreditSource.Trial, Total = 1, Remaining = 1
            });
            await seed.SaveChangesAsync();
            childId = child.Id;
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = new ApplicationDbContext(database.Options);
            await using var uow = new UnitOfWork(context);
            await uow.BeginTransactionAsync();
            try
            {
                await new TokenQuotaService(uow).IncrementUsageAsync(childId);
                await uow.CommitTransactionAsync();
                return "consumed";
            }
            catch (ConflictException)
            {
                await uow.RollbackTransactionAsync();
                return "rejected";
            }
        }));

        Assert.Equal(1, outcomes.Count(outcome => outcome == "consumed"));
        Assert.Equal(3, outcomes.Count(outcome => outcome == "rejected"));
        await using var db = new ApplicationDbContext(database.Options);
        Assert.Equal(0, (await db.CreditLots.SingleAsync()).Remaining);
    }

    [PostgresFact]
    public async Task AdminDeductRacingWithStoryRequests_NeverGoesNegative()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        int childId;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            var child = new ChildProfile
            {
                OwnerUserId = database.ParentUserId, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi",
                Status = ChildProfileStatus.Active
            };
            seed.ChildProfiles.Add(child);
            seed.CreditLots.Add(new CreditLot
            {
                UserId = database.ParentUserId, Source = CreditSource.Legacy, Total = 3, Remaining = 3
            });
            await seed.SaveChangesAsync();
            childId = child.Id;
        }

        // 3 lượt; Admin trừ 2 lượt hai lần và phụ huynh gửi 2 yêu cầu cùng lúc: tổng muốn lấy là 6.
        async Task<int> AdminDeductAsync()
        {
            await using var context = new ApplicationDbContext(database.Options);
            await using var uow = new UnitOfWork(context);
            try
            {
                await new TokenQuotaService(uow).AdjustAsync(database.AdministratorId, new AdjustCreditsRequestDto
                {
                    UserId = database.ParentUserId, Amount = -2, Reason = "Thu hồi lượt cấp nhầm"
                });
                return 2;
            }
            catch (BadRequestException)
            {
                return 0;
            }
        }

        async Task<int> StoryRequestAsync()
        {
            await using var context = new ApplicationDbContext(database.Options);
            await using var uow = new UnitOfWork(context);
            await uow.BeginTransactionAsync();
            try
            {
                await new TokenQuotaService(uow).IncrementUsageAsync(childId);
                await uow.CommitTransactionAsync();
                return 1;
            }
            catch (ConflictException)
            {
                await uow.RollbackTransactionAsync();
                return 0;
            }
        }

        var taken = await Task.WhenAll(AdminDeductAsync(), StoryRequestAsync(), AdminDeductAsync(), StoryRequestAsync());

        await using var db = new ApplicationDbContext(database.Options);
        var remaining = (await db.CreditLots.SingleAsync()).Remaining;
        Assert.InRange(remaining, 0, 3);
        Assert.Equal(3 - taken.Sum(), remaining);
        Assert.Equal(taken.Count(amount => amount == 2), await db.CreditAdjustments.CountAsync());
    }

    [PostgresFact]
    public async Task RevenueStatisticsAndPeriodReport_RunAgainstPostgres()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var today = DateTime.UtcNow.Date;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            PaymentTransaction Paid(int planId, int amount, string code, DateTime paidAt) => new()
            {
                PlanId = planId, PayerUserId = database.ParentUserId, TransactionCode = code, Amount = amount,
                Status = PaymentStatus.Paid, PaidAt = paidAt, ExpiresAt = paidAt.AddMinutes(15)
            };
            seed.PaymentTransactions.AddRange(
                Paid(2, 49000, "SEPAYPGREV00001", today.AddHours(1)),
                Paid(CreativePlanId, 99000, "SEPAYPGREV00002", today.AddHours(23)),
                Paid(CreativePlanId, 99000, "SEPAYPGREV00003", today.AddDays(1)),   // ngày sau: ngoài kỳ
                new PaymentTransaction
                {
                    PlanId = CreativePlanId, PayerUserId = database.ParentUserId, TransactionCode = "SEPAYPGREV00004",
                    Amount = 99000, Status = PaymentStatus.MismatchAmount, ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                });
            var lot = new CreditLot { UserId = database.ParentUserId, Source = CreditSource.Cycle, Total = 4, Remaining = 3 };
            seed.CreditLots.Add(lot);
            await seed.SaveChangesAsync();
            seed.CreditUsages.Add(new CreditUsage { UserId = database.ParentUserId, LotId = lot.Id, Amount = 1, Reason = CreditUsageReason.StoryRequest });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(database.Options);
        await using var uow = new UnitOfWork(context);
        var revenueService = new RevenueStatisticsService(uow);

        var revenue = await revenueService.GetAsync(today, today);

        Assert.Equal((148000L, 2, 1, 1), (revenue.TotalRevenueVnd, revenue.PaidTransactionCount, revenue.BuyingParents, revenue.RepeatBuyingParents));
        Assert.Equal((1, 4, 1), (revenue.TransactionsByStatus.MismatchAmount, revenue.CreditsGranted, revenue.CreditsUsed));
        Assert.Equal(24600L, revenue.CreatorFundVnd);

        var reports = new BusinessReportService(uow, new AuditLogWriter(uow), revenueService, new UserAccountStatisticsService(uow));
        var generated = await reports.GenerateAsync(database.AdministratorId, new GenerateBusinessReportRequestDto { PeriodStart = today, PeriodEnd = today });
        Assert.Equal(148000L, generated.Revenue!.TotalRevenueVnd);
        await Assert.ThrowsAsync<ConflictException>(() => reports.GenerateAsync(
            database.AdministratorId, new GenerateBusinessReportRequestDto { PeriodStart = today.AddDays(-3), PeriodEnd = today }));
        await reports.GenerateAsync(
            database.AdministratorId, new GenerateBusinessReportRequestDto { PeriodStart = today.AddDays(1), PeriodEnd = today.AddDays(1) });
        Assert.Equal(148000L, (await reports.GetByIdAsync(generated.Id)).Revenue!.TotalRevenueVnd);
    }

    [PostgresFact]
    public async Task CreatorStatistics_RunAgainstPostgres()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTime.UtcNow;
        int creatorId, storyId;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            var creator = NewParent("pg_creator");
            creator.Role = UserRole.ContentCreator;
            seed.Users.Add(creator);
            var child = new ChildProfile
            {
                OwnerUserId = database.ParentUserId, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi",
                Status = ChildProfileStatus.Active
            };
            var payment = new PaymentTransaction
            {
                PlanId = CreativePlanId, PayerUserId = database.ParentUserId, TransactionCode = "SEPAYPGCRS00001",
                Amount = 100000, Status = PaymentStatus.Paid, PaidAt = now.AddMinutes(-30), ExpiresAt = now
            };
            var handover = new RefreshToken
            {
                UserAccountId = database.ParentUserId, TokenHash = "pg-creator-stats", SessionScope = SessionScope.Supervisor,
                IssuedAt = now.AddHours(-1), ExpiresAt = now.AddDays(7)
            };
            seed.AddRange(child, payment, handover);
            await seed.SaveChangesAsync();
            var story = new Story
            {
                AuthorUserId = creator.Id, Title = "Chú ếch nhỏ", Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = now.AddDays(-30), Language = "vi", AgeBand = "6-8"
            };
            seed.AddRange(
                story,
                new UserSubscription
                {
                    UserId = database.ParentUserId, PlanId = CreativePlanId, Status = SubscriptionStatus.Active,
                    StartAt = now.AddDays(-1), EndAt = now.AddDays(29), PaymentId = payment.Id
                });
            await seed.SaveChangesAsync();
            seed.AddRange(
                new ReadingSession
                {
                    StoryId = story.Id, ChildProfileId = child.Id, Status = SessionStatus.Completed, SupervisorSessionId = handover.Id,
                    StartedAt = now.AddMinutes(-20), CompletedAt = now.AddMinutes(-10), TimeSpentSeconds = 600
                },
                new ReadingProgress { StoryId = story.Id, ChildProfileId = child.Id, IsFavorited = true });
            await seed.SaveChangesAsync();
            (creatorId, storyId) = (creator.Id, story.Id);
        }

        await using var context = new ApplicationDbContext(database.Options);
        await using var uow = new UnitOfWork(context);
        var statistics = new CreatorStatisticsService(uow, new RevenueStatisticsService(uow));

        var reading = await statistics.GetReadingAsync(creatorId, now.AddDays(-1), now, null);
        var revenue = await statistics.GetRevenueAsync(creatorId, now.AddDays(-1), now, storyId);

        var row = Assert.Single(reading.Stories);
        Assert.Equal((1, 1, 1, 1, 1m), (row.Views, row.Favorites, row.CompletedReadings, row.ValidReads, row.CompletionRate));
        Assert.Equal((10000L, 1, 1, 10000L), (revenue.CreatorFundVnd, revenue.TotalValidReadsAllCreators, revenue.MyValidReads, revenue.EstimatedEarningsVnd));
    }

    [PostgresFact]
    public async Task CreatorPackRacingWebhookAndMarkPaid_GrantsOneAiAndOneSubmitLot()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        PaymentTransaction transaction;
        int creatorId;
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            var creator = NewParent("pg_pack_creator");
            creator.Role = UserRole.ContentCreator;
            seed.Users.Add(creator);
            await seed.SaveChangesAsync();
            creatorId = creator.Id;
            transaction = new PaymentTransaction
            {
                PlanId = 5, PayerUserId = creatorId, TransactionCode = "SEPAYPACK000001", Amount = 99000,
                AiCredits = 6, SubmitCredits = 20, Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            seed.PaymentTransactions.Add(transaction);
            await seed.SaveChangesAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var context = new ApplicationDbContext(database.Options);
            await using var uow = new UnitOfWork(context);
            var plan = await context.SubscriptionPlans.SingleAsync(item => item.Id == 5);
            await NewSubscriptionService(uow).ApplyPaidTransactionAsync(transaction, plan, DateTime.UtcNow);
        }));

        await using var db = new ApplicationDbContext(database.Options);
        Assert.Equal(
            ["AI:CreatorPack:6", "Submit:CreatorPack:20"],
            (await db.CreditLots.Where(lot => lot.UserId == creatorId).OrderBy(lot => lot.CreditType).ToListAsync())
                .Select(lot => $"{lot.CreditType}:{lot.Source}:{lot.Remaining}"));
        await using var context2 = new ApplicationDbContext(database.Options);
        await using var uow2 = new UnitOfWork(context2);
        var quota = await new TokenQuotaService(uow2).GetCreatorQuotaAsync(creatorId);
        Assert.Equal((8, 25), (quota.AiCredits.TotalRemaining, quota.SubmitCredits.TotalRemaining));
    }

    private static SubscriptionService NewSubscriptionService(UnitOfWork uow) =>
        new(uow, new TokenQuotaService(uow), new NoNotifications(), NullLogger<SubscriptionService>.Instance);

    private static UserAccount NewParent(string username) => new()
    {
        Username = username, Email = username + "@example.test", PasswordHash = "not-a-real-hash", FullName = username,
        Role = UserRole.User, Status = AccountStatus.Active
    };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task<NotificationDto> CreateAsync(int recipientUserId, NotificationType type, string? payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDto());
        public Task<NotificationDto> CreateStoredAsync(int recipientUserId, NotificationType type, string? payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDto());
        public Task<List<NotificationDto>> ListAsync(int currentUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<NotificationDto>());
        public Task<List<NotificationDto>> ListAsync(int currentUserId, string? group, CancellationToken cancellationToken) =>
            Task.FromResult(new List<NotificationDto>());
        public Task<int> GetUnreadCountAsync(int currentUserId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task MarkAsReadAsync(int notificationId, int currentUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkAllAsReadAsync(int currentUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int notificationId, int currentUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
