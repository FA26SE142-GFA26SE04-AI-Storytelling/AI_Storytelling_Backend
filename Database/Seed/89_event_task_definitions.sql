-- 85. event_task_definitions - demo fixtures for the event lifecycle.
INSERT INTO event_task_definitions (
    "Id", "EventRuleVersionId", "TaskCode", "TargetTrackType", "Title", "Description", "SortOrder", "VerifierType", "TargetValue", "IsMandatory", "Points", "AllowMultiplePerStory", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 'T01', 'Parent', 'Thảo luận truyện', 'Cùng bé thảo luận một truyện trong kho đọc.', 1, 'GuidedDiscussionCheckin', 1, true, 10, false, NOW() - INTERVAL '12 days', false)
ON CONFLICT ("Id") DO NOTHING;
