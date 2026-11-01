using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Rubrics;
using StoryPlatform.Contracts.AI.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class ModerationRubricTests
{
    private static RubricDefinition Definition(string context = "Read the complete story context.") => new("Assess child safety.",
        GrayZoneRules.CriterionCodes.Select(code => new RubricCriterion(code, code, "Assess evidence.", context,
            Enumerable.Range(0, 5).ToDictionary(x => x.ToString(), x => "Level " + x), [])).ToArray());
    private static RubricWriteRequest Write(string version = "v1") => new(version, "Rubric test", Definition());

    // Real EF mappings/repository and SQLite transactions; PostgreSQL advisory locks and Vertex are not simulated as live proof.
    private sealed class Db(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            foreach (var type in b.Model.GetEntityTypes().Select(x => x.ClrType).ToArray())
                if (type != typeof(UserAccount) && type != typeof(ModerationRubricVersion) && type != typeof(ModerationRubricAuditLog)) b.Ignore(type);
            b.Entity<UserAccount>().Ignore(x => x.Stories).Ignore(x => x.ChildProfiles);
            b.ApplyConfiguration(new ModerationRubricConfiguration());
            b.ApplyConfiguration(new ModerationRubricAuditConfiguration());
        }
    }
    private sealed class Session : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public Db Db { get; }
        public Mock<IUnitOfWork> Uow { get; } = new();
        private readonly UnitOfWork _repositories;
        private IDbContextTransaction? _transaction;
        public Provider Provider { get; } = new();
        public ModerationRubricService Service { get; }
        public Session()
        {
            _connection.Open();
            Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
            Db.Users.AddRange(new UserAccount { Id = 1, Role = UserRole.Administrator, Status = AccountStatus.Active },
                new UserAccount { Id = 2, Role = UserRole.Moderator, Status = AccountStatus.Active },
                new UserAccount { Id = 3, Role = UserRole.User, Status = AccountStatus.Active });
            Db.SaveChanges(); Db.ChangeTracker.Clear();
            _repositories = new(Db);
            Uow.Setup(x => x.Repository<UserAccount>()).Returns(_repositories.Repository<UserAccount>());
            Uow.Setup(x => x.Repository<ModerationRubricVersion>()).Returns(_repositories.Repository<ModerationRubricVersion>());
            Uow.Setup(x => x.Repository<ModerationRubricAuditLog>()).Returns(_repositories.Repository<ModerationRubricAuditLog>());
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) => _transaction = await Db.Database.BeginTransactionAsync(ct));
            Uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => _repositories.SaveChangesAsync(ct));
            Uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
            { await _repositories.SaveChangesAsync(ct); await _transaction!.CommitAsync(ct); await _transaction.DisposeAsync(); _transaction = null; Db.ChangeTracker.Clear(); });
            Uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
            { if (_transaction is not null) { await _transaction.RollbackAsync(ct); await _transaction.DisposeAsync(); _transaction = null; } Db.ChangeTracker.Clear(); });
            Service = new(Uow.Object, [Provider]);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    private sealed class Provider : IModelModerationProvider
    {
        public string Name => "Vertex";
        public GrayZoneScan? Received { get; private set; }
        public Func<Task>? DuringCall { get; set; }
        public async Task<ModelModerationResult> EvaluateAsync(GrayZoneScan scan, CancellationToken ct)
        {
            Received = scan;
            if (DuringCall is not null) await DuringCall();
            return new(0, "test-provider", []) { RubricVersion = scan.RubricVersion };
        }
    }
    [Fact]
    public async Task LifecyclePreservesImmutableVersionsAndAudit()
    {
        await using var s = new Session();
        var first = await s.Service.CreateAsync(2, Write(), default);
        var updated = await s.Service.UpdateAsync(2, first.Id, Write() with { Revision = first.Revision, Definition = Definition("Consider consequences.") }, default);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, first.Id, Write() with { Revision = first.Revision }, default));
        var active = await s.Service.ActivateAsync(1, first.Id, updated.Revision, default);
        Assert.Equal("Active", active.Status);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, active.Id, Write() with { Revision = active.Revision }, default));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeleteAsync(2, active.Id, active.Revision, default));
        var clone = await s.Service.CloneAsync(2, active.Id, new("v2", "Next"), default);
        await s.Service.ActivateAsync(1, clone.Id, clone.Revision, default);
        Assert.Equal("Retired", (await s.Service.GetAsync(2, first.Id, default)).Status);
        Assert.Equal(clone.Id, (await s.Service.GetActiveAsync(default)).Id);
        Assert.Equal(1, await s.Db.ModerationRubricVersions.CountAsync(x => x.Status == ModerationRubricStatus.Active));
        Assert.Equal(6, await s.Db.ModerationRubricAuditLogs.CountAsync());
        Assert.Equal(active.ContentHash, (await s.Service.GetAsync(2, first.Id, default)).ContentHash);
        s.Uow.Verify(x => x.AcquireTransactionLockAsync(It.Is<int>(id => id < 0), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }
    [Theory]
    [InlineData(3, false)] [InlineData(3, true)]
    public async Task EnforcesRolesInService(int actor, bool activate)
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            if (activate) await s.Service.ActivateAsync(actor, row.Id, row.Revision, default);
            else await s.Service.GetAsync(actor, row.Id, default);
        });
        Assert.Equal("Draft", (await s.Service.GetAsync(2, row.Id, default)).Status);
    }
    [Fact]
    public async Task ModeratorCanActivateDraft()
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        var active = await s.Service.ActivateAsync(2, row.Id, row.Revision, default);
        Assert.Equal("Active", active.Status);
        Assert.Equal(2, active.ActivatedByUserId);
        Assert.Equal(row.Id, (await s.Service.GetActiveAsync(default)).Id);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RejectsLockedOrInactiveModerator(bool locked)
    {
        await using var s = new Session();
        var user = await s.Db.Users.SingleAsync(x => x.Id == 2);
        if (locked) user.LockedUntil = DateTime.UtcNow.AddHours(1); else user.Status = AccountStatus.Registered;
        await s.Db.SaveChangesAsync(); s.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.CreateAsync(2, Write(), default));
    }
    [Fact]
    public async Task DeletedDraftRemainsAuditableAndCannotBeActivated()
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        await s.Service.DeleteAsync(2, row.Id, row.Revision, default);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetAsync(2, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.ActivateAsync(1, row.Id, row.Revision, default));
        Assert.True((await s.Db.ModerationRubricVersions.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Contains(await s.Db.ModerationRubricAuditLogs.ToListAsync(), x => x.Action == "Deleted");
    }
    [Fact]
    public async Task CreationStartsAsDraftWithoutImplicitActivationOrFileImport()
    {
        await using var s = new Session();
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.GetActiveAsync(default));
        var first = await s.Service.CreateAsync(2, Write(), default);
        Assert.Equal("Draft", first.Status);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(2, Write(), default));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.GetActiveAsync(default));
        Assert.Single(await s.Db.ModerationRubricVersions.ToListAsync());
        RubricRules.Validate(first.Definition);
        Assert.DoesNotContain(typeof(ModerationRubricService).Assembly.GetManifestResourceNames(), x => x.Contains("InitialGrayZoneRubric"));
    }
    [Fact]
    public async Task TrialPinsSnapshotAndDoesNotCreateStoryOrModerationState()
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        s.Provider.DuringCall = async () => { await s.Service.UpdateAsync(2, row.Id, Write() with { Revision = row.Revision, Definition = Definition("Changed") }, default); };
        await s.Service.EvaluateAsync(2, row.Id, new(row.Revision, "phase1", "6-8", "vi", "Đèn nhỏ", "An cùng mẹ sửa đèn."), default);
        Assert.Equal(row.ContentHash, s.Provider.Received!.Rubric!.ContentHash);
        Assert.NotEqual(row.ContentHash, (await s.Service.GetAsync(2, row.Id, default)).ContentHash);
        var audit = await s.Db.ModerationRubricAuditLogs.SingleAsync(x => x.Action == "Evaluated");
        Assert.Contains(row.ContentHash, audit.AfterJson);
        // Isolated context has only rubric/account tables: any access to a Story/ModerationItem repository fails the mock setup.
        s.Uow.Verify(x => x.Repository<Story>(), Times.Never);
        s.Uow.Verify(x => x.Repository<ModerationItem>(), Times.Never);
    }
    [Fact]
    public async Task RuntimePinsActiveRubricAndInvalidatesReuseWhenActiveChanges()
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        await s.Service.ActivateAsync(1, row.Id, row.Revision, default);
        var evaluator = new GrayZoneEvaluator(new() { Provider = "Vertex", WarningThreshold = 30, BlockThreshold = 70, MaximumRiskScore = 100 }, [s.Provider], s.Service);
        var scan = new GrayZoneScan("phase1", "6-8", "vi", [new("paragraph", "p0", "An giúp mẹ.")], []);
        var assessment = await evaluator.EvaluateAsync(scan, default);
        Assert.Equal(row.Id, assessment.RubricId);
        Assert.True(await evaluator.CanReuseAsync(assessment, default));
        var next = await s.Service.CloneAsync(2, row.Id, new("v2", "Next"), default);
        s.Provider.DuringCall = async () => { await s.Service.ActivateAsync(1, next.Id, next.Revision, default); };
        var inFlight = await evaluator.EvaluateAsync(scan, default);
        Assert.Equal(row.Id, inFlight.RubricId);
        Assert.False(await evaluator.CanReuseAsync(inFlight, default));
        Assert.Equal(row.Id, s.Provider.Received!.Rubric!.Id);
        Assert.Equal(GrayZoneEvaluator.Fingerprint(scan), GrayZoneEvaluator.Fingerprint(s.Provider.Received));
    }
    [Fact]
    public async Task RuntimeRequiresActiveRubricButHardRulesStillWin()
    {
        await using var s = new Session();
        var evaluator = new GrayZoneEvaluator(new() { Provider = "Vertex", WarningThreshold = 30, BlockThreshold = 70, MaximumRiskScore = 100 }, [s.Provider], s.Service);
        await Assert.ThrowsAsync<ConflictException>(() => evaluator.EvaluateAsync(new("phase1", "6-8", "vi", [new("section", "text", "An giúp mẹ.")], []), default));
        Assert.Equal(ModerationZone.HardBlock, (await evaluator.EvaluateAsync(new("phase1", "6-8", "vi", [new("section", "text", "test@example.com")], []), default)).Zone);
        Assert.Null(s.Provider.Received);
    }
    [Fact]
    public async Task ActivationRejectsStaleRevisionWithoutRetiringActive()
    {
        await using var s = new Session();
        var row = await s.Service.CreateAsync(2, Write(), default);
        await s.Service.ActivateAsync(1, row.Id, row.Revision, default);
        var next = await s.Service.CloneAsync(2, row.Id, new("v2", "Next"), default);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.ActivateAsync(1, next.Id, 99, default));
        Assert.Equal(row.Id, (await s.Service.GetActiveAsync(default)).Id);
    }
    [Fact]
    public void ControllerProtectsManagementAndActivation()
    {
        Assert.Equal("Moderator,Administrator", typeof(ModerationRubricsController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Empty(typeof(ModerationRubricsController).GetMethod("Activate")!.GetCustomAttributes(typeof(AuthorizeAttribute), false));
        Assert.Null(typeof(ModerationRubricsController).GetMethod("Bootstrap"));
    }
    [Fact]
    public void CanonicalHashIgnoresOrderAndRejectsInvalidCriteriaAndLevels()
    {
        var definition = Definition();
        Assert.Equal(RubricRules.Hash(definition), RubricRules.Hash(definition with { Criteria = definition.Criteria.Reverse().Select(c => c with { Levels = c.Levels.Reverse().ToDictionary(x => x.Key, x => x.Value) }).ToArray() }));
        Assert.Throws<ArgumentException>(() => RubricRules.Validate(definition with { Criteria = definition.Criteria.Skip(1).ToArray() }));
        var levels = new Dictionary<string, string>(definition.Criteria[0].Levels); levels.Remove("4");
        Assert.Throws<ArgumentException>(() => RubricRules.Validate(definition with { Criteria = definition.Criteria.Select((c, i) => i == 0 ? c with { Levels = levels } : c).ToArray() }));
    }
    [Fact]
    public void SeedDraftDefinitionHashMatchesRuntimeCanonicalization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoryPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = File.ReadAllText(Path.Combine(root.FullName, "Database/Seed/52_moderation_rubric_versions.sql"));
        var match = Regex.Match(sql, "'(?<definition>\\{.*\\})', '(?<hash>[A-F0-9]{64})', 'Draft'");
        Assert.True(match.Success);
        var definition = JsonSerializer.Deserialize<RubricDefinition>(match.Groups["definition"].Value.Replace("''", "'"), RubricRules.Json)!;
        Assert.Equal(match.Groups["hash"].Value, RubricRules.Hash(definition));
    }
}
