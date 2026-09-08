using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.BLL.Common.Models;
using StoryPlatform.BLL.Modules.Story.DTOs;

namespace StoryPlatform.BLL.Modules.Story.Interfaces;

public interface IStoryService
{
    Task<PagedResult<StoryDto>> GetStoriesAsync(StoryFilterRequestDto filter, CancellationToken cancellationToken = default);
    Task<StoryDto> GetStoryByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<StoryDto> CreateStoryAsync(int authorUserId, CreateStoryRequestDto request, CancellationToken cancellationToken = default);
    Task<StoryDto> UpdateStoryAsync(int id, int currentUserId, UpdateStoryRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteStoryAsync(int id, int currentUserId, CancellationToken cancellationToken = default);
    Task<bool> PublishStoryAsync(int id, int currentUserId, CancellationToken cancellationToken = default);
}
