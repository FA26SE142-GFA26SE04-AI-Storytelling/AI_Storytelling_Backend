-- 60. listening_interactions (21: bộ cũ 900-905, bộ tuổi 6 920-924, bộ tuổi 7 930-934, bộ tuổi 8 935-939)
-- Tương tác lúc nghe chỉ neo được vào cuối một đoạn văn không phải đoạn cuối; truyện 900 có 6 đoạn nên đủ chỗ.
-- Bộ này giống gói Phase 4 sinh ra cho nội dung của 11_stories.sql (Id 900): vị trí neo và câu trích đã được
-- ListeningInteractionRules kiểm đạt (offset tính theo đúng chuỗi nội dung, không đổi chữ khi sửa seed).
-- Đoạn kết thúc tại offset: P1=44, P2=148, P3=197, P4=231, P5=256, P6=296.
-- Các tương tác chưa gắn scene (StorySceneId NULL): Phase 5 gắn sau khi truyện được duyệt.
-- Id 900 giữ nguyên để khớp docs/api/api-test-tuong-tac-luc-nghe-p1-p4.html.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "AnchorOffset", "Kind", "Prompt", "SourceQuote", "SourceStart", "SourceEnd", "CharacterName", "HiddenWord", "Hint", "RevealOffset", "Explanation", "MatchFeedback", "NoMatchFeedback", "WaitFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (900, 900, 'Checkpoint', 44, 'Emotion', 'Trong rừng tối, con nghĩ gì về Thỏ?', 'Trời bắt đầu tối.', 25, 42, NULL, NULL, NULL, NULL, 'Trời tối nên Thỏ thấy sợ.', NULL, 'Con nghĩ thú vị quá! Cùng nghe tiếp nhé.', NULL, NOW() - INTERVAL '1 day', false),
    (901, 900, 'Checkpoint', 197, 'Prediction', 'Tiếng sột soạt đó là của ai?', 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 148, 195, NULL, NULL, NULL, NULL, 'Đoạn này cho biết đó là bác Gấu.', NULL, 'Con nghĩ thú vị quá! Cùng nghe tiếp nhé.', NULL, NOW() - INTERVAL '1 day', false),
    (902, 900, 'RolePlay', 148, NULL, NULL, 'Có ai ở đó không?', 91, 108, 'Thỏ', NULL, NULL, NULL, NULL, 'Con đã thử nói giống Thỏ rồi, giỏi lắm!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (903, 900, 'PicturePrediction', 231, NULL, 'Ai đang chờ Thỏ ở cửa hang?', 'Bác Gấu dẫn Thỏ về tới cửa hang.', 197, 229, NULL, NULL, NULL, 256, NULL, NULL, NULL, 'Con chọn rồi! Mình cùng nghe tiếp xem nhé.', NOW() - INTERVAL '1 day', false),
    (904, 900, 'EventOrdering', 296, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Con nhớ giỏi quá, đúng thứ tự rồi!', 'Mình cùng nghe lại mấy đoạn này rồi xếp lại nhé.', NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- Bộ tuổi 7 của truyện 900. HotspotJson được định vị từ tranh thật sau khi media sẵn sàng.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "TargetAge", "AnchorOffset", "Prompt", "TargetLabel",
    "HotspotJson", "AcceptedOrdersJson", "SourceQuote", "SourceStart", "SourceEnd", "CharacterName",
    "MatchFeedback", "NoMatchFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (930, 900, 'PictureSearch', 7, 44, 'Tìm Thỏ con trong tranh', 'Thỏ con', NULL, NULL,
        'Thỏ con đi lạc vào rừng.', 0, 24, NULL, 'Con tìm giỏi quá!', 'Nhìn kỹ lại nhé.', NOW() - INTERVAL '1 day', false),
    (931, 900, 'TrueFalse', 7, 148, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        'Con nghe kỹ quá!', 'Mình nghe lại nhé.', NOW() - INTERVAL '1 day', false),
    (932, 900, 'SentenceBuilder', 7, 197, NULL, NULL, NULL, '[[1,2,3,4]]',
        'Thỏ con đi lạc vào rừng.', 0, 24, NULL, 'Con ghép giỏi quá!', 'Nghe lạ quá, mình thử lại nhé.', NOW() - INTERVAL '1 day', false),
    (933, 900, 'FollowAction', 7, 231, 'Bé giúp Thỏ nhé: dắt bác Gấu về cửa hang', NULL, NULL, NULL,
        NULL, NULL, NULL, 'Thỏ', 'Con kéo đúng chỗ rồi!', 'Bé thử lại nhé.', NOW() - INTERVAL '1 day', false),
    (934, 900, 'EventOrdering', 7, 256, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        'Con xếp giỏi quá!', 'Mình nghe lại rồi xếp nhé.', NOW() - INTERVAL '1 day', false)
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
    (921, 900, 'RolePlay', 6, NULL, 148, NULL, NULL, 'Có ai ở đó không?', 91, 108, 'Thỏ', NULL, NULL, NULL, NULL, 'Con đã thử nói giống Thỏ rồi, giỏi lắm!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (922, 900, 'WhoSaidIt', 6, NULL, 197, NULL, NULL, 'Có ai ở đó không?', 91, 108, NULL, NULL, NULL, NULL, NULL, 'Đúng là bạn Thỏ đã hỏi rồi!', 'Mình nghe lại nhé, ai đã hỏi câu này nào?', NULL, NOW() - INTERVAL '1 day', false),
    (923, 900, 'CharacterLetter', 6, 0, 231, NULL, NULL, NULL, NULL, NULL, 'Thỏ', NULL, NULL, NULL, NULL, 'Con chọn chữ giỏi quá!', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (924, 900, 'PicturePrediction', 6, NULL, 256, NULL, 'Trong rừng, theo bé, Thỏ sẽ làm gì tiếp?', 'Bác Gấu dẫn Thỏ về tới cửa hang.', 197, 229, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Con chọn rồi! Mình cùng nghe tiếp nhé.', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- Bộ tuổi 8 của truyện 900 (935-939): suy luận (P1), đọc luân phiên (P2), đặt tên cảnh (P3), nối nguyên nhân (P4), bong bóng suy nghĩ (P5).
-- Đúng vùng của ListeningInteractionPlanner cho truyện 6 đoạn có thoại; đã qua ListeningInteractionRules tuổi 8 (SeedDataAlignmentTests).
-- Offset: P1=44, P2=148, P3=197, P4=231, P5=256, P6=296; chưa gắn scene, Phase 5 gắn sau khi truyện được duyệt.
INSERT INTO listening_interactions (
    "Id", "StoryVersionId", "Type", "TargetAge", "AnchorOffset", "Prompt", "SourceQuote", "SourceStart", "SourceEnd",
    "CharacterName", "Explanation", "MatchFeedback", "CreatedAt", "IsDeleted"
)
VALUES
    (935, 900, 'InferenceQuestion', 8, 44, 'Vì sao Thỏ con thấy lo trong rừng?', 'Thỏ con đi lạc vào rừng. Trời bắt đầu tối.', 0, 42,
        NULL, 'Thỏ đi lạc và trời bắt đầu tối nên Thỏ lo.', 'Con suy luận giỏi quá!', NOW() - INTERVAL '1 day', false),
    (936, 900, 'AlternateReading', 8, 148, NULL, NULL, NULL, NULL,
        NULL, NULL, 'Con đọc cùng bạn hay quá!', NOW() - INTERVAL '1 day', false),
    (937, 900, 'SceneTitle', 8, 197, NULL, NULL, NULL, NULL,
        NULL, 'Cả cảnh kể bác Gấu giúp Thỏ tìm đường về.', 'Con chọn hay quá!', NOW() - INTERVAL '1 day', false),
    (938, 900, 'ThoughtBubble', 8, 256, 'Thỏ đang nghĩ gì?', 'Mẹ Thỏ ôm Thỏ vào lòng.', 231, 254,
        'Thỏ', NULL, 'Con đoán đúng rồi!', NOW() - INTERVAL '1 day', false),
    (939, 900, 'CauseEffect', 8, 231, NULL, NULL, NULL, NULL,
        NULL, NULL, 'Con nối giỏi quá!', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
