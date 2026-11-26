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

    [Theory]
    [InlineData("The Title field is required.", "Title: không được để trống.")]
    [InlineData("The field Topic must be a string with a maximum length of 200.", "Topic: tối đa 200 ký tự.")]
    [InlineData("The field Lesson must be a string with a minimum length of 2 and a maximum length of 500.", "Lesson: từ 2 đến 500 ký tự.")]
    [InlineData("The field Tags must be a string or array type with a maximum length of '10'.", "Tags: tối đa 10 ký tự hoặc mục.")]
    [InlineData("The field Tags must be a string or array type with a minimum length of '1'.", "Tags: tối thiểu 1 ký tự hoặc mục.")]
    [InlineData("The field ReadingLevel must be between 1 and 5.", "ReadingLevel: phải từ 1 đến 5.")]
    [InlineData("The field ChildProfileId must be between 1 and 2147483647.", "ChildProfileId: phải từ 1 trở lên.")]
    [InlineData("The field AgeBand must match the regular expression '^(6-8|9-10)$'.", "AgeBand: không đúng định dạng.")]
    [InlineData("The Email field is not a valid e-mail address.", "Email: email không hợp lệ.")]
    [InlineData("The JSON value could not be converted to System.Int32. Path: $.childProfileId | LineNumber: 0 | BytePositionInLine: 20.", "childProfileId: sai kiểu dữ liệu.")]
    public void TranslateValidation_DefaultEnglishMessage_NamesTheField(string input, string expected) =>
        Assert.Equal(expected, UserFacingMessages.TranslateValidation(input));

    [Fact]
    public void TranslateValidation_VietnameseMessage_IsKept() =>
        Assert.Equal("Ý tưởng câu chuyện từ 2 đến 200 ký tự.", UserFacingMessages.TranslateValidation("Ý tưởng câu chuyện từ 2 đến 200 ký tự."));

    [Fact]
    public void TranslateValidation_MissingRequestBody_IsFormatMessage() =>
        Assert.Equal("Dữ liệu gửi lên không đúng định dạng. Bạn tải lại trang rồi thử lại nhé.",
            UserFacingMessages.TranslateValidation("The request field is required."));

    [Fact]
    public void NotFound_EnglishEntityName_IsVietnamese() =>
        Assert.Equal("Không tìm thấy truyện. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập.",
            new NotFoundException("Story").Message);
}
