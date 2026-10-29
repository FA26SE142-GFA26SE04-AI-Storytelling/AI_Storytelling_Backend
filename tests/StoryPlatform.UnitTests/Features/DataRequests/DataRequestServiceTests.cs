using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.DataRequests.DTOs;
using StoryPlatform.Application.Features.DataRequests.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.DataRequests;

public class DataRequestServiceTests
{
    private readonly Mock<IGenericRepository<ChildProfile>> _childProfileRepository = new();
    private readonly Mock<IGenericRepository<DataRequest>> _dataRequestRepository = new();
    private readonly Mock<IGenericRepository<LearningProfile>> _learningProfileRepository = new();
    private readonly Mock<IGenericRepository<SafetyPolicy>> _safetyPolicyRepository = new();
    private readonly Mock<IGenericRepository<ReadingSession>> _readingSessionRepository = new();
    private readonly Mock<IGenericRepository<Achievement>> _achievementRepository = new();
    private readonly Mock<IGenericRepository<Badge>> _badgeRepository = new();
    private readonly Mock<IGenericRepository<VocabularyNotebookEntry>> _vocabularyRepository = new();
    private readonly Mock<IGenericRepository<InterventionCase>> _interventionCaseRepository = new();
    private readonly Mock<IGenericRepository<Recommendation>> _recommendationRepository = new();
    private readonly Mock<IGenericRepository<TelemetryLog>> _telemetryLogRepository = new();
    private readonly Mock<IGenericRepository<QuizAttempt>> _quizAttemptRepository = new();
    private readonly Mock<IGenericRepository<AssignmentRecipient>> _assignmentRecipientRepository = new();
    private readonly Mock<IGenericRepository<Assignment>> _assignmentRepository = new();
    private readonly Mock<IGenericRepository<Story>> _storyRepository = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepository = new();
    private readonly Mock<IGenericRepository<ChildAccessCredential>> _credentialRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly Mock<IWorkbookExportBuilder> _exportBuilder = new();
    private readonly Mock<IArchiveExportBuilder> _archiveExportBuilder = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IChildSessionRevoker> _childSessionRevoker = new();
    private readonly Mock<IChildProfileEraser> _eraser = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly DataRequestService _sut;

