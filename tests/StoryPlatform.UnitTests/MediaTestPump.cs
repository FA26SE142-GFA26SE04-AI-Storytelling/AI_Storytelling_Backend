using System.Runtime.CompilerServices;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests;

/// <summary>Drives the three independent worker loops with virtual retry time, without provider calls or sleeps.</summary>
internal static class MediaTestPump
{
    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void AdvanceTo(DateTime value) => _now = new DateTimeOffset(value, TimeSpan.Zero).AddMilliseconds(1);
    }
    private sealed record State(IUnitOfWork Uow, Clock Clock);
    private static readonly ConditionalWeakTable<MediaGenerationService, State> States = new();
    public static void Register(MediaGenerationService service, IUnitOfWork uow, Clock clock) => States.Add(service, new(uow, clock));

    public static async Task<MediaJobProcessResult> RunPackageAsync(this MediaGenerationService service)
    {
        var state = States.GetValue(service, _ => throw new InvalidOperationException("Register the test service first"));
        var initial = await service.ProcessNextAsync();
        if (!initial.Success) return initial;
        MediaJobProcessResult? failure = null;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            foreach (var type in new[] { MediaType.Illustration, MediaType.TtsAudio })
            {
                var result = await service.ProcessBranchNextAsync(type);
                if (result.JobFound && !result.Success) failure = result;
            }
            await service.ProcessNextAsync();
            var parent = (await state.Uow.Repository<StoryGenerationJob>().FindAsync(x => x.Operation == GenerationJobOperation.GenerateMediaPackage))
                .OrderByDescending(x => x.Id).First();
            if (parent.Status == GenerationJobStatus.Completed) return MediaJobProcessResult.Completed;
            if (parent.Status == GenerationJobStatus.Failed)
                return failure ?? MediaJobProcessResult.Failed(true, parent.ErrorCode ?? "MEDIA_GENERATION_FAILED");
            var pending = await state.Uow.Repository<StoryGenerationJob>().FindAsync(x => x.ParentJobId == parent.Id && x.NextAttemptAt != null);
            if (pending.Count > 0) state.Clock.AdvanceTo(pending.Max(x => x.NextAttemptAt!.Value));
            // An open circuit has no bounded attempt consumption; tests inspect one iteration.
            if (failure?.ErrorCode == "MEDIA_CIRCUIT_OPEN") return failure;
        }
        throw new InvalidOperationException("Media worker test did not converge");
    }
}
