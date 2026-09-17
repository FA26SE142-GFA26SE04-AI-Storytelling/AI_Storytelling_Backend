using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Security;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private const int MinimumJwtSecretKeyLength = 32;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;
    private readonly int _refreshTokenExpiryDays;
    private readonly int _childTokenExpiryMinutes;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _secretKey = GetRequiredSetting(configuration, "SecretKey");
        if (_secretKey.Length < MinimumJwtSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"'JwtSettings:SecretKey' phải có ít nhất {MinimumJwtSecretKeyLength} ký tự.");
        }

        _issuer = GetRequiredSetting(configuration, "Issuer");
        _audience = GetRequiredSetting(configuration, "Audience");
        _expiryMinutes = GetRequiredPositiveInteger(configuration, "ExpiryMinutes");
        _refreshTokenExpiryDays = GetRequiredPositiveInteger(configuration, "RefreshTokenExpiryDays");
        _childTokenExpiryMinutes = GetRequiredPositiveInteger(configuration, "ChildTokenExpiryMinutes");
    }

    public string GenerateAccessToken(UserAccount user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("token_version", user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = GetExpirationDate(),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateChildAccessToken(int childProfileId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);
        var childProfileIdValue = childProfileId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, childProfileIdValue),
            new(ClaimTypes.NameIdentifier, childProfileIdValue),
            new("token_type", "child"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_childTokenExpiryMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private const string MfaChallengeTokenType = "mfa_challenge";
    private static readonly TimeSpan MfaChallengeTokenTtl = TimeSpan.FromMinutes(5);

    public string GenerateMfaChallengeToken(int userId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);
        var userIdValue = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userIdValue),
            new("token_type", MfaChallengeTokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(MfaChallengeTokenTtl),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public bool TryValidateMfaChallengeToken(string token, out int userId)
    {
        userId = 0;
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            }, out _);

            var tokenType = principal.FindFirst("token_type")?.Value;
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (tokenType != MfaChallengeTokenType || !int.TryParse(subject, out userId))
            {
                userId = 0;
                return false;
            }

            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public DateTime GetExpirationDate()
    {
        return DateTime.UtcNow.AddMinutes(_expiryMinutes);
    }

    public DateTime GetRefreshTokenExpirationDate()
    {
        return DateTime.UtcNow.AddDays(_refreshTokenExpiryDays);
    }

    public long ExpiresInSeconds => _expiryMinutes * 60L;

    public long ChildTokenExpiresInSeconds => _childTokenExpiryMinutes * 60L;

    private static string GetRequiredSetting(IConfiguration configuration, string settingName)
    {
        var key = $"JwtSettings:{settingName}";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Thiếu cấu hình bắt buộc '{key}'.");
        }

        return value;
    }

    private static int GetRequiredPositiveInteger(IConfiguration configuration, string settingName)
    {
        var key = $"JwtSettings:{settingName}";
        if (!int.TryParse(configuration[key], out var value) || value <= 0)
        {
            throw new InvalidOperationException($"'{key}' phải là số nguyên dương.");
        }

        return value;
    }
}
