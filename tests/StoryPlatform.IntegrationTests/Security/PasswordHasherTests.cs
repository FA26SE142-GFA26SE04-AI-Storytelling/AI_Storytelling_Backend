using System;
using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.IntegrationTests.Security;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void HashPassword_WithValidPassword_ShouldProduceValidBCryptHash()
    {
        // Arrange
        var password = "SecurePassword@123";

        // Act
        var hash = _sut.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        Assert.StartsWith("$2a$11$", hash);
        Assert.Equal(60, hash.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_WithNullOrWhitespace_ShouldThrowArgumentException(string? invalidPassword)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.HashPassword(invalidPassword!));
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var password = "CorrectPassword#2026";
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        var password = "CorrectPassword#2026";
        var wrongPassword = "WrongPassword#2026";
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null, "$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S")]
    [InlineData("", "$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S")]
    [InlineData("Demo@123", null)]
    [InlineData("Demo@123", "")]
    [InlineData("Demo@123", "   ")]
    public void VerifyPassword_WithNullOrWhitespaceInputs_ShouldReturnFalse(string? password, string? hash)
    {
        // Act
        var result = _sut.VerifyPassword(password!, hash!);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("Demo@123", "not_a_bcrypt_hash")]
    [InlineData("Demo@123", "$2a$99$invalidcostcharacters")]
    [InlineData("Demo@123", "random_gibberish_string_12345")]
    public void VerifyPassword_WithMalformedHash_ShouldReturnFalseWithoutThrowing(string password, string malformedHash)
    {
        // Act
        var result = _sut.VerifyPassword(password, malformedHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_WithDatabaseSeedHash_ShouldVerifySuccessfully()
    {
        // Hash từ Database/Seed-Database.ps1 cho các tài khoản demo
        var seedHash = "$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S";
        var seedPassword = "Demo@123";

        // Act
        var result = _sut.VerifyPassword(seedPassword, seedHash);

        // Assert
        Assert.True(result);
    }
}
