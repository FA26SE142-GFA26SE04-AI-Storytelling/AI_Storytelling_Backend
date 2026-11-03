using System.Security.Cryptography;
using System.Text;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ContentGeneration.Quality;

public sealed record ReadabilityEvaluationEvidence(string ContentHash, string Algorithm, string Language,
    int ReadingLevel, ContentReadabilityLimits Limits)
{
    public static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
