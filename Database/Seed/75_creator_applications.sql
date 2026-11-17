-- 75. creator_applications (1): don dang ky da duoc Admin (Id 1) duyet cua creator_demo (Id 13), Luong 5 M5.
INSERT INTO creator_applications (
    "Id", "ApplicantUserId", "Introduction", "PortfolioUrl", "Status", "ReviewedByUserId", "ReviewedAt",
    "RejectionReason", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 13, 'Tôi sáng tác truyện thiếu nhi về tình bạn và thiên nhiên, đã xuất bản truyện ngắn trên báo Nhi Đồng.',
     NULL, 'Approved', 1, NOW() - INTERVAL '35 days' + INTERVAL '30 minutes', NULL, NOW() - INTERVAL '35 days', false)
ON CONFLICT ("Id") DO NOTHING;
