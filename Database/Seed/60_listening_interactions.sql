-- 60. listening_interactions (5, đều của truyện 900 / phiên bản 900)
-- Tương tác lúc nghe chỉ neo được vào cuối một đoạn văn không phải đoạn cuối; truyện 900 có 6 đoạn nên đủ chỗ.
-- Bộ này giống gói Phase 4 sinh ra cho nội dung của 11_stories.sql (Id 900): vị trí neo và câu trích đã được
-- ListeningInteractionRules kiểm đạt (offset tính theo đúng chuỗi nội dung, không đổi chữ khi sửa seed).
-- Đoạn kết thúc tại offset: P1=44, P2=111, P3=160, P4=194, P5=219, P6=259.
-- Các tương tác chưa gắn scene (StorySceneId NULL): Phase 5 gắn sau khi truyện được duyệt.
-- Id 900 giữ nguyên để khớp docs/api/api-test-tuong-tac-luc-nghe-p1-p4.html.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "AnchorOffset", "Kind", "Prompt", "SourceQuote", "SourceStart", "SourceEnd", "CharacterName", "HiddenWord", "Hint", "RevealOffset", "Explanation", "MatchFeedback", "NoMatchFeedback", "WaitFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (900, 900, 'Checkpoint', 44, 'Emotion', 'Trong rừng tối, con nghĩ gì về Thỏ?', 'Trời bắt đầu tối.', 25, 42, NULL, NULL, NULL, NULL, 'Trời tối nên Thỏ thấy sợ.', NULL, 'Con nghĩ thú vị quá! Cùng nghe tiếp nhé.', NULL, NOW() - INTERVAL '1 day', false),
    (901, 900, 'Checkpoint', 160, 'Prediction', 'Tiếng sột soạt đó là của ai?', 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 111, 158, NULL, NULL, NULL, NULL, 'Đoạn này cho biết đó là bác Gấu.', NULL, 'Con nghĩ thú vị quá! Cùng nghe tiếp nhé.', NULL, NOW() - INTERVAL '1 day', false),
    (902, 900, 'RolePlay', 111, NULL, NULL, 'Có ai ở đó không?', 91, 108, 'Thỏ', NULL, NULL, NULL, NULL, 'Con đã thử nói giống Thỏ rồi, giỏi lắm!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (903, 900, 'PicturePrediction', 194, NULL, 'Ai đang chờ Thỏ ở cửa hang?', 'Bác Gấu dẫn Thỏ về tới cửa hang.', 160, 192, NULL, NULL, NULL, 219, NULL, NULL, NULL, 'Con chọn rồi! Mình cùng nghe tiếp xem nhé.', NOW() - INTERVAL '1 day', false),
    (904, 900, 'EventOrdering', 259, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Con nhớ giỏi quá, đúng thứ tự rồi!', 'Mình cùng nghe lại mấy đoạn này rồi xếp lại nhé.', NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
