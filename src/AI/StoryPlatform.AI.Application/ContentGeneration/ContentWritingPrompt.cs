using System.Globalization;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Application.ContentGeneration;

internal static class ContentWritingPrompt
{
    public static string Append(string prompt, GenerationConstraintsDto constraints, int target,
        ContentRefinementFeedback? feedback = null)
    {
        if (constraints.MinimumWords > 0)
        {
            prompt += FormattableString.Invariant($"\nCount ONLY the storySections[].content body: {constraints.MinimumWords}-{constraints.MaximumWords} Unicode lexical units, target {target}. Whitespace-separated Vietnamese syllables count individually; do not use linguistic word segmentation. Exclude title, headings, description and lesson from this count. Aim near the target, not the upper bound. Do not pad by repeating sentences.");
            var opening = (int)Math.Round(target * .2);
            var ending = opening;
            prompt += FormattableString.Invariant($"\nPlan the body before writing: opening about {opening}, development about {target - opening - ending}, ending about {ending} lexical units. Distribute these budgets across the required sections; do not add a planning section to the output.");
        }
        if (constraints.Readability is { } limits)
        {
            prompt += FormattableString.Invariant($"\nReading limits: grade <= {limits.MaximumGradeLevel}, ease >= {limits.MinimumEaseScore}, average lexical units per sentence <= {limits.MaximumAverageWordsPerSentence}. Use many short, complete sentences and concrete, familiar words. Aim for average sentence length comfortably below the limit. Reach the total length through events, actions and dialogue, not longer sentences.");
            var sentenceTarget = Math.Max(5, (int)Math.Floor(limits.MaximumAverageWordsPerSentence * .7m));
            prompt += FormattableString.Invariant($"\nAim for about {sentenceTarget} lexical units per sentence, with roughly {(int)Math.Ceiling((double)target / sentenceTarget)} complete sentences across the body. This is a planning aid, not an extra output field. Use sentence-ending punctuation; commas alone do not split long sentences.");
        }
        if (feedback is not null)
        {
            var delta = feedback.CurrentWordCount - feedback.TargetWords;
            prompt += FormattableString.Invariant($"\nCore measured {feedback.CurrentWordCount} body lexical units. Target {feedback.TargetWords}, valid range {feedback.MinimumWords}-{feedback.MaximumWords}. ");
            if (delta > 0 && feedback.FailedGateCodes.Contains("CONTENT_LENGTH_INVALID"))
                prompt += $"Remove approximately {delta.ToString(CultureInfo.InvariantCulture)} units by shortening repetition and nonessential description; preserve the main events and ending. ";
            else if (delta < 0 && feedback.FailedGateCodes.Contains("CONTENT_LENGTH_INVALID"))
                prompt += $"Add approximately {(-delta).ToString(CultureInfo.InvariantCulture)} units through relevant actions and dialogue. ";
            prompt += "Resolve the listed failed gates while preserving all other constraints. Keep unaffected details, section order, characters, lesson and approved outline. Return the complete corrected story, not commentary or a patch.";
        }
        return prompt;
    }
}
