-- 60. listening_interactions (11: 900-904 bộ cũ chưa gắn tuổi và 920-924 bộ 6 tuổi của truyện 900 / phiên bản 900, 905 của truyện 901)
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

-- Điền từ (FillBlank) của truyện 901 (bé 6 Phúc, 9-10 tuổi): neo cuối đoạn 2 (offset 142), đã gắn scene 902
-- và có đủ audio text / hint / match / noMatch nên GET content của bé trả tương tác này.
-- Đoạn kết thúc tại offset: P1=50, P2=142, P3=270, P4=333. Câu trích [50, 93) nằm trong đoạn 2.
-- Đáp án chấp nhận ở 61_listening_interaction_options.sql (911 "khủng long", 912 "con khủng long").
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "AnchorOffset", "StorySceneId", "Kind", "Prompt", "SourceQuote", "SourceStart", "SourceEnd", "CharacterName", "HiddenWord", "Hint", "RevealOffset", "Explanation", "MatchFeedback", "NoMatchFeedback", "WaitFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (905, 901, 'FillBlank', 142, 902, NULL, NULL, 'Phúc đứng ngắm bộ xương khủng long rất lớn.', 50, 93, NULL, 'khủng long', 'Một loài vật rất to sống từ thời xa xưa.', NULL, NULL, 'Con nghe kỹ quá, đúng là khủng long rồi!', 'Mình cùng nghe lại nhé: Phúc ngắm bộ xương khủng long.', NULL, NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Audio của tương tác 905: 19_media_assets.sql chèn sẵn 909-912 ở dạng giữ chỗ (đã xóa mềm) vì media_assets
-- nạp trước listening_interactions; khi tương tác đã có thì gắn vào 905 và bật lên (cùng cách 73_story_ratings.sql
-- gắn moderation_items). Mỗi phần chữ bé nghe một file: text (đọc nguyên câu ngắn), hint, match, noMatch.
UPDATE media_assets AS m
SET "Type" = 'ListeningAudio', "ListeningInteractionId" = 905, "AudioPartKey" = v.part,
    "Status" = 'Ready', "ValidationStatus" = 'Passed', "IsDeleted" = false
FROM (VALUES (909, 'hint'), (910, 'match'), (911, 'noMatch'), (912, 'text')) AS v(id, part)
WHERE m."Id" = v.id AND m."StoryVersionId" = 901 AND m."ListeningInteractionId" IS NULL;

-- Bộ tương tác riêng của bé 6 tuổi (TargetAge = 6) cho truyện 900 / phiên bản 900: 920-924, mỗi loại một tương tác.
-- Các dòng 900-905 ở trên là bộ cũ chưa gắn tuổi (TargetAge NULL): không còn tới trẻ, không tính vào duyệt / readiness.
-- Neo theo kế hoạch ListeningInteractionPlanner.Create(6, ...) với 6 đoạn có thoại ở đoạn 2:
--   P1 (44) Điểm dừng nghĩa đen · P2 (111) Nói theo nhân vật · P3 (160) Chạm vào nhân vật nói câu đó
--   P4 (194) Tên nhân vật và chữ cái · P5 (219) Đoán tiếp bằng tranh.
-- Chưa gắn scene (StorySceneId NULL): Phase 5 gắn sau khi truyện được duyệt.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "TargetAge", "BlankIndex", "AnchorOffset", "Kind", "Prompt", "SourceQuote", "SourceStart", "SourceEnd", "CharacterName", "HiddenWord", "Hint", "RevealOffset", "Explanation", "MatchFeedback", "NoMatchFeedback", "WaitFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (920, 900, 'Checkpoint', 6, NULL, 44, 'Literal', 'Ai đi lạc vào rừng?', 'Thỏ con đi lạc vào rừng.', 0, 24, NULL, NULL, NULL, NULL, 'Câu đầu truyện nói Thỏ con đi lạc.', NULL, 'Bé nhìn lại tranh nhé.', NULL, NOW() - INTERVAL '1 day', false),
    (921, 900, 'RolePlay', 6, NULL, 111, NULL, NULL, 'Có ai ở đó không?', 91, 108, 'Thỏ', NULL, NULL, NULL, NULL, 'Con đã thử nói giống Thỏ rồi, giỏi lắm!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (922, 900, 'WhoSaidIt', 6, NULL, 160, NULL, NULL, 'Có ai ở đó không?', 91, 108, NULL, NULL, NULL, NULL, NULL, 'Đúng là bạn Thỏ đã hỏi rồi!', 'Mình nghe lại nhé, ai đã hỏi câu này nào?', NULL, NOW() - INTERVAL '1 day', false),
    (923, 900, 'CharacterLetter', 6, 0, 194, NULL, NULL, NULL, NULL, NULL, 'Thỏ', NULL, NULL, NULL, NULL, 'Con chọn chữ giỏi quá!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (924, 900, 'PicturePrediction', 6, NULL, 219, NULL, 'Theo bé, Thỏ sẽ làm gì tiếp?', 'Bác Gấu dẫn Thỏ về tới cửa hang.', 160, 192, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Con chọn rồi! Mình cùng nghe tiếp nhé.', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
