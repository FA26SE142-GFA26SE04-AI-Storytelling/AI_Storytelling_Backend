using StoryPlatform.Contracts.AI.Moderation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.Moderation.Rubrics;
using System.Text.Json.Serialization;

namespace StoryPlatform.Application.Features.Moderation;

public sealed class GrayZoneOptions
{
    public const string SectionName = "GrayZoneModeration";
    public string? Provider { get; set; }
    public decimal? WarningThreshold { get; set; }
    public decimal? BlockThreshold { get; set; }
    public decimal? MaximumRiskScore { get; set; }
    public string PolicyVersion { get; set; } = "1";
}

public sealed record ModerationText(string Kind, string Name, string Text, int? StartOffset = null, int? EndOffset = null);
public sealed record GrayZoneScan(string Phase, string AgeBand, string Language,
    IReadOnlyList<ModerationText> Parts, IReadOnlyList<string> BlockedTerms)
{
    public int? ReadingLevel { get; init; }
    public string RubricVersion { get; init; } = GrayZoneRules.RubricVersion;
    [JsonIgnore] public RubricSnapshot? Rubric { get; init; }
}
public sealed record ModerationFinding(string Kind, string Name, string Category, string Reason, int? StartOffset = null, int? EndOffset = null)
{
    public string? EvidenceQuote { get; init; }
    public string? ContextNote { get; init; }
}
public sealed record ModelModerationResult(decimal RiskScore, string Model,
    IReadOnlyList<ModerationFinding> Findings)
{
    public string? RubricVersion { get; init; }
    public IReadOnlyList<GrayZoneCriterionScore> Scores { get; init; } = [];
}
public sealed record GrayZoneAssessment(ModerationZone Zone, string Fingerprint, string Phase,
    string PolicyVersion, string Provider, string Model, decimal? RiskScore,
    IReadOnlyList<ModerationFinding> Findings)
{
    public string? RubricVersion { get; init; }
    public int? RubricId { get; init; }
    public string? RubricHash { get; init; }
    public IReadOnlyList<GrayZoneCriterionScore> Scores { get; init; } = [];
    public string? AgeBand { get; init; }
    public string? Language { get; init; }
    public int? AudienceRevision { get; init; }
}

/// <summary>Independent from generation Safety-AI. Implement in infrastructure once a provider is selected.</summary>
public interface IModelModerationProvider
{
    string Name { get; }
    Task<ModelModerationResult> EvaluateAsync(GrayZoneScan scan, CancellationToken ct);
}

public interface IGrayZoneEvaluator
{
    bool IsConfigured { get; }
    bool CanReuse(GrayZoneAssessment assessment) => false;
    Task<bool> CanReuseAsync(GrayZoneAssessment assessment, CancellationToken ct) => Task.FromResult(CanReuse(assessment));
    Task<GrayZoneAssessment> EvaluateAsync(GrayZoneScan scan, CancellationToken ct);
}

