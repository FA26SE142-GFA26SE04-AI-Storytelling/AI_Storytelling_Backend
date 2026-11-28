using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public sealed class ChildFreeAnswerService(IUnitOfWork unitOfWork, IMediaStorage storage,
    ILogger<ChildFreeAnswerService> logger) : IChildFreeAnswerService
{
    public async Task<bool> SaveAsync(int childProfileId, int sessionId, int interactionId,
        ReadAloudRequest request, CancellationToken ct = default)
    {
        var session = await unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session is null || session.IsDeleted || session.ChildProfileId != childProfileId)
            throw new NotFoundException("Phiên đọc", sessionId);
        if (session.ForceExitRequestedAt is not null || session.Status == SessionStatus.ForceExited)
            throw new ForbiddenException(ReadingSessionInterruptPolicy.ChildMessage);
        if (!ChildReadingPolicy.IsActive(session.Status))
            throw new ConflictException(ReadAloudPolicy.SessionEndedMessage);
        var profile = await unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        var interaction = await unitOfWork.Repository<ListeningInteraction>().GetByIdAsync(interactionId, ct);
        if (profile is null || interaction is null || interaction.IsDeleted || interaction.StorySceneId is null ||
            interaction.StoryVersionId != session.StoryVersionId || interaction.ParentStoryPackageId != session.ParentStoryPackageId ||
            interaction.TargetAge != ListeningAgeSets.AgeFor(profile.Age, profile.AgeBand) ||
            interaction.Type is not (ListeningInteractionType.CharacterTrial or ListeningInteractionType.WhatIf))
            throw new NotFoundException("Tương tác", interactionId);
        var package = await ParentStoryPackageReader.ForSessionAsync(unitOfWork, session, ct);
        if (package is not null && !System.Text.Json.JsonSerializer.Deserialize<ParentPackageManifest>(
                package.ManifestJson, ParentPackageContext.Json)!.ListeningIds.Contains(interactionId))
            throw new NotFoundException("Tương tác", interactionId);
        var policy = await unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId && !value.IsDeleted, cancellationToken: ct);
        if (policy is not { VoiceAnswersEnabled: true }) throw new ForbiddenException(ReadAloudPolicy.VoiceOffMessage);
        if (request.Audio.Length == 0) throw new BadRequestException(ReadAloudPolicy.EmptyAudioMessage);
        if (request.Audio.Length > ReadAloudPolicy.MaxAudioBytes) throw new BadRequestException(ReadAloudPolicy.AudioTooLargeMessage);
        if (!ReadAloudPolicy.IsSupportedContentType(request.ContentType))
            throw new BadRequestException(ReadAloudPolicy.UnsupportedAudioMessage);

        var contentType = request.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
        var path = $"{ReadAloudPolicy.StorageFolder}/{childProfileId}/{sessionId}/free-answer-{interactionId}/"
            + $"{Guid.NewGuid():N}{StorySummaryPolicy.Extension(contentType)}";
        try
        {
            using var stream = new MemoryStream(request.Audio);
            await storage.UploadAsync(path, stream, contentType, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Free answer upload failed. SessionId={SessionId}, ErrorType={ErrorType}",
                sessionId, exception.GetType().Name);
            return false;
        }

        string? oldPath = null;
        try
        {
            await unitOfWork.BeginTransactionAsync(ct);
            await unitOfWork.AcquireTransactionLockAsync(session.Id, ct);
            var repo = unitOfWork.Repository<ReadAloudRecording>();
            var existing = await repo.FirstOrDefaultAsync(row => row.ReadingSessionId == sessionId &&
                row.ListeningInteractionId == interactionId && row.StorySegmentId == null &&
                row.ListeningInteractionOptionId == null && !row.IsDeleted, cancellationToken: ct);
            oldPath = existing?.AudioStoragePath;
            var recording = existing ?? new ReadAloudRecording
            {
                ReadingSessionId = sessionId, ChildProfileId = childProfileId, ListeningInteractionId = interactionId
            };
            recording.AudioStoragePath = path;
            recording.AudioContentType = contentType;
            recording.AudioBytes = request.Audio.Length;
            recording.TotalCount = 0;
            recording.GoodCount = recording.RetryCount = recording.SkippedCount = 0;
            recording.WordStatusesJson = null;
            recording.RecordedAt = DateTime.UtcNow;
            if (existing is null) await repo.AddAsync(recording, ct);
            else repo.Update(recording);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            await TryDeleteAsync(path);
            throw;
        }
        if (!string.IsNullOrWhiteSpace(oldPath) && oldPath != path) await TryDeleteAsync(oldPath);
        return true;
    }

    private async Task TryDeleteAsync(string path)
    {
        try { await storage.DeleteAsync(path, CancellationToken.None); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Free answer cleanup failed. ErrorType={ErrorType}", exception.GetType().Name);
        }
    }
}
