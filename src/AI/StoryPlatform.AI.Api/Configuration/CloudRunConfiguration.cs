using Microsoft.Extensions.Configuration;

namespace StoryPlatform.AI.Api.Configuration;

/// <summary>Cloud Run must not inherit local provider defaults or local credentials.</summary>
public static class CloudRunConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["K_SERVICE"])) return;

        foreach (var key in new[] { "AI:Google:ProjectId", "AI:Google:Location", "AI:Google:Model", "AI:InternalApiKey" })
            if (string.IsNullOrWhiteSpace(configuration[key]))
                throw new InvalidOperationException($"{key} must be explicitly configured on Cloud Run.");

        if (!string.Equals(configuration["AI:Provider"], "Vertex", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cloud Run requires AI:Provider=Vertex.");
        if (!string.Equals(configuration["AI:Google:AuthMode"], "Adc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cloud Run requires AI:Google:AuthMode=Adc.");
        if (!string.IsNullOrWhiteSpace(configuration["GOOGLE_APPLICATION_CREDENTIALS"]))
            throw new InvalidOperationException("Cloud Run must use its attached service account, not GOOGLE_APPLICATION_CREDENTIALS.");
        if (!int.TryParse(configuration["AI:Google:TimeoutSeconds"], out var timeout) || timeout is < 1 or > 300)
            throw new InvalidOperationException("AI:Google:TimeoutSeconds must be between 1 and 300 on Cloud Run.");
    }
}
