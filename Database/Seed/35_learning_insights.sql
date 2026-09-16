-- 35. learning_insights (10)
INSERT INTO learning_insights ("Id", "ChildProfileId", "Observation", "Evidence", "Status", "DetectedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Tre doc nhanh hon muc trung binh cua do tuoi', 'Thoi gian hoan thanh giam 20% so voi tuan truoc', 'InsightDetected', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (2, 2, 'Tre gap kho khan voi tu vung moi', 'Ty le tra loi dung tu vung giam con 60%', 'Reviewed', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (3, 3, 'Tre rat thich the loai co tich', 'Da doc 5 truyen co tich trong tuan', 'InsightDetected', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (4, 4, 'Tre co kha nang suy luan tot', 'Tra loi dung 90% cau hoi suy luan', 'Reviewed', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (5, 5, 'Tre it hoan thanh bai doc duoc giao', 'Chi hoan thanh 2/5 bai duoc giao', 'InsightDetected', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (6, 6, 'Tre tien bo ro ret ve toc do doc', 'Toc do doc tang 30% trong thang', 'Reviewed', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (7, 7, 'Tre can ho tro them ve phat am', 'Ghi nhan nhieu loi phat am trong AsrWordResult', 'InsightDetected', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (8, 8, 'Tre rat tich cuc tham gia thao luan', 'Tra loi 100% cau hoi thao luan', 'Reviewed', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (9, 9, 'Tre it tuong tac voi ung dung gan day', 'So phien doc giam 50% so voi thang truoc', 'InsightDetected', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 'Tre thich the loai the thao va phieu luu', 'Da doc va danh dau yeu thich 4 truyen the thao', 'Reviewed', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
