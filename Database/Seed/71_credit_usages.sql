-- 71. credit_usages (3) - lich su tru luot: yeu cau tao truyen AI va Admin tru luot.
INSERT INTO credit_usages ("Id", "UserId", "LotId", "Amount", "Reason", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 1, 1, 'StoryRequest', NOW() - INTERVAL '45 days', false),
    (2, 2, 3, 1, 'StoryRequest', NOW() - INTERVAL '6 days', false),
    (3, 4, 6, 1, 'AdminDeduction', NOW() - INTERVAL '3 days', false)
ON CONFLICT ("Id") DO NOTHING;
