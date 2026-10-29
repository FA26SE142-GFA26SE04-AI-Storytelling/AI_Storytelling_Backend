-- 53. story_assistance_steps: terminal Close receipt for synthetic task 1001.
-- No model was called. Replaying this operation returns the same Closed receipt.
INSERT INTO story_assistance_steps (
    "Id", "TaskId", "Sequence", "OperationId", "RequestHash", "Kind", "Status", "InputJson", "OutputJson",
    "ResponseJson", "ResultingTaskRevision", "CompletedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1, 'seed-cse-1001-close', '3F7BE36534B897E89079B1CAD1C2AC1FBD6B4E75EFF59C0FC7AD2B97A539A058',
     'Close', 'Succeeded', '{"operationId":"seed-cse-1001-close","expectedTaskRevision":1}'::jsonb, NULL,
     '{"taskId":1001,"stepId":1001,"taskRevision":2,"status":"Closed","isRunning":false,"step":{"stepId":1001,"sequence":1,"operationId":"seed-cse-1001-close","kind":"Close","status":"Succeeded","resultingTaskRevision":2,"errorCode":null,"result":null,"direction":null,"createdAt":"2026-10-14T08:05:00Z","directionStepId":null,"answer":null},"pollUrl":null,"snapshotHash":"4F000F5A3AC71C291E77525127691859FA7C63D102499AD7EFCF80B573696972"}', 2, '2026-10-14T08:05:00Z', '2026-10-14T08:05:00Z', false)
ON CONFLICT ("Id") DO NOTHING;
