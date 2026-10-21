namespace StoryPlatform.Contracts.AI.Moderation;

public sealed record GrayZonePart(string Kind, string Name, string Text);
public sealed record GrayZoneCriterionScore(string Code, int Level);
public sealed record GrayZoneFinding(string CriterionCode, string Kind, string Name,
    string EvidenceQuote, string Reason, string ContextNote);
public sealed record EvaluateGrayZoneRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public int? ReadingLevel { get; init; }
    public string Language { get; init; } = "vi";
    public string RubricVersion { get; init; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required]
    public RubricSnapshot? Rubric { get; init; }
    public IReadOnlyList<string> BlockedTerms { get; init; } = [];
    public IReadOnlyList<GrayZonePart> Parts { get; init; } = [];
}

public sealed record EvaluateGrayZoneResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public string RubricVersion { get; init; } = GrayZoneRules.RubricVersion;
    public string Provider { get; init; } = "VertexAI";
    public int? RubricId { get; init; }
    public string? RubricHash { get; init; }
    public string Model { get; init; } = string.Empty;
    public decimal RiskScore { get; init; }
    public IReadOnlyList<GrayZoneCriterionScore> Scores { get; init; } = [];
    public IReadOnlyList<GrayZoneFinding> Findings { get; init; } = [];
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public long LatencyMs { get; init; }
}

public static class GrayZoneRules
{
    public const string RubricVersion = "gray-zone-moderation-v1";
    public static IReadOnlyList<string> CriterionCodes { get; } = Array.AsReadOnly(new[] { "GZ01", "GZ02", "GZ03", "GZ04", "GZ05" });

    public static void ValidateRequest(EvaluateGrayZoneRequest request)
    {
        if (request.Rubric is null) throw new ArgumentException("GRAY_ZONE_RUBRIC_REQUIRED");
        RubricRules.Validate(request.Rubric);
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 100
            || request.SourceFingerprint is null || request.SourceFingerprint.Length != 64 || !request.SourceFingerprint.All(Uri.IsHexDigit)
            || request.Phase is not ("phase1" or "phase2" or "phase3" or "phase4")
            || request.AgeBand is not ("6-8" or "9-10") || request.Language is not ("vi" or "en")
            || request.ReadingLevel is <= 0 || request.RubricVersion != request.Rubric.Version
            || request.Parts is null || request.Parts.Count is 0 or > 2000
            || request.Parts.Any(x => x is null || string.IsNullOrWhiteSpace(x.Kind) || x.Kind.Length > 40
                || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Text is null)
            || request.Parts.Sum(x => (long)x.Text.Length) is 0 or > 200_000
            || request.Parts.Select(x => (x.Kind, x.Name)).Distinct().Count() != request.Parts.Count
            || request.BlockedTerms is null || request.BlockedTerms.Count > 1000
            || request.BlockedTerms.Any(x => x is null || x.Length > 500))
            throw new ArgumentException("GRAY_ZONE_INPUT_INVALID");
    }

    public static decimal ValidateAndScore(EvaluateGrayZoneRequest request,
        IReadOnlyList<GrayZoneCriterionScore> scores, IReadOnlyList<GrayZoneFinding> findings)
    {
        if (scores is null || scores.Count != CriterionCodes.Count
            || scores.Any(x => x is null || !CriterionCodes.Contains(x.Code) || x.Level is < 0 or > 4)
            || scores.Select(x => x.Code).Distinct().Count() != CriterionCodes.Count
            || findings is null || findings.Count > 20)
            throw new InvalidOperationException("GRAY_ZONE_OUTPUT_INVALID");
        foreach (var finding in findings)
        {
            if (finding is null || !scores.Any(x => x.Code == finding.CriterionCode && x.Level > 0)
                || string.IsNullOrWhiteSpace(finding.EvidenceQuote) || finding.EvidenceQuote.Length > 2000
                || string.IsNullOrWhiteSpace(finding.Reason) || finding.Reason.Length > 1500
                || string.IsNullOrWhiteSpace(finding.ContextNote) || finding.ContextNote.Length > 1000
                || !request.Parts.Any(x => x.Kind == finding.Kind && x.Name == finding.Name
                    && x.Text.Contains(finding.EvidenceQuote, StringComparison.Ordinal)))
                throw new InvalidOperationException("GRAY_ZONE_EVIDENCE_INVALID");
        }
        if (scores.Any(x => x.Level > 0 && !findings.Any(f => f.CriterionCode == x.Code)))
            throw new InvalidOperationException("GRAY_ZONE_EVIDENCE_REQUIRED");
        return scores.Max(x => x.Level) * 25m;
    }

    public static void ValidateResponse(EvaluateGrayZoneRequest request, EvaluateGrayZoneResponse response)
    {
        if (response.RequestId != request.RequestId || response.SourceFingerprint != request.SourceFingerprint
            || response.RubricId != request.Rubric?.Id || response.RubricHash != request.Rubric?.ContentHash
            || response.RubricVersion != request.RubricVersion || response.Provider != "VertexAI"
            || string.IsNullOrWhiteSpace(response.Model) || response.Model.Length > 200
            || response.RiskScore != ValidateAndScore(request, response.Scores, response.Findings))
            throw new InvalidOperationException("GRAY_ZONE_RESPONSE_INVALID");
    }
}
