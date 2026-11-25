-- 81. reading_schedule_windows (3)
-- Khung giờ được dùng app của bé 3 Chi (reading_schedules.Id = 1), giờ Việt Nam, không vắt qua nửa đêm.
-- Tối đa 2 khung mỗi loại ngày; Slot đánh số 1–2 theo giờ bắt đầu như ReadingScheduleService lưu.
-- Giờ nhắc đọc (19:00 ngày thường, 09:00 cuối tuần) nằm trong các khung này và trước giờ ngủ.
INSERT INTO reading_schedule_windows (
    "Id", "ReadingScheduleId", "DayType", "Slot", "StartTime", "EndTime", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 'Weekday', 1, '17:00', '20:00', NOW() - INTERVAL '6 days', false),
    (2, 1, 'Weekend', 1, '08:00', '11:00', NOW() - INTERVAL '6 days', false),
    (3, 1, 'Weekend', 2, '15:00', '20:00', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
