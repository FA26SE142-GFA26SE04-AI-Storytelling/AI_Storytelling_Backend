namespace StoryPlatform.Infrastructure.Storage;

public sealed class SupabaseStorageHttpClientHandler : DelegatingHandler
{
    private readonly SupabaseStorageOptions _options;

    public SupabaseStorageHttpClientHandler(SupabaseStorageOptions options) => _options = options;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new InvalidOperationException("SUPABASE_STORAGE_NOT_CONFIGURED");

        // Modern sb_secret_* keys are opaque API keys, not JWT bearer tokens.
        request.Headers.TryAddWithoutValidation("apikey", _options.SecretKey);
        return base.SendAsync(request, cancellationToken);
    }
}
