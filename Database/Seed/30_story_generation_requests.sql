-- 30. story_generation_requests (5 - chi cho story co Source = Ai)
-- HandoffJobId duoc gan sau khi insert job vi hai bang tham chieu vong nhau.
INSERT INTO story_generation_requests (
    "Id", "StoryId", "SubmittedByUserId", "IdempotencyKey", "InputFingerprint", "ContextFingerprint",
    "ContextSnapshotJson", "AcceptedInputJson", "Status", "AttemptCount", "MaxAttempts", "ConcurrencyToken",
    "GuardrailDecision", "CanRetry", "GuardrailCheckVersion", "GuardrailCheckedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 7, 'seed-ai-story-2', encode(sha256('seed-input-2'::bytea), 'hex'), encode(sha256('seed-context-2'::bytea), 'hex'),
     '{"childProfileId":2,"ageBand":"Age_9_12","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":["Co tich"],"allowedCategoryCodes":["ANIMALS","ADVENTURE"],"restrictedCategoryCodes":["FANTASY"],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Kham pha rung xanh","genre":"Phieu luu","characterMode":"specified","characters":["Mot ban nho dung cam"],"settingMode":"specified","setting":"Khu rung xanh","lesson":"Tran trong thien nhien","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-1'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '28 days', NOW() - INTERVAL '28 days', false),
    (2, 4, 8, 'seed-ai-story-4', encode(sha256('seed-input-4'::bytea), 'hex'), encode(sha256('seed-context-4'::bytea), 'hex'),
     '{"childProfileId":4,"ageBand":"Age_9_12","readingLevel":4,"vocabularyLevel":"level_4","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":["Khoa hoc"],"allowedCategoryCodes":["FANTASY","SCIENCE"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Hanh trinh vu tru","genre":"Khoa hoc vien tuong","characterMode":"specified","characters":["Mot nha du hanh nho tuoi"],"settingMode":"specified","setting":"Ngoai vu tru","lesson":"Kham pha va hoc hoi khong ngung","vocabularyLevel":"level_4","language":"vi","targetLength":1000}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-2'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '24 days', NOW() - INTERVAL '24 days', false),
    (3, 6, 9, 'seed-ai-story-6', encode(sha256('seed-input-6'::bytea), 'hex'), encode(sha256('seed-context-6'::bytea), 'hex'),
     '{"childProfileId":6,"ageBand":"Age_9_12","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"always_manual","interests":["Gia dinh"],"allowedCategoryCodes":["FAMILY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Bi mat dai duong","genre":"Phieu luu","characterMode":"ai_suggested","characters":[],"settingMode":"specified","setting":"Day dai duong","lesson":"Bao ve moi truong bien","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-3'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false),
    (4, 8, 10, 'seed-ai-story-8', encode(sha256('seed-input-8'::bytea), 'hex'), encode(sha256('seed-context-8'::bytea), 'hex'),
     '{"childProfileId":8,"ageBand":"Age_9_12","readingLevel":5,"vocabularyLevel":"level_5","language":"vi","maximumLength":1800,"requiredApprovalMode":"auto_publish_on_threshold","interests":[],"allowedCategoryCodes":["FANTASY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":[]}'::jsonb,
     '{"topic":"Sieu nhan ti hon","genre":"Sieu anh hung","characterMode":"specified","characters":["Cau be co suc manh dac biet"],"settingMode":"specified","setting":"Mot ngoi lang nho","lesson":"Dung cam bao ve nguoi yeu the","vocabularyLevel":"level_5","language":"vi","targetLength":1000}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-4'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', false),
    (5, 10, 7, 'seed-ai-story-10', encode(sha256('seed-input-10'::bytea), 'hex'), encode(sha256('seed-context-10'::bytea), 'hex'),
     '{"childProfileId":10,"ageBand":"Age_9_12","readingLevel":3,"vocabularyLevel":"level_3","language":"vi","maximumLength":1800,"requiredApprovalMode":"always_manual","interests":[],"allowedCategoryCodes":["FAMILY"],"restrictedCategoryCodes":[],"blockedCategoryCodes":["FANTASY"]}'::jsonb,
     '{"topic":"Tran bong da dang nho","genre":"The thao","characterMode":"ai_suggested","characters":[],"settingMode":"specified","setting":"San bong cua truong","lesson":"Tinh than fair-play trong the thao","vocabularyLevel":"level_3","language":"vi","targetLength":900}'::jsonb,
     'InputAccepted', 1, 3, md5('seed-request-5'), 'Allow', false, 'seed-v1', NOW() - INTERVAL '13 days', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
