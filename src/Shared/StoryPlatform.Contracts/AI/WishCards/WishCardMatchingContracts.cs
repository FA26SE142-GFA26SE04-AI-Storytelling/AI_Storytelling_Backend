using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StoryPlatform.Contracts.AI.WishCards;

public sealed record WishCardCandidate(int Id, string Group, string Label);
public sealed record WishCardEvidence(int CardId, string SourceQuote, string Reason);
public sealed record MatchWishCardsRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<WishCardCandidate> Candidates { get; init; } = [];
}
public sealed record MatchWishCardsResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<WishCardEvidence> Matches { get; init; } = [];
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = "wish-card-matching/v1";
}

public static class WishCardMatchingRules
{
    public const int MaximumCards = 128;
    public static string Fingerprint(MatchWishCardsRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.Title, request.Content, request.AgeBand, request.Language,
            Candidates = request.Candidates.OrderBy(x => x.Id).ToArray()
        }))));

    public static void ValidateRequest(MatchWishCardsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 100 ||
            string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 100_000 || request.Title is null || request.Title.Length > 500 ||
            request.AgeBand is not ("6-8" or "9-10") || request.Language is not ("vi" or "en") ||
            request.Candidates is null || request.Candidates.Count is < 1 or > MaximumCards ||
            request.Candidates.Any(x => x is null || x.Id <= 0 || string.IsNullOrWhiteSpace(x.Label) || x.Label.Length > 100 ||
                x.Group is not ("Character" or "Place" or "Challenge" or "Lesson")) ||
            request.Candidates.Select(x => x.Id).Distinct().Count() != request.Candidates.Count ||
            request.SourceFingerprint != Fingerprint(request))
            throw new ArgumentException("WISH_CARD_REQUEST_INVALID");
    }

    public static void ValidateResponse(MatchWishCardsRequest request, MatchWishCardsResponse response)
    {
        if (response.RequestId != request.RequestId || response.SourceFingerprint != request.SourceFingerprint ||
            response.PromptVersion != "wish-card-matching/v1" || string.IsNullOrWhiteSpace(response.Model) || response.Model.Length > 200 ||
            response.Matches is null || response.Matches.Count > request.Candidates.Count ||
            response.Matches.Select(x => x?.CardId).Distinct().Count() != response.Matches.Count ||
            response.Matches.Any(x => x is null || !request.Candidates.Any(c => c.Id == x.CardId) ||
                string.IsNullOrWhiteSpace(x.SourceQuote) || x.SourceQuote.Length > 500 ||
                !request.Content.Contains(x.SourceQuote, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(x.Reason) || x.Reason.Length > 500))
            throw new JsonException("WISH_CARD_OUTPUT_INVALID");
    }
}
