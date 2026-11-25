-- 81. event_moderator_assignments - demo fixtures for the event lifecycle.
INSERT INTO event_moderator_assignments (
    "Id", "EventId", "ModeratorId", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 11, NOW() - INTERVAL '12 days', false)
ON CONFLICT ("Id") DO NOTHING;
