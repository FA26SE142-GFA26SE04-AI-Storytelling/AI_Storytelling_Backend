-- Parent V4 private draft, with a historical NON-PASSING check only.
-- Apply AddParentManualAuthoring before using these seed files. Never grants access to a child.
-- ContextFingerprint intentionally describes an obsolete context: run checks again through API.
-- Fixed demo IDs follow the existing seed fixture; ON CONFLICT preserves existing data.
INSERT INTO parent_story_operations (
    "Id", "StoryId", "ActorUserId", "ChildProfileId", "StoryVersionId", "Kind", "OperationId", "RequestHash",
    "ContextFingerprint", "ResultJson", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 15, 2, 1, 20, 'Check', 'seed-parent-v4-historical-check',
     '9A5A7401D2A8857303130A477EDA216E011A9651B1A3D6683AC05D8982B9840A',
     '8A1AF97AB8C7799725D7E35E2F900A7CF61752A99F169C61701CE178854AC611',
     '{"checkId":1,"storyVersionId":20,"contextFingerprint":"8A1AF97AB8C7799725D7E35E2F900A7CF61752A99F169C61701CE178854AC611","canPrepare":false,"errors":["SEMANTIC_SAFETY_REVIEW_REQUIRED"],"warnings":[],"coverage":"Historical demo context; re-run checks. Not semantic safety approval."}',
     '2026-10-18T01:06:00Z', false)
ON CONFLICT ("Id") DO NOTHING;
