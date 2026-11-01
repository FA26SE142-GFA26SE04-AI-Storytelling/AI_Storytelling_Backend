-- 61. story_wishes (5): dieu uoc mau cho Be uoc (WF-L2D).
-- Child 1 (Be An): 1 dieu uoc dang cho va 1 dieu uoc da tu choi.
-- Child 3: 1 dieu uoc da thanh truyen bang truyen co san (story 3).
-- Wishes 4/5: fresh Parent generation entry points for child 1/3. No fake AI requests/jobs or completed mappings.
INSERT INTO story_wishes (
    "Id", "ChildProfileId", "Kind", "Status", "CharacterCardId", "PlaceCardId", "ChallengeCardId", "LessonCardId",
    "LessonLeftToParent", "SubmittedAt", "PendingSince", "FulfilledStoryId", "FulfilledWithExistingStory",
    "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 'Wish', 'Pending', 1, 5, 9, 13, false, NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day',
        NULL, false, NULL, NOW() - INTERVAL '1 day', false),
    (2, 1, 'Wish', 'Declined', 4, 6, NULL, NULL, true, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days',
        NULL, false, NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false),
    (3, 3, 'Wish', 'Fulfilled', 2, 7, 10, 16, false, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days',
        3, true, NOW() - INTERVAL '3 days', NOW() - INTERVAL '4 days', false),
    (4, 1, 'Wish', 'Pending', 26, 8, 10, 25, false, NOW(), NOW(),
        NULL, false, NULL, NOW(), false),
    (5, 3, 'Wish', 'Pending', 4, 7, 10, NULL, true, NOW(), NOW(),
        NULL, false, NULL, NOW(), false)
ON CONFLICT ("Id") DO NOTHING;
