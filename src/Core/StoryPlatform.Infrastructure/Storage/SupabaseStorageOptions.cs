namespace StoryPlatform.Infrastructure.Storage;

public sealed class SupabaseStorageOptions
{
    public const string SectionName = "Supabase:Storage";

    public string Url { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "story-media";
}
