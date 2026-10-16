namespace StoryPlatform.Contracts.AI.CreatorAssistance;

/// <summary>Boundary validation shared by both services; never trusts model supplied patch positions.</summary>
public static class AssistanceValidation
{
    public static bool Boundary(string text, int index) => index >= 0 && index <= text.Length &&
        !(index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]));

    public static void ValidateTarget(string entry, EditorSnapshot snapshot, AssistanceTarget target)
    {
        if (snapshot is null || snapshot.Title is null || snapshot.Content is null || target is null ||
            snapshot.Title.Length > 200 || target.Start < 0 || target.Length < 0 ||
            target.Start > snapshot.Content.Length - target.Length || !Boundary(snapshot.Content, target.Start) ||
            !Boundary(snapshot.Content, target.Start + target.Length)) throw new ArgumentException("INVALID_TARGET");
        var valid = entry switch
        {
            "ContinueWriting" => target.Kind == "cursor" && target.Length == 0,
            "SupportSelection" => target.Kind == "selection" && target.Length > 0 && target.Length < snapshot.Content.Length,
            "ReviewDraft" => target.Kind == "wholeDraft" && target.Start == 0 && target.Length == snapshot.Content.Length && target.Length > 0,
            _ => false
        };
        if (!valid) throw new ArgumentException("INVALID_TARGET");
    }

    public static void ValidateConstraints(AssistanceConstraints constraints)
    {
        if (constraints?.Keep is null || constraints.Avoid is null || constraints.Keep.Length > 10 || constraints.Avoid.Length > 10 ||
            constraints.Keep.Concat(constraints.Avoid).Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 500))
            throw new ArgumentException("INVALID_CONSTRAINTS");
    }

    public static AssistanceAiResult ValidateResult(AssistanceAiRequest request, AssistanceAiResult result)
    {
        static bool Text(string? s, int max = 2000) => !string.IsNullOrWhiteSpace(s) && s.Length <= max;
        if (result is null || result.Options is null || result.Findings is null || result.ChecksPerformed is null ||
            result.Warnings is null || result.Assumptions is null || result.Warnings.Length > 10 || result.Assumptions.Length > 10 ||
            result.Warnings.Concat(result.Assumptions).Any(s => !Text(s)) ||
            result.Provider?.Length > 100 || result.Model?.Length > 200 || result.PromptVersion?.Length > 100)
            throw new ArgumentException("AI_OUTPUT_INVALID");
        if (request.Mode == "Trial")
        {
            ValidateTarget(request.EntryPoint, request.Snapshot, request.Target);
            if (request.EntryPoint == "ReviewDraft" || result.ResultType != "Trial" || result.Question is not null ||
                result.Options.Length != 0 || result.Findings.Length != 0 || result.Patch is null ||
                !Text(result.Patch.Text, request.MaximumTrialCharacters)) throw new ArgumentException("AI_OUTPUT_INVALID");
            var t = request.Target;
            return result with { Patch = new(t.Kind == "cursor" ? "insert" : "replace", t.Start, t.Length,
                result.Patch.Text, request.Snapshot.Content.Substring(t.Start, t.Length)) };
        }
        if (request.EntryPoint == "ReviewDraft")
        {
            if (result.ResultType != "ReviewFindings" || result.Question is not null || result.Patch is not null ||
                result.Options.Length != 0 || result.Findings.Length > 20 || result.ChecksPerformed.Length is < 1 or > 10 ||
                result.ChecksPerformed.Any(s => !Text(s, 200))) throw new ArgumentException("AI_OUTPUT_INVALID");
            var valid = result.Findings.Where(f => f is not null && Text(f.Id, 50) && Text(f.Quote, 4000) && Text(f.Comment) &&
                Text(f.SuggestedGoal) && f.Length > 0 && f.Start >= 0 && f.Start <= request.Snapshot.Content.Length - f.Length &&
                Boundary(request.Snapshot.Content, f.Start) && Boundary(request.Snapshot.Content, f.Start + f.Length) &&
                request.Snapshot.Content.Substring(f.Start, f.Length) == f.Quote).ToArray();
            if ((result.Findings.Length > 0 && valid.Length == 0) || valid.Select(f => f.Id).Distinct().Count() != valid.Length)
                throw new ArgumentException("AI_OUTPUT_INVALID");
            return result with { Findings = valid, Coverage = "FullSnapshot", Warnings = valid.Length == result.Findings.Length ? result.Warnings :
                result.Warnings.Take(9).Append("Some annotations had invalid anchors and were omitted.").ToArray() };
        }
        if (result.ResultType == "Clarification" && request.Answer is null)
        {
            var q = result.Question;
            if (q is null || !Text(q.Id, 50) || !Text(q.Text) || q.Choices is null || q.Choices.Length is < 2 or > 4 ||
                q.Choices.Any(c => c is null || !Text(c.Id, 50) || !Text(c.Text)) ||
                q.Choices.Select(c => c.Id).Distinct().Count() != q.Choices.Length || result.Patch is not null ||
                result.Options.Length != 0 || result.Findings.Length != 0) throw new ArgumentException("AI_OUTPUT_INVALID");
            return result;
        }
        if (result.ResultType != "Options" || result.Options.Length is < 2 or > 3 || result.Question is not null ||
            result.Patch is not null || result.Findings.Length != 0 ||
            result.Options.Any(o => o is null || !Text(o.Id, 50) || !Text(o.Summary, 500) || !Text(o.Direction)) ||
            result.Options.Select(o => o.Id).Distinct().Count() != result.Options.Length) throw new ArgumentException("AI_OUTPUT_INVALID");
        return result;
    }
}
