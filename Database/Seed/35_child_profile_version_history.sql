-- 35. child_profile_version_history (10)
INSERT INTO child_profile_version_history (
    "Id", "ChildProfileId", "RecommendationId", "AppliedByUserId", "PreviousConfig", "NewConfig", "VersionStatus", "AppliedAt", "CreatedAt", "IsDeleted", "Source"
)
VALUES
    (1, 1, 1, 2, '{"readingLevel":2}', '{"readingLevel":3}', 'ActiveVersion', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false, 'Recommendation'),
    (2, 2, 2, 3, '{"focus":"comprehension"}', '{"focus":"vocabulary"}', 'Superseded', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false, 'Recommendation'),
    (3, 3, 3, 4, '{"content":"mixed"}', '{"content":"fairytale"}', 'ActiveVersion', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false, 'Recommendation'),
    (4, 4, 4, 5, '{"readingLevel":4}', '{"readingLevel":4}', 'ActiveVersion', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false, 'Recommendation'),
    (5, 5, 5, 6, '{"interventionLevel":0}', '{"interventionLevel":1}', 'DraftVersion', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false, 'Recommendation'),
    (6, 6, 6, 7, '{"readingLevel":3}', '{"readingLevel":4}', 'ActiveVersion', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false, 'Recommendation'),
    (7, 7, 7, 8, '{"pronunciationSupport":false}', '{"pronunciationSupport":true}', 'ActiveVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false, 'Recommendation'),
    (8, 8, 8, 9, '{"discussionLevel":"basic"}', '{"discussionLevel":"advanced"}', 'DraftVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false, 'Recommendation'),
    (9, 9, 9, 10, '{"reminderEnabled":false}', '{"reminderEnabled":true}', 'Superseded', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false, 'Recommendation'),
    (10, 10, 10, 2, '{"preferredGenre":"mixed"}', '{"preferredGenre":"sports"}', 'ActiveVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false, 'Recommendation')
ON CONFLICT ("Id") DO NOTHING;
