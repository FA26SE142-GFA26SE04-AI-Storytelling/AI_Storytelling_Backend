-- 62. wish_cards (26): bo the uoc ban dau cho Be uoc (4 nhom) va Be sang tac (3 nhom).
-- AgeBand NULL = moi nhom tuoi. The "Gap quai vat" gan nhan SCARY_CONTENT (id 8) de thu loc danh sach chan.
-- Hinh va audio la duong dan giu cho; Moderator thay bang duong dan that qua /api/v1/moderation/wish-cards.
INSERT INTO wish_cards (
    "Id", "Group", "Label", "ImageUrl", "AudioUrl", "AgeBand", "ContentCategoryId", "SortOrder", "IsActive",
    "CreatedByUserId", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 'Character', 'Chú mèo', 'https://cdn.example.com/wish-cards/chu-meo.png', NULL, NULL, 1, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (2, 'Character', 'Cô bé phi hành gia', 'https://cdn.example.com/wish-cards/phi-hanh-gia.png', NULL, NULL, NULL, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (3, 'Character', 'Chú rồng nhỏ', 'https://cdn.example.com/wish-cards/rong-nho.png', NULL, NULL, 2, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (4, 'Character', 'Chú thỏ', 'https://cdn.example.com/wish-cards/chu-tho.png', NULL, NULL, 1, 4, true, NULL, NOW() - INTERVAL '10 days', false),
    (5, 'Place', 'Khu rừng', 'https://cdn.example.com/wish-cards/khu-rung.png', NULL, NULL, 3, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (6, 'Place', 'Đáy biển', 'https://cdn.example.com/wish-cards/day-bien.png', NULL, NULL, 3, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (7, 'Place', 'Trường học', 'https://cdn.example.com/wish-cards/truong-hoc.png', NULL, NULL, NULL, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (8, 'Place', 'Ngôi nhà của bé', 'https://cdn.example.com/wish-cards/ngoi-nha.png', NULL, NULL, 6, 4, true, NULL, NOW() - INTERVAL '10 days', false),
    (9, 'Challenge', 'Bị lạc đường', 'https://cdn.example.com/wish-cards/bi-lac-duong.png', NULL, NULL, 3, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (10, 'Challenge', 'Giúp bạn', 'https://cdn.example.com/wish-cards/giup-ban.png', NULL, NULL, 5, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (11, 'Challenge', 'Sợ bóng tối', 'https://cdn.example.com/wish-cards/so-bong-toi.png', NULL, 'Age_9_10', NULL, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (12, 'Challenge', 'Gặp quái vật', 'https://cdn.example.com/wish-cards/quai-vat.png', NULL, 'Age_9_10', 8, 4, true, NULL, NOW() - INTERVAL '10 days', false),
    (13, 'Lesson', 'Chia sẻ', 'https://cdn.example.com/wish-cards/chia-se.png', NULL, NULL, 5, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (14, 'Lesson', 'Dũng cảm', 'https://cdn.example.com/wish-cards/dung-cam.png', NULL, NULL, NULL, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (15, 'Lesson', 'Trung thực', 'https://cdn.example.com/wish-cards/trung-thuc.png', NULL, NULL, NULL, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (16, 'Lesson', 'Tình bạn', 'https://cdn.example.com/wish-cards/tinh-ban.png', NULL, NULL, 5, 4, true, NULL, NOW() - INTERVAL '10 days', false),
    (17, 'Desire', 'Tìm đường về nhà', 'https://cdn.example.com/wish-cards/tim-duong-ve-nha.png', NULL, NULL, 3, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (18, 'Desire', 'Có một người bạn mới', 'https://cdn.example.com/wish-cards/nguoi-ban-moi.png', NULL, NULL, 5, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (19, 'Desire', 'Tìm kho báu', 'https://cdn.example.com/wish-cards/tim-kho-bau.png', NULL, NULL, 3, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (20, 'Event', 'Gặp cơn mưa lớn', 'https://cdn.example.com/wish-cards/mua-lon.png', NULL, NULL, NULL, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (21, 'Event', 'Bị lạc trong rừng', 'https://cdn.example.com/wish-cards/lac-trong-rung.png', NULL, NULL, 3, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (22, 'Event', 'Gặp một bạn đang buồn', 'https://cdn.example.com/wish-cards/ban-dang-buon.png', NULL, NULL, 5, 3, true, NULL, NOW() - INTERVAL '10 days', false),
    (23, 'Ending', 'Vui vẻ cùng bạn bè', 'https://cdn.example.com/wish-cards/vui-ve.png', NULL, NULL, 5, 1, true, NULL, NOW() - INTERVAL '10 days', false),
    (24, 'Ending', 'Ấm áp bên gia đình', 'https://cdn.example.com/wish-cards/am-ap.png', NULL, NULL, 6, 2, true, NULL, NOW() - INTERVAL '10 days', false),
    (25, 'Lesson', 'Biết lắng nghe', 'https://cdn.example.com/wish-cards/lang-nghe.png', NULL, NULL, 5, 5, true, NULL, NOW(), false),
    (26, 'Character', 'Mèo Mun', 'https://cdn.example.com/wish-cards/meo-mun.png', NULL, NULL, 1, 5, true, NULL, NOW(), false)
ON CONFLICT ("Id") DO NOTHING;
