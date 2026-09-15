using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.DataRequests.DTOs;
using StoryPlatform.Application.Features.DataRequests.Services;
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
    private readonly Mock<IGenericRepository<O2OAssessment>> _o2oAssessmentRepository = new();
    private readonly Mock<IGenericRepository<SupervisionRelationship>> _supervisionRelationshipRepository = new();
    private readonly Mock<IGenericRepository<Assignment>> _assignmentRepository = new();
    private readonly Mock<IGenericRepository<SharedStoryRecipient>> _sharedStoryRecipientRepository = new();
    private readonly Mock<IGenericRepository<SharedStory>> _sharedStoryRepository = new();
    private readonly Mock<IGenericRepository<Story>> _storyRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly Mock<IWorkbookExportBuilder> _exportBuilder = new();
    private readonly Mock<IArchiveExportBuilder> _archiveExportBuilder = new();
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
        _unitOfWork.Setup(work => work.Repository<O2OAssessment>()).Returns(_o2oAssessmentRepository.Object);
        _unitOfWork.Setup(work => work.Repository<SupervisionRelationship>()).Returns(_supervisionRelationshipRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Assignment>()).Returns(_assignmentRepository.Object);
        _unitOfWork.Setup(work => work.Repository<SharedStoryRecipient>()).Returns(_sharedStoryRecipientRepository.Object);
        _unitOfWork.Setup(work => work.Repository<SharedStory>()).Returns(_sharedStoryRepository.Object);
        _unitOfWork.Setup(work => work.Repository<Story>()).Returns(_storyRepository.Object);

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
        _o2oAssessmentRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<O2OAssessment, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<O2OAssessment>());
        _supervisionRelationshipRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SupervisionRelationship>());
        _assignmentRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Assignment>());
        _storyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Story, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Story>());
        _sharedStoryRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SharedStory, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SharedStory>());
        _sharedStoryRecipientRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SharedStoryRecipient, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SharedStoryRecipient>());

        _sut = new DataRequestService(
            _unitOfWork.Object, _auditLogWriter.Object, _exportBuilder.Object, _archiveExportBuilder.Object);
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
    public async Task ResolveDeleteAsync_Valid_CascadesAnonymizesAndAudits()
    {
        var request = MakeRequest(1, 5, DataRequestType.Delete);
        _dataRequestRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var child = MakeChild(5, ownerUserId: 1);
        child.DateOfBirth = new DateOnly(2018, 5, 1);
        _childProfileRepository.Setup(repo => repo.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(child);

        var relationship = new SupervisionRelationship { Id = 1, ChildProfileId = 5, SupervisorUserId = 1 };
        _supervisionRelationshipRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SupervisionRelationship, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { relationship });

        var recipient = new AssignmentRecipient { Id = 2, ChildProfileId = 5, Status = AssignmentStatus.Assigned };
        _assignmentRecipientRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<AssignmentRecipient, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { recipient });

        var directAssignment = new Assignment { Id = 3, ChildProfileId = 5, Status = AssignmentStatus.Assigned };
        _assignmentRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { directAssignment });

        var story = new Story { Id = 4, ChildProfileId = 5 };
        _storyRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Story, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { story });

        var sharedStory = new SharedStory { Id = 6, StoryId = 4, ClassGroupId = 1 };
        _sharedStoryRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SharedStory, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sharedStory });

        var sharedRecipient = new SharedStoryRecipient { Id = 7, SharedStoryId = 6, Status = RecipientStatus.Accepted };
        _sharedStoryRecipientRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SharedStoryRecipient, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sharedRecipient });

        var result = await _sut.ResolveDeleteAsync(10, 1);

        Assert.NotNull(relationship.RevokedAt);
        Assert.Equal(10, relationship.RevokedByUserId);

        Assert.Equal(AssignmentStatus.Cancelled, recipient.Status);
        Assert.Equal(10, recipient.CancelledByUserId);
        Assert.NotNull(recipient.CancelledAt);

        Assert.Equal(AssignmentStatus.Cancelled, directAssignment.Status);

        Assert.Equal(RecipientStatus.Revoked, sharedRecipient.Status);

        Assert.Equal("[Đã ẩn danh]", child.Nickname);
        Assert.Null(child.DateOfBirth);
        Assert.Equal(ChildProfileStatus.Archived, child.Status);
        Assert.Equal(1, child.OwnerUserId);

        Assert.Equal(DataRequestStatus.Resolved, request.Status);
        Assert.Equal(10, request.ResolvedByUserId);
        Assert.Equal("Resolved", result.Status);

        _auditLogWriter.Verify(writer => writer.LogAsync(
            10, "DataRequestDeleted", nameof(DataRequest), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
