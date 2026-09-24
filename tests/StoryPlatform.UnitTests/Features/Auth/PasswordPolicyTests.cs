using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.Auth.DTOs;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("abc12345", true)]
    [InlineData("Mậtkhẩu1", true)]
    [InlineData("a1b2c3d4e5", true)]
    [InlineData("abc1234", false)]
    [InlineData("abcdefgh", false)]
    [InlineData("12345678", false)]
    [InlineData("!@#$%^&*", false)]
    public void RegisterRequest_PasswordPolicy(string password, bool expectedValid)
    {
        var request = new RegisterRequestDto
        {
            Username = "parent01",
            Email = "parent01@example.com",
            FullName = "Phụ huynh",
            Password = password,
            ConfirmPassword = password
        };

        Assert.Equal(expectedValid, IsValid(request));
    }

    [Theory]
    [InlineData("abc12345", true)]
    [InlineData("abcdefgh", false)]
    [InlineData("12345678", false)]
    [InlineData("abc123", false)]
    public void ResetPasswordRequest_PasswordPolicy(string password, bool expectedValid)
    {
        var request = new ResetPasswordRequestDto
        {
            Email = "parent01@example.com",
            ResetToken = "token",
            NewPassword = password,
            ConfirmPassword = password
        };

        Assert.Equal(expectedValid, IsValid(request));
    }

    [Theory]
    [InlineData("abc12345", true)]
    [InlineData("abcdefgh", false)]
    [InlineData("12345678", false)]
    [InlineData("abc123", false)]
    public void ChangePasswordRequest_PasswordPolicy(string password, bool expectedValid)
    {
        var request = new ChangePasswordRequestDto
        {
            CurrentPassword = "old-password1",
            NewPassword = password,
            ConfirmPassword = password
        };

        Assert.Equal(expectedValid, IsValid(request));
    }

    [Fact]
    public void RegisterRequest_MissingLetterOrDigit_ReturnsCompositionMessage()
    {
        var request = new RegisterRequestDto
        {
            Username = "parent01",
            Email = "parent01@example.com",
            FullName = "Phụ huynh",
            Password = "abcdefgh",
            ConfirmPassword = "abcdefgh"
        };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, result => result.ErrorMessage == PasswordPolicy.CompositionErrorMessage);
    }

    private static bool IsValid(object request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true);
}
