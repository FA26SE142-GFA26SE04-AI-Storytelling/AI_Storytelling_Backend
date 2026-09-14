using System.Text.RegularExpressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Services;

public class ChildAccessCredentialService : IChildAccessCredentialService
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public ChildAccessCredentialService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task SetPinAsync(
        int childProfileId, int currentUserId, SetChildAccessCredentialRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);
        ValidateCredential(request.AvatarId, request.Pin);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);
        var pinHash = _passwordHasher.HashPassword(request.Pin);

        if (credential == null)
        {
            credential = new ChildAccessCredential
            {
                ChildProfileId = childProfileId,
                AvatarId = request.AvatarId.Trim(),
                PinHash = pinHash,
                CreatedByUserId = currentUserId
            };
            await credentialRepo.AddAsync(credential, cancellationToken);
        }
        else
        {
            credential.AvatarId = request.AvatarId.Trim();
            credential.PinHash = pinHash;
            credential.FailedAttempts = 0;
            credential.LockedUntil = null;
            credential.UpdatedAt = DateTime.UtcNow;
            credentialRepo.Update(credential);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChildSessionDto> LoginWithPinAsync(
        int childProfileId, string pin,
        CancellationToken cancellationToken = default)
    {
        ValidatePin(pin);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);
        if (credential == null)
        {
            throw new NotFoundException("Thông tin truy cập của hồ sơ trẻ", childProfileId);
        }

        if (credential.LockedUntil.HasValue && credential.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new ForbiddenException(
                $"Truy cập tạm thời bị khoá do nhập sai PIN quá {MaxFailedAttempts} lần. "
                + $"Vui lòng thử lại sau {LockoutMinutes} phút hoặc nhờ Supervisor mở lại.");
        }

        if (!_passwordHasher.VerifyPassword(pin, credential.PinHash))
        {
            credential.FailedAttempts += 1;
            if (credential.FailedAttempts >= MaxFailedAttempts)
            {
                credential.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                credential.FailedAttempts = 0;
            }

            credentialRepo.Update(credential);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("PIN không chính xác.");
        }

        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        credential.UpdatedAt = DateTime.UtcNow;
        credentialRepo.Update(credential);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ChildSessionDto
        {
            ChildProfileId = credential.ChildProfileId,
            AvatarId = credential.AvatarId,
            AccessToken = _jwtTokenGenerator.GenerateChildAccessToken(credential.ChildProfileId),
            ExpiresInSeconds = _jwtTokenGenerator.ChildTokenExpiresInSeconds
        };
    }

    public async Task<ChildAccessCredentialDto> GetCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var credential = await _unitOfWork.Repository<ChildAccessCredential>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (credential == null)
        {
            throw new NotFoundException("Thông tin truy cập của hồ sơ trẻ", childProfileId);
        }

        return new ChildAccessCredentialDto
        {
            ChildProfileId = credential.ChildProfileId,
            AvatarId = credential.AvatarId,
            HasPin = !string.IsNullOrEmpty(credential.PinHash),
            IsLocked = credential.LockedUntil.HasValue && credential.LockedUntil.Value > DateTime.UtcNow
        };
    }

    public async Task RevokeCredentialAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

        var credentialRepo = _unitOfWork.Repository<ChildAccessCredential>();
        var credential = await credentialRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        if (credential != null)
        {
            credentialRepo.Delete(credential);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ChildSessionProfileDto> GetMySessionProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        return new ChildSessionProfileDto
        {
            ChildProfileId = profile.Id,
            Nickname = profile.Nickname,
            AgeBand = profile.AgeBand.ToString()
        };
    }

    private static void ValidateCredential(string avatarId, string pin)
    {
        if (string.IsNullOrWhiteSpace(avatarId) || avatarId.Trim().Length > 100)
        {
            throw new BadRequestException("Avatar ID phải từ 1 đến 100 ký tự.");
        }

        ValidatePin(pin);
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !Regex.IsMatch(pin, @"^\d{4,6}$"))
        {
            throw new BadRequestException("PIN phải gồm 4-6 chữ số.");
        }
    }
}
