using System;
using StoryPlatform.Application.Abstractions.Security;

namespace StoryPlatform.Infrastructure.Security;

public class PasswordHasher : IPasswordHasher
{
    private const int DefaultWorkFactor = 11;

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        }

        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: DefaultWorkFactor);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (Exception)
        {
            // Trả về false nếu hash bị lỗi format hoặc hư hại, tránh văng lỗi 500 không mong muốn
            return false;
        }
    }
}
