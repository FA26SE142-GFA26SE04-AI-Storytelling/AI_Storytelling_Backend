namespace StoryPlatform.Application.Features.Stories.Interfaces;

/// <summary>Đổi các tham chiếu ảnh bìa (scene:...) trong một loạt truyện thành link ảnh đã ký (tham chiếu hỏng → null).</summary>
public interface IStoryCoverResolver
{
    Task<Func<string?, string?>> ResolveAsync(IEnumerable<string?> covers, CancellationToken cancellationToken = default);
}
