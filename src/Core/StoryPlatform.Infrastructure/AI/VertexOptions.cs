namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Vertex AI connection configuration.
/// When <c>UseVertex</c> is true, providers authenticate via a Google service account or ADC
/// and route all calls through the Vertex AI endpoint (aiplatform.googleapis.com for global).
/// When false, the system falls back to the public Gemini REST API key method.
/// </summary>
public sealed class VertexOptions
{
    public const string SectionName = "AI:Vertex";

    /// <summary>
    /// Enable Vertex AI authentication via service account or Application Default Credentials (ADC).
    /// When false the existing x-goog-api-key header method is used.
    /// </summary>
    public bool UseVertex { get; set; }

    /// <summary>Your Google Cloud project ID (e.g. "gen-lang-client-0675088605").</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Vertex AI region/location. Defaults to "global" for latest serverless models.
    /// Regional examples: "us-central1", "europe-west3", "asia-southeast1".
    /// </summary>
    public string Location { get; set; } = "global";

    /// <summary>
    /// Path to the service account JSON key file on disk.
    /// If empty, falls back to Application Default Credentials (ADC).
    /// </summary>
    public string CredentialsPath { get; set; } = string.Empty;
}
