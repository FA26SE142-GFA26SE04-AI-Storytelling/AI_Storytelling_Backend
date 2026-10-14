-- 55. reading_roadmap_items (8): position and story are unique within each roadmap.
INSERT INTO reading_roadmap_items (
    "Id", "ReadingRoadmapId", "StoryId", "Position", "Days", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 1, 1, 2, NOW() - INTERVAL '14 days', false),
    (2, 2, 1, 1, 3, NOW() - INTERVAL '2 days', false),
    (3, 3, 6, 1, 2, NOW() - INTERVAL '2 days', false),
    (4, 3, 10, 2, 3, NOW() - INTERVAL '2 days', false),
    (5, 4, 1, 1, 2, NOW() - INTERVAL '5 days', false),
    (6, 5, 6, 1, 2, NOW() - INTERVAL '11 days', false),
    (7, 5, 10, 2, 3, NOW() - INTERVAL '11 days', false),
    (8, 6, 3, 1, 2, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
