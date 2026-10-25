using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes;

public static class StoryWishPolicy
{
    public const int MaxWaiting = 2;
    public const int MaxPerDay = 1;
    public const int ListLimit = 10;
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);

    public const string TooManyWaitingMessage = "Em đã có 2 điều ước đang chờ rồi, mình đợi bố mẹ nhé!";
    public const string DailyLimitMessage = "Mai em ước tiếp nhé!";
    public const string SubmittedMessage = "Bố mẹ sẽ xem điều ước của em nhé!";
    public const string DraftMissingMessage = "Em bắt đầu ước lại nhé!";
    public const string NeedRequiredCardsMessage = "Em chọn nhân vật và nơi chốn nhé!";
    public const string CardUnavailableMessage = "Có thẻ không còn dùng được, em chọn lại nhé.";
    public const string PickCardMessage = "Em chọn một thẻ nhé.";
    public const string WaitingStatusMessage = "Bố mẹ đang xem";
    public const string FulfilledNewMessage = "Điều ước của em đã thành truyện!";
    public const string FulfilledExistingMessage = "Bố mẹ đã tìm được truyện cho điều ước của em!";
    public const string NotThisTimeMessage = "Lần này bố mẹ sẽ kể cho em truyện khác nhé";

    public static DateTime StartOfLocalDayUtc(DateTime utcNow)
    {
        var localMidnight = (utcNow + LocalOffset).Date;
        return DateTime.SpecifyKind(localMidnight - LocalOffset, DateTimeKind.Utc);
    }

    public static string StatusMessage(StoryWish wish) => wish.Status switch
    {
        StoryWishStatus.Pending or StoryWishStatus.Accepted => WaitingStatusMessage,
        StoryWishStatus.Fulfilled => wish.FulfilledWithExistingStory ? FulfilledExistingMessage : FulfilledNewMessage,
        _ => NotThisTimeMessage
    };
}
