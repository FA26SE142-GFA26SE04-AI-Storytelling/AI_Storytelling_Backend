-- 56. story_version_authoring_metadata: one row per seeded content version.
INSERT INTO story_version_authoring_metadata (
    "StoryVersionId", "StoryId", "AuthoringOrigin", "AssistanceProvenanceJson", "SourceDeclaration", "RightsDeclaration", "ImportFileName", "ImportSourceHash", "ImportPreviewHash"
)
VALUES
    (1, 1, NULL, NULL, '', '', NULL, NULL, NULL),
    (2, 2, NULL, NULL, '', '', NULL, NULL, NULL),
    (3, 3, NULL, NULL, '', '', NULL, NULL, NULL),
    (4, 4, NULL, NULL, '', '', NULL, NULL, NULL),
    (5, 5, NULL, NULL, '', '', NULL, NULL, NULL),
    (6, 6, NULL, NULL, '', '', NULL, NULL, NULL),
    (7, 7, NULL, NULL, '', '', NULL, NULL, NULL),
    (8, 8, NULL, NULL, '', '', NULL, NULL, NULL),
    (9, 9, NULL, NULL, '', '', NULL, NULL, NULL),
    (10, 10, NULL, NULL, '', '', NULL, NULL, NULL),
    (11, 11, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (12, 11, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (13, 12, 'Import', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', 'chiec-o-tinh-ban.txt', '73FEF5D8FF880C53B1CB38331E746F013429C04260746C0558C7E746D02473FD', NULL),
    (14, 13, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (15, 13, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (16, 13, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (17, 14, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (18, 14, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (19, 14, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (20, 15, 'Write', NULL, 'Nội dung mẫu tự sáng tác', 'Có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (21, 16, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (22, 17, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (23, 18, 'Write', NULL, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', NULL, NULL, NULL),
    (24, 19, NULL, NULL, '', '', NULL, NULL, NULL)
ON CONFLICT ("StoryVersionId") DO NOTHING;
