using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public sealed class ChildMediaUrlSigner : IChildMediaUrlSigner
{
    /// <summary>Bé có thể đọc một truyện khá lâu sau khi mở, nên link sống lâu hơn link xem trước của phụ huynh (10 phút).</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromHours(2);

    /// <summary>Giới hạn số yêu cầu ký đồng thời tới kho media.</summary>
    private const int MaxConcurrency = 8;

    private readonly IMediaStorage _storage;
    private readonly ILogger<ChildMediaUrlSigner> _logger;

    public ChildMediaUrlSigner(IMediaStorage storage, ILogger<ChildMediaUrlSigner> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<Func<string?, string?>> SignAsync(IEnumerable<string?> paths, CancellationToken cancellationToken = default)
    {
        var unique = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!)
            .Distinct(StringComparer.Ordinal).ToList();
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var signed = await Task.WhenAll(unique.Select(async path =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return (Path: path, Url: await SignOneAsync(path, cancellationToken)); }
            finally { gate.Release(); }
        }));
        var map = signed.ToDictionary(item => item.Path, item => item.Url, StringComparer.Ordinal);
        return path => string.IsNullOrWhiteSpace(path) ? null : map.GetValueOrDefault(path);
    }

    private async Task<string?> SignOneAsync(string path, CancellationToken cancellationToken)
    {
        // Dữ liệu seed cũ có thể lưu sẵn link https đầy đủ thay vì đường dẫn trong kho.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return uri.Scheme == Uri.UriSchemeHttps ? path : null;
        try
        {
            return await _storage.GetSignedUrlAsync(path, Expiry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Một tranh/âm thanh lỗi không được làm hỏng cả phiên đọc: trả null để giao diện dùng hình/giọng mặc định.
            _logger.LogWarning(ex, "Could not sign child media path {StoragePath}", path);
            return null;
        }
    }
}

/// <summary>Ký toàn bộ link media trong các DTO trả cho bé.</summary>
public static class ChildMediaUrlSigning
{
    public static async Task SignAsync(this IChildMediaUrlSigner? signer, ReadingContentDto content, CancellationToken ct)
    {
        if (signer is null) return;
        var interactions = content.Scenes.Select(scene => scene.Interaction).OfType<ReadingInteractionDto>().ToList();
        var paths = content.Scenes.Select(scene => scene.IllustrationUrl)
            .Concat(content.Scenes.SelectMany(scene => scene.Illustrations).Select(picture => picture.Url))
            .Concat(content.Scenes.SelectMany(scene => scene.Segments).Select(segment => segment.AudioUrl))
            .Concat(content.Vocabulary.Select(word => word.AudioUrl))
            .Concat(interactions.Select(interaction => interaction.ModelAudioUrl))
            .Concat(interactions.SelectMany(interaction => interaction.AudioUrls.Values))
            .Concat(interactions.SelectMany(interaction => interaction.Options)
                .SelectMany(option => new[] { option.ImageUrl, option.AudioUrl, option.FeedbackAudioUrl }));
        var url = await signer.SignAsync(paths, ct);

        foreach (var scene in content.Scenes)
        {
            scene.IllustrationUrl = url(scene.IllustrationUrl);
            foreach (var picture in scene.Illustrations) picture.Url = url(picture.Url);
            foreach (var segment in scene.Segments) segment.AudioUrl = url(segment.AudioUrl);
        }
        foreach (var word in content.Vocabulary) word.AudioUrl = url(word.AudioUrl);
        foreach (var interaction in interactions)
        {
            interaction.ModelAudioUrl = url(interaction.ModelAudioUrl);
            interaction.AudioUrls = interaction.AudioUrls
                .Select(pair => (pair.Key, Url: url(pair.Value)))
                .Where(pair => pair.Url is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Url!);
            foreach (var option in interaction.Options)
            {
                option.ImageUrl = url(option.ImageUrl);
                option.AudioUrl = url(option.AudioUrl);
                option.FeedbackAudioUrl = url(option.FeedbackAudioUrl);
            }
        }
    }

    /// <summary>Ký các link trong một bảng tra (khoá → đường dẫn); link không ký được bị bỏ khỏi bảng.</summary>
    public static async Task<IReadOnlyDictionary<TKey, string>> SignValuesAsync<TKey>(
        this IChildMediaUrlSigner? signer, IEnumerable<KeyValuePair<TKey, string>> map, CancellationToken ct) where TKey : notnull
    {
        var pairs = map.ToList();
        if (signer is null) return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
        var url = await signer.SignAsync(pairs.Select(pair => (string?)pair.Value), ct);
        return pairs.Select(pair => (pair.Key, Url: url(pair.Value)))
            .Where(pair => pair.Url is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Url!);
    }

    /// <summary>Ký một link đơn lẻ (phản hồi tương tác, âm thanh từ vựng).</summary>
    public static async Task<string?> SignAsync(this IChildMediaUrlSigner? signer, string? path, CancellationToken ct)
    {
        if (signer is null || string.IsNullOrWhiteSpace(path)) return path;
        return (await signer.SignAsync(new[] { path }, ct))(path);
    }
}
