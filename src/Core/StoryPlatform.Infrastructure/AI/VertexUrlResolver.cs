using System;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Resolves the Vertex AI or Gemini REST endpoint URL for a given model.
/// When Location is "global", Google Cloud uses domain aiplatform.googleapis.com (without global- subdomain).
/// For regional locations (e.g. "us-central1"), it uses {location}-aiplatform.googleapis.com.
/// </summary>
public static class VertexUrlResolver
{
    public static string Resolve(VertexOptions vertex, GeminiOptions gemini, string model)
    {
        if (!vertex.UseVertex)
        {
            return $"{gemini.Endpoint.TrimEnd('/')}/{model}:generateContent";
        }

        var host = ResolveHost(vertex);

        return $"https://{host}/v1/projects/{vertex.ProjectId}/locations/{vertex.Location}/publishers/google/models/{model}:generateContent";
    }

    public static string ResolveHost(VertexOptions vertex) =>
        string.Equals(vertex.Location, "global", StringComparison.OrdinalIgnoreCase)
            ? "aiplatform.googleapis.com"
            : $"{vertex.Location}-aiplatform.googleapis.com";
}
