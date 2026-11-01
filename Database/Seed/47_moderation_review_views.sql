-- 47. moderation_review_views (9): active, expired and historical access grants.
INSERT INTO moderation_review_views (
    "Id", "ModeratorUserId", "StoryId", "StoryVersionId", "ReadingRoadmapId",
    "SourceTab", "GrantedAt", "ExpiresAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 11, 1, 1, NULL,
     'published_content', NOW() - INTERVAL '1 hour', NOW() + INTERVAL '1 hour',
     NOW() - INTERVAL '1 hour', false),
    (2, 12, NULL, NULL, 1,
     'roadmap_templates', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '1 hour',
     NOW() - INTERVAL '3 hours', false),
    (3, 11, NULL, NULL, 3,
     'roadmap_templates', NOW() - INTERVAL '30 minutes', NOW() + INTERVAL '90 minutes',
     NOW() - INTERVAL '30 minutes', false),
    (4, 12, 6, 6, NULL,
     'published_content', NOW() - INTERVAL '45 minutes', NOW() + INTERVAL '75 minutes',
     NOW() - INTERVAL '45 minutes', false),
    (5, 11, 10, 10, NULL,
     'published_content', NOW() - INTERVAL '4 hours', NOW() - INTERVAL '2 hours',
     NOW() - INTERVAL '4 hours', false),
    (6, 12, NULL, NULL, 5,
     'roadmap_templates', NOW() - INTERVAL '20 minutes', NOW() + INTERVAL '100 minutes',
     NOW() - INTERVAL '20 minutes', false),
    (7, 11, NULL, NULL, 2,
     'roadmap_templates', NOW() - INTERVAL '25 minutes', NOW() + INTERVAL '95 minutes',
     NOW() - INTERVAL '25 minutes', false),
    (8, 12, 3, 3, NULL,
     'published_content', NOW() - INTERVAL '5 hours', NOW() - INTERVAL '3 hours',
     NOW() - INTERVAL '5 hours', false),
    (9, 11, NULL, NULL, 4,
     'roadmap_templates', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days' + INTERVAL '2 hours',
     NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
