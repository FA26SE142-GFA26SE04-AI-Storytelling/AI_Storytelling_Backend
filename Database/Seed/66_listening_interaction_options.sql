-- 66. listening_interaction_options (0)
-- Thuoc ve listening_interactions, bang nay cung de trong trong seed.
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "IsMatch", "CreatedAt", "IsDeleted"
)
SELECT 0, 0, 1, '', false, NOW(), false
WHERE false
ON CONFLICT ("Id") DO NOTHING;
