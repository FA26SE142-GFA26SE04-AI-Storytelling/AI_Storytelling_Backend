namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Vertex AI connection configuration.
/// When <c>UseVertex</c> is true, providers authenticate via a Google service account
/// and route all calls through the regional Vertex AI endpoint.
/// When false (default), the system falls back to the public Gemini REST API key method.
/// </summary>
public sealed class VertexOptions
{
    public const string SectionName = "AI:Vertex";

    /// <summary>
    /// Enable Vertex AI authentication via service account.
    /// When false the existing x-goog-api-key header method is used.
    /// </summary>
    public bool UseVertex { get; set; } = false;

    /// <summary>Your Google Cloud project ID (e.g. "my-project-123").</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Vertex AI region. Common values: "us-central1", "europe-west3", "asia-southeast1".
    /// Must be a supported Vertex AI region.
    /// </summary>
    public string Location { get; set; } = "us-central1";

    /// <summary>
    /// Path to the service account JSON key file on disk.
    /// If empty, falls back to the <c>GOOGLE_APPLICATION_CREDENTIALS</c> environment variable.
    /// In containerised environments this is typically mounted as a secret volume.
    /// </summary>
    public string CredentialsPath { get; set; } = string.Empty;
}
