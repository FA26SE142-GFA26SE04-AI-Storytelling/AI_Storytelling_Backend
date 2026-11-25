-- 91. event_evaluations - demo fixtures for the event lifecycle.
INSERT INTO event_evaluations (
    "Id", "EventSubmissionId", "EvaluatorUserId", "CriteriaScoresJson", "TotalWeightedScore", "SubmittedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 11, '{"creativity":90,"education":90,"structure":90,"media":90}', 90, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false)
ON CONFLICT ("Id") DO NOTHING;
