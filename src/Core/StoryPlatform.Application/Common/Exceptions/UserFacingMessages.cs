using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Common.Exceptions;

/// <summary>
/// Đổi message kỹ thuật (mã lỗi dạng MÃ_LỖI, "MÃ_LỖI: chi tiết" hoặc câu tiếng Anh mặc định)
/// thành câu tiếng Việt thân thiện trước khi trả về người dùng. Mã gốc vẫn được ghi log.
/// </summary>
public static partial class UserFacingMessages
{
    public const string Generic = "Yêu cầu chưa thực hiện được. Bạn vui lòng thử lại; nếu vẫn lỗi, hãy tải lại trang hoặc liên hệ quản trị viên.";

    private const string Stale = "Dữ liệu vừa được cập nhật ở nơi khác. Bạn tải lại trang rồi thử lại nhé.";
    private const string Busy = "Hệ thống đang xử lý một yêu cầu trước đó. Bạn đợi một lát rồi thử lại nhé.";
    private const string Invalid = "Thông tin gửi lên chưa hợp lệ. Bạn kiểm tra lại rồi thử lại nhé.";
    private const string Missing = "Còn thiếu thông tin cần thiết. Bạn kiểm tra lại các mục bắt buộc nhé.";
    private const string NotFound = "Không tìm thấy dữ liệu yêu cầu. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập.";
    private const string Duplicate = "Yêu cầu này đã được gửi trước đó với nội dung khác. Bạn tải lại trang rồi thử lại nhé.";
    private const string UseNewScreen = "Thao tác này không dùng ở màn hình hiện tại. Bạn tải lại trang để dùng đúng chức năng nhé.";
    private const string MediaUnavailable = "Hình ảnh hoặc giọng đọc của truyện chưa sẵn sàng. Bạn đợi xử lý xong rồi thử lại nhé.";

