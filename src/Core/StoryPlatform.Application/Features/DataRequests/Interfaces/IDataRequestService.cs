using StoryPlatform.Application.Features.DataRequests.DTOs;

namespace StoryPlatform.Application.Features.DataRequests.Interfaces;

/// <summary>
/// Xử lý yêu cầu xuất/xóa dữ liệu cá nhân trẻ (Bước 5.4).
/// </summary>
public interface IDataRequestService
{
    Task<DataRequestDto> CreateAsync(
        int ownerUserId, CreateDataRequestDto request, CancellationToken cancellationToken = default);

    Task<List<DataRequestDto>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<ExportedFileDto> ResolveExportAsync(
        int adminUserId, int dataRequestId, string format = "xlsx", CancellationToken cancellationToken = default);

    Task<DataRequestDto> ResolveDeleteAsync(
        int adminUserId, int dataRequestId, CancellationToken cancellationToken = default);
}
