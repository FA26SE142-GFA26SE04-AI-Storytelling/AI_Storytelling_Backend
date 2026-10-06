using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Api.Controllers;

public class SupervisionController : BaseApiController
{
    private readonly ISupervisionService _supervisionService;

    public SupervisionController(ISupervisionService supervisionService)
    {
        _supervisionService = supervisionService;
    }

    /// <summary>
    /// Tạo lời mời giám sát cho một hồ sơ trẻ (Bước 1.5).
    /// </summary>
    [HttpPost("{childProfileId:int}/invitations")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<InvitationDto>>> CreateInvitation(
        int childProfileId,
        [FromBody] CreateInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.CreateInvitationAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<InvitationDto>.Ok(result, "Tạo lời mời giám sát thành công."));
    }

    /// <summary>
    /// Danh sách toàn bộ lời mời giám sát (mọi trạng thái) của 1 hồ sơ trẻ.
    /// </summary>
    [HttpGet("{childProfileId:int}/invitations")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<List<InvitationDto>>>> ListInvitations(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListInvitationsAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách lời mời giám sát thành công.");
    }

    /// <summary>
    /// Nhập mã mời để nhận OTP gửi tới email được mời (Bước 1.6, BR-1.4).
    /// </summary>
    [HttpPost("invitations/claim")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<ClaimInvitationResultDto>>> ClaimInvitation(
        [FromBody] ClaimInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ClaimInvitationAsync(
            request.InvitationCode, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Đã gửi mã OTP tới email được mời.");
    }

    /// <summary>
    /// Xác thực OTP và xem thông tin hồ sơ ở phạm vi cho phép trước khi Accept/Reject.
    /// </summary>
    [HttpPost("invitations/verify-otp")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<InvitationPreviewDto>>> VerifyInvitationOtp(
        [FromBody] VerifyInvitationOtpRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.VerifyInvitationOtpAsync(
            request.InvitationCode, request.Otp, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Xác thực OTP thành công.");
    }

    /// <summary>
    /// Từ chối lời mời giám sát (sau khi đã xác thực OTP).
    /// </summary>
    [HttpPost("invitations/reject")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> RejectInvitation(
        [FromBody] RejectInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        await _supervisionService.RejectInvitationAsync(
            request.InvitationCode, GetCurrentUserId(), cancellationToken);

        return HandleResult<object?>(null, "Đã từ chối lời mời giám sát.");
    }

    /// <summary>
    /// Chấp nhận lời mời giám sát — yêu cầu đã xác thực OTP (Bước 1.6); cấp preset quyền mặc định.
    /// </summary>
    [HttpPost("invitations/accept")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<SupervisionRelationshipDto>>> AcceptInvitation(
        [FromBody] AcceptInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.AcceptInvitationAsync(
            request.InvitationCode, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Chấp nhận lời mời giám sát thành công.");
    }

    /// <summary>
    /// Huỷ 1 lời mời giám sát còn đang Pending.
    /// </summary>
    [HttpDelete("invitations/{invitationId:int}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> CancelInvitation(
        int invitationId, CancellationToken cancellationToken)
    {
        await _supervisionService.CancelInvitationAsync(invitationId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Huỷ lời mời giám sát thành công.");
    }

    /// <summary>
    /// Danh sách người đang giám sát hiệu lực (chưa bị thu hồi) của 1 hồ sơ trẻ.
    /// </summary>
    [HttpGet("{childProfileId:int}/relationships")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<List<SupervisionRelationshipDto>>>> ListSupervisors(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListSupervisorsAsync(childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách người giám sát thành công.");
    }

    /// <summary>
    /// Thu hồi một quan hệ giám sát (Bước 1.6b) — chỉ Owner được thực hiện.
    /// </summary>
    [HttpDelete("relationships/{relationshipId:int}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeSupervision(
        int relationshipId,
        CancellationToken cancellationToken)
    {
        await _supervisionService.RevokeSupervisionAsync(
            relationshipId, GetCurrentUserId(), cancellationToken);

        return HandleResult<object?>(null, "Thu hồi quan hệ giám sát thành công.");
    }

    /// <summary>
    /// Owner gửi yêu cầu chuyển quyền Owner cho một Additional Supervisor (Bước 1.7b);
    /// yêu cầu hết hạn sau 7 ngày.
    /// </summary>
    [HttpPost("{childProfileId:int}/transfer-ownership")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<OwnershipTransferRequestDto>>> RequestOwnershipTransfer(
        int childProfileId,
        [FromBody] TransferOwnershipRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _supervisionService.RequestOwnershipTransferAsync(
            childProfileId, GetCurrentUserId(), request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<OwnershipTransferRequestDto>.Ok(
                result, "Tạo yêu cầu chuyển nhượng quyền Owner thành công."));
    }

    /// <summary>
    /// Danh sách yêu cầu chuyển nhượng quyền Owner của một hồ sơ trẻ.
    /// </summary>
    [HttpGet("{childProfileId:int}/ownership-transfers")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<List<OwnershipTransferRequestDto>>>> ListOwnershipTransfers(
        int childProfileId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListOwnershipTransferRequestsAsync(
            childProfileId, GetCurrentUserId(), cancellationToken);
        return HandleResult(
            result, "Lấy danh sách yêu cầu chuyển nhượng quyền Owner thành công.");
    }

    /// <summary>
    /// Người được đề nghị chấp nhận yêu cầu chuyển nhượng quyền Owner — thực hiện đổi role ngay.
    /// </summary>
    [HttpPost("ownership-transfers/{ownershipTransferRequestId:int}/accept")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<SupervisionRelationshipDto>>> AcceptOwnershipTransfer(
        int ownershipTransferRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.AcceptOwnershipTransferAsync(
            ownershipTransferRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Chấp nhận chuyển nhượng quyền Owner thành công.");
    }

    /// <summary>
    /// Người được đề nghị từ chối yêu cầu chuyển nhượng quyền Owner.
    /// </summary>
    [HttpPost("ownership-transfers/{ownershipTransferRequestId:int}/reject")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<OwnershipTransferRequestDto>>> RejectOwnershipTransfer(
        int ownershipTransferRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.RejectOwnershipTransferAsync(
            ownershipTransferRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Từ chối chuyển nhượng quyền Owner thành công.");
    }

    /// <summary>
    /// Owner thu hồi yêu cầu chuyển quyền Owner đang chờ phản hồi.
    /// </summary>
    [HttpPost("ownership-transfers/{ownershipTransferRequestId:int}/cancel")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<OwnershipTransferRequestDto>>> CancelOwnershipTransfer(
        int ownershipTransferRequestId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.CancelOwnershipTransferAsync(
            ownershipTransferRequestId, GetCurrentUserId(), cancellationToken);

        return HandleResult(result, "Đã thu hồi yêu cầu chuyển quyền Owner.");
    }

    /// <summary>
    /// Danh sách permission đã cấp cho 1 Additional Supervisor. Chỉ Owner xem được.
    /// </summary>
    [HttpGet("relationships/{relationshipId:int}/permissions")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<List<string>>>> ListPermissions(
        int relationshipId, CancellationToken cancellationToken)
    {
        var result = await _supervisionService.ListPermissionsAsync(relationshipId, GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách quyền đã cấp thành công.");
    }

    /// <summary>
    /// Cấp 1 permission cho Additional Supervisor (Bước 1.7, Mục 3 Bước D).
    /// </summary>
    [HttpPost("relationships/{relationshipId:int}/permissions/{permission}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> GrantPermission(
        int relationshipId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        await _supervisionService.GrantPermissionAsync(
            relationshipId, GetCurrentUserId(), permission, cancellationToken);

        return HandleResult<object?>(null, "Cấp quyền thành công.");
    }

    /// <summary>
    /// Thu hồi 1 permission cụ thể của Additional Supervisor (Bước 1.7, Mục 3 Bước F).
    /// </summary>
    [HttpDelete("relationships/{relationshipId:int}/permissions/{permission}")]
    [Authorize(Roles = "Parent")]
    public async Task<ActionResult<ApiResponse<object?>>> RevokePermission(
        int relationshipId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        await _supervisionService.RevokePermissionAsync(
            relationshipId, GetCurrentUserId(), permission, cancellationToken);

        return HandleResult<object?>(null, "Thu hồi quyền thành công.");
    }
}
