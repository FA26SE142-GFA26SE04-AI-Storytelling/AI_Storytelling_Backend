-- 93. reward_fulfillments - demo fixtures for the event lifecycle.
INSERT INTO reward_fulfillments (
    "Id", "EventAwardId", "AttemptNo", "Status", "HandledByUserId", "DeliveredAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1, 'Delivered', 11, NOW() - INTERVAL '1 days', NOW() - INTERVAL '29 hours', false)
ON CONFLICT ("Id") DO NOTHING;