    public DataRequestServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<ChildProfile>()).Returns(_childProfileRepository.Object);
        _unitOfWork.Setup(work => work.Repository<DataRequest>()).Returns(_dataRequestRepository.Object);
        _unitOfWork.Setup(work => work.Repository<LearningProfile>()).Returns(_learningProfileRepository.Object);
        _unitOfWork.Setup(work => work.Repository<SafetyPolicy>()).Returns(_safetyPolicyRepository.Object);
        _unitOfWork.Setup(work => work.Repository<ReadingSession>()).Returns(_readingSessionRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Achievement>()).Returns(_achievementRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Badge>()).Returns(_badgeRepository.Object);
        _unitOfWork.Setup(work => work.Repository<VocabularyNotebookEntry>()).Returns(_vocabularyRepository.Object);
        _unitOfWork.Setup(work => work.Repository<InterventionCase>()).Returns(_interventionCaseRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Recommendation>()).Returns(_recommendationRepository.Object);
        _unitOfWork.Setup(work => work.Repository<TelemetryLog>()).Returns(_telemetryLogRepository.Object);
        _unitOfWork.Setup(work => work.Repository<QuizAttempt>()).Returns(_quizAttemptRepository.Object);
        _unitOfWork.Setup(work => work.Repository<AssignmentRecipient>()).Returns(_assignmentRecipientRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Assignment>()).Returns(_assignmentRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Story>()).Returns(_storyRepository.Object);
        _unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(_userRepository.Object);
        _unitOfWork.Setup(work => work.Repository<ChildAccessCredential>()).Returns(_credentialRepository.Object);

        _learningProfileRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LearningProfile>());
        _safetyPolicyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SafetyPolicy>());
        _readingSessionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<ReadingSession, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ReadingSession>());
        _achievementRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Achievement, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Achievement>());
        _badgeRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Badge, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Badge>());
        _vocabularyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<VocabularyNotebookEntry, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VocabularyNotebookEntry>());
        _interventionCaseRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<InterventionCase, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InterventionCase>());
        _recommendationRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Recommendation, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Recommendation>());
        _telemetryLogRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<TelemetryLog, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TelemetryLog>());
        _quizAttemptRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<QuizAttempt, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<QuizAttempt>());
        _assignmentRecipientRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<AssignmentRecipient, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AssignmentRecipient>());
        _assignmentRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Assignment>());
        _storyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Story, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Story>());
        _credentialRepository.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildAccessCredential, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildAccessCredential?)null);

        _sut = new DataRequestService(
            _unitOfWork.Object, _auditLogWriter.Object, _exportBuilder.Object,
            _archiveExportBuilder.Object, _passwordHasher.Object,
            _eraser.Object, _notificationService.Object, _emailSender.Object);
    }

    private static ChildProfile MakeChild(int id, int ownerUserId) => new()
    {
        Id = id,
        OwnerUserId = ownerUserId,
        Nickname = "Bé An",
        Status = ChildProfileStatus.Active
    };

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_ChildNotFound_ThrowsNotFound()
    {
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildProfile?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(
            1, new CreateDataRequestDto { ChildProfileId = 5, RequestType = "export" }));
    }

    [Fact]
    public async Task CreateAsync_NotOwner_ThrowsForbidden()
    {
        var child = MakeChild(5, ownerUserId: 99);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateAsync(
            1, new CreateDataRequestDto { ChildProfileId = 5, RequestType = "export" }));
    }

    [Fact]
    public async Task CreateAsync_InvalidRequestType_ThrowsBadRequest()
    {
        var child = MakeChild(5, ownerUserId: 1);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(
            1, new CreateDataRequestDto { ChildProfileId = 5, RequestType = "wipe" }));
    }

    [Fact]
    public async Task CreateAsync_DuplicatePendingSameType_ThrowsConflict()
    {
        var child = MakeChild(5, ownerUserId: 1);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);
        _dataRequestRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<DataRequest, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(
            1, new CreateDataRequestDto { ChildProfileId = 5, RequestType = "export" }));

        _dataRequestRepository.Verify(repo => repo.AddAsync(It.IsAny<DataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ValidOwner_InsertsPendingRequest()
    {
        var child = MakeChild(5, ownerUserId: 1);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);
        _dataRequestRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<DataRequest, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DataRequest? added = null;
        _dataRequestRepository.Setup(repo => repo.AddAsync(It.IsAny<DataRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DataRequest, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((DataRequest entity, CancellationToken _) => entity);

        var result = await _sut.CreateAsync(1, new CreateDataRequestDto { ChildProfileId = 5, RequestType = "export" });

        Assert.NotNull(added);
        Assert.Equal(5, added!.ChildProfileId);
        Assert.Equal(1, added.RequestedByUserId);
        Assert.Equal(DataRequestType.Export, added.RequestType);
        Assert.Equal(DataRequestStatus.Pending, added.Status);
        Assert.Equal("export", result.RequestType);
        Assert.Equal("Pending", result.Status);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreateAsync_DeleteWithWrongPassword_ThrowsBadRequest()
    {
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(1, 2));
        _userRepository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, PasswordHash = "stored-hash" });
        _passwordHasher.Setup(p => p.VerifyPassword("wrong", "stored-hash"))
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(
            2, new CreateDataRequestDto
            {
                ChildProfileId = 1,
                RequestType = "delete",
                CurrentPassword = "wrong",
                ConfirmNickname = "Bé An"
            }));

        _dataRequestRepository.Verify(r => r.AddAsync(
            It.IsAny<DataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DeleteWithWrongNickname_ThrowsBadRequest()
    {
        var child = MakeChild(1, 2);
        child.Nickname = "Bé An";
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);
        _userRepository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, PasswordHash = "stored-hash" });
        _passwordHasher.Setup(p => p.VerifyPassword("Pass@123", "stored-hash"))
            .Returns(true);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(
            2, new CreateDataRequestDto
            {
                ChildProfileId = 1,
                RequestType = "delete",
                CurrentPassword = "Pass@123",
                ConfirmNickname = "Be An"
            }));
    }

    [Fact]
    public async Task CreateAsync_ExportDoesNotNeedPasswordOrNickname()
    {
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(1, 2));
        _dataRequestRepository.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<DataRequest, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _dataRequestRepository.Setup(r => r.AddAsync(
                It.IsAny<DataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataRequest value, CancellationToken _) => value);

        var result = await _sut.CreateAsync(2, new CreateDataRequestDto
        {
            ChildProfileId = 1,
            RequestType = "export"
        });

        Assert.Equal("export", result.RequestType);
        _passwordHasher.Verify(p => p.VerifyPassword(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ---------- ListPendingAsync ----------

    [Fact]
    public async Task ListPendingAsync_ReturnsOnlyPendingRequestsOrderedByCreatedAt()
    {
        var older = new DataRequest
        {
            Id = 1, ChildProfileId = 5, RequestType = DataRequestType.Export,
            Status = DataRequestStatus.Pending, CreatedAt = new DateTime(2026, 1, 1)
        };
        var newer = new DataRequest
        {
            Id = 2, ChildProfileId = 6, RequestType = DataRequestType.Delete,
            Status = DataRequestStatus.Pending, CreatedAt = new DateTime(2026, 2, 1)
        };
        var resolved = new DataRequest
        {
            Id = 3, ChildProfileId = 7, RequestType = DataRequestType.Export,
            Status = DataRequestStatus.Resolved, CreatedAt = new DateTime(2026, 1, 15)
        };
        _dataRequestRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<DataRequest, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<DataRequest, bool>> predicate, string? _, CancellationToken _) =>
                new[] { older, newer, resolved }.Where(predicate.Compile()).ToList());

        var result = await _sut.ListPendingAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.All(result, dto => Assert.Equal("Pending", dto.Status));
    }

    [Fact]
    public async Task ListMineAsync_ReturnsOnlyRequestsOfOwnerNewestFirst()
    {
        Expression<Func<DataRequest, bool>>? captured = null;
        _dataRequestRepository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<DataRequest, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<DataRequest, bool>>, string?, CancellationToken>(
                (predicate, _, _) => captured = predicate)
            .ReturnsAsync(new List<DataRequest>
            {
                new() { Id = 1, RequestedByUserId = 2, ChildProfileId = 1,
                    RequestType = DataRequestType.Export, CreatedAt = DateTime.UtcNow.AddHours(-2) },
                new() { Id = 2, RequestedByUserId = 2, ChildProfileId = 1,
                    RequestType = DataRequestType.Delete, CreatedAt = DateTime.UtcNow }
            });

        var result = await _sut.ListMineAsync(2, 1);

        Assert.Equal(new[] { 2, 1 }, result.Select(r => r.Id));
        var matches = captured!.Compile();
        Assert.True(matches(new DataRequest { RequestedByUserId = 2, ChildProfileId = 1 }));
        Assert.False(matches(new DataRequest { RequestedByUserId = 3, ChildProfileId = 1 }));
        Assert.False(matches(new DataRequest { RequestedByUserId = 2, ChildProfileId = 9 }));
    }

    [Fact]
    public async Task CancelAsync_PendingOwnRequest_SetsCancelledAndAudits()
    {
        var request = new DataRequest
            { Id = 5, RequestedByUserId = 2, ChildProfileId = 1, Status = DataRequestStatus.Pending };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(1, 2));

        var result = await _sut.CancelAsync(2, 5);

        Assert.Equal(DataRequestStatus.Cancelled, request.Status);
        Assert.Equal("Cancelled", result.Status);
        _auditLogWriter.Verify(w => w.LogAsync(
            2, "DataRequestCancelled", nameof(DataRequest), 5,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_NotRequester_ThrowsForbidden()
    {
        var request = new DataRequest
            { Id = 5, RequestedByUserId = 2, ChildProfileId = 1, Status = DataRequestStatus.Pending };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CancelAsync(3, 5));

        Assert.Equal(DataRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task CancelAsync_AlreadyResolved_ThrowsConflict()
    {
        var request = new DataRequest
            { Id = 5, RequestedByUserId = 2, ChildProfileId = 1, Status = DataRequestStatus.Resolved };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(1, 2));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CancelAsync(2, 5));
    }

    // ---------- ResolveExportAsync ----------

    private static DataRequest MakeRequest(
        int id, int childProfileId, DataRequestType type, DataRequestStatus status = DataRequestStatus.Pending) => new()
    {
        Id = id,
        ChildProfileId = childProfileId,
        RequestedByUserId = 1,
        RequestType = type,
        Status = status
    };

    [Fact]
    public async Task ResolveExportAsync_RequestNotFound_ThrowsNotFound()
    {
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ResolveExportAsync(10, 1));
    }

    [Fact]
    public async Task ResolveExportAsync_WrongRequestType_ThrowsBadRequest()
    {
        var request = MakeRequest(1, 5, DataRequestType.Delete);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResolveExportAsync(10, 1));
    }

    [Fact]
    public async Task ResolveExportAsync_AlreadyResolved_ThrowsConflict()
    {
        var request = MakeRequest(1, 5, DataRequestType.Export, DataRequestStatus.Resolved);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResolveExportAsync(10, 1));
    }

    [Fact]
    public async Task ResolveExportAsync_Valid_BuildsWorkbookResolvesAndAudits()
    {
        var request = MakeRequest(1, 5, DataRequestType.Export);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var child = MakeChild(5, ownerUserId: 1);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);

        IReadOnlyList<ExportSheet>? capturedSheets = null;
        _exportBuilder.Setup(builder => builder.BuildWorkbook(It.IsAny<IReadOnlyList<ExportSheet>>()))
            .Callback<IReadOnlyList<ExportSheet>>(sheets => capturedSheets = sheets)
            .Returns([1, 2, 3]);

        var result = await _sut.ResolveExportAsync(10, 1);

        Assert.Equal(new byte[] { 1, 2, 3 }, result.Content);
        Assert.Contains("5", result.FileName);
        Assert.NotNull(capturedSheets);
        var childSheet = Assert.Single(capturedSheets!, sheet => sheet.Name == "child_profiles");
        Assert.Single(childSheet.Rows);
        Assert.Contains("Bé An", childSheet.Rows[0]);

        Assert.Equal(DataRequestStatus.Resolved, request.Status);
        Assert.Equal(10, request.ResolvedByUserId);
        Assert.NotNull(request.ResolvedAt);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            10, "DataRequestExported", nameof(DataRequest), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _archiveExportBuilder.Verify(builder => builder.BuildCsvArchive(It.IsAny<IReadOnlyList<ExportSheet>>()), Times.Never);
    }

    [Fact]
    public async Task ResolveExportAsync_InvalidFormat_ThrowsBadRequest()
    {
        var request = MakeRequest(1, 5, DataRequestType.Export);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(5, ownerUserId: 1));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResolveExportAsync(10, 1, "pdf"));
    }

    [Fact]
    public async Task ResolveExportAsync_CsvFormat_UsesArchiveBuilderAndReturnsZipFile()
    {
        var request = MakeRequest(1, 5, DataRequestType.Export);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeChild(5, ownerUserId: 1));
        _archiveExportBuilder.Setup(builder => builder.BuildCsvArchive(It.IsAny<IReadOnlyList<ExportSheet>>()))
            .Returns([9, 9, 9]);

        var result = await _sut.ResolveExportAsync(10, 1, "csv");

        Assert.Equal(new byte[] { 9, 9, 9 }, result.Content);
        Assert.EndsWith(".zip", result.FileName);
        _exportBuilder.Verify(builder => builder.BuildWorkbook(It.IsAny<IReadOnlyList<ExportSheet>>()), Times.Never);
        Assert.Equal(DataRequestStatus.Resolved, request.Status);
    }

    // ---------- ResolveDeleteAsync ----------

    [Fact]
    public async Task ResolveDeleteAsync_RequestNotFound_ThrowsNotFound()
    {
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ResolveDeleteAsync(10, 1));
    }

    [Fact]
    public async Task ResolveDeleteAsync_WrongRequestType_ThrowsBadRequest()
    {
        var request = MakeRequest(1, 5, DataRequestType.Export);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResolveDeleteAsync(10, 1));
    }

    [Fact]
    public async Task ResolveDeleteAsync_AlreadyResolved_ThrowsConflict()
    {
        var request = MakeRequest(1, 5, DataRequestType.Delete, DataRequestStatus.Resolved);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResolveDeleteAsync(10, 1));
    }

    [Fact]
    public async Task ResolveDeleteAsync_HardDeleteMethod_ThrowsBadRequest()
    {
        var request = MakeRequest(1, 5, DataRequestType.Delete);
        request.DeletionMethod = DataRequestDeletionMethod.HardDelete;
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResolveDeleteAsync(10, 1));
    }

    [Fact]
    public async Task ResolveDeleteAsync_Valid_UsesEraserAndAudits()
    {
        var request = MakeRequest(1, 5, DataRequestType.Delete);
        var child = MakeChild(5, 1);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(child);

        var result = await _sut.ResolveDeleteAsync(10, 1);

        _eraser.Verify(e => e.EraseAsync(child, 10, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(DataRequestStatus.Resolved, request.Status);
        Assert.Equal(10, request.ResolvedByUserId);
        Assert.Equal("Resolved", result.Status);
        _auditLogWriter.Verify(writer => writer.LogAsync(10, "DataRequestDeleted", nameof(DataRequest), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveDeleteAsync_NotifiesRequester()
    {
        var request = new DataRequest { Id = 5, RequestedByUserId = 2, ChildProfileId = 1,
            RequestType = DataRequestType.Delete, Status = DataRequestStatus.Pending };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var child = MakeChild(1, 2);
        _childProfileRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(child);
        _userRepository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserAccount { Id = 2, Email = "owner@example.com", FullName = "An" });

        await _sut.ResolveDeleteAsync(9, 5);

        _eraser.Verify(e => e.EraseAsync(child, 9, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.CreateAsync(2, NotificationType.DataRequestCompleted,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(e => e.SendDataRequestCompletedEmailAsync("owner@example.com", "An", "delete", "resolved", null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_PendingRequest_SetsRejectedWithReasonAndNotifies()
    {
        var request = new DataRequest
        {
            Id = 6, RequestedByUserId = 2, ChildProfileId = 1,
            RequestType = DataRequestType.Export, Status = DataRequestStatus.Pending
        };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _userRepository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount
                { Id = 2, Email = "owner@example.com", FullName = "An" });

        var result = await _sut.RejectAsync(9, 6, "  Thiếu thông tin xác minh  ");

        Assert.Equal(DataRequestStatus.Rejected, request.Status);
        Assert.Equal("Thiếu thông tin xác minh", request.RejectionReason);
        Assert.Equal(9, request.ResolvedByUserId);
        Assert.Equal("Rejected", result.Status);
        Assert.Equal("Thiếu thông tin xác minh", result.RejectionReason);
        _notificationService.Verify(n => n.CreateAsync(
            2, NotificationType.DataRequestCompleted,
            It.Is<string>(payload => payload.Contains("\"result\":\"rejected\"")),
            It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(e => e.SendDataRequestCompletedEmailAsync(
            "owner@example.com", "An", "export", "rejected",
            "Thiếu thông tin xác minh", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_BlankReason_ThrowsBadRequest(string reason)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.RejectAsync(9, 6, reason));
    }

    [Fact]
    public async Task RejectAsync_AlreadyHandled_ThrowsConflict()
    {
        var request = new DataRequest
            { Id = 6, RequestedByUserId = 2, Status = DataRequestStatus.Cancelled };
        _dataRequestRepository.Setup(r => r.GetByIdAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.RejectAsync(9, 6, "lý do"));
    }
}
