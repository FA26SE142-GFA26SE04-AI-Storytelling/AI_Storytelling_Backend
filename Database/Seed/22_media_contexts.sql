-- 22. media_contexts (4)
-- Chi tao context cho cac StoryVersion da hoan tat Phase 5 (Story.Status = Ready).
INSERT INTO media_contexts (
    "Id", "StoryVersionId", "Revision", "ContextJson", "CreatedAt", "IsDeleted")
SELECT
    v."Id",
    v."Id",
    1,
    jsonb_build_object(
        'schemaVersion', 1,
        'sourceStoryVersionId', v."Id",
        'storyFacts', jsonb_build_object(
            'title', v."Title",
            'lesson', v."Lesson",
            'approvedOutline', jsonb_build_object(
                'outlineOpening', v."OutlineOpening",
                'outlineDevelopment', v."OutlineDevelopment",
                'outlineEnding', v."OutlineEnding"),
            'canonicalContent', v."Content",
            'acceptedInput', NULL,
            'acceptedContext', NULL),
        'visualDesign', jsonb_build_object(
            'style', 'child-friendly storybook illustration',
            'continuityRule', 'Keep character identity, clothing, important objects, locations and timeline consistent.',
            'prohibitedChanges', jsonb_build_array(
                'plot', 'lesson', 'character identity', 'canonical scene text'))),
    v."CreatedAt",
    false
FROM story_versions AS v
WHERE v."Id" IN (1, 3, 6, 10)
  AND v."Content" IS NOT NULL
  AND v."Content" <> ''
ON CONFLICT ("Id") DO UPDATE SET
    "StoryVersionId" = EXCLUDED."StoryVersionId",
    "Revision" = EXCLUDED."Revision",
    "ContextJson" = EXCLUDED."ContextJson",
    "IsDeleted" = false;
