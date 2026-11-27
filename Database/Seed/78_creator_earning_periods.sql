-- 78. creator_earning_periods (1): ky chia tien cach day 2 thang da chot (Luong 5 5h). Thang truoc de worker tu chot
-- tu ngay chot khi chay that (hoac Admin bam Chot ky ngay).
INSERT INTO creator_earning_periods (
    "Id", "PeriodMonth", "PeriodStart", "PeriodEnd", "CreatorFundVnd", "TotalValidReads", "DistributedVnd", "ClosedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '2 months') AT TIME ZONE 'UTC',
     date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '2 months') AT TIME ZONE 'UTC',
     (date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '1 month') - INTERVAL '1 day') AT TIME ZONE 'UTC',
     786000, 120, 78600,
     date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '1 month') AT TIME ZONE 'UTC' + INTERVAL '7 days',
     date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '1 month') AT TIME ZONE 'UTC' + INTERVAL '7 days',
     false)
ON CONFLICT ("Id") DO NOTHING;
