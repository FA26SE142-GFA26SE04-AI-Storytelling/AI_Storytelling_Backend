using System.Text.Json;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Versioned ordinal assessment, not a probability of safety.</summary>
public static class ContentSafetyScoring
{
    public const string Version = "content-safety-v2";
    public const string Instruction = "Assess violence, fear, sensitive/sexual content, harmful language, personal data and supplied policy restrictions. " +
        "Return riskLevel none/low/moderate/high/critical and safetyScore exactly 100/90/70/40/0 respectively. " +
        "Use the highest observed risk. Evidence must be verbatim nonempty quotes from the title or full story for every non-none assessment. " +
        "For none return evidence=[] and no violations. Never allow hard violations or blocked topics; high/critical must have isAllowed=false and canRefine=false. " +
        "The score is an ordinal rubric assessment, never a calibrated probability. The trusted score rule version is content-safety-v2.";

    public static void Validate(EvaluateContentSafetyResponse response, string source)
    {
        var expected = response.RiskLevel switch { "none" => 100d, "low" => 90d, "moderate" => 70d, "high" => 40d, "critical" => 0d, _ => -1d };
        if (response.Evidence is null || response.Violations is null || expected < 0 || response.SafetyScore is not double score || !double.IsFinite(score) || score != expected ||
            response.ScoreRuleVersion != Version || response.RiskLevel is "high" or "critical" && (response.IsAllowed || response.CanRefine) ||
            response.RiskLevel != "none" && response.Evidence.Count == 0 || response.RiskLevel == "none" && response.Violations.Count > 0 ||
            response.Evidence.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1000 || !source.Contains(x, StringComparison.Ordinal)))
            throw new JsonException("CONTENT_SAFETY_SCORE_INVALID");
    }
}
