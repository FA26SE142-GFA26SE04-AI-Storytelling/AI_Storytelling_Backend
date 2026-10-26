using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.WishCards;

public static class WishCardRules
{
    /// <summary>Bé ước: mỗi màn hình tối đa 8 thẻ (BR của UC-L2D-01).</summary>
    public const int MaxCardsPerWishScreen = 8;
    public const int MaxLabelLength = 100;
    public const int MaxUrlLength = 500;

    /// <summary>Đọc tên nhóm (không phân biệt hoa thường); từ chối chữ lạ và số.</summary>
    public static bool TryParseGroup(string? text, out WishCardGroup group)
    {
        group = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || char.IsDigit(trimmed[0]))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out group) && Enum.IsDefined(group);
    }

    /// <summary>4 nhóm của Bé ước: nhân vật, nơi chốn, thử thách, bài học.</summary>
    public static bool IsWishGroup(WishCardGroup group) =>
        group is WishCardGroup.Character or WishCardGroup.Place or WishCardGroup.Challenge or WishCardGroup.Lesson;

    /// <summary>Chỉ nhận đường dẫn tuyệt đối http/https, không dài hơn cột lưu.</summary>
    public static bool IsValidUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUrlLength)
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

public static class ChildWishCardPolicy
{
    public const string WishesOffMessage = "Bây giờ mình chưa ước truyện được nhé!";
    public const string ProfileUnavailableMessage = "Hồ sơ này hiện chưa ước truyện được.";
    public const string UnknownGroupMessage = "Em chọn một nhóm thẻ nhé.";
}
