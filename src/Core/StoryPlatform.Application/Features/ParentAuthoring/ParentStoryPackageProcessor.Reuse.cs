using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed partial class ParentStoryPackageProcessor
{
    private async Task ReuseUnchangedAsync(StoryGenerationJob job, string token, ParentStoryPackage p, ParentPackageWorkState state, CancellationToken ct)
    {
        if (p.PreviousPackageId is not int previousId || state.Completed.Contains("reuse")) return;
        var previous = await uow.Repository<ParentStoryPackage>().FirstOrDefaultAsync(x => x.Id == previousId && x.Status == "Confirmed" && !x.IsDeleted, cancellationToken: ct);
        if (previous is null || previous.SourceFingerprint != p.SourceFingerprint || previous.ContextFingerprint != p.ContextFingerprint || previous.ChildProfileId != p.ChildProfileId) return;
        await ParentStoryPackageReader.ValidateManifestAsync(uow, previous, ct);
        var wanted = ParentStoryPackageService.Components(p); var had = ParentStoryPackageService.Components(previous);
        await Checkpoint(job, token, async () =>
        {
            var wordIds = new Dictionary<int, int>(); var interactionIds = new Dictionary<int, int>(); var optionIds = new Dictionary<int, int>();
            if (wanted.Vocabulary && had.Vocabulary && !state.Regenerate.Contains("vocabulary"))
            {
                var words = await uow.Repository<StoryVocabulary>().FindAsync(x => x.ParentStoryPackageId == previous.Id && !x.IsDeleted, cancellationToken: ct);
                foreach (var old in words)
                {
                    var copy = new StoryVocabulary { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, Term = old.Term, Definition = old.Definition,
                        SourceQuote = old.SourceQuote, SourceStart = old.SourceStart, SourceEnd = old.SourceEnd, Example = old.Example };
                    await uow.Repository<StoryVocabulary>().AddAsync(copy, ct); await uow.SaveChangesAsync(ct); wordIds.Add(old.Id, copy.Id);
                }
                state.Completed = state.Completed.Append("vocabulary").ToArray();
            }
            if (wanted.Quiz && had.Quiz && !state.Regenerate.Contains("quiz"))
            {
                var items = await uow.Repository<QuizItem>().FindAsync(x => x.ParentStoryPackageId == previous.Id && !x.IsDeleted, cancellationToken: ct);
                await uow.Repository<QuizItem>().AddRangeAsync(items.Select(old => new QuizItem { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId,
                    Type = old.Type, Question = old.Question, CorrectAnswer = old.CorrectAnswer, Choices = old.Choices, LearningGoal = old.LearningGoal,
                    SourceQuote = old.SourceQuote, SourceStart = old.SourceStart, SourceEnd = old.SourceEnd, Explanation = old.Explanation, Hint = old.Hint,
                    OptionFeedbackJson = old.OptionFeedbackJson }), ct);
                state.Completed = state.Completed.Append("quiz").ToArray();
            }
            if (wanted.Listening && had.Listening && !state.Regenerate.Contains("listening"))
            {
                var interactions = await uow.Repository<ListeningInteraction>().FindAsync(x => x.ParentStoryPackageId == previous.Id && !x.IsDeleted, cancellationToken: ct);
                foreach (var old in interactions)
                {
                    var copy = new ListeningInteraction { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, Type = old.Type, AnchorOffset = old.AnchorOffset,
                        StorySceneId = old.StorySceneId, Kind = old.Kind, Prompt = old.Prompt, SourceQuote = old.SourceQuote, SourceStart = old.SourceStart, SourceEnd = old.SourceEnd,
                        CharacterName = old.CharacterName, HiddenWord = old.HiddenWord, Hint = old.Hint, RevealOffset = old.RevealOffset, Explanation = old.Explanation,
                        MatchFeedback = old.MatchFeedback, NoMatchFeedback = old.NoMatchFeedback, WaitFeedback = old.WaitFeedback };
                    await uow.Repository<ListeningInteraction>().AddAsync(copy, ct); await uow.SaveChangesAsync(ct); interactionIds.Add(old.Id, copy.Id);
                    var oldOptions = await uow.Repository<ListeningInteractionOption>().FindAsync(x => x.ListeningInteractionId == old.Id && !x.IsDeleted, cancellationToken: ct);
                    foreach (var option in oldOptions)
                    {
                        var cloned = JsonSerializer.Deserialize<ListeningInteractionOption>(JsonSerializer.Serialize(option, Json), Json)!;
                        cloned.Id = 0; cloned.ListeningInteractionId = copy.Id; cloned.ListeningInteraction = null;
                        await uow.Repository<ListeningInteractionOption>().AddAsync(cloned, ct); await uow.SaveChangesAsync(ct); optionIds.Add(option.Id, cloned.Id);
                    }
                    state.Completed = state.Completed.Append("listening:" + old.Type).Distinct().ToArray();
                }
            }
            // Copy metadata only. Shared immutable binaries keep their original storage paths.
            var manifest = JsonSerializer.Deserialize<ParentPackageManifest>(previous.ManifestJson, Json)!;
            foreach (var id in manifest.MediaAssetIds)
            {
                var old = await uow.Repository<MediaAsset>().GetByIdAsync(id, ct);
                if (old is null || old.Status != MediaStatus.Ready || old.ValidationStatus != ValidationStatus.Passed) continue;
                var word = old.StoryVocabularyId is int w && wordIds.TryGetValue(w, out var nw) ? nw : (int?)null;
                var interaction = old.ListeningInteractionId is int i && interactionIds.TryGetValue(i, out var ni) ? ni : (int?)null;
                if (word is null && interaction is null) continue;
                var copy = new MediaAsset { ParentStoryPackageId = p.Id, StoryVersionId = p.StoryVersionId, StorySceneId = old.StorySceneId,
                    StoryVocabularyId = word, ListeningInteractionId = interaction,
                    ListeningInteractionOptionId = old.ListeningInteractionOptionId is int o ? optionIds[o] : null,
                    AudioPartKey = old.AudioPartKey, Type = old.Type, Status = old.Status, ValidationStatus = old.ValidationStatus,
                    Url = old.Url, MimeType = old.MimeType, Provider = old.Provider, Model = old.Model, ValidationResultJson = old.ValidationResultJson,
                    WordTimings = old.WordTimings, CompletedAt = old.CompletedAt, RequestedPackageRevision = p.Revision };
                await uow.Repository<MediaAsset>().AddAsync(copy, ct);
            }
            state.Completed = state.Completed.Append("reuse").ToArray();
        }, state, ct);
    }
}
