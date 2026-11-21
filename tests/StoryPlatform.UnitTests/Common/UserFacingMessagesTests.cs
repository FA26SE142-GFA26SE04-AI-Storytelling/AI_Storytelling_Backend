using StoryPlatform.Application.Common.Exceptions;
using Xunit;

namespace StoryPlatform.UnitTests.Common;

public class UserFacingMessagesTests
{
    [Fact]
    public void Translate_KnownCode_ReturnsVietnamese() =>
        Assert.Equal("Bạn cần lưu truyện trước khi thực hiện thao tác này.", UserFacingMessages.Translate("SAVED_VERSION_REQUIRED"));

    [Fact]
    public void Translate_VietnameseMessage_IsKept() =>
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không chính xác.", UserFacingMessages.Translate("Tên đăng nhập hoặc mật khẩu không chính xác."));

    [Fact]
    public void Translate_CodeWithVietnameseDetail_ReturnsDetail() =>
        Assert.Equal("Ý tưởng có chủ đề chưa phù hợp.", UserFacingMessages.Translate("INPUT_TOPIC_BLOCKED: Ý tưởng có chủ đề chưa phù hợp."));

    [Theory]
    [InlineData("STORY_STATUS_INVALID:Draft", "Trạng thái hiện tại của truyện không cho phép thao tác này.")]
    [InlineData("SOMETHING_UNKNOWN_CHANGED", "Dữ liệu vừa được cập nhật ở nơi khác. Bạn tải lại trang rồi thử lại nhé.")]
    [InlineData("WIDGET_NOT_FOUND", "Không tìm thấy dữ liệu yêu cầu. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập.")]
    [InlineData("Some English sentence here.", UserFacingMessages.Generic)]
    public void Translate_CodesAndEnglish_NeverLeakRawText(string input, string expected) =>
        Assert.Equal(expected, UserFacingMessages.Translate(input));

    [Fact]
    public void TranslateValidation_DefaultRequiredMessage_IsVietnamese() =>
        Assert.Equal("Còn thiếu thông tin cần thiết. Bạn kiểm tra lại các mục bắt buộc nhé.",
            UserFacingMessages.TranslateValidation("The Title field is required."));

    [Fact]
    public void NotFound_EnglishEntityName_IsVietnamese() =>
        Assert.Equal("Không tìm thấy truyện. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập.",
            new NotFoundException("Story").Message);
}
