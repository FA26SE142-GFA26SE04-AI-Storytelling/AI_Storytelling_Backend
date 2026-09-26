-- 3. organizations (3 - moi to chuc chi duoc phep co 1 template nen khong day them cho du 10)
INSERT INTO organizations ("Id", "Name", "Address", "ContactEmail", "CreatedByUserId", "VerificationStatus", "VerifiedByAdminId", "RejectionReason", "ReactivatedAt", "ReactivatedByAdminId", "ClosureRequestedAt", "ClosedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Truong Tieu Hoc Demo A', '123 Duong ABC, Quan 1, TP.HCM', 'contact.a@truongdemo.edu.vn', 1, 'Active', 1, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '58 days', false),
    (2, 'Truong Tieu Hoc Demo B', '456 Duong XYZ, Quan 3, TP.HCM', 'contact.b@truongdemo.edu.vn', 1, 'Active', 1, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '56 days', false),
    (3, 'Trung Tam Giao Duc Demo C', '789 Duong DEF, Quan 7, TP.HCM', 'contact.c@trungtamdemo.edu.vn', 1, 'PendingVerification', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;
