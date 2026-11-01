using System.Linq.Expressions;
using Moq;
using Microsoft.AspNetCore.DataProtection;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
namespace StoryPlatform.UnitTests;
    internal sealed class ParentAuthoringTestStore
    {
        private readonly Dictionary<Type, object> _data = [];
        private Dictionary<Type, object>? _snapshot;
        private readonly SemaphoreSlim _transaction = new(1, 1);
        public Mock<IUnitOfWork> Uow { get; } = new();
        public ParentAuthoringService Service { get; }
        public AuthoringGuideService Guides => new(Uow.Object);
        public CreatorAuthoringService Creator { get; }
        public IImportPreviewProtector Protector { get; } = new ImportPreviewProtector(new EphemeralDataProtectionProvider());

        public ParentAuthoringTestStore(CreatorAuthoringOptions? options = null)
        {
            Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<StoryAuthoringOperation>(); Register<AuditLog>();
            Register<ChildProfile>();
            Register<ChildSession>(); Register<StoryAssistanceTask>(); Register<StoryAssistanceStep>();
            Register<SafetyPolicy>(); Register<SafetyPolicyCategory>(); Register<ContentCategory>();
            Register<LearningProfile>(); Register<StoryVocabulary>(); Register<QuizItem>(); Register<ModerationItem>(); Register<ParentStoryOperation>(); Register<StoryGenerationJob>(); Register<StoryGenerationRequest>(); Register<MediaContext>(); Register<MediaAsset>(); Register<StoryScene>(); Register<StorySegment>(); Register<IllustrationBeat>();
            Register<AuthoringGuideCatalog>(); Register<AuthoringGuideCatalogVersion>();
            Register<AuthoringGuideItem>(); Register<AuthoringGuidePlaceholder>();
            Register<MediaAssetGenerationTask>();
            Register<ListeningInteraction>(); Register<ListeningInteractionOption>();
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns<CancellationToken>(async ct => {
                await _transaction.WaitAsync(ct);
                _snapshot = _data.ToDictionary(x => x.Key, x => System.Text.Json.JsonSerializer.Deserialize(
                    System.Text.Json.JsonSerializer.Serialize(x.Value, x.Value.GetType()), x.Value.GetType())!);
            });
            Uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { _snapshot = null; _transaction.Release(); return Task.CompletedTask; });
            Uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => {
                if (_snapshot is not null) { _data.Clear(); foreach (var pair in _snapshot) _data[pair.Key] = pair.Value; }
                _snapshot = null; _transaction.Release(); return Task.CompletedTask;
            });
            Items<UserAccount>().AddRange([
                new UserAccount { Id = 1, Status = AccountStatus.Active },
                new UserAccount { Id = 2, Status = AccountStatus.Active },
                new UserAccount { Id = 9, Status = AccountStatus.Active, Role = UserRole.Administrator }
            ]);
            new AuthoringGuideCatalogSeeder(Uow.Object).SeedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Creator = new CreatorAuthoringService(Uow.Object, options ?? new CreatorAuthoringOptions(), Protector);
            Items<ChildProfile>().Add(new ChildProfile { Id = 3, OwnerUserId = 1, Status = ChildProfileStatus.Active, Language = "vi", AgeBand = AgeBand.Age_6_8 });
            Items<SafetyPolicy>().Add(new SafetyPolicy { Id = 1, ChildProfileId = 3, MaxStoryLength = 5000 });
            Service = new ParentAuthoringService(Uow.Object, options ?? new CreatorAuthoringOptions(), Protector,
                new ChildProfileAccessGuard(Uow.Object), new StorySegmentService());
        }

        public List<T> Items<T>() where T : class
        {
            if (!_data.TryGetValue(typeof(T), out var list)) _data[typeof(T)] = list = new List<T>();
            return (List<T>)list;
        }

        private void Register<T>() where T : BaseEntity
        {
            var repo = new Mock<IGenericRepository<T>>();
            Uow.Setup(x => x.Repository<T>()).Returns(repo.Object);
            repo.Setup(x => x.CountAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
                .Returns<Expression<Func<T, bool>>, CancellationToken>((predicate, _) => Task.FromResult(Items<T>().Count(predicate.Compile())));
            repo.Setup(x => x.Query()).Returns(() => Items<T>().AsQueryable());
            repo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns<object, CancellationToken>((id, _) => Task.FromResult(Items<T>().FirstOrDefault(x => x.Id == (int)id)));
            repo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns<Expression<Func<T, bool>>, string?, CancellationToken>((predicate, _, _) => Task.FromResult(Items<T>().FirstOrDefault(predicate.Compile())));
            repo.Setup(x => x.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
                .Returns<Expression<Func<T, bool>>, CancellationToken>((predicate, _) => Task.FromResult(Items<T>().Any(predicate.Compile())));
            repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns<Expression<Func<T, bool>>, string?, CancellationToken>((predicate, _, _) => Task.FromResult<IReadOnlyList<T>>(Items<T>().Where(predicate.Compile()).ToList()));
            repo.Setup(x => x.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns<T, CancellationToken>((entity, _) =>
            {
                entity.Id = Items<T>().Select(x => x.Id).DefaultIfEmpty().Max() + 1;
                Items<T>().Add(entity);
                return Task.FromResult(entity);
            });
            repo.Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<CancellationToken>()))
                .Returns<IEnumerable<T>, CancellationToken>((entities, _) =>
                {
                    foreach (var entity in entities)
                    {
                        entity.Id = Items<T>().Select(x => x.Id).DefaultIfEmpty().Max() + 1;
                        Items<T>().Add(entity);
                    }
                    return Task.CompletedTask;
                });
            repo.Setup(x => x.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<T, bool>>>(),
                    It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns<int, int, Expression<Func<T, bool>>?, Func<IQueryable<T>, IOrderedQueryable<T>>?, string?, CancellationToken>((index, size, filter, order, _, _) =>
                {
                    var rows = Items<T>().AsQueryable();
                    if (filter is not null) rows = rows.Where(filter);
                    var count = rows.Count();
                    if (order is not null) rows = order(rows);
                    return Task.FromResult<(IReadOnlyList<T>, int)>((rows.Skip((index - 1) * size).Take(size).ToList(), count));
                });
        }
    }
