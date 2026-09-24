-- 42. recommendations (10)
INSERT INTO recommendations ("Id", "ChildProfileId", "LearningInsightId", "Category", "CurrentState", "ProposedChange", "Evidence", "Status", "ExpiresAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'ReadingDifficulty', 'Dang o muc do 2', 'De xuat nang len muc do 3', 'Diem quiz dat 90% trong 3 lan gan nhat', 'AwaitingReview', NOW() - INTERVAL '8 days' + INTERVAL '14 days', NOW() - INTERVAL '8 days', false),
    (2, 2, 2, 'LearningFocus', 'Tap trung vao doc hieu', 'De xuat tap trung on tu vung', 'Ty le tra loi dung tu vung giam con 60%', 'Detected', NULL, NOW() - INTERVAL '7 days', false),
    (3, 3, 3, 'ContentPersonalization', 'Noi dung da the loai', 'De xuat uu tien noi dung co tich', 'Da doc 5 truyen co tich trong tuan', 'Approved', NULL, NOW() - INTERVAL '6 days', false),
    (4, 4, 4, 'ReadingDifficulty', 'Dang o muc do 4', 'De xuat giu nguyen muc do', 'Tra loi dung 90% cau hoi suy luan', 'Applied', NULL, NOW() - INTERVAL '5 days', false),
    (5, 5, 5, 'LearningIntervention', 'Hoan thanh bai giao thap', 'De xuat can thiep, nhac nho phu huynh', 'Chi hoan thanh 2/5 bai duoc giao', 'RecommendationCreated', NULL, NOW() - INTERVAL '4 days', false),
    (6, 6, 6, 'ReadingDifficulty', 'Dang o muc do 3', 'De xuat nang len muc do 4', 'Toc do doc tang 30% trong thang', 'Validated', NULL, NOW() - INTERVAL '3 days', false),
    (7, 7, 7, 'LearningIntervention', 'Nhieu loi phat am', 'De xuat bo sung bai luyen phat am', 'Ghi nhan nhieu loi phat am trong AsrWordResult', 'Modified', NULL, NOW() - INTERVAL '2 days', false),
    (8, 8, 8, 'ContentPersonalization', 'Tich cuc thao luan', 'De xuat them cau hoi thao luan nang cao', 'Tra loi 100% cau hoi thao luan', 'Monitoring', NULL, NOW() - INTERVAL '1 days', false),
    (9, 9, 9, 'LearningIntervention', 'Giam tuong tac', 'De xuat gui nhac nho hang ngay', 'So phien doc giam 50% so voi thang truoc', 'ObserveMore', NULL, NOW() - INTERVAL '1 days', false),
    (10, 10, 10, 'ContentPersonalization', 'Thich the thao, phieu luu', 'De xuat uu tien truyen the thao', 'Da doc va danh dau yeu thich 4 truyen the thao', 'ReassessmentRequired', NULL, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
