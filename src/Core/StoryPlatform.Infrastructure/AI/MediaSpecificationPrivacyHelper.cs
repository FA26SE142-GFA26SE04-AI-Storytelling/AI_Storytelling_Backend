using System.Collections.Generic;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Scans all outbound text fields of a <see cref="SceneSpecification"/> plus the final prompt
/// before dispatching to a media evaluation provider, without altering the outbound prompt payload.
/// </summary>
internal static class MediaSpecificationPrivacyHelper
{
    public static void EnsureSafe(
        IMediaOutboundPrivacyGuard privacyGuard,
        string operationName,
        SceneSpecification specification,
        string finalPrompt)
    {
        privacyGuard.EnsureSafeForProvider(operationName, CollectSegments(specification, finalPrompt));
    }

    private static IEnumerable<string?> CollectSegments(SceneSpecification spec, string finalPrompt)
    {
        yield return finalPrompt;
        yield return spec.SceneText;
        yield return spec.VisualDescription;
        yield return spec.BeatText;
        yield return spec.BeatVisualFocus;
        yield return spec.PromptFeedback;

        if (spec.MustShow is not null)
        {
            foreach (var item in spec.MustShow)
            {
                yield return item;
            }
        }

        if (spec.MustNotContradict is not null)
        {
            foreach (var item in spec.MustNotContradict)
            {
                yield return item;
            }
        }

        if (spec.MediaContext is null)
        {
            yield break;
        }

        var facts = spec.MediaContext.StoryFacts;
        if (facts is not null)
        {
            yield return facts.Title;
            yield return facts.Lesson;
            yield return facts.CanonicalContent;

            if (facts.ApprovedOutline is not null)
            {
                yield return facts.ApprovedOutline.OutlineOpening;
                yield return facts.ApprovedOutline.OutlineDevelopment;
                yield return facts.ApprovedOutline.OutlineEnding;
            }
        }

        var design = spec.MediaContext.VisualDesign;
        if (design is not null)
        {
            yield return design.Style;
            yield return design.ContinuityRule;
            if (design.ProhibitedChanges is not null)
            {
                foreach (var item in design.ProhibitedChanges)
                {
                    yield return item;
                }
            }
        }
    }
}
