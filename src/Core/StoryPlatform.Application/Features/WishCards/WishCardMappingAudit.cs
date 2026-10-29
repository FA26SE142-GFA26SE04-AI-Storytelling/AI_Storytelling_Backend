using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.WishCards;

public static class WishCardMappingAudit
{
    public const string Action = "WishCardMappingsChanged";
    public const string EntityType = nameof(Story);

    public static string Fingerprint(IEnumerable<StoryWishCardMatch> mappings) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mappings.OrderBy(x => x.Id).Select(x => new
        { x.Id, x.StoryId, x.WishCardId, x.Source, x.CreatedByUserId }).ToArray()))));
}
