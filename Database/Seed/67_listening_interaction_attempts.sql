-- 67. listening_interaction_attempts (0)
-- Luot "da thu" do tre tao khi doc that; seed khong co tuong tac nen khong co luot thu.
INSERT INTO listening_interaction_attempts (
    "Id", "ReadingSessionId", "ChildProfileId", "ListeningInteractionId", "AttemptedAt", "CreatedAt", "IsDeleted"
)
SELECT 0, 0, 0, 0, NOW(), NOW(), false
WHERE false
ON CONFLICT ("Id") DO NOTHING;
