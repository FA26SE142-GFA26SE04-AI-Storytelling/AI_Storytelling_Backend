-- 84. event_rule_versions - demo fixtures for the event lifecycle.
INSERT INTO event_rule_versions (
    "Id", "EventId", "VersionNo", "Revision", "ContentHash", "RulesText", "CompletionMode", "IsActive", "ActivatedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1, 1, md5('event-demo-rule-1001'), 'Gia đình đọc truyện và cùng thảo luận. Phiếu chỉ tính sau khi đọc đúng phiên bản dự thi.', 'AllMandatory', true, NOW() - INTERVAL '12 days', NOW() - INTERVAL '12 days', false)
ON CONFLICT ("Id") DO NOTHING;
