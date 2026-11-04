-- 64. story_summary_flags (1)
-- Canh tre danh co "con thac mac" trong ban tom tat 1; canh 1 thuoc phien ban truyen 1 (15_story_scenes).
INSERT INTO story_summary_flags (
    "Id", "StorySummaryId", "StorySceneId", "CreatedAt", "IsDeleted"
)
SELECT 1, summary."Id", scene."Id", summary."CreatedAt", false
FROM story_summaries AS summary
JOIN story_scenes AS scene ON scene."Id" = 1 AND scene."StoryVersionId" = summary."StoryVersionId"
WHERE summary."Id" = 1
ON CONFLICT ("Id") DO NOTHING;
