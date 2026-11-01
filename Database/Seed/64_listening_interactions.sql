-- 64. listening_interactions (0)
-- Tuong tac luc nghe chi neo duoc vao cuoi mot doan van khong phai doan cuoi.
-- Moi Content trong seed hien chi co mot doan nen khong co cho neo; Phase 4 sinh bang AI khi chay that.
-- Giu bang trong nhu learning_artifact_generation_batches, khong bia du lieu trai luat.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "AnchorOffset", "CreatedAt", "IsDeleted"
)
SELECT 0, 0, 'Checkpoint', 0, NOW(), false
WHERE false
ON CONFLICT ("Id") DO NOTHING;
