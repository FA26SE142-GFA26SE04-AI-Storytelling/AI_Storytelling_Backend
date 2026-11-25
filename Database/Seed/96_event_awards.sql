-- 92. event_awards - demo fixtures for the event lifecycle.
INSERT INTO event_awards (
    "Id", "EventId", "EventActivityId", "ResultSource", "TargetAudience", "RankOrTier", "SlotIndex", "RewardType", "RewardName", "UnitValueVnd", "CreditGrantAmount", "SubmitCreditGrantAmount", "ParticipationId", "ReallocationRound", "Status", "ClaimDeadlineAt", "ApprovedByAdminId", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1002, 'MissionCompletion', 'Parent', 'Gia đình đọc truyện', 1, 'PhysicalGift', 'Sách mẫu', 0, 0, 0, 1002, 0, 'Fulfilled', NOW() + INTERVAL '1 day', 1, NOW() - INTERVAL '30 hours', false)
ON CONFLICT ("Id") DO NOTHING;
