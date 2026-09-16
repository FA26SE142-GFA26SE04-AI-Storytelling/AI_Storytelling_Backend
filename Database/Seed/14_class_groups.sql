-- 14. class_groups (5)
INSERT INTO class_groups ("Id", "OrganizationId", "TeacherUserId", "Name", "Status", "KnowledgeTreeExp", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, 'Lop 1A', 'Active', 50, NOW() - INTERVAL '38 days', false),
    (2, 1, 8, 'Lop 1B', 'Active', 30, NOW() - INTERVAL '37 days', false),
    (3, 2, 9, 'Lop 2A', 'Active', 80, NOW() - INTERVAL '36 days', false),
    (4, 2, 10, 'Lop 2B', 'Active', 20, NOW() - INTERVAL '35 days', false),
    (5, 3, 7, 'Lop 3A', 'Active', 0, NOW() - INTERVAL '17 days', false)
ON CONFLICT ("Id") DO NOTHING;
