-- 66. story_version_media_states: one row per seeded content version.
INSERT INTO story_version_media_states (
    "StoryVersionId", "MediaPackageRevision", "ConfirmedMediaPackageRevision", "MediaConfirmedByUserId", "MediaConfirmedAt"
)
VALUES
    (1, 0, NULL, NULL, NULL),
    (2, 0, NULL, NULL, NULL),
    (3, 0, NULL, NULL, NULL),
    (4, 0, NULL, NULL, NULL),
    (5, 0, NULL, NULL, NULL),
    (6, 0, NULL, NULL, NULL),
    (7, 0, NULL, NULL, NULL),
    (8, 0, NULL, NULL, NULL),
    (9, 0, NULL, NULL, NULL),
    (10, 0, NULL, NULL, NULL),
    (11, 0, NULL, NULL, NULL),
    (12, 0, NULL, NULL, NULL),
    (13, 0, NULL, NULL, NULL),
    (14, 0, NULL, NULL, NULL),
    (15, 0, NULL, NULL, NULL),
    (16, 0, NULL, NULL, NULL),
    (17, 0, NULL, NULL, NULL),
    (18, 0, NULL, NULL, NULL),
    (19, 0, NULL, NULL, NULL),
    (20, 0, NULL, NULL, NULL)
ON CONFLICT ("StoryVersionId") DO NOTHING;