    private static readonly Dictionary<string, string> Codes = new(StringComparer.Ordinal)
    {
        // Chung
        ["PAGINATION_INVALID"] = "Trang dữ liệu yêu cầu không hợp lệ.",
        ["INVALID_PAGE"] = "Trang dữ liệu yêu cầu không hợp lệ.",
        ["INVALID_ID"] = "Mã định danh không hợp lệ.",
        ["OPERATION_ID_INVALID"] = Invalid,
        ["OPERATION_ID_REUSED"] = Duplicate,
        ["INVALID_OPERATION_ID"] = Invalid,
        ["OPERATION_PAYLOAD_CONFLICT"] = Duplicate,
        ["RETRY_KEY_CONFLICT"] = Duplicate,
        ["HANDOFF_RETRY_KEY_CONFLICT"] = Duplicate,
        ["FIELD_NOT_EDITABLE"] = "Trường thông tin này không được phép chỉnh sửa.",
        ["FILE_REQUIRED"] = "Bạn chưa chọn tệp để tải lên.",
        ["CONSENT_REQUIRED"] = "Bạn cần đồng ý với điều khoản trước khi tiếp tục.",
        ["CONSENT_OUTDATED"] = "Điều khoản đã được cập nhật. Bạn vui lòng xem và đồng ý lại.",
        ["AGE_BAND_INVALID"] = "Độ tuổi đã chọn không hợp lệ.",
        ["LANGUAGE_CONTEXT_MISMATCH"] = "Ngôn ngữ của truyện không khớp với thiết lập hiện tại.",
        ["AI_OUTPUT_TRUNCATED"] = "AI trả về kết quả chưa đầy đủ. Bạn thử lại nhé.",

        // Truyện và phiên bản
        ["STORY_REQUIRED"] = "Bạn cần chọn một truyện.",
        ["STORY_CONTENT_REQUIRED"] = "Truyện chưa có nội dung.",
        ["CONTENT_REQUIRED"] = "Truyện chưa có nội dung.",
        ["STORY_NOT_READY"] = "Truyện chưa sẵn sàng cho thao tác này.",
        ["STORY_NOT_READY_FOR_REVIEW"] = "Truyện chưa sẵn sàng để duyệt.",
        ["STORY_NOT_PUBLISHED"] = "Truyện chưa được xuất bản.",
        ["STORY_STATUS_INVALID"] = "Trạng thái hiện tại của truyện không cho phép thao tác này.",
        ["INVALID_STORY_STATUS"] = "Trạng thái hiện tại của truyện không cho phép thao tác này.",
        ["STALE_STORY_VERSION"] = Stale,
        ["STALE_BASE_VERSION"] = Stale,
        ["STALE_PUBLISHED_VERSION"] = Stale,
        ["STALE_REPAIR_SOURCE"] = Stale,
        ["STALE_PROPOSAL_VERSION"] = Stale,
        ["STALE_JOB_RESULT"] = Stale,
        ["STORY_REVISION_CONFLICT"] = Stale,
        ["SAVED_VERSION_REQUIRED"] = "Bạn cần lưu truyện trước khi thực hiện thao tác này.",
        ["CURRENT_SAVED_VERSION_REQUIRED"] = "Bạn cần lưu bản mới nhất của truyện trước khi tiếp tục.",
        ["CURRENT_STORY_VERSION_REQUIRED"] = "Truyện chưa có phiên bản hiện tại để thực hiện thao tác này.",
        ["CURRENT_UNPUBLISHED_VERSION_REQUIRED"] = "Bạn cần tạo bản chỉnh sửa mới (chưa xuất bản) trước khi sửa truyện.",
        ["PUBLISHED_VERSION_REQUIRED"] = "Truyện cần có bản đã xuất bản cho thao tác này.",
        ["SUBMITTED_VERSION_IMMUTABLE_CREATE_REVISION"] = "Bản đã gửi duyệt không thể sửa. Bạn hãy tạo bản chỉnh sửa mới.",
        ["APPROVED_OUTLINE_REQUIRED"] = "Bạn cần duyệt dàn ý trước khi AI viết truyện.",
        ["AI_STORY_DRAFT_REQUIRED"] = "Truyện này không phải bản nháp AI.",
        ["AI_STORY_USE_GENERATION_REVIEW_API"] = UseNewScreen,
        ["GENERATION_JOB_ACTIVE"] = "AI đang xử lý truyện này. Bạn đợi xong rồi thử lại nhé.",
        ["GENERATION_REQUEST_NOT_ACCEPTED"] = "Yêu cầu tạo truyện chưa được chấp nhận.",
        ["OUTPUT_MODE_CONFLICT"] = "Chế độ đầu ra đã chọn không khớp với truyện.",
        ["CONTEXT_SNAPSHOT_MISSING"] = "Thiếu thông tin thiết lập của truyện. Bạn tải lại trang rồi thử lại nhé.",
        ["READABILITY_POLICY_CHANGED"] = "Tiêu chí độ dễ đọc vừa thay đổi. Bạn tải lại trang rồi thử lại nhé.",
        ["REVISION_NOT_REQUIRED"] = "Truyện không cần chỉnh sửa thêm.",

        // An toàn và kiểm duyệt nội dung
        ["CONTENT_SAFETY_BLOCKED"] = "Nội dung chưa an toàn cho trẻ nên đã bị chặn. Bạn hãy chỉnh lại nội dung nhé.",
        ["CONTENT_SAFETY_SCORE_NOT_MET"] = "Nội dung chưa đạt mức an toàn tối thiểu cho lứa tuổi đã chọn.",
        ["CONTENT_CHECK_NOT_PASSED"] = "Nội dung chưa qua bước kiểm tra. Bạn chỉnh lại rồi kiểm tra lại nhé.",
        ["CONTENT_MODERATION_CHECK_REQUIRED"] = "Bạn cần chạy kiểm tra nội dung trước khi tiếp tục.",
        ["CONTENT_READABILITY_REPAIR_REQUIRED"] = "Truyện cần được chỉnh cho dễ đọc hơn trước khi tiếp tục.",
        ["GRAY_ZONE_BLOCKED"] = "Nội dung đang chờ kiểm duyệt viên xem xét nên chưa tiếp tục được.",
        ["GRAY_ZONE_DECISION_INVALID"] = "Quyết định kiểm duyệt không hợp lệ.",
        ["SAFETY_POLICY_REQUIRED"] = "Chưa có chính sách an toàn cho thao tác này. Bạn liên hệ quản trị viên nhé.",
        ["SAFETY_MODERATION_REQUIRED"] = "Nội dung cần được kiểm duyệt an toàn trước khi tiếp tục.",
        ["MODERATION_NOT_CONFIGURED"] = "Dịch vụ kiểm duyệt chưa được cấu hình. Bạn liên hệ quản trị viên nhé.",
        ["MODERATION_PROVIDER_NOT_REGISTERED"] = "Dịch vụ kiểm duyệt chưa được cấu hình. Bạn liên hệ quản trị viên nhé.",
        ["MODERATION_CHECKPOINT_MISSING"] = "Thiếu kết quả kiểm duyệt. Bạn chạy kiểm tra lại nhé.",
        ["MODERATION_CONTEXT_CHANGED"] = Stale,
        ["MODERATION_SOURCE_CHANGED"] = Stale,
        ["MODERATION_SOURCE_LOCKED"] = "Nội dung đang được kiểm duyệt nên tạm thời không sửa được.",
        ["MODERATION_REVIEW_IN_PROGRESS"] = "Nội dung đang được kiểm duyệt. Bạn đợi kết quả nhé.",
        ["OPEN_MODERATION_REQUEST"] = "Truyện đang có một yêu cầu kiểm duyệt chưa xử lý xong.",
        ["STALE_MODERATION_ITEM"] = Stale,
        ["VALID_MODERATION_CLAIM_REQUIRED"] = "Bạn cần nhận mục này trước khi ra quyết định kiểm duyệt.",
        ["MODERATOR_REQUIRED"] = "Thao tác này chỉ dành cho kiểm duyệt viên.",
        ["MODERATOR_REMOVAL_REQUIRED"] = "Thao tác gỡ truyện cần kiểm duyệt viên thực hiện.",
        ["DECISION_REASON_REQUIRED"] = "Bạn cần nhập lý do cho quyết định này.",
        ["REMOVAL_REASON_REQUIRED"] = "Bạn cần nhập lý do gỡ truyện.",
        ["REMOVAL_REQUEST_PENDING"] = "Truyện đang có yêu cầu gỡ chờ xử lý.",
        ["NOT_A_REMOVAL_REQUEST"] = "Mục này không phải yêu cầu gỡ truyện.",
        ["STALE_REMOVAL_DECISION"] = Stale,

        // Rubric kiểm duyệt
        ["RUBRIC_ACCESS_DENIED"] = "Bạn không có quyền với bộ tiêu chí này.",
        ["RUBRIC_ACTIVE_REQUIRED"] = "Cần có một bộ tiêu chí kiểm duyệt đang dùng.",
        ["RUBRIC_DEFINITION_INVALID"] = "Bộ tiêu chí kiểm duyệt chưa hợp lệ.",
        ["RUBRIC_METADATA_INVALID"] = "Thông tin bộ tiêu chí chưa hợp lệ.",
        ["RUBRIC_CONTENT_INVALID"] = "Nội dung bộ tiêu chí chưa hợp lệ.",
        ["RUBRIC_QUERY_INVALID"] = Invalid,
        ["RUBRIC_EVALUATION_INPUT_INVALID"] = Invalid,
        ["RUBRIC_VERSION_CONFLICT"] = Stale,
        ["RUBRIC_REVISION_STALE"] = Stale,
        ["RUBRIC_VERSION_EXISTS"] = "Phiên bản bộ tiêu chí này đã tồn tại.",
        ["RUBRIC_VERSION_NUMBER_IMMUTABLE"] = "Không thể đổi số phiên bản của bộ tiêu chí.",
        ["RUBRIC_IMMUTABLE"] = "Bộ tiêu chí đã dùng không thể sửa. Bạn hãy tạo phiên bản mới.",

        // Creator và xuất bản
        ["CREATOR_CONTEXT_INVALID"] = "Thiết lập tạo truyện chưa hợp lệ.",
        ["CREATOR_CONTEXT_MISSING"] = "Thiếu thiết lập tạo truyện. Bạn tải lại trang rồi thử lại nhé.",
        ["CREATOR_AUDIENCE_INVALID"] = "Độ tuổi hoặc trình độ đọc đã chọn không hợp lệ.",
        ["CREATOR_GENERATION_POLICY_INVALID"] = "Chính sách tạo truyện chưa hợp lệ. Bạn liên hệ quản trị viên nhé.",
        ["CREATOR_PUBLICATION_LOCKED"] = "Truyện đang trong quá trình xuất bản nên tạm thời không sửa được.",
        ["CREATOR_REVIEW_SERVICE_REQUIRED"] = UseNewScreen,

        // Tự viết, gói truyện của phụ huynh
        ["AUTHORING_ACCESS_REQUIRED"] = "Tài khoản của bạn chưa được dùng chức năng tự viết truyện.",
        ["AUTHORING_JOB_ACTIVE"] = Busy,
        ["AUTHORING_PIPELINE_CONFLICT"] = Stale,
        ["AUTHORING_SOURCE_CHANGED"] = Stale,
        ["MANUAL_CONTENT_INCOMPLETE"] = "Truyện còn thiếu nội dung. Bạn điền đủ các phần rồi thử lại nhé.",
        ["MANUAL_CONTENT_INVALID"] = "Nội dung truyện chưa hợp lệ. Bạn kiểm tra lại nhé.",
        ["WRITING_PURPOSE_INVALID"] = "Mục đích viết truyện chưa hợp lệ.",
        ["EXPERIENCE_MODE_INVALID"] = "Chế độ trải nghiệm đã chọn không hợp lệ.",
        ["PLACEHOLDER_VALUES_INVALID"] = "Các ô điền trong mẫu chưa hợp lệ. Bạn kiểm tra lại nhé.",
        ["PARENT_MANUAL_USE_AUTHORING_API"] = UseNewScreen,
        ["PARENT_USE_PACKAGE_API"] = UseNewScreen,
        ["PARENT_STORY_FINALIZED"] = "Truyện đã hoàn tất nên không sửa được nữa.",
        ["PARENT_STORY_CHILD_REQUIRED"] = "Bạn cần chọn bé cho truyện này.",
        ["PARENT_SELF_CHECK_REQUIRED"] = "Bạn cần tự kiểm tra nội dung trước khi tiếp tục.",
        ["PARENT_CONTENT_REOPEN_REQUIRED"] = "Bạn cần mở lại nội dung để chỉnh sửa trước.",
        ["PARENT_CONTENT_FINALIZATION_REQUIRED"] = "Bạn cần hoàn tất nội dung truyện trước khi tiếp tục.",
        ["PARENT_PACKAGE_NOT_FOUND"] = "Không tìm thấy gói truyện.",
        ["PARENT_PACKAGE_SOURCE_CHANGED"] = Stale,
        ["PARENT_BATCH_NOT_READY"] = "Một số truyện trong gói chưa sẵn sàng để gửi.",
        ["LEGACY_FAMILY_PACKAGE"] = "Gói truyện này được tạo bằng phiên bản cũ nên không hỗ trợ thao tác này.",
        ["CONFIRMED_CONTENT_PACKAGE_IMMUTABLE"] = "Gói truyện đã xác nhận nên không sửa được nữa.",
        ["PACKAGE_IMMUTABLE"] = "Gói truyện đã xác nhận nên không sửa được nữa.",
        ["PACKAGE_COMPONENTS_REQUIRED"] = "Bạn cần chọn ít nhất một thành phần cho gói truyện.",
        ["PACKAGE_COMPONENT_INVALID"] = "Thành phần đã chọn cho gói truyện không hợp lệ.",
        ["PACKAGE_RETRY_COMPONENT_INVALID"] = "Thành phần cần làm lại không hợp lệ.",
        ["PACKAGE_RETRY_NOT_AVAILABLE"] = "Gói truyện hiện không thể làm lại.",
        ["PACKAGE_CONTEXT_INVALID"] = "Thiết lập gói truyện chưa hợp lệ.",
        ["PACKAGE_MANIFEST_INVALID"] = "Dữ liệu gói truyện chưa hợp lệ. Bạn tạo lại gói nhé.",
        ["PACKAGE_RECIPIENTS_INVALID"] = "Danh sách bé nhận truyện chưa hợp lệ.",
        ["PACKAGE_CHECK_NOT_PASSED"] = "Gói truyện chưa qua bước kiểm tra.",
        ["PACKAGE_ASSESSMENT_ACKNOWLEDGEMENT_REQUIRED"] = "Bạn cần xác nhận đã đọc kết quả đánh giá trước khi tiếp tục.",
        ["PACKAGE_CONFIRMATION_RECEIPT_INVALID"] = "Xác nhận gói truyện không hợp lệ. Bạn tải lại trang rồi thử lại nhé.",
        ["PACKAGE_CONFIRMATION_RECEIPT_MISSING"] = "Gói truyện chưa được xác nhận.",
        ["PACKAGE_JOB_ACTIVE"] = Busy,
        ["PACKAGE_JOB_ALREADY_EXISTS"] = "Gói truyện này đang được tạo.",
        ["PACKAGE_LEARNING_INCOMPLETE"] = "Học liệu của gói truyện chưa hoàn tất.",
        ["PACKAGE_LISTENING_MEDIA_INCOMPLETE"] = "Âm thanh luyện nghe của gói truyện chưa hoàn tất.",
        ["PACKAGE_MEDIA_UNAVAILABLE"] = MediaUnavailable,
        ["PACKAGE_NOT_READY"] = "Gói truyện chưa sẵn sàng.",
        ["PACKAGE_OPERATION_KEY_CONFLICT"] = Duplicate,
        ["PACKAGE_REVISION_CHANGED"] = Stale,
        ["PREPARATION_NOT_READY"] = "Truyện chưa chuẩn bị xong. Bạn đợi một lát nhé.",

        // Bé và lộ trình
        ["CHILD_REQUIRED"] = "Bạn cần chọn bé.",
        ["CHILD_NOT_ACTIVE"] = "Hồ sơ của bé đang tạm ngưng.",
        ["CHILD_CONTEXT_REQUIRED"] = "Bạn cần chọn bé trước khi tiếp tục.",
        ["CHILD_CONTEXT_MISMATCH"] = "Truyện không thuộc về bé đang chọn.",
        ["CHILD_CONTEXT_CHANGED"] = Stale,
        ["CHILD_POLICY_INVALID"] = "Thiết lập của bé chưa hợp lệ.",
        ["CHILD_ALREADY_HAS_GRANT"] = "Bé đã được cấp quyền này rồi.",
        ["GRANT_ISSUER_INACTIVE"] = "Tài khoản cấp quyền đang tạm ngưng.",

        // Học liệu và luyện nghe
        ["LEARNING_ARTIFACT_NOT_ENABLED"] = "Học liệu chưa được bật cho truyện này.",
        ["LEARNING_BATCH_ACTIVE"] = "AI đang tạo học liệu. Bạn đợi xong rồi thử lại nhé.",
        ["LEARNING_JOB_ACTIVE"] = "AI đang tạo học liệu. Bạn đợi xong rồi thử lại nhé.",
        ["LEARNING_BATCH_INCOMPLETE"] = "Học liệu chưa tạo xong.",
        ["LEARNING_BATCH_NOT_FOUND"] = "Không tìm thấy lượt tạo học liệu.",
        ["LEARNING_BATCH_RETRY_EXHAUSTED"] = "Đã hết số lần thử lại tạo học liệu cho truyện này.",
        ["LEARNING_JOB_NOT_FAILED"] = "Học liệu không bị lỗi nên không cần thử lại.",
        ["LEARNING_PROMPT_CATALOG_REQUIRED"] = "Chưa có cấu hình tạo học liệu. Bạn liên hệ quản trị viên nhé.",
        ["LEARNING_RETRY_KEY_CONFLICT"] = Duplicate,
        ["LEARNING_STORY_CONCURRENCY_LIMIT"] = "Đang có quá nhiều truyện tạo học liệu cùng lúc. Bạn đợi một lát rồi thử lại nhé.",
        ["LEARNING_EVIDENCE_NOT_IN_STORY"] = "Câu hỏi hoặc từ vựng tham chiếu đoạn không có trong truyện.",
        ["VOCABULARY_EVIDENCE_MISMATCH"] = "Từ vựng không khớp với nội dung truyện.",
        ["QUIZ_OPTION_FEEDBACK_INVALID"] = "Phản hồi của các đáp án câu đố chưa hợp lệ.",
        ["STALE_LEARNING_SOURCE"] = Stale,
        ["STALE_LEARNING_CONTEXT"] = Stale,
        ["STALE_LEARNING_BATCH"] = Stale,
        ["STALE_LEARNING_JOB"] = Stale,
        ["LISTENING_NOT_SUPPORTED"] = "Truyện này chưa hỗ trợ luyện nghe.",
        ["LISTENING_CONTEXT_MISSING"] = "Thiếu thiết lập luyện nghe cho truyện.",
        ["LISTENING_OPTION_NOT_FOUND"] = "Không tìm thấy lựa chọn luyện nghe.",
        ["LISTENING_PROPOSAL_INVALID"] = "Đề xuất luyện nghe chưa hợp lệ.",
        ["LISTENING_PACKAGE_INVALID"] = "Gói luyện nghe chưa hợp lệ.",
        ["LISTENING_PLAN_INFEASIBLE"] = "Truyện chưa đủ nội dung để tạo bài luyện nghe.",
        ["LISTENING_TYPE_NOT_IN_PLAN"] = "Dạng bài luyện nghe này không có trong kế hoạch của truyện.",
        ["LISTENING_TEXT_INVALID"] = "Nội dung luyện nghe chưa hợp lệ.",
        ["SEGMENT_BOUNDARY_INVALID"] = "Cách chia đoạn của truyện chưa hợp lệ.",
        ["SEGMENT_COVERAGE_INVALID"] = "Các đoạn chưa bao phủ hết nội dung truyện.",
        ["INVALID_PERSISTED_SCENE_COVERAGE"] = "Dữ liệu cảnh của truyện chưa hợp lệ. Bạn thử tạo lại nhé.",

        // Hình ảnh và giọng đọc
        ["MEDIA_JOB_ACTIVE"] = "Hình ảnh và giọng đọc đang được tạo. Bạn đợi xong rồi thử lại nhé.",
        ["MEDIA_JOB_ALREADY_EXISTS"] = "Hình ảnh và giọng đọc đang được tạo.",
        ["MEDIA_JOB_NOT_FAILED"] = "Việc tạo hình ảnh và giọng đọc không bị lỗi nên không cần thử lại.",
        ["MEDIA_JOB_RETRY_NOT_ALLOWED"] = "Hiện chưa thể thử lại việc tạo hình ảnh và giọng đọc.",
        ["MEDIA_ALREADY_EXISTS_USE_REGENERATE"] = "Truyện đã có hình ảnh và giọng đọc. Bạn dùng chức năng Tạo lại nhé.",
        ["MEDIA_REGENERATION_NOT_ALLOWED"] = "Hiện chưa thể tạo lại hình ảnh và giọng đọc.",
        ["MEDIA_CONFIGURATION_CHANGED"] = Stale,
        ["MEDIA_CONTEXT_REQUIRED"] = "Thiếu thiết lập tạo hình ảnh và giọng đọc.",
        ["MEDIA_MODE_INVALID"] = "Cách tạo hình ảnh đã chọn không hợp lệ.",
        ["MEDIA_MODE_ALREADY_SELECTED"] = "Bạn đã chọn cách tạo hình ảnh cho truyện này rồi.",
        ["MEDIA_TARGET_ALREADY_SELECTED"] = "Mục này đã được chọn rồi.",
        ["MEDIA_FILE_REQUIRED"] = "Bạn chưa chọn tệp hình ảnh hoặc âm thanh.",
        ["MEDIA_FILE_TYPE_INVALID"] = "Định dạng tệp chưa được hỗ trợ. Bạn chọn tệp ảnh hoặc âm thanh hợp lệ nhé.",
        ["MEDIA_FILE_SIZE_INVALID"] = "Dung lượng tệp vượt quá giới hạn cho phép.",
        ["MEDIA_RIGHTS_DECLARATION_REQUIRED"] = "Bạn cần cam kết quyền sử dụng hình ảnh hoặc âm thanh trước khi tải lên.",
        ["MEDIA_STORAGE_PATH_REQUIRED"] = "Không lưu được tệp. Bạn thử lại nhé.",
        ["MEDIA_SERVICE_NOT_CONFIGURED"] = "Dịch vụ tạo hình ảnh và giọng đọc chưa được cấu hình. Bạn liên hệ quản trị viên nhé.",
        ["MEDIA_NOT_IN_CURRENT_VERSION"] = "Hình ảnh hoặc âm thanh này không thuộc phiên bản hiện tại của truyện.",
        ["MEDIA_OPERATION_ID_CONFLICT"] = Duplicate,
        ["MEDIA_OPERATION_ID_REQUIRED"] = Invalid,
        ["MEDIA_PACKAGE_CONFIRMATION_REQUIRED"] = "Bạn cần xác nhận hình ảnh và giọng đọc trước khi tiếp tục.",
        ["MEDIA_PACKAGE_INCOMPLETE"] = MediaUnavailable,
        ["MEDIA_PACKAGE_NOT_AWAITING_CONFIRMATION"] = "Hình ảnh và giọng đọc hiện không ở bước chờ xác nhận.",
        ["MEDIA_PREPARATION_REQUIRED"] = "Bạn cần chuẩn bị truyện trước khi tạo hình ảnh và giọng đọc.",
        ["MEDIA_PREVIOUS_ASSET_UNAVAILABLE"] = "Không tìm thấy hình ảnh hoặc âm thanh trước đó.",
        ["MEDIA_REPLACEMENT_NOT_FAILED"] = "Mục này không bị lỗi nên không cần thay.",
        ["MEDIA_REPLACEMENT_UNRESOLVED"] = "Còn hình ảnh hoặc âm thanh bị lỗi chưa được thay.",
        ["STALE_MEDIA_PACKAGE"] = Stale,
        ["STALE_MEDIA_RESULT"] = Stale,
        ["INVALID_MEDIA_HANDOFF"] = "Dữ liệu hình ảnh và giọng đọc chưa hợp lệ. Bạn thử tạo lại nhé.",
        ["CONTENT_TOO_SHORT"] = "Tệp tải lên bị hỏng hoặc quá nhỏ.",
        ["UNRECOGNIZED_IMAGE_HEADER"] = "Tệp ảnh không đúng định dạng.",
        ["UNRECOGNIZED_AUDIO_HEADER"] = "Tệp âm thanh không đúng định dạng.",
        ["AUDIO_CONTENT_EMPTY"] = "Tệp âm thanh không có nội dung.",
        ["AUDIO_MAGIC_BYTES_INVALID"] = "Tệp âm thanh không đúng định dạng.",
        ["AUDIO_WORD_TIMINGS_MISSING"] = "Giọng đọc thiếu thông tin canh chữ. Bạn thử tạo lại nhé.",

        // Hướng dẫn viết (guide)
        ["GUIDE_CATALOG_UNAVAILABLE"] = "Chưa tải được danh sách hướng dẫn viết. Bạn thử lại sau ít phút nhé.",
        ["GUIDE_CATALOG_EMPTY"] = "Chưa có hướng dẫn viết nào.",
        ["GUIDE_CATALOG_CHANGED"] = Stale,
        ["GUIDE_REVISION_CONFLICT"] = Stale,
        ["GUIDE_REVISION_TOKEN_REQUIRED"] = Stale,
        ["GUIDE_SOURCE_VERSION_INVALID"] = Stale,
        ["GUIDE_DRAFT_EXISTS"] = "Đã có một bản nháp hướng dẫn đang soạn.",
        ["GUIDE_DRAFT_NOT_FOUND"] = "Không tìm thấy bản nháp hướng dẫn.",
        ["GUIDE_ITEM_NOT_FOUND"] = "Không tìm thấy mục hướng dẫn.",
        ["GUIDE_FIELD_REQUIRED"] = Missing,
        ["GUIDE_TEMPLATE_REQUIRED"] = "Mục hướng dẫn cần có mẫu câu.",
        ["GUIDE_HAS_NO_TEMPLATE"] = "Mục hướng dẫn này không có mẫu câu.",
        ["GUIDE_QUESTION_HAS_TEMPLATE"] = "Mục dạng câu hỏi không được có mẫu câu.",
        ["GUIDE_PLACEHOLDERS_MISMATCH"] = "Các ô điền trong mẫu câu không khớp.",
        ["GUIDE_PLACEHOLDER_LABEL_INVALID"] = "Tên ô điền trong mẫu câu chưa hợp lệ.",
        ["GUIDE_ID_DUPLICATED"] = "Mã mục hướng dẫn bị trùng.",
        ["GUIDE_ID_INVALID"] = "Mã mục hướng dẫn chưa hợp lệ.",
        ["GUIDE_AUDIENCE_INVALID"] = "Đối tượng của hướng dẫn chưa hợp lệ.",
        ["GUIDE_GROUP_INVALID"] = "Nhóm hướng dẫn chưa hợp lệ.",
        ["GUIDE_ITEM_TYPE_INVALID"] = "Loại mục hướng dẫn chưa hợp lệ.",
        ["GUIDE_PURPOSE_INVALID"] = "Mục đích của hướng dẫn chưa hợp lệ.",

        // Điều ước và thẻ điều ước
        ["WISH_ALREADY_HANDLED"] = "Điều ước này đã được xử lý rồi.",
        ["WISH_CHILD_MISMATCH"] = "Điều ước không thuộc về bé đang chọn.",
        ["WISH_GENERATION_NOT_STARTED"] = "Truyện cho điều ước này chưa được bắt đầu tạo.",
        ["WISH_GENERATION_REQUEST_INVALID"] = Invalid,
        ["WISH_GENERATION_LINK_CONFLICT"] = "Điều ước này đã gắn với một truyện khác.",
        ["WISH_REQUIRES_NEW_PARENT_STORY"] = "Điều ước này cần một truyện mới do phụ huynh tạo.",
        ["WISH_STORY_NOT_SUGGESTED"] = "Truyện này không nằm trong danh sách gợi ý cho điều ước.",
        ["WISH_CARD_CATALOG_TOO_LARGE"] = "Danh sách thẻ điều ước quá lớn.",
        ["WISH_CARD_MAPPING_STALE"] = Stale,
        ["WISH_CARD_REQUEST_INVALID"] = Invalid,
        ["WISH_CARD_SOURCE_STALE"] = Stale,

        // Trợ lý viết của Creator
        ["EDITOR_CONTEXT_STALE"] = "Nội dung trong trình soạn thảo vừa thay đổi. Bạn thử lại với nội dung mới nhé.",
        ["TASK_REVISION_CONFLICT"] = Stale,
        ["TASK_CLOSED"] = "Phiên trợ lý này đã đóng.",
        ["TASK_BUSY"] = "Trợ lý đang xử lý yêu cầu trước. Bạn đợi một lát nhé.",
        ["INVALID_TASK_STATE"] = "Phiên trợ lý không ở trạng thái phù hợp cho thao tác này.",
        ["OPERATION_NOT_RUNNING"] = "Không có thao tác nào đang chạy.",
        ["INVALID_QUESTION"] = "Câu hỏi không hợp lệ.",
        ["INVALID_CHOICE"] = "Lựa chọn không hợp lệ.",
        ["INVALID_ANSWER"] = "Câu trả lời không hợp lệ.",
        ["INVALID_ANSWER_REFERENCE"] = "Câu trả lời không khớp với câu hỏi.",
        ["INVALID_OPTION"] = "Lựa chọn không hợp lệ.",
        ["INVALID_DIRECTION"] = "Hướng chỉnh sửa không hợp lệ.",
        ["INVALID_CONSTRAINTS"] = "Yêu cầu chỉnh sửa chưa hợp lệ.",
        ["INVALID_CONTEXT"] = "Nội dung gửi cho trợ lý chưa hợp lệ.",
        ["CONTEXT_LIMIT_EXCEEDED"] = "Nội dung gửi cho trợ lý quá dài. Bạn chọn đoạn ngắn hơn nhé.",
        ["INVALID_REVIEW_REFERENCE"] = "Góp ý tham chiếu không còn hợp lệ.",
        ["INVALID_REVIEW_TARGET"] = "Đoạn được góp ý không còn hợp lệ.",
        ["ASSISTANCE_RATE_LIMIT"] = "Bạn đang gửi yêu cầu cho trợ lý quá nhanh. Bạn đợi một lát rồi thử lại nhé.",
        ["ASSISTANCE_DAILY_LIMIT"] = "Bạn đã dùng hết lượt trợ lý trong hôm nay. Hẹn bạn ngày mai nhé.",
        ["ASSISTANCE_ACCESS_REVOKED"] = "Bạn không còn quyền dùng trợ lý cho truyện này.",

        // Báo cáo, tạo truyện AI
        ["AI_STORY_CONCURRENCY_LIMIT"] = "Bạn đang tạo 2 truyện. Hãy hoàn tất một truyện trước khi bắt đầu truyện khác.",
    };

