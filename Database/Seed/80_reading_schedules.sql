-- 80. reading_schedules (4)
-- Cấu hình "Thời gian & thói quen" mẫu. Bé 1 và bé 2 (parent_demo1) cố ý KHÔNG có dòng để
-- docs/api/reading-time-api-test-guide.html vẫn test được lần PUT đầu tiên (Id kế tiếp = 5).
-- Giá trị nằm trong giới hạn của ReadingSchedulePolicy: nghỉ sau 15–60 phút, nghỉ 1–15 phút,
-- màn hình 10–240 phút, giờ ngủ 18:00–23:30, giờ nhắc từ 06:00, trước giờ ngủ và nằm trong khung giờ.
-- Trạng thái lúc chạy (ContinuousReadingSince / LastReadingSeenAt / BreakUntil) để NULL như sau khi phụ huynh lưu.
-- Không ghi LastBreakActivity (nullable): để NULL = chưa có lượt nghỉ nào.
-- Dòng mẫu:
--   Bé 3 Chi (parent_demo2, 6–8 tuổi): bật đủ mọi thứ; có khung giờ ở 81_reading_schedule_windows.sql.
--   Bé 6 Phúc (parent_demo3, 9–10 tuổi): không giới hạn khung giờ, chỉ nghỉ mắt + giới hạn màn hình.
--   Bé 7 Giang (parent_demo4, 6–8 tuổi): chỉ bật thói quen đọc, nhắc ngày thường, mục tiêu 5 ngày/tuần.
--   Bé 11 Lâm (parent_demo2): phụ huynh đã lưu một lần nhưng tắt hết (vẫn đếm thời gian dùng app).
INSERT INTO reading_schedules (
    "Id", "ChildProfileId", "Enabled", "BedtimeWeekday", "BedtimeWeekend",
    "BreakEnabled", "BreakAfterMinutes", "BreakDurationMinutes",
    "BreakRemindEyes", "BreakRemindWater", "BreakRemindMove",
    "ScreenLimitEnabled", "ScreenLimitWeekdayMinutes", "ScreenLimitWeekendMinutes",
    "HabitEnabled", "ReadingReminderWeekday", "ReadingReminderWeekend", "WeeklyGoalDays",
    "ContinuousReadingSince", "LastReadingSeenAt", "BreakUntil", "UsageDate", "UsageSeconds",
    "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 3, true, '20:30', '21:30',
     true, 20, 5, true, true, true,
     true, 60, 120,
     true, '19:00', '09:00', 4,
     NULL, NULL, NULL, (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date, 900,
     NOW() - INTERVAL '6 days', NOW() - INTERVAL '2 days', false),
    (2, 6, false, NULL, NULL,
     true, 30, 5, true, false, true,
     true, 90, 150,
     false, NULL, NULL, NULL,
     NULL, NULL, NULL, (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date, 2400,
     NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (3, 7, false, NULL, NULL,
     false, 20, 5, true, true, true,
     false, 60, 120,
     true, '18:30', NULL, 5,
     NULL, NULL, NULL, NULL, 0,
     NOW() - INTERVAL '4 days', NULL, false),
    (4, 11, false, NULL, NULL,
     false, 20, 5, true, true, true,
     false, 60, 120,
     false, NULL, NULL, NULL,
     NULL, NULL, NULL, (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 1, 600,
     NOW() - INTERVAL '3 days', NULL, false)
ON CONFLICT ("Id") DO NOTHING;
