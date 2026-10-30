-- 23. story_generation_requests (5 - chi cho story co Source = Ai)
-- HandoffJobId duoc gan sau khi insert job vi hai bang tham chieu vong nhau.
INSERT INTO story_generation_requests (
    "Id", "StoryId", "SubmittedByUserId", "IdempotencyKey", "InputFingerprint", "ContextFingerprint",
    "ContextSnapshotJson", "AcceptedInputJson", "Status", "AttemptCount", "MaxAttempts", "ConcurrencyToken",
    "GuardrailDecision", "CanRetry", "GuardrailCheckVersion", "GuardrailCheckedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 7, 'seed-ai-story-2', encode(sha256('seed-input-2'::bytea), 'hex'), encode(sha256('seed-context-2'::bytea), 'hex'),
     '{"childProfileId":2,"ageBand":"9-10","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":["Cổ tích"],"allowedCategoryCodes":["ANIMALS","ADVENTURE"],"restrictedCategoryCodes":["FANTASY"],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Khám phá rừng xanh","genre":"Phiêu lưu","characterMode":"specified","characters":["Một bạn nhỏ dũng cảm"],"settingMode":"specified","setting":"Khu rừng xanh","lesson":"Trân trọng thiên nhiên","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-1'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '28 days', NOW() - INTERVAL '28 days', false),
    (2, 4, 8, 'seed-ai-story-4', encode(sha256('seed-input-4'::bytea), 'hex'), encode(sha256('seed-context-4'::bytea), 'hex'),
     '{"childProfileId":4,"ageBand":"9-10","readingLevel":4,"vocabularyLevel":"level_4","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":["Khoa học"],"allowedCategoryCodes":["FANTASY","SCIENCE"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Hành trình vũ trụ","genre":"Khoa học viễn tưởng","characterMode":"specified","characters":["Một nhà du hành nhỏ tuổi"],"settingMode":"specified","setting":"Ngoài vũ trụ","lesson":"Khám phá và học hỏi không ngừng","vocabularyLevel":"level_4","language":"vi","targetLength":1000}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-2'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '24 days', NOW() - INTERVAL '24 days', false),
    (3, 6, 9, 'seed-ai-story-6', encode(sha256('seed-input-6'::bytea), 'hex'), encode(sha256('seed-context-6'::bytea), 'hex'),
     '{"childProfileId":6,"ageBand":"9-10","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"always_manual","interests":["Gia đình"],"allowedCategoryCodes":["FAMILY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Bí mật đại dương","genre":"Phiêu lưu","characterMode":"ai_suggested","characters":[],"settingMode":"specified","setting":"Đáy đại dương","lesson":"Bảo vệ môi trường biển","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-3'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false),
    (4, 8, 10, 'seed-ai-story-8', encode(sha256('seed-input-8'::bytea), 'hex'), encode(sha256('seed-context-8'::bytea), 'hex'),
     '{"childProfileId":8,"ageBand":"9-10","readingLevel":5,"vocabularyLevel":"level_5","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":[],"allowedCategoryCodes":["FANTASY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Siêu nhân tí hon","genre":"Siêu anh hùng","characterMode":"specified","characters":["Cậu bé có sức mạnh đặc biệt"],"settingMode":"specified","setting":"Một ngôi làng nhỏ","lesson":"Dũng cảm bảo vệ người yếu thế","vocabularyLevel":"level_5","language":"vi","targetLength":1000}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-4'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', false),
    (5, 10, 7, 'seed-ai-story-10', encode(sha256('seed-input-10'::bytea), 'hex'), encode(sha256('seed-context-10'::bytea), 'hex'),
     '{"childProfileId":10,"ageBand":"9-10","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"always_manual","interests":[],"allowedCategoryCodes":["FAMILY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":["FANTASY"]}'::jsonb,
     '{"topic":"Trận bóng đá đáng nhớ","genre":"Thể thao","characterMode":"ai_suggested","characters":[],"settingMode":"specified","setting":"Sân bóng của trường","lesson":"Tinh thần fair-play trong thể thao","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-5'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '13 days', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
