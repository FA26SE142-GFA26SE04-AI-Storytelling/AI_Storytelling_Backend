namespace StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;

/// <summary>Tra quyền của bé (chức năng phụ huynh bật/tắt) theo cài đặt an toàn hiện tại.</summary>
public interface IChildFeatureService
{
    Task<bool> IsEnabledAsync(int childProfileId, string code, CancellationToken cancellationToken = default);

    /// <summary>Mã các chức năng đang tắt, theo thứ tự danh mục.</summary>
    Task<IReadOnlyList<string>> GetDisabledAsync(int childProfileId, CancellationToken cancellationToken = default);
}