    /// <summary>Message đã thân thiện (có tiếng Việt) thì giữ nguyên; mã lỗi / câu tiếng Anh được dịch.</summary>
    public static string Translate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return Generic;
        var text = message.Trim();
        if (VietnamesePattern().IsMatch(text) && !CodePrefixPattern().IsMatch(text)) return text;

        var match = CodePrefixPattern().Match(text);
        if (match.Success)
        {
            var code = match.Groups["code"].Value;
            var detail = match.Groups["detail"].Value.Trim();
            if (detail.Length > 0 && VietnamesePattern().IsMatch(detail)) return detail;
            if (Codes.TryGetValue(code, out var known)) return known;
            return ByShape(code);
        }
        return Generic;
    }

    /// <summary>Lỗi kiểm tra dữ liệu đầu vào: giữ câu tiếng Việt, đổi câu mặc định tiếng Anh của ASP.NET.</summary>
    public static string TranslateValidation(string message)
    {
        var text = message.Trim();
        if (VietnamesePattern().IsMatch(text)) return text;
        if (text.Contains("is required", StringComparison.OrdinalIgnoreCase)) return Missing;
        if (text.Contains("must be between", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("maximum length", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("minimum length", StringComparison.OrdinalIgnoreCase))
            return "Có thông tin vượt quá giới hạn cho phép (quá dài, quá ngắn hoặc ngoài khoảng). Bạn kiểm tra lại nhé.";
        if (text.Contains("JSON", StringComparison.OrdinalIgnoreCase) || text.Contains("could not be converted", StringComparison.OrdinalIgnoreCase))
            return "Dữ liệu gửi lên không đúng định dạng. Bạn tải lại trang rồi thử lại nhé.";
        return Translate(text) == Generic ? Invalid : Translate(text);
    }

    /// <summary>Mã lỗi gốc (để ghi log / trả kèm trong errors), null nếu message không phải mã.</summary>
    public static string? CodeOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = CodePrefixPattern().Match(message.Trim());
        return match.Success ? match.Groups["code"].Value : null;
    }

    private static string ByShape(string code) =>
        code.StartsWith("STALE_", StringComparison.Ordinal) || code.EndsWith("_CHANGED", StringComparison.Ordinal) ||
        code.EndsWith("_STALE", StringComparison.Ordinal) ? Stale :
        code.EndsWith("_CONFLICT", StringComparison.Ordinal) ? Duplicate :
        code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) ? NotFound :
        code.EndsWith("_ACTIVE", StringComparison.Ordinal) || code.EndsWith("_IN_PROGRESS", StringComparison.Ordinal) ? Busy :
        code.EndsWith("_REQUIRED", StringComparison.Ordinal) || code.EndsWith("_MISSING", StringComparison.Ordinal) ? Missing :
        code.EndsWith("_INVALID", StringComparison.Ordinal) || code.StartsWith("INVALID_", StringComparison.Ordinal) ? Invalid :
        Generic;

    [GeneratedRegex(@"^(?<code>[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)(?:\s*:\s*(?<detail>.*))?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CodePrefixPattern();

    [GeneratedRegex(@"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VietnamesePattern();
}
