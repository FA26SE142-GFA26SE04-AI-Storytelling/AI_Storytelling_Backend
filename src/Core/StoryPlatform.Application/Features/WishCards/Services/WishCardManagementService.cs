using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.WishCards.Services;

public class WishCardManagementService : IWishCardManagementService
{
    private const int MaxPageSize = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public WishCardManagementService(IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<PagedResult<WishCardDto>> ListAsync(WishCardQueryDto query, CancellationToken ct = default)
    {
        WishCardGroup? group = null;
        if (!string.IsNullOrWhiteSpace(query.Group))
        {
            group = WishCardRules.TryParseGroup(query.Group, out var parsed)
                ? parsed
                : throw new BadRequestException("Nhóm thẻ không hợp lệ.");
        }

        var isActive = query.IsActive;
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (rows, total) = await _unitOfWork.Repository<WishCard>().GetPagedAsync(
            pageIndex,
            pageSize,
            card => !card.IsDeleted
                    && (group == null || card.Group == group)
                    && (isActive == null || card.IsActive == isActive),
            orderBy: cards => cards.OrderBy(card => card.Group).ThenBy(card => card.SortOrder).ThenBy(card => card.Id),
            cancellationToken: ct);
        return new PagedResult<WishCardDto>(rows.Select(ToDto).ToList(), total, pageIndex, pageSize);
    }

    public async Task<WishCardDto> GetAsync(int id, CancellationToken ct = default) => ToDto(await RequireAsync(id, ct));

    public async Task<WishCardDto> CreateAsync(
        int actorUserId, CreateWishCardRequestDto request, CancellationToken ct = default)
    {
        if (!WishCardRules.TryParseGroup(request.Group, out var group))
        {
            throw new BadRequestException("Nhóm thẻ không hợp lệ.");
        }

        var fields = await ValidateAsync(request.Label, request.ImageUrl, request.AudioUrl, request.AgeBand,
            request.ContentCategoryId, ct);
        await EnsureLabelIsFreeAsync(group, fields.Label, null, ct);

        var card = await _unitOfWork.Repository<WishCard>().AddAsync(new WishCard
        {
            Group = group,
            Label = fields.Label,
            ImageUrl = fields.ImageUrl,
            AudioUrl = fields.AudioUrl,
            AgeBand = fields.AgeBand,
            ContentCategoryId = request.ContentCategoryId,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedByUserId = actorUserId
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditLogWriter.LogAsync(actorUserId, "WishCardCreated", nameof(WishCard), card.Id, null, Snapshot(card), ct);
        return ToDto(card);
    }

    public async Task<WishCardDto> UpdateAsync(
        int actorUserId, int id, UpdateWishCardRequestDto request, CancellationToken ct = default)
    {
        var card = await RequireAsync(id, ct);
        var fields = await ValidateAsync(request.Label, request.ImageUrl, request.AudioUrl, request.AgeBand,
            request.ContentCategoryId, ct);
        await EnsureLabelIsFreeAsync(card.Group, fields.Label, card.Id, ct);

        var before = Snapshot(card);
        card.Label = fields.Label;
        card.ImageUrl = fields.ImageUrl;
        card.AudioUrl = fields.AudioUrl;
        card.AgeBand = fields.AgeBand;
        card.ContentCategoryId = request.ContentCategoryId;
        card.SortOrder = request.SortOrder;
        card.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<WishCard>().Update(card);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditLogWriter.LogAsync(actorUserId, "WishCardUpdated", nameof(WishCard), card.Id, before, Snapshot(card), ct);
        return ToDto(card);
    }

    public async Task<WishCardDto> SetActiveAsync(
        int actorUserId, int id, bool isActive, CancellationToken ct = default)
    {
        var card = await RequireAsync(id, ct);
        if (card.IsActive == isActive)
        {
            return ToDto(card);
        }

        var before = Snapshot(card);
        card.IsActive = isActive;
        card.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<WishCard>().Update(card);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditLogWriter.LogAsync(
            actorUserId, "WishCardVisibilityChanged", nameof(WishCard), card.Id, before, Snapshot(card), ct);
        return ToDto(card);
    }

    private async Task<WishCard> RequireAsync(int id, CancellationToken ct)
    {
        var card = await _unitOfWork.Repository<WishCard>().GetByIdAsync(id, ct);
        if (card == null || card.IsDeleted)
        {
            throw new NotFoundException("Thẻ ước", id);
        }

        return card;
    }

    private sealed record Fields(string Label, string ImageUrl, string? AudioUrl, AgeBand? AgeBand);

    private async Task<Fields> ValidateAsync(
        string? label, string? imageUrl, string? audioUrl, string? ageBand, int? categoryId, CancellationToken ct)
    {
        var cleanLabel = label?.Trim();
        if (string.IsNullOrEmpty(cleanLabel) || cleanLabel.Length > WishCardRules.MaxLabelLength)
        {
            throw new BadRequestException($"Nhãn thẻ là bắt buộc và tối đa {WishCardRules.MaxLabelLength} ký tự.");
        }

        var cleanImage = imageUrl?.Trim();
        if (!WishCardRules.IsValidUrl(cleanImage))
        {
            throw new BadRequestException("Đường dẫn hình thẻ không hợp lệ.");
        }

        var cleanAudio = string.IsNullOrWhiteSpace(audioUrl) ? null : audioUrl.Trim();
        if (cleanAudio != null && !WishCardRules.IsValidUrl(cleanAudio))
        {
            throw new BadRequestException("Đường dẫn âm thanh của thẻ không hợp lệ.");
        }

        // RoadmapAge.Parse tự báo lỗi với giá trị lạ; để trống = mọi nhóm tuổi.
        var parsedAge = RoadmapAge.Parse(ageBand);

        if (categoryId.HasValue)
        {
            var category = await _unitOfWork.Repository<ContentCategory>().GetByIdAsync(categoryId.Value, ct);
            if (category == null || category.IsDeleted || !category.IsActive)
            {
                throw new BadRequestException("Nhãn nội dung không tồn tại hoặc đã tắt.");
            }
        }

        return new Fields(cleanLabel, cleanImage!, cleanAudio, parsedAge);
    }

    private async Task EnsureLabelIsFreeAsync(WishCardGroup group, string label, int? exceptId, CancellationToken ct)
    {
        var lowered = label.ToLowerInvariant();
        var taken = await _unitOfWork.Repository<WishCard>().ExistsAsync(
            card => !card.IsDeleted
                    && card.Group == group
                    && card.Id != exceptId
                    && card.Label.ToLower() == lowered,
            ct);
        if (taken)
        {
            throw new ConflictException("Đã có thẻ cùng tên trong nhóm này.");
        }
    }

    private static object Snapshot(WishCard card) => new
    {
        group = card.Group.ToString(),
        card.Label,
        card.ImageUrl,
        card.AudioUrl,
        ageBand = card.AgeBand?.ToString(),
        card.ContentCategoryId,
        card.SortOrder,
        card.IsActive
    };

    private static WishCardDto ToDto(WishCard card) => new()
    {
        Id = card.Id,
        Group = card.Group.ToString(),
        Label = card.Label,
        ImageUrl = card.ImageUrl,
        AudioUrl = card.AudioUrl,
        AgeBand = card.AgeBand.HasValue ? RoadmapAge.ToText(card.AgeBand.Value) : null,
        ContentCategoryId = card.ContentCategoryId,
        SortOrder = card.SortOrder,
        IsActive = card.IsActive,
        CreatedAt = card.CreatedAt
    };
}
