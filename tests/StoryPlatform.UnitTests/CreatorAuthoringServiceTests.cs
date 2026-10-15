using System.IO.Compression;
using System.Linq.Expressions;
using System.Text;
using Moq;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Nodes;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAuthoringServiceTests
{
    [Fact]
    public async Task ManualSave_CannotWriteIntoParentStory_EvenForItsAuthor()
    {
        var store = new Store();
        store.Items<Story>().Add(new Story { Id = 1, AuthorUserId = 1, ChildProfileId = 3, Scope = StoryScope.Parent });
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.SaveContentAsync(1, 1,
            new SaveCreatorContentRequest { OperationId = "manual", Content = "Text" }));
        Assert.Empty(store.Items<StoryVersion>());
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task ImportConfirm_RejectsTamperedTokenOrChangedText()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var preview = await store.Service.PreviewImportAsync(1, draft.StoryId, Preview(draft, "preview"));
        var changedText = await Assert.ThrowsAsync<BadRequestException>(() => store.Service.ConfirmImportAsync(1, draft.StoryId,
            Confirm(draft, preview, "changed-text") with { Content = "Changed" }));
        Assert.Equal("Nội dung khác bản xem trước; cần preview lại.", changedText.Message);
        var invalidToken = await Assert.ThrowsAsync<BadRequestException>(() => store.Service.ConfirmImportAsync(1, draft.StoryId,
            Confirm(draft, preview, "changed-token") with { PreviewToken = "invalid-token" }));
        Assert.Equal("PreviewToken không hợp lệ.", invalidToken.Message);
        Assert.Empty(store.Items<StoryVersion>());
        Assert.Equal(draft.RevisionToken, (await store.Service.GetDraftAsync(1, draft.StoryId)).RevisionToken);
    }

    [Fact]
    public async Task CurrentVersionPointerAndManualProvenance_AreKeptOnSharedRows()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var first = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "v1", "First"));
        var next = await store.Service.SaveContentAsync(1, draft.StoryId, Save(first.Story, "v2", "Second"));
        Assert.Equal(next.Version.VersionId, Assert.Single(store.Items<StoryVersion>(), x => x.IsCurrent).Id);
        Assert.Equal("First", store.Items<StoryVersion>().Single(x => x.Id == first.Version.VersionId).Content);
        Assert.Equal(StorySource.Manual, store.Items<Story>().Single().Source);
        Assert.False(store.Items<Story>().Single().IsPublished);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task CreatorWithoutChild_CreatesEmptyDraft_WithoutAIOrStoryPipeline()
    {
        var store = new Store();
        var result = await store.Service.CreateDraftAsync(1, NewDraft("draft"));
        Assert.Equal(1, result.AuthorUserId);
        Assert.Null(result.SelectedVersionId);
        var story = Assert.Single(store.Items<Story>());
        Assert.Null(story.ChildProfileId);
        Assert.Equal(StoryScope.Creator, story.Scope);
        Assert.Equal(StorySource.Manual, story.Source);
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Equal(UserRole.Parent, store.Items<UserAccount>().Single(x => x.Id == 1).Role);
    }

    [Fact]
    public async Task DefaultOptions_AllowActiveAccountToCreateAndListDrafts()
    {
        var store = new Store(new CreatorAuthoringOptions());
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("draft"));
        var list = await store.Service.GetMineAsync(1, 1, 20);
        Assert.Equal(draft.StoryId, Assert.Single(list.Items).StoryId);
    }

    [Theory]
    [InlineData(AccountStatus.Registered)]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.PasswordResetPending)]
    public async Task InactiveAccount_IsDenied(AccountStatus status)
    {
        var store = new Store();
        store.Items<UserAccount>().Single(x => x.Id == 1).Status = status;
        await Assert.ThrowsAsync<ForbiddenException>(() => store.Service.CreateDraftAsync(1, NewDraft("draft")));
        Assert.Empty(store.Items<Story>());
    }

    [Fact]
    public async Task MissingAccount_IsDenied()
    {
        var store = new Store();
        store.Items<UserAccount>().RemoveAll(x => x.Id == 1);
        await Assert.ThrowsAsync<ForbiddenException>(() => store.Service.CreateDraftAsync(1, NewDraft("draft")));
        Assert.Empty(store.Items<Story>());
    }

    [Fact]
    public async Task LockedAccount_IsDenied()
    {
        var store = new Store();
        store.Items<UserAccount>().Single(x => x.Id == 1).LockedUntil = DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<ForbiddenException>(() => store.Service.CreateDraftAsync(1, NewDraft("draft")));
    }

    [Fact]
    public async Task CreateRetry_ReturnsSameDraft_AndRejectsKeyWithDifferentPayload()
    {
        var store = new Store();
        var request = NewDraft("same-key");
        var original = await store.Service.CreateDraftAsync(1, request);
        var retry = await store.Service.CreateDraftAsync(1, request);
        Assert.Equal(original, retry);
        Assert.Single(store.Items<Story>());
        await Assert.ThrowsAsync<ConflictException>(() => store.Service.CreateDraftAsync(1, request with { Title = "Truyện khác" }));
    }

    [Fact]
    public async Task Save_CreatesNewImmutableVersions_PreservesOriginalTextAndOrigin()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var saved = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "v1", "  Lan\n\n  và Minh  "));
        var next = await store.Service.SaveContentAsync(1, draft.StoryId, Save(saved.Story, "v2", "Lan giúp Minh."));
        var old = await store.Service.GetVersionAsync(1, draft.StoryId, saved.Version.VersionId);
        Assert.Equal("  Lan\n\n  và Minh  ", old.Content);
        Assert.Equal("Write", next.Version.Origin);
        Assert.Equal(saved.Version.VersionId, next.Version.BaseVersionId);
        Assert.Equal(2, next.Version.VersionNo);
        Assert.NotEqual(saved.Story.RevisionToken, next.Story.RevisionToken);
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task SaveRetry_StillReplaysAfterAnotherVersionWasSaved()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var request = Save(draft, "save", "Lan.");
        var first = await store.Service.SaveContentAsync(1, draft.StoryId, request);
        await store.Service.SaveContentAsync(1, draft.StoryId, Save(first.Story, "second", "Minh."));
        var retry = await store.Service.SaveContentAsync(1, draft.StoryId, request);
        Assert.Equal(first, retry);
        Assert.Equal(2, store.Items<StoryVersion>().Count);
        Assert.Equal(2, (await store.Service.GetDraftAsync(1, draft.StoryId)).LastVersionNo);
    }

    [Fact]
    public async Task StaleTokenOrBaseVersion_ConflictsWithoutAppendingARecord()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var saved = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "save", "Lan."));
        var staleToken = await Assert.ThrowsAsync<ConflictException>(() => store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "stale", "Wrong.")));
        Assert.Equal("Bản nháp đã thay đổi.", staleToken.Message);
        var wrongBase = await Assert.ThrowsAsync<ConflictException>(() => store.Service.SaveContentAsync(1, draft.StoryId,
            Save(saved.Story, "wrong-base", "Wrong.") with { BaseVersionId = 123 }));
        Assert.Equal("Phiên bản nguồn đã thay đổi.", wrongBase.Message);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ConcurrentSaveCommands_WithSameRevision_AllowOnlyOneAppend()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        async Task<Exception?> Attempt(string key)
        {
            try { await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, key, key)); return null; }
            catch (Exception ex) { return ex; }
        }
        var results = await Task.WhenAll(Attempt("save-a"), Attempt("save-b"));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is ConflictException);
        Assert.Single(store.Items<StoryVersion>());
        store.Uow.Verify(x => x.AcquireTransactionLockAsync(-1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnotherCreator_CannotReadSaveRestoreOrImportIntoProduct()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var saved = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "save", "Lan."));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.GetDraftAsync(2, draft.StoryId));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.GetVersionsAsync(2, draft.StoryId));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.SaveContentAsync(2, draft.StoryId, Save(saved.Story, "other", "Other.")));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.RestoreVersionAsync(2, draft.StoryId,
            new RestoreCreatorVersionRequest { OperationId = "restore-other", RevisionToken = saved.Story.RevisionToken,
                BaseVersionId = saved.Version.VersionId, SourceVersionId = saved.Version.VersionId }));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.PreviewImportAsync(2, draft.StoryId,
            Preview(draft, "other-preview")));
        Assert.Empty((await store.Service.GetMineAsync(2, 1, 20)).Items);
    }

    [Fact]
    public async Task PastePreview_DoesNotChangeDraft_AndConfirmImportsOnlyTheReviewedText()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var preview = await store.Service.PreviewImportAsync(1, draft.StoryId, Preview(draft, "preview"));
        Assert.Empty(store.Items<StoryVersion>());
        Assert.Equal(draft.RevisionToken, (await store.Service.GetDraftAsync(1, draft.StoryId)).RevisionToken);
        var request = Confirm(draft, preview, "confirm");
        var imported = await store.Service.ConfirmImportAsync(1, draft.StoryId, request);
        Assert.Equal(preview.Content, imported.Version.Content);
        Assert.Equal("Import", imported.Version.Origin);
        Assert.Equal(preview.SourceHash, imported.Version.ImportSourceHash);
        Assert.Equal(imported, await store.Service.ConfirmImportAsync(1, draft.StoryId, request));
        Assert.Single(store.Items<StoryVersion>());
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".docx")]
    public async Task FilePreview_ExtractsWithoutSavingAVersion_AndCanBeRepeated(string extension)
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        byte[] bytes;
        if (extension == ".docx")
        {
            using var document = new MemoryStream();
            using (var archive = new ZipArchive(document, ZipArchiveMode.Create, true))
            {
                using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
                writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Lan giúp Minh.</w:t></w:r></w:p></w:body></w:document>");
            }
            bytes = document.ToArray();
        }
        else bytes = Encoding.UTF8.GetBytes("Lan giúp Minh.");
        var preview = await store.Service.PreviewFileAsync(1, draft.StoryId, draft.RevisionToken, "file",
            "truyen" + extension, new MemoryStream(bytes));
        var retry = await store.Service.PreviewFileAsync(1, draft.StoryId, draft.RevisionToken, "file",
            "truyen" + extension, new MemoryStream(bytes));
        Assert.Equal(preview.Content, retry.Content);
        Assert.Equal(preview.SourceHash, retry.SourceHash);
        Assert.Equal("Lan giúp Minh.", preview.Content);
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task InvalidFileAndOversize_DoNotReplaceExistingContent()
    {
        var store = new Store(new CreatorAuthoringOptions { MaximumImportBytes = 4 });
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var saved = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "save", "Lan."));
        await Assert.ThrowsAsync<BadRequestException>(() => store.Service.PreviewFileAsync(1, draft.StoryId,
            saved.Story.RevisionToken, "bad-file", "bad.pdf", new MemoryStream([1])));
        await Assert.ThrowsAsync<BadRequestException>(() => store.Service.PreviewFileAsync(1, draft.StoryId,
            saved.Story.RevisionToken, "oversize", "large.txt", new MemoryStream(Encoding.UTF8.GetBytes("12345"))));
        await Assert.ThrowsAsync<BadRequestException>(() => store.Service.PreviewFileAsync(1, draft.StoryId,
            saved.Story.RevisionToken, "encoding", "bad.txt", new MemoryStream([0xff, 0xff])));
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ExpiredPreviewOrMissingRights_IsRejectedWithoutVersion()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var preview = await store.Service.PreviewImportAsync(1, draft.StoryId, Preview(draft, "preview"));
        await Assert.ThrowsAsync<BadRequestException>(() => store.Service.ConfirmImportAsync(1, draft.StoryId,
            Confirm(draft, preview, "missing-rights") with { RightsDeclaration = " " }));
        var payload = JsonNode.Parse(store.Protector.Unprotect(preview.PreviewToken))!;
        payload["expiresAt"] = DateTime.UtcNow.AddSeconds(-1);
        preview = preview with { PreviewToken = store.Protector.Protect(payload.ToJsonString()) };
        await Assert.ThrowsAsync<ConflictException>(() => store.Service.ConfirmImportAsync(1, draft.StoryId, Confirm(draft, preview, "expired")));
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task PreviewFromAnotherProduct_CannotBeConfirmed()
    {
        var store = new Store();
        var first = await store.Service.CreateDraftAsync(1, NewDraft("first"));
        var second = await store.Service.CreateDraftAsync(1, NewDraft("second"));
        var preview = await store.Service.PreviewImportAsync(1, first.StoryId, Preview(first, "preview"));
        await Assert.ThrowsAsync<NotFoundException>(() => store.Service.ConfirmImportAsync(1, second.StoryId, Confirm(second, preview, "cross")));
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Restore_CreatesAnotherVersion_AndDoesNotMutateHistory()
    {
        var store = new Store();
        var draft = await store.Service.CreateDraftAsync(1, NewDraft("create"));
        var first = await store.Service.SaveContentAsync(1, draft.StoryId, Save(draft, "first", "Lan."));
        var second = await store.Service.SaveContentAsync(1, draft.StoryId, Save(first.Story, "second", "Minh."));
        var restored = await store.Service.RestoreVersionAsync(1, draft.StoryId, new RestoreCreatorVersionRequest
        {
            OperationId = "restore", RevisionToken = second.Story.RevisionToken,
            BaseVersionId = second.Version.VersionId, SourceVersionId = first.Version.VersionId
        });
        Assert.Equal(3, restored.Version.VersionNo);
        Assert.Equal("Restore", restored.Version.Origin);
        Assert.Equal(first.Version.VersionId, restored.Version.RestoredFromVersionId);
        Assert.Equal("Lan.", restored.Version.Content);
        Assert.Equal("Minh.", (await store.Service.GetVersionAsync(1, draft.StoryId, second.Version.VersionId)).Content);
        Assert.Equal(3, (await store.Service.GetVersionsAsync(1, draft.StoryId)).Count);
    }

    private static CreateCreatorDraftRequest NewDraft(string key) => new() { OperationId = key, Title = "Truyện" };
    private static SaveCreatorContentRequest Save(CreatorDraftDto draft, string key, string content) => new()
    {
        OperationId = key, RevisionToken = draft.RevisionToken, BaseVersionId = draft.SelectedVersionId,
        Title = "Truyện", Content = content
    };
    private static PreviewCreatorImportRequest Preview(CreatorDraftDto draft, string key) => new()
    {
        OperationId = key, RevisionToken = draft.RevisionToken, Content = "Lan và Minh."
    };
    private static ConfirmCreatorImportRequest Confirm(CreatorDraftDto draft, CreatorImportPreviewDto preview, string key) => new()
    {
        OperationId = key, RevisionToken = draft.RevisionToken, BaseVersionId = draft.SelectedVersionId,
        PreviewToken = preview.PreviewToken, Content = preview.Content, Title = "Nhập truyện", SourceDeclaration = "Tác giả", RightsDeclaration = "Tôi có quyền sử dụng"
    };

    /// <summary>In-memory repository double; no DB, migrations, AI, or quota dependency.</summary>
    private sealed class Store
    {
        private readonly Dictionary<Type, object> _data = [];
        private readonly SemaphoreSlim _transaction = new(1, 1);
        public Mock<IUnitOfWork> Uow { get; } = new();
        public CreatorAuthoringService Service { get; }
        public IImportPreviewProtector Protector { get; } = new ImportPreviewProtector(new EphemeralDataProtectionProvider());

        public Store(CreatorAuthoringOptions? options = null)
        {
            Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<AuditLog>();
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns<CancellationToken>(ct => _transaction.WaitAsync(ct));
            Uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { _transaction.Release(); return Task.CompletedTask; });
            Uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { _transaction.Release(); return Task.CompletedTask; });
            Items<UserAccount>().AddRange([
                new UserAccount { Id = 1, Status = AccountStatus.Active },
                new UserAccount { Id = 2, Status = AccountStatus.Active },
                new UserAccount { Id = 9, Status = AccountStatus.Active, Role = UserRole.Administrator }
            ]);
            Service = new CreatorAuthoringService(Uow.Object, options ?? new CreatorAuthoringOptions(), Protector);
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
}
