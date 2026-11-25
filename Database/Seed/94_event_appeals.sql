-- 90. event_appeals - demo fixtures for the event lifecycle.
INSERT INTO event_appeals (
    "Id", "EventId", "ParticipationId", "AppellantUserId", "ReasonText", "Status", "ReviewedByUserId", "ResolutionNote", "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1004, 3, 'Đề nghị kiểm tra lại tiến độ.', 'Rejected', 11, 'Chưa có xác nhận thảo luận trong kỳ.', NOW() - INTERVAL '36 hours', NOW() - INTERVAL '40 hours', false)
ON CONFLICT ("Id") DO NOTHING;