public sealed partial class GrayZoneEvaluator : IGrayZoneEvaluator
{
    private readonly GrayZoneOptions _options;
    private readonly IEnumerable<IModelModerationProvider> _providers;
    private readonly IModerationRubricSource _rubrics;
    public GrayZoneEvaluator(GrayZoneOptions options, IEnumerable<IModelModerationProvider> providers, IModerationRubricSource rubrics)
        => (_options, _providers, _rubrics) = (options, providers, rubrics ?? throw new ArgumentNullException(nameof(rubrics)));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Provider);
    private string PolicyKey => _options.PolicyVersion + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(new { _options.Provider, _options.WarningThreshold, _options.BlockThreshold, _options.MaximumRiskScore, GrayZoneRules.RubricVersion }))))[..16];
    private string Key(RubricSnapshot rubric) => PolicyKey + ":" + rubric.Id + ":" + rubric.ContentHash;
    public async Task<bool> CanReuseAsync(GrayZoneAssessment assessment, CancellationToken ct)
    {
        var rubric = await _rubrics.GetActiveAsync(ct);
        return assessment.PolicyVersion == Key(rubric);
    }

    public async Task<GrayZoneAssessment> EvaluateAsync(GrayZoneScan scan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (scan.Parts.Count == 0 || scan.Parts.Any(x => string.IsNullOrWhiteSpace(x.Kind) || string.IsNullOrWhiteSpace(x.Name))
            || scan.Parts.Select(x => (x.Kind, x.Name)).Distinct().Count() != scan.Parts.Count)
            throw new InvalidOperationException("MODERATION_INPUT_INVALID");
        var fingerprint = Fingerprint(scan);
        // Hard rules always win, even if the provider is unavailable.
        foreach (var part in scan.Parts)
        {
            var pii = Email().IsMatch(part.Text) || Phone().IsMatch(part.Text);
            var blocked = scan.BlockedTerms.Where(x => !string.IsNullOrWhiteSpace(x)).Any(term =>
                Regex.IsMatch(part.Text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            if (pii || blocked)
                return new(ModerationZone.HardBlock, fingerprint, scan.Phase, PolicyKey,
                    "rule-based", "rules-v1", null, [new(part.Kind, part.Name, pii ? "pii" : "blocked_topic", "Hard rule matched")])
                    { AgeBand = scan.AgeBand, Language = scan.Language };
        }
        if (!IsConfigured || _options.WarningThreshold is not { } warning || _options.BlockThreshold is not { } block
            || _options.MaximumRiskScore is not { } max || warning < 0 || block <= warning || max < block
            || string.IsNullOrWhiteSpace(_options.PolicyVersion)
            || string.Equals(_options.Provider, "Vertex", StringComparison.OrdinalIgnoreCase) && max != 100m)
            throw new InvalidOperationException("MODERATION_NOT_CONFIGURED");
        var provider = _providers.SingleOrDefault(x => string.Equals(x.Name, _options.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("MODERATION_PROVIDER_NOT_REGISTERED");
        var rubric = await _rubrics.GetActiveAsync(ct);
        var pinnedScan = scan with { Rubric = rubric, RubricVersion = rubric.Version };
        var result = await provider.EvaluateAsync(pinnedScan, ct);
        if (result.RubricVersion != rubric.Version
            || result.RiskScore < 0 || result.RiskScore > max || string.IsNullOrWhiteSpace(result.Model)
            || result.Findings is null || result.Findings.Any(x => string.IsNullOrWhiteSpace(x.Reason)
                || string.IsNullOrWhiteSpace(x.Category) || !scan.Parts.Any(p => p.Kind == x.Kind && p.Name == x.Name)))
            throw new InvalidOperationException("MODERATION_RESPONSE_INVALID");
        var zone = result.RiskScore >= block ? ModerationZone.HardBlock
            : result.RiskScore >= warning ? ModerationZone.GrayZone : ModerationZone.Safe;
        if (zone != ModerationZone.Safe && result.Findings.Count == 0)
            throw new InvalidOperationException("MODERATION_EVIDENCE_REQUIRED");
        return new(zone, fingerprint, scan.Phase, Key(rubric), provider.Name, result.Model,
            result.RiskScore, result.Findings.Select(f =>
            {
                var part = scan.Parts.Single(x => x.Kind == f.Kind && x.Name == f.Name);
                return f with { StartOffset = part.StartOffset, EndOffset = part.EndOffset };
            }).ToArray()) { AgeBand = scan.AgeBand, Language = scan.Language, RubricVersion = result.RubricVersion, Scores = result.Scores,
                RubricId = rubric.Id, RubricHash = rubric.ContentHash };
    }

    public static string Fingerprint(GrayZoneScan scan) => Convert.ToHexString(SHA256.HashData(
        // Rubric identity belongs in PolicyKey; keep the source fingerprint stable across rubric versions.
        Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(scan with { RubricVersion = GrayZoneRules.RubricVersion, Rubric = null }))));

    public static IReadOnlyList<ModerationText> TextParts(string title, string content)
    {
        var result = new List<ModerationText> { new("section", "title", title) };
        var index = 0;
        foreach (Match match in Regex.Matches(content, @"[^\r\n]+"))
            if (!string.IsNullOrWhiteSpace(match.Value))
                result.Add(new("paragraph", $"paragraph:{index++}", match.Value, match.Index, match.Index + match.Length));
        return result;
    }

    public static string Redact(string text) => Phone().Replace(Email().Replace(text, "[email]"), "[số điện thoại]");
    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}")] private static partial Regex Email();
    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\s().-]?){9,15}(?!\d)")] private static partial Regex Phone();
}
