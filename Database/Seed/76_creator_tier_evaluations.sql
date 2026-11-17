-- 76. creator_tier_evaluations (1): ket qua xet cap Creator doi tac thang truoc cua creator_demo (Id 13), Luong 5 5g.
-- Chua dat (it truyen Published, it luot doc hop le) nen van la Creator thuong; thang hien tai duoc xet khi chay that.
INSERT INTO creator_tier_evaluations (
    "Id", "CreatorUserId", "PeriodMonth", "WindowStart", "WindowEnd", "PublishedStories", "CompletionRate",
    "ValidReads", "ViolationRemovals", "Eligible", "TierBefore", "TierAfter", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 13,
     date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '1 month') AT TIME ZONE 'Asia/Ho_Chi_Minh',
     date_trunc('day', NOW() - INTERVAL '1 month') - INTERVAL '90 days', date_trunc('day', NOW() - INTERVAL '1 month'),
     3, 0.5000, 12, 0, false, 'Standard', 'Standard', NOW() - INTERVAL '1 month', false)
ON CONFLICT ("Id") DO NOTHING;
