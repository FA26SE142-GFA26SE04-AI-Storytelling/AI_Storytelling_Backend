-- 52. child_roadmap_items (7): one open step per child; no duplicate unfinished story per child.
-- Child 1 (Be An): one completed step, one open step, two waiting steps (one copied from roadmap template 5).
-- Child 3 (Be Chi): one open step copied from roadmap template 5.
-- Child 6 (Be Phuc): one skipped step (story unavailable) and one removed step.
INSERT INTO child_roadmap_items (
    "Id", "ChildProfileId", "StoryId", "Position", "Status", "SkipReason", "Source", "SourceReadingRoadmapId",
    "AddedByUserId", "Note", "ExpectedDays", "ExpectedCompletionAt", "OpenedAt", "CompletedAt", "SkippedAt",
    "RemovedAt", "RemovedByUserId", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 1, 1, 'Completed', NULL, 'Manual', NULL, 2, 'Con đọc trước truyện này nhé', 2, NULL,
        NOW() - INTERVAL '6 days', NOW() - INTERVAL '5 days', NULL, NULL, NULL, NOW() - INTERVAL '7 days', false),
    (2, 1, 3, 2, 'Open', NULL, 'Manual', NULL, 2, NULL, 3, NOW() + INTERVAL '2 days',
        NOW() - INTERVAL '1 day', NULL, NULL, NULL, NULL, NOW() - INTERVAL '7 days', false),
    (3, 1, 6, 3, 'Waiting', NULL, 'Manual', NULL, 2, NULL, NULL, NULL,
        NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '7 days', false),
    (4, 1, 10, 4, 'Waiting', NULL, 'TemplateCopy', 5, 2, NULL, 3, NULL,
        NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '3 days', false),
    (5, 3, 6, 1, 'Open', NULL, 'TemplateCopy', 5, 3, NULL, 2, NOW() + INTERVAL '1 day',
        NOW() - INTERVAL '1 day', NULL, NULL, NULL, NULL, NOW() - INTERVAL '4 days', false),
    (6, 6, 1, 1, 'Skipped', 'StoryUnavailable', 'Manual', NULL, 4, NULL, NULL, NULL,
        NULL, NULL, NOW() - INTERVAL '2 days', NULL, NULL, NOW() - INTERVAL '5 days', false),
    (7, 6, 3, 2, 'Removed', NULL, 'Manual', NULL, 4, NULL, NULL, NULL,
        NULL, NULL, NULL, NOW() - INTERVAL '1 day', 4, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
