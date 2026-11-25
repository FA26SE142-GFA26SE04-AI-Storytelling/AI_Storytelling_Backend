-- 89. event_task_progress - demo fixtures for the event lifecycle.
INSERT INTO event_task_progress (
    "Id", "ParticipationId", "TaskDefinitionId", "ChildProfileId", "StoryId", "SelfDeclaredPayloadJson", "ActivityLocalDate", "CompletedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1002, 1001, 1, 13, '{"confirmed":true}', ((NOW() - INTERVAL '4 days') AT TIME ZONE 'Asia/Ho_Chi_Minh')::date, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false)
ON CONFLICT ("Id") DO NOTHING;
