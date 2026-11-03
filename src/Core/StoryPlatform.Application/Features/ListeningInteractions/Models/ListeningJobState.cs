using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;

namespace StoryPlatform.Application.Features.ListeningInteractions.Models;

/// <summary>Durable request and attempt receipt in the job's existing JSON column, not a new database entity.</summary>
public sealed class ListeningJobState
{
    public string SourceFingerprint { get; set; } = string.Empty;
    public GenerateListeningInteractionRequest? PinnedRequest { get; set; }
    public int ProviderCalls { get; set; }
    public int ValidationFailures { get; set; }
    public int MaximumValidationAttempts { get; set; } = 2;
    public IReadOnlyList<ListeningPreviousErrorDto> PreviousErrors { get; set; } = [];
    public GenerationMetadataDto? Metadata { get; set; }

    public static string Fingerprint(ListeningScope scope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { scope.Version.Id, scope.Version.Title, scope.Version.Content, scope.Version.Lesson, scope.Context, scope.Plan }))));
}
