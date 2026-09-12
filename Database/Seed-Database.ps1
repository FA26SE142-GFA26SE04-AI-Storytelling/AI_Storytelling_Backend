<#
.SYNOPSIS
    Insert du lieu mau (seed data) cho toan bo cac bang cua database AIStorytellingDB (PostgreSQL).

.DESCRIPTION
    Script nay ket noi toi PostgreSQL da cai san tren may (dung psql.exe) va chay 1 file .sql
    chua cac cau lenh INSERT cho 48 bang, theo dung thu tu phu thuoc khoa ngoai (FK).
    Moi INSERT dung "ON CONFLICT (\"Id\") DO NOTHING" nen co the chay lai nhieu lan an toan.
    Sau khi insert xong, script se reset lai cac sequence (identity) cho tung bang de cac
    ban ghi moi do ung dung tao ra sau nay khong bi trung Id.

.PARAMETER PgHost
    Dia chi host cua PostgreSQL. Mac dinh: localhost

.PARAMETER Port
    Cong ket noi. Mac dinh: 5432

.PARAMETER Database
    Ten database. Mac dinh: AIStorytellingDB (lay tu appsettings.json cua StoryPlatform.Api)

.PARAMETER Username
    Ten dang nhap PostgreSQL. Mac dinh: postgres

.PARAMETER Password
    Mat khau PostgreSQL. Mac dinh: 12345 (lay tu appsettings.json cua StoryPlatform.Api)

.EXAMPLE
    ./Seed-Database.ps1

.EXAMPLE
    ./Seed-Database.ps1 -PgHost "localhost" -Port 5432 -Database "AIStorytellingDB" -Username "postgres" -Password "12345"
#>

[CmdletBinding()]
param(
    [string]$PgHost = "localhost",
    [int]$Port = 5432,
    [string]$Database = "AIStorytellingDB",
    [string]$Username = "postgres",
    [string]$Password = "12345"
)

$ErrorActionPreference = "Stop"

function Find-Psql {
    $cmd = Get-Command psql.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = Get-ChildItem "C:\Program Files\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending

    foreach ($dir in $candidates) {
        $path = Join-Path $dir.FullName "bin\psql.exe"
        if (Test-Path $path) { return $path }
    }

    return $null
}

$psqlPath = Find-Psql
if (-not $psqlPath) {
    Write-Error "Khong tim thay psql.exe. Hay them thu muc 'bin' cua PostgreSQL (vi du: C:\Program Files\PostgreSQL\16\bin) vao PATH, hoac cai dat lai PostgreSQL."
    exit 1
}

Write-Host "Dung psql tai: $psqlPath" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Noi dung SQL seed - insert theo dung thu tu phu thuoc FK, dung Id co dinh
# ---------------------------------------------------------------------------
$sql = @'
BEGIN;

-- 1. user_accounts (10) - Mat khau cho tat ca tai khoan demo: Demo@123
INSERT INTO user_accounts (
    "Id", "Username", "Email", "PasswordHash", "FullName", "PhoneNumber", 
    "AvatarUrl", "Role", "Status", "ResetTokenHash", "ResetTokenExpiresAt", 
    "RefreshTokenHash", "RefreshTokenExpiresAt", "LastLoginAt", 
    "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 'admin_demo', 'admin@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Nguyen Van Admin', '0900000001', 
     'https://ui-avatars.com/api/?name=Admin+Demo&background=0D8ABC&color=fff', 'Administrator', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-admin'::bytea), 'hex'), NOW() + INTERVAL '7 days', NOW() - INTERVAL '2 hours', 
     NOW() - INTERVAL '60 days', NOW() - INTERVAL '2 hours', false),

    (2, 'parent_demo1', 'parent1@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Tran Thi Hoa', '0900000002', 
     'https://ui-avatars.com/api/?name=Hoa+Tran&background=F39C12&color=fff', 'Parent', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '5 days', 
     NOW() - INTERVAL '55 days', NOW() - INTERVAL '5 days', false),

    (3, 'parent_demo2', 'parent2@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Nguyen Van Binh', '0900000003', 
     'https://ui-avatars.com/api/?name=Binh+Nguyen&background=27AE60&color=fff', 'Parent', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-parent2'::bytea), 'hex'), NOW() + INTERVAL '6 days', NOW() - INTERVAL '1 day', 
     NOW() - INTERVAL '52 days', NOW() - INTERVAL '1 day', false),

    (4, 'parent_demo3', 'parent3@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Le Thi Cuc', '0900000004', 
     'https://ui-avatars.com/api/?name=Cuc+Le&background=8E44AD&color=fff', 'Parent', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '10 days', 
     NOW() - INTERVAL '50 days', NOW() - INTERVAL '10 days', false),

    (5, 'parent_demo4', 'parent4@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Pham Van Dung', '0900000005', 
     NULL, 'Parent', 'Suspended', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '30 days', 
     NOW() - INTERVAL '48 days', NOW() - INTERVAL '20 days', false),

    (6, 'parent_demo5', 'parent5@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Hoang Thi Em', '0900000006', 
     NULL, 'Parent', 'Registered', NULL, NULL, 
     NULL, NULL, NULL, 
     NOW() - INTERVAL '46 days', NULL, false),

    (7, 'teacher_demo1', 'teacher1@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Vu Van Phong', '0900000007', 
     'https://ui-avatars.com/api/?name=Phong+Vu&background=2980B9&color=fff', 'Teacher', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-teacher1'::bytea), 'hex'), NOW() + INTERVAL '7 days', NOW() - INTERVAL '3 hours', 
     NOW() - INTERVAL '45 days', NOW() - INTERVAL '3 hours', false),

    (8, 'teacher_demo2', 'teacher2@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Do Thi Giang', '0900000008', 
     'https://ui-avatars.com/api/?name=Giang+Do&background=E74C3C&color=fff', 'Teacher', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '3 days', 
     NOW() - INTERVAL '44 days', NOW() - INTERVAL '3 days', false),

    (9, 'teacher_demo3', 'teacher3@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Bui Van Hai', '0900000009', 
     NULL, 'Teacher', 'PasswordResetPending', encode(sha256('demo-reset-token-teacher3'::bytea), 'hex'), NOW() + INTERVAL '1 hour', 
     NULL, NULL, NOW() - INTERVAL '2 days', 
     NOW() - INTERVAL '43 days', NOW() - INTERVAL '1 hour', false),

    (10, 'teacher_demo4', 'teacher4@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Dang Thi Kim', '0900000010', 
     'https://ui-avatars.com/api/?name=Kim+Dang&background=34495E&color=fff', 'Teacher', 'LoggedOut', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '12 hours', 
     NOW() - INTERVAL '42 days', NOW() - INTERVAL '6 hours', false)
ON CONFLICT ("Id") DO NOTHING;

-- 2. organizations (3 - moi to chuc chi duoc phep co 1 template nen khong day them cho du 10)
INSERT INTO organizations ("Id", "Name", "Address", "ContactEmail", "CreatedByUserId", "VerificationStatus", "VerifiedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Truong Tieu Hoc Demo A', '123 Duong ABC, Quan 1, TP.HCM', 'contact.a@truongdemo.edu.vn', 1, 'Active', 1, NOW() - INTERVAL '58 days', false),
    (2, 'Truong Tieu Hoc Demo B', '456 Duong XYZ, Quan 3, TP.HCM', 'contact.b@truongdemo.edu.vn', 1, 'Active', 1, NOW() - INTERVAL '56 days', false),
    (3, 'Trung Tam Giao Duc Demo C', '789 Duong DEF, Quan 7, TP.HCM', 'contact.c@trungtamdemo.edu.vn', 1, 'PendingVerification', NULL, NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 3. content_categories (6)
INSERT INTO content_categories ("Id", "Code", "DisplayName", "IsActive", "CreatedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'ANIMALS', 'Dong vat', true, 1, NOW() - INTERVAL '50 days', false),
    (2, 'FANTASY', 'Co tich - Ky ao', true, 1, NOW() - INTERVAL '50 days', false),
    (3, 'ADVENTURE', 'Phieu luu', true, 1, NOW() - INTERVAL '50 days', false),
    (4, 'SCIENCE', 'Khoa hoc', true, 1, NOW() - INTERVAL '50 days', false),
    (5, 'FRIENDSHIP', 'Tinh ban', true, 1, NOW() - INTERVAL '50 days', false),
    (6, 'FAMILY', 'Gia dinh', true, 1, NOW() - INTERVAL '50 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 4. organization_memberships (8)
INSERT INTO organization_memberships ("Id", "OrganizationId", "UserId", "OrgRole", "Status", "InvitedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, 'Teacher', 'Active', 1, NOW() - INTERVAL '48 days', false),
    (2, 1, 8, 'Teacher', 'Active', 1, NOW() - INTERVAL '47 days', false),
    (3, 2, 9, 'Teacher', 'Active', 1, NOW() - INTERVAL '46 days', false),
    (4, 2, 10, 'Teacher', 'Active', 1, NOW() - INTERVAL '45 days', false),
    (5, 3, 7, 'Teacher', 'Pending', 1, NOW() - INTERVAL '19 days', false),
    (6, 1, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '58 days', false),
    (7, 2, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '56 days', false),
    (8, 3, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 5. organization_permissions (10)
INSERT INTO organization_permissions ("Id", "OrganizationMembershipId", "Permission", "GrantedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'CreateClassGroup', 1, NOW() - INTERVAL '48 days', false),
    (2, 2, 'ManageClassMembers', 1, NOW() - INTERVAL '47 days', false),
    (3, 3, 'InviteTeacher', 1, NOW() - INTERVAL '46 days', false),
    (4, 4, 'RemoveTeacher', 1, NOW() - INTERVAL '45 days', false),
    (5, 5, 'EditOrgSafetyPolicy', 1, NOW() - INTERVAL '19 days', false),
    (6, 6, 'EditOrgInfo', 1, NOW() - INTERVAL '58 days', false),
    (7, 7, 'ViewOrgDashboard', 1, NOW() - INTERVAL '56 days', false),
    (8, 8, 'CreateClassGroup', 1, NOW() - INTERVAL '20 days', false),
    (9, 1, 'ViewOrgDashboard', 1, NOW() - INTERVAL '48 days', false),
    (10, 6, 'ViewOrgDashboard', 1, NOW() - INTERVAL '58 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 6. org_safety_policy_templates (3 - unique theo OrganizationId)
INSERT INTO org_safety_policy_templates ("Id", "OrganizationId", "MaxStoryLengthBaseline", "RequiredApprovalModeDefault", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2000, 'AlwaysManual', NOW() - INTERVAL '48 days', false),
    (2, 2, 2500, 'AutoPublishOnThreshold', NOW() - INTERVAL '46 days', false),
    (3, 3, 1800, 'AlwaysManual', NOW() - INTERVAL '19 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 7. child_profiles (10)
INSERT INTO child_profiles ("Id", "OwnerUserId", "OrganizationId", "Nickname", "AgeBand", "Language", "Scope", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, NULL, 'Be An', 'Age_6_8', 'vi', 'Personal', 'Active', NOW() - INTERVAL '40 days', false),
    (2, 2, 1, 'Be Binh', 'Age_9_12', 'vi', 'Organization', 'Active', NOW() - INTERVAL '39 days', false),
    (3, 3, NULL, 'Be Chi', 'Age_6_8', 'vi', 'Personal', 'Active', NOW() - INTERVAL '38 days', false),
    (4, 3, 2, 'Be Dat', 'Age_9_12', 'vi', 'Organization', 'Active', NOW() - INTERVAL '37 days', false),
    (5, 4, NULL, 'Be Em', 'Age_6_8', 'vi', 'Personal', 'PendingParentConsent', NOW() - INTERVAL '36 days', false),
    (6, 4, 1, 'Be Phuc', 'Age_9_12', 'vi', 'Organization', 'Active', NOW() - INTERVAL '35 days', false),
    (7, 5, NULL, 'Be Giang', 'Age_6_8', 'vi', 'Personal', 'Active', NOW() - INTERVAL '34 days', false),
    (8, 5, 2, 'Be Hanh', 'Age_9_12', 'vi', 'Organization', 'Active', NOW() - INTERVAL '33 days', false),
    (9, 6, NULL, 'Be Y', 'Age_6_8', 'vi', 'Personal', 'Draft', NOW() - INTERVAL '32 days', false),
    (10, 6, 3, 'Be Khang', 'Age_9_12', 'vi', 'Organization', 'PendingSupervision', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 8. org_safety_policy_categories (8)
INSERT INTO org_safety_policy_categories ("Id", "OrgSafetyPolicyTemplateId", "ContentCategoryId", "Rule", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Allowed', NOW() - INTERVAL '48 days', false),
    (2, 1, 2, 'Restricted', NOW() - INTERVAL '48 days', false),
    (3, 1, 3, 'Allowed', NOW() - INTERVAL '48 days', false),
    (4, 2, 1, 'Allowed', NOW() - INTERVAL '46 days', false),
    (5, 2, 4, 'Restricted', NOW() - INTERVAL '46 days', false),
    (6, 2, 5, 'Allowed', NOW() - INTERVAL '46 days', false),
    (7, 3, 2, 'Blocked', NOW() - INTERVAL '19 days', false),
    (8, 3, 6, 'Allowed', NOW() - INTERVAL '19 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 9. org_consent_records (5 - chi cho child_profiles co OrganizationId)
INSERT INTO org_consent_records ("Id", "ChildProfileId", "OrganizationId", "DecidedByUserId", "DecidedAt", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 1, 2, NOW() - INTERVAL '38 days', 'Accepted', NOW() - INTERVAL '39 days', false),
    (2, 4, 2, 3, NOW() - INTERVAL '36 days', 'Accepted', NOW() - INTERVAL '37 days', false),
    (3, 6, 1, 4, NOW() - INTERVAL '34 days', 'Accepted', NOW() - INTERVAL '35 days', false),
    (4, 8, 2, NULL, NULL, 'Pending', NOW() - INTERVAL '33 days', false),
    (5, 10, 3, NULL, NULL, 'Pending', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 10. safety_policies (10 - unique theo ChildProfileId)
INSERT INTO safety_policies ("Id", "ChildProfileId", "MaxStoryLength", "ParentalGateEnabled", "ConsentRecorded", "ConsentRecordedAt", "RequiredApprovalMode", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1500, true, true, NOW() - INTERVAL '40 days', 'AlwaysManual', NOW() - INTERVAL '40 days', false),
    (2, 2, 1800, true, true, NOW() - INTERVAL '39 days', 'AutoPublishOnThreshold', NOW() - INTERVAL '39 days', false),
    (3, 3, 1500, true, true, NOW() - INTERVAL '38 days', 'AlwaysManual', NOW() - INTERVAL '38 days', false),
    (4, 4, 2000, true, true, NOW() - INTERVAL '37 days', 'AutoPublishOnThreshold', NOW() - INTERVAL '37 days', false),
    (5, 5, 1200, true, false, NULL, 'AlwaysManual', NOW() - INTERVAL '36 days', false),
    (6, 6, 1800, true, true, NOW() - INTERVAL '35 days', 'AlwaysManual', NOW() - INTERVAL '35 days', false),
    (7, 7, 1500, true, true, NOW() - INTERVAL '34 days', 'AlwaysManual', NOW() - INTERVAL '34 days', false),
    (8, 8, 2000, true, true, NOW() - INTERVAL '33 days', 'AutoPublishOnThreshold', NOW() - INTERVAL '33 days', false),
    (9, 9, 1000, false, false, NULL, 'AlwaysManual', NOW() - INTERVAL '32 days', false),
    (10, 10, 1800, true, false, NULL, 'AlwaysManual', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 11. safety_policy_categories (10)
INSERT INTO safety_policy_categories ("Id", "SafetyPolicyId", "ContentCategoryId", "Rule", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Allowed', NOW() - INTERVAL '40 days', false),
    (2, 2, 2, 'Restricted', NOW() - INTERVAL '39 days', false),
    (3, 3, 3, 'Allowed', NOW() - INTERVAL '38 days', false),
    (4, 4, 4, 'Allowed', NOW() - INTERVAL '37 days', false),
    (5, 5, 5, 'Restricted', NOW() - INTERVAL '36 days', false),
    (6, 6, 6, 'Allowed', NOW() - INTERVAL '35 days', false),
    (7, 7, 1, 'Blocked', NOW() - INTERVAL '34 days', false),
    (8, 8, 2, 'Allowed', NOW() - INTERVAL '33 days', false),
    (9, 9, 3, 'Restricted', NOW() - INTERVAL '32 days', false),
    (10, 10, 4, 'Allowed', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 12. learning_profiles (10 - unique theo ChildProfileId)
INSERT INTO learning_profiles ("Id", "ChildProfileId", "ReadingLevel", "ComprehensionGoal", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Hieu duoc y chinh cua cau chuyen', NOW() - INTERVAL '40 days', false),
    (2, 2, 3, 'Tom tat duoc dien bien cau chuyen', NOW() - INTERVAL '39 days', false),
    (3, 3, 1, 'Nhan biet nhan vat chinh', NOW() - INTERVAL '38 days', false),
    (4, 4, 4, 'Suy luan duoc bai hoc rut ra', NOW() - INTERVAL '37 days', false),
    (5, 5, 2, 'Doc troi chay doan van ngan', NOW() - INTERVAL '36 days', false),
    (6, 6, 3, 'Hieu moi quan he nhan qua trong truyen', NOW() - INTERVAL '35 days', false),
    (7, 7, 1, 'Nhan biet tu vung co ban', NOW() - INTERVAL '34 days', false),
    (8, 8, 5, 'Phan tich duoc dong co nhan vat', NOW() - INTERVAL '33 days', false),
    (9, 9, 2, 'Doc hieu cau don gian', NOW() - INTERVAL '32 days', false),
    (10, 10, 3, 'Ke lai duoc cau chuyen bang loi cua minh', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 13. learning_profile_topics (10)
INSERT INTO learning_profile_topics ("Id", "LearningProfileId", "Topic", "Relation", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Dong vat', 'FavoriteTopic', NOW() - INTERVAL '40 days', false),
    (2, 2, 'Co tich', 'FavoriteTopic', NOW() - INTERVAL '39 days', false),
    (3, 3, 'Phieu luu', 'PriorityFocusArea', NOW() - INTERVAL '38 days', false),
    (4, 4, 'Khoa hoc', 'FavoriteTopic', NOW() - INTERVAL '37 days', false),
    (5, 5, 'Tinh ban', 'PriorityFocusArea', NOW() - INTERVAL '36 days', false),
    (6, 6, 'Gia dinh', 'FavoriteTopic', NOW() - INTERVAL '35 days', false),
    (7, 7, 'Sieu nhan', 'FavoriteTopic', NOW() - INTERVAL '34 days', false),
    (8, 8, 'Bien ca', 'PriorityFocusArea', NOW() - INTERVAL '33 days', false),
    (9, 9, 'Vu tru', 'FavoriteTopic', NOW() - INTERVAL '32 days', false),
    (10, 10, 'The thao', 'PriorityFocusArea', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 14. class_groups (5)
INSERT INTO class_groups ("Id", "OrganizationId", "TeacherUserId", "Name", "Status", "KnowledgeTreeExp", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, 'Lop 1A', 'Active', 50, NOW() - INTERVAL '38 days', false),
    (2, 1, 8, 'Lop 1B', 'Active', 30, NOW() - INTERVAL '37 days', false),
    (3, 2, 9, 'Lop 2A', 'Active', 80, NOW() - INTERVAL '36 days', false),
    (4, 2, 10, 'Lop 2B', 'Active', 20, NOW() - INTERVAL '35 days', false),
    (5, 3, 7, 'Lop 3A', 'Active', 0, NOW() - INTERVAL '17 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 15. class_group_members (10)
INSERT INTO class_group_members ("Id", "ClassGroupId", "ChildProfileId", "JoinedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, NOW() - INTERVAL '35 days', NOW() - INTERVAL '35 days', false),
    (2, 1, 6, NOW() - INTERVAL '34 days', NOW() - INTERVAL '34 days', false),
    (3, 2, 4, NOW() - INTERVAL '33 days', NOW() - INTERVAL '33 days', false),
    (4, 2, 8, NOW() - INTERVAL '32 days', NOW() - INTERVAL '32 days', false),
    (5, 3, 10, NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', false),
    (6, 3, 2, NOW() - INTERVAL '31 days', NOW() - INTERVAL '31 days', false),
    (7, 4, 6, NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false),
    (8, 4, 10, NOW() - INTERVAL '16 days', NOW() - INTERVAL '16 days', false),
    (9, 5, 4, NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', false),
    (10, 5, 8, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 16. stories (10)
INSERT INTO stories ("Id", "Title", "Description", "Content", "CoverImageUrl", "Genre", "MoralLesson", "Language", "AgeBand", "Status", "Source", "IsPublished", "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Chu Tho Thong Minh', 'Cau chuyen ve chu tho biet dung tri thong minh de thoat hiem', 'Ngay xua, co mot chu tho rat thong minh song trong khu rung...', 'https://example.com/covers/story1.png', 'Ngu ngon', 'Su thong minh giup vuot qua kho khan', 'vi', 'Age_6_8', 'Ready', 'Manual', true, 2, 1, NOW() - INTERVAL '30 days', false),
    (2, 'Rung Xanh Ky Dieu', 'Hanh trinh kham pha khu rung day mau sac', 'Trong khu rung xanh co rat nhieu dieu ky dieu dang cho duoc kham pha...', 'https://example.com/covers/story2.png', 'Phieu luu', 'Tran trong thien nhien', 'vi', 'Age_9_12', 'Draft', 'Ai', false, 7, 2, NOW() - INTERVAL '28 days', false),
    (3, 'Vuong Quoc Co Tich', 'Mot vuong quoc noi phep thuat va long tot ngu tri', 'Ngay xua o mot vuong quoc xa xoi...', 'https://example.com/covers/story3.png', 'Co tich', 'Long tot se duoc den dap', 'vi', 'Age_6_8', 'Ready', 'Manual', true, 3, 3, NOW() - INTERVAL '26 days', false),
    (4, 'Hanh Trinh Vu Tru', 'Chuyen phieu luu cua mot nha du hanh nho tuoi', 'Mot ngay no, ban nho quyet dinh du hanh len vu tru...', 'https://example.com/covers/story4.png', 'Khoa hoc vien tuong', 'Kham pha va hoc hoi khong ngung', 'vi', 'Age_9_12', 'ContentReview', 'Ai', false, 8, 4, NOW() - INTERVAL '24 days', false),
    (5, 'Nguoi Ban Tot', 'Cau chuyen ve tinh ban chan thanh giua hai dua tre', 'Hai nguoi ban than thiet cung nhau vuot qua kho khan...', 'https://example.com/covers/story5.png', 'Doi thuong', 'Tinh ban chan thanh la vo gia', 'vi', 'Age_6_8', 'Approved', 'Manual', false, 4, 5, NOW() - INTERVAL '22 days', false),
    (6, 'Bi Mat Dai Duong', 'Kham pha the gioi ky dieu duoi day dai duong', 'Sau lop song bien xanh la ca mot the gioi ky dieu...', 'https://example.com/covers/story6.png', 'Phieu luu', 'Bao ve moi truong bien', 'vi', 'Age_9_12', 'Ready', 'Ai', true, 9, 6, NOW() - INTERVAL '20 days', false),
    (7, 'Gia Dinh Hanh Phuc', 'Nhung khoanh khac am ap trong mot gia dinh nho', 'Moi buoi toi, ca gia dinh quay quan ben nhau...', 'https://example.com/covers/story7.png', 'Doi thuong', 'Yeu thuong gia dinh', 'vi', 'Age_6_8', 'Draft', 'Manual', false, 5, 7, NOW() - INTERVAL '19 days', false),
    (8, 'Sieu Nhan Ti Hon', 'Cau be co suc manh dac biet bao ve xom lang', 'Trong mot ngoi lang nho, co mot cau be so huu suc manh dac biet...', 'https://example.com/covers/story8.png', 'Sieu anh hung', 'Dung cam bao ve nguoi yeu the', 'vi', 'Age_9_12', 'OutlineReview', 'Ai', false, 10, 8, NOW() - INTERVAL '17 days', false),
    (9, 'Chuyen Phieu Luu Rung Ram', 'Nhom ban cung nhau kham pha khu rung bi an', 'Bon nguoi ban quyet dinh kham pha khu rung phia sau lang...', 'https://example.com/covers/story9.png', 'Phieu luu', 'Doan ket tao nen suc manh', 'vi', 'Age_6_8', 'Rejected', 'Manual', false, 6, 9, NOW() - INTERVAL '15 days', false),
    (10, 'Tran Bong Da Dang Nho', 'Bai hoc ve tinh than the thao va fair-play', 'Tran chung ket bong da cua truong dien ra day kich tinh...', 'https://example.com/covers/story10.png', 'The thao', 'Tinh than fair-play trong the thao', 'vi', 'Age_9_12', 'Ready', 'Ai', true, 7, 10, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 17. story_categories (10)
INSERT INTO story_categories ("Id", "StoryId", "ContentCategoryId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NOW() - INTERVAL '30 days', false),
    (2, 2, 2, NOW() - INTERVAL '28 days', false),
    (3, 3, 3, NOW() - INTERVAL '26 days', false),
    (4, 4, 4, NOW() - INTERVAL '24 days', false),
    (5, 5, 5, NOW() - INTERVAL '22 days', false),
    (6, 6, 6, NOW() - INTERVAL '20 days', false),
    (7, 7, 1, NOW() - INTERVAL '19 days', false),
    (8, 8, 2, NOW() - INTERVAL '17 days', false),
    (9, 9, 3, NOW() - INTERVAL '15 days', false),
    (10, 10, 4, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 18. story_versions (10)
INSERT INTO story_versions ("Id", "StoryId", "VersionNo", "Title", "Content", "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson", "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId", "IsCurrent", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Chu Tho Thong Minh - Ban 1', 'Ngay xua, co mot chu tho rat thong minh song trong khu rung...', 'Gioi thieu chu tho va khu rung', 'Chu tho gap nguy hiem va tim cach thoat', 'Chu tho thoat hiem nho su thong minh', 'Su thong minh giup vuot qua kho khan', 3.200, 78.500, 95.000, 'Initial', 2, true, NOW() - INTERVAL '30 days', false),
    (2, 2, 1, 'Rung Xanh Ky Dieu - Ban 1', 'Trong khu rung xanh co rat nhieu dieu ky dieu dang cho duoc kham pha...', 'Gioi thieu khu rung', 'Hanh trinh kham pha', 'Bai hoc ve thien nhien', 'Tran trong thien nhien', 4.100, 72.300, 90.000, 'Initial', NULL, true, NOW() - INTERVAL '28 days', false),
    (3, 3, 1, 'Vuong Quoc Co Tich - Ban 1', 'Ngay xua o mot vuong quoc xa xoi...', 'Gioi thieu vuong quoc', 'Thu thach cua nhan vat chinh', 'Long tot chien thang', 'Long tot se duoc den dap', 2.800, 82.000, 97.000, 'Initial', 3, true, NOW() - INTERVAL '26 days', false),
    (4, 4, 1, 'Hanh Trinh Vu Tru - Ban 1', 'Mot ngay no, ban nho quyet dinh du hanh len vu tru...', 'Gioi thieu chuyen du hanh', 'Kham pha cac hanh tinh', 'Tro ve trai dat voi nhieu bai hoc', 'Kham pha va hoc hoi khong ngung', 4.500, 68.000, 88.000, 'Initial', 8, true, NOW() - INTERVAL '24 days', false),
    (5, 5, 1, 'Nguoi Ban Tot - Ban 1', 'Hai nguoi ban than thiet cung nhau vuot qua kho khan...', 'Gioi thieu hai nguoi ban', 'Kho khan thu thach tinh ban', 'Tinh ban duoc cung co', 'Tinh ban chan thanh la vo gia', 3.000, 80.000, 96.000, 'Initial', NULL, true, NOW() - INTERVAL '22 days', false),
    (6, 6, 1, 'Bi Mat Dai Duong - Ban 1', 'Sau lop song bien xanh la ca mot the gioi ky dieu...', 'Gioi thieu the gioi dai duong', 'Kham pha bi mat duoi day bien', 'Bai hoc bao ve moi truong', 'Bao ve moi truong bien', 3.900, 74.000, 92.000, 'Initial', 9, true, NOW() - INTERVAL '20 days', false),
    (7, 7, 1, 'Gia Dinh Hanh Phuc - Ban 1', 'Moi buoi toi, ca gia dinh quay quan ben nhau...', 'Gioi thieu gia dinh', 'Nhung khoanh khac dang nho', 'Tran trong gia dinh', 'Yeu thuong gia dinh', 2.500, 85.000, 98.000, 'Initial', NULL, true, NOW() - INTERVAL '19 days', false),
    (8, 8, 1, 'Sieu Nhan Ti Hon - Ban 1', 'Trong mot ngoi lang nho, co mot cau be so huu suc manh dac biet...', 'Gioi thieu sieu nhan ti hon', 'Cuoc chien bao ve xom lang', 'Chien thang cai xau', 'Dung cam bao ve nguoi yeu the', 4.200, 70.000, 89.000, 'Initial', 10, true, NOW() - INTERVAL '17 days', false),
    (9, 9, 1, 'Chuyen Phieu Luu Rung Ram - Ban 1', 'Bon nguoi ban quyet dinh kham pha khu rung phia sau lang...', 'Gioi thieu nhom ban', 'Thu thach trong rung ram', 'Doan ket vuot kho khan', 'Doan ket tao nen suc manh', 3.600, 76.000, 91.000, 'Initial', NULL, false, NOW() - INTERVAL '15 days', false),
    (10, 10, 1, 'Tran Bong Da Dang Nho - Ban 1', 'Tran chung ket bong da cua truong dien ra day kich tinh...', 'Gioi thieu tran dau', 'Dien bien gay can', 'Bai hoc ve fair-play', 'Tinh than fair-play trong the thao', 4.000, 73.000, 94.000, 'Initial', 7, true, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 19. discussion_questions (10)
INSERT INTO discussion_questions ("Id", "StoryVersionId", "Question", "IsMoralLesson", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Bai hoc chinh cua cau chuyen la gi?', true, NOW() - INTERVAL '30 days', false),
    (2, 2, 'Vi sao chung ta can bao ve rung xanh?', true, NOW() - INTERVAL '28 days', false),
    (3, 3, 'Nhan vat nao trong truyen the hien long tot?', false, NOW() - INTERVAL '26 days', false),
    (4, 4, 'Con hoc duoc dieu gi tu chuyen du hanh vu tru?', true, NOW() - INTERVAL '24 days', false),
    (5, 5, 'Tinh ban trong cau chuyen duoc the hien nhu the nao?', false, NOW() - INTERVAL '22 days', false),
    (6, 6, 'Lam sao de bao ve dai duong?', true, NOW() - INTERVAL '20 days', false),
    (7, 7, 'Gia dinh trong truyen co diem gi dac biet?', false, NOW() - INTERVAL '19 days', false),
    (8, 8, 'Vi sao sieu nhan ti hon lai dung cam?', true, NOW() - INTERVAL '17 days', false),
    (9, 9, 'Doan ket giup nhom ban vuot qua kho khan nhu the nao?', true, NOW() - INTERVAL '15 days', false),
    (10, 10, 'Fair-play trong the thao nghia la gi?', true, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 20. quiz_items (10)
INSERT INTO quiz_items ("Id", "StoryVersionId", "Type", "Question", "Choices", "CorrectAnswer", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'MultipleChoice', 'Nhan vat chinh trong cau chuyen la ai?', '["Chu Tho","Chu Ruoi","Chu Rua"]', 'Chu Tho', NOW() - INTERVAL '30 days', false),
    (2, 2, 'TrueFalse', 'Khu rung xanh co nhieu dieu ky dieu.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '28 days', false),
    (3, 3, 'MultipleChoice', 'Vuong quoc trong truyen co dieu gi dac biet?', '["Phep thuat","Cong nghe","Chien tranh"]', 'Phep thuat', NOW() - INTERVAL '26 days', false),
    (4, 4, 'ShortAnswer', 'Ke ten mot hanh tinh xuat hien trong truyen.', NULL, 'Sao Hoa', NOW() - INTERVAL '24 days', false),
    (5, 5, 'TrueFalse', 'Hai nguoi ban trong truyen luon giup do nhau.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'MultipleChoice', 'Bi mat duoi day dai duong la gi?', '["Rap san ho ky dieu","Kho bau","Tau dam"]', 'Rap san ho ky dieu', NOW() - INTERVAL '20 days', false),
    (7, 7, 'ShortAnswer', 'Gia dinh trong truyen thuong lam gi vao buoi toi?', NULL, 'Quay quan ben nhau', NOW() - INTERVAL '19 days', false),
    (8, 8, 'TrueFalse', 'Sieu nhan ti hon co suc manh dac biet.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '17 days', false),
    (9, 9, 'MultipleChoice', 'Nhom ban trong truyen co bao nhieu nguoi?', '["Ba","Bon","Nam"]', 'Bon', NOW() - INTERVAL '15 days', false),
    (10, 10, 'ShortAnswer', 'Fair-play nghia la gi?', NULL, 'Choi dep va trung thuc', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 21. media_assets (10)
INSERT INTO media_assets ("Id", "StoryVersionId", "Type", "Url", "Status", "SceneIndex", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Illustration', 'https://example.com/media/story1-scene1.png', 'Ready', 1, NOW() - INTERVAL '30 days', false),
    (2, 2, 'TtsAudio', 'https://example.com/media/story2-audio1.mp3', 'Ready', 1, NOW() - INTERVAL '28 days', false),
    (3, 3, 'Illustration', 'https://example.com/media/story3-scene1.png', 'Ready', 1, NOW() - INTERVAL '26 days', false),
    (4, 4, 'Illustration', 'https://example.com/media/story4-scene1.png', 'Processing', 1, NOW() - INTERVAL '24 days', false),
    (5, 5, 'TtsAudio', 'https://example.com/media/story5-audio1.mp3', 'Ready', 1, NOW() - INTERVAL '22 days', false),
    (6, 6, 'Illustration', 'https://example.com/media/story6-scene1.png', 'Ready', 1, NOW() - INTERVAL '20 days', false),
    (7, 7, 'TtsAudio', 'https://example.com/media/story7-audio1.mp3', 'Queued', 1, NOW() - INTERVAL '19 days', false),
    (8, 8, 'Illustration', 'https://example.com/media/story8-scene1.png', 'Ready', 1, NOW() - INTERVAL '17 days', false),
    (9, 9, 'Illustration', 'https://example.com/media/story9-scene1.png', 'Failed', 1, NOW() - INTERVAL '15 days', false),
    (10, 10, 'TtsAudio', 'https://example.com/media/story10-audio1.mp3', 'Ready', 1, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 22. story_vocabulary (10)
INSERT INTO story_vocabulary ("Id", "StoryVersionId", "Term", "Definition", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'thong minh', 'Co kha nang suy nghi nhanh va chinh xac', NOW() - INTERVAL '30 days', false),
    (2, 2, 'dung cam', 'Khong so hai truoc kho khan, nguy hiem', NOW() - INTERVAL '28 days', false),
    (3, 3, 'kien nhan', 'Chiu dung, khong noi nong khi gap kho khan', NOW() - INTERVAL '26 days', false),
    (4, 4, 'trung thuc', 'Luon noi that, khong gian doi', NOW() - INTERVAL '24 days', false),
    (5, 5, 'doan ket', 'Cung nhau hop suc de dat muc tieu chung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'yeu thuong', 'Tinh cam quan tam, che cho nguoi khac', NOW() - INTERVAL '20 days', false),
    (7, 7, 'sang tao', 'Co kha nang nghi ra y tuong moi', NOW() - INTERVAL '19 days', false),
    (8, 8, 'cham chi', 'Chiu kho lam viec, khong luoi bieng', NOW() - INTERVAL '17 days', false),
    (9, 9, 'le phep', 'Cu xu dung muc, ton trong nguoi khac', NOW() - INTERVAL '15 days', false),
    (10, 10, 'tu tin', 'Tin tuong vao kha nang cua ban than', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 23. prompt_catalog_versions (5)
INSERT INTO prompt_catalog_versions ("Id", "VersionNo", "GradeBand", "EffectiveDate", "RestrictedKeywords", "Status", "CreatedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'v1.0', '6-8', NOW() - INTERVAL '55 days', 'bao luc, so hai qua muc', 'Deprecated', 1, NOW() - INTERVAL '55 days', false),
    (2, 'v1.1', '6-8', NOW() - INTERVAL '40 days', 'bao luc, so hai qua muc, phan biet doi xu', 'Published', 1, NOW() - INTERVAL '40 days', false),
    (3, 'v1.0', '9-12', NOW() - INTERVAL '50 days', 'bao luc cuc doan', 'RolledBack', 1, NOW() - INTERVAL '50 days', false),
    (4, 'v1.1', '9-12', NOW() - INTERVAL '30 days', 'bao luc cuc doan, noi dung nguoi lon', 'Published', 1, NOW() - INTERVAL '30 days', false),
    (5, 'v1.2', '9-12', NOW() - INTERVAL '5 days', 'bao luc cuc doan, noi dung nguoi lon, chinh tri', 'Draft', 1, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 24. story_generation_jobs (10)
INSERT INTO story_generation_jobs ("Id", "StoryId", "PromptCatalogVersionId", "Stage", "StartedAt", "CompletedAt", "GuardrailResult", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 2, 'Ready', NOW() - INTERVAL '28 days', NOW() - INTERVAL '28 days', 'Passed', NOW() - INTERVAL '28 days', false),
    (2, 4, 4, 'ContentReview', NOW() - INTERVAL '24 days', NULL, 'Passed', NOW() - INTERVAL '24 days', false),
    (3, 6, 4, 'Ready', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', 'Passed', NOW() - INTERVAL '20 days', false),
    (4, 8, 4, 'OutlineApproved', NOW() - INTERVAL '17 days', NULL, 'FastFail', NOW() - INTERVAL '17 days', false),
    (5, 10, 5, 'Ready', NOW() - INTERVAL '13 days', NOW() - INTERVAL '13 days', 'Passed', NOW() - INTERVAL '13 days', false),
    (6, 1, 1, 'Ready', NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', 'Passed', NOW() - INTERVAL '30 days', false),
    (7, 3, 3, 'Ready', NOW() - INTERVAL '26 days', NOW() - INTERVAL '26 days', 'Passed', NOW() - INTERVAL '26 days', false),
    (8, 5, 3, 'Approved', NOW() - INTERVAL '22 days', NOW() - INTERVAL '22 days', 'Passed', NOW() - INTERVAL '22 days', false),
    (9, 7, 2, 'OutlineDraft', NOW() - INTERVAL '19 days', NULL, 'Passed', NOW() - INTERVAL '19 days', false),
    (10, 9, 2, 'Rejected', NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', 'FastFail', NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 25. assignments (10)
INSERT INTO assignments ("Id", "StoryId", "AssignedByUserId", "ChildProfileId", "ClassGroupId", "AssignedAt", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, NULL, 1, NOW() - INTERVAL '15 days', 'Assigned', NOW() - INTERVAL '15 days', false),
    (2, 2, 8, NULL, 2, NOW() - INTERVAL '14 days', 'InProgress', NOW() - INTERVAL '14 days', false),
    (3, 3, 9, NULL, 3, NOW() - INTERVAL '13 days', 'Completed', NOW() - INTERVAL '13 days', false),
    (4, 4, 10, NULL, 4, NOW() - INTERVAL '12 days', 'Cancelled', NOW() - INTERVAL '12 days', false),
    (5, 5, 7, NULL, 5, NOW() - INTERVAL '11 days', 'Assigned', NOW() - INTERVAL '11 days', false),
    (6, 6, 8, NULL, 1, NOW() - INTERVAL '10 days', 'InProgress', NOW() - INTERVAL '10 days', false),
    (7, 7, 9, NULL, 2, NOW() - INTERVAL '9 days', 'Completed', NOW() - INTERVAL '9 days', false),
    (8, 8, 10, NULL, 3, NOW() - INTERVAL '8 days', 'Assigned', NOW() - INTERVAL '8 days', false),
    (9, 9, 7, NULL, 4, NOW() - INTERVAL '7 days', 'InProgress', NOW() - INTERVAL '7 days', false),
    (10, 10, 8, NULL, 5, NOW() - INTERVAL '6 days', 'Completed', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 26. assignment_recipients (10)
INSERT INTO assignment_recipients ("Id", "AssignmentId", "ChildProfileId", "Status", "CompletedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Pending', NULL, NOW() - INTERVAL '15 days', false),
    (2, 2, 4, 'Accepted', NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 10, 'Completed', NOW() - INTERVAL '10 days', NOW() - INTERVAL '13 days', false),
    (4, 4, 6, 'Revoked', NULL, NOW() - INTERVAL '12 days', false),
    (5, 5, 8, 'Pending', NULL, NOW() - INTERVAL '11 days', false),
    (6, 6, 2, 'Accepted', NULL, NOW() - INTERVAL '10 days', false),
    (7, 7, 4, 'Completed', NOW() - INTERVAL '8 days', NOW() - INTERVAL '9 days', false),
    (8, 8, 6, 'Pending', NULL, NOW() - INTERVAL '8 days', false),
    (9, 9, 8, 'Accepted', NULL, NOW() - INTERVAL '7 days', false),
    (10, 10, 10, 'Completed', NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 27. reading_sessions (10)
INSERT INTO reading_sessions ("Id", "ChildProfileId", "StoryId", "AssignmentRecipientId", "StartedAt", "CompletedAt", "Status", "PagesCompleted", "TimeSpentSeconds", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NULL, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days' + INTERVAL '10 minutes', 'Completed', 10, 600, NOW() - INTERVAL '10 days', false),
    (2, 2, 1, 1, NOW() - INTERVAL '9 days', NULL, 'Reading', 3, 200, NOW() - INTERVAL '9 days', false),
    (3, 3, 3, NULL, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days' + INTERVAL '8 minutes', 'Completed', 8, 480, NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 2, NOW() - INTERVAL '7 days', NULL, 'Activity', 5, 350, NOW() - INTERVAL '7 days', false),
    (5, 5, 5, NULL, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days' + INTERVAL '6 minutes', 'Completed', 6, 360, NOW() - INTERVAL '6 days', false),
    (6, 6, 6, NULL, NOW() - INTERVAL '5 days', NULL, 'Started', 1, 60, NOW() - INTERVAL '5 days', false),
    (7, 7, 7, NULL, NOW() - INTERVAL '4 days', NULL, 'Abandoned', 2, 90, NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 5, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days' + INTERVAL '12 minutes', 'Completed', 12, 720, NOW() - INTERVAL '3 days', false),
    (9, 9, 9, NULL, NOW() - INTERVAL '2 days', NULL, 'Reading', 4, 240, NOW() - INTERVAL '2 days', false),
    (10, 10, 10, NULL, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days' + INTERVAL '9 minutes', 'Completed', 9, 540, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 28. reading_progress (10)
INSERT INTO reading_progress ("Id", "ChildProfileId", "StoryId", "LastPageRead", "IsBookmarked", "IsFavorited", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 10, true, true, NOW() - INTERVAL '10 days', false),
    (2, 2, 1, 3, false, false, NOW() - INTERVAL '9 days', false),
    (3, 3, 3, 8, true, true, NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 5, false, true, NOW() - INTERVAL '7 days', false),
    (5, 5, 5, 6, true, false, NOW() - INTERVAL '6 days', false),
    (6, 6, 6, 1, false, false, NOW() - INTERVAL '5 days', false),
    (7, 7, 7, 2, false, false, NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 12, true, true, NOW() - INTERVAL '3 days', false),
    (9, 9, 9, 4, false, true, NOW() - INTERVAL '2 days', false),
    (10, 10, 10, 9, true, true, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 29. quiz_attempts (10)
INSERT INTO quiz_attempts ("Id", "QuizItemId", "ReadingSessionId", "AnswerGiven", "IsCorrect", "AnsweredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Chu Tho', true, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 2, 'Dung', true, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 3, 'Cong nghe', false, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 'Sao Hoa', true, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 5, 'Dung', true, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 6, 'Kho bau', false, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 7, 'Quay quan ben nhau', true, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 'Dung', true, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 9, 'Bon', true, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 10, 'Choi dep va trung thuc', true, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 30. telemetry_logs (10)
INSERT INTO telemetry_logs ("Id", "ReadingSessionId", "EventType", "EventPayload", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'SessionCompleted', '{"pages":10}', NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 'SessionStarted', '{}', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 'PageCompleted', '{"page":8}', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 'PageViewed', '{"page":5}', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 'QuizCompleted', '{"score":100}', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 'TtsStarted', '{}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 'VocabularyOpened', '{"term":"sang tao"}', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 'StoryFavorited', '{"storyId":8}', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 'QuizAnswered', '{"correct":true}', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 'BadgeUnlocked', '{"badge":"FAST_READER"}', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 31. vocabulary_notebook_entries (10)
INSERT INTO vocabulary_notebook_entries ("Id", "ChildProfileId", "StoryVocabularyId", "CollectedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 2, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 3, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 4, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 5, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 6, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 7, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 8, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 9, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 10, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 32. achievements (10 - unique theo ChildProfileId)
INSERT INTO achievements ("Id", "ChildProfileId", "ExpTotal", "StreakDays", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 120, 5, NOW() - INTERVAL '10 days', false),
    (2, 2, 80, 2, NOW() - INTERVAL '9 days', false),
    (3, 3, 200, 8, NOW() - INTERVAL '8 days', false),
    (4, 4, 150, 4, NOW() - INTERVAL '7 days', false),
    (5, 5, 60, 1, NOW() - INTERVAL '6 days', false),
    (6, 6, 90, 3, NOW() - INTERVAL '5 days', false),
    (7, 7, 40, 1, NOW() - INTERVAL '4 days', false),
    (8, 8, 250, 10, NOW() - INTERVAL '3 days', false),
    (9, 9, 70, 2, NOW() - INTERVAL '2 days', false),
    (10, 10, 180, 6, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 33. badges (10)
INSERT INTO badges ("Id", "ChildProfileId", "BadgeCode", "EarnedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'FIRST_STORY_COMPLETED', NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 'FAST_READER', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 'VOCAB_MASTER', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 'QUIZ_CHAMPION', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 'STREAK_7_DAYS', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 'BOOKWORM', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 'EARLY_BIRD', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 'NIGHT_OWL', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 'HELPER', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 'EXPLORER', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 34. learning_insights (10)
INSERT INTO learning_insights ("Id", "ChildProfileId", "Observation", "Evidence", "Status", "DetectedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Tre doc nhanh hon muc trung binh cua do tuoi', 'Thoi gian hoan thanh giam 20% so voi tuan truoc', 'InsightDetected', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (2, 2, 'Tre gap kho khan voi tu vung moi', 'Ty le tra loi dung tu vung giam con 60%', 'Reviewed', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (3, 3, 'Tre rat thich the loai co tich', 'Da doc 5 truyen co tich trong tuan', 'InsightDetected', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (4, 4, 'Tre co kha nang suy luan tot', 'Tra loi dung 90% cau hoi suy luan', 'Reviewed', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (5, 5, 'Tre it hoan thanh bai doc duoc giao', 'Chi hoan thanh 2/5 bai duoc giao', 'InsightDetected', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (6, 6, 'Tre tien bo ro ret ve toc do doc', 'Toc do doc tang 30% trong thang', 'Reviewed', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (7, 7, 'Tre can ho tro them ve phat am', 'Ghi nhan nhieu loi phat am trong AsrWordResult', 'InsightDetected', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (8, 8, 'Tre rat tich cuc tham gia thao luan', 'Tra loi 100% cau hoi thao luan', 'Reviewed', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (9, 9, 'Tre it tuong tac voi ung dung gan day', 'So phien doc giam 50% so voi thang truoc', 'InsightDetected', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 'Tre thich the loai the thao va phieu luu', 'Da doc va danh dau yeu thich 4 truyen the thao', 'Reviewed', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 35. recommendations (10)
INSERT INTO recommendations ("Id", "ChildProfileId", "LearningInsightId", "Category", "CurrentState", "ProposedChange", "Evidence", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'ReadingDifficulty', 'Dang o muc do 2', 'De xuat nang len muc do 3', 'Diem quiz dat 90% trong 3 lan gan nhat', 'AwaitingReview', NOW() - INTERVAL '8 days', false),
    (2, 2, 2, 'LearningFocus', 'Tap trung vao doc hieu', 'De xuat tap trung on tu vung', 'Ty le tra loi dung tu vung giam con 60%', 'Detected', NOW() - INTERVAL '7 days', false),
    (3, 3, 3, 'ContentPersonalization', 'Noi dung da the loai', 'De xuat uu tien noi dung co tich', 'Da doc 5 truyen co tich trong tuan', 'Approved', NOW() - INTERVAL '6 days', false),
    (4, 4, 4, 'ReadingDifficulty', 'Dang o muc do 4', 'De xuat giu nguyen muc do', 'Tra loi dung 90% cau hoi suy luan', 'Applied', NOW() - INTERVAL '5 days', false),
    (5, 5, 5, 'LearningIntervention', 'Hoan thanh bai giao thap', 'De xuat can thiep, nhac nho phu huynh', 'Chi hoan thanh 2/5 bai duoc giao', 'RecommendationCreated', NOW() - INTERVAL '4 days', false),
    (6, 6, 6, 'ReadingDifficulty', 'Dang o muc do 3', 'De xuat nang len muc do 4', 'Toc do doc tang 30% trong thang', 'Validated', NOW() - INTERVAL '3 days', false),
    (7, 7, 7, 'LearningIntervention', 'Nhieu loi phat am', 'De xuat bo sung bai luyen phat am', 'Ghi nhan nhieu loi phat am trong AsrWordResult', 'Modified', NOW() - INTERVAL '2 days', false),
    (8, 8, 8, 'ContentPersonalization', 'Tich cuc thao luan', 'De xuat them cau hoi thao luan nang cao', 'Tra loi 100% cau hoi thao luan', 'Monitoring', NOW() - INTERVAL '1 days', false),
    (9, 9, 9, 'LearningIntervention', 'Giam tuong tac', 'De xuat gui nhac nho hang ngay', 'So phien doc giam 50% so voi thang truoc', 'ObserveMore', NOW() - INTERVAL '1 days', false),
    (10, 10, 10, 'ContentPersonalization', 'Thich the thao, phieu luu', 'De xuat uu tien truyen the thao', 'Da doc va danh dau yeu thich 4 truyen the thao', 'ReassessmentRequired', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 36. recommendation_reviews (10)
INSERT INTO recommendation_reviews ("Id", "RecommendationId", "ReviewerUserId", "Decision", "IsFinal", "HadFinalAuthority", "Reason", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Accept', true, true, 'Dong y voi de xuat cua he thong', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (2, 2, 3, 'Modify', false, true, 'Can dieu chinh them ve muc do kho', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (3, 3, 4, 'Accept', true, true, 'Phu hop voi so thich cua tre', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (4, 4, 5, 'ObserveMore', false, false, 'Can theo doi them mot thoi gian', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (5, 5, 6, 'Accept', true, true, 'Dong y can thiep som', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (6, 6, 7, 'Accept', true, true, 'Giao vien dong y voi de xuat', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (7, 7, 8, 'Modify', false, true, 'De nghi bo sung them bai tap', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (8, 8, 9, 'Accept', true, true, 'Phu hop voi kha nang cua tre', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (9, 9, 10, 'Reject', true, true, 'Chua du bang chung de can thiep', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 2, 'Accept', true, true, 'Dong y voi de xuat ca nhan hoa', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 37. child_profile_version_history (10)
INSERT INTO child_profile_version_history ("Id", "ChildProfileId", "RecommendationId", "AppliedByUserId", "PreviousConfig", "NewConfig", "VersionStatus", "AppliedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 2, '{"readingLevel":2}', '{"readingLevel":3}', 'ActiveVersion', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (2, 2, 2, 3, '{"focus":"comprehension"}', '{"focus":"vocabulary"}', 'Superseded', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (3, 3, 3, 4, '{"content":"mixed"}', '{"content":"fairytale"}', 'ActiveVersion', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (4, 4, 4, 5, '{"readingLevel":4}', '{"readingLevel":4}', 'ActiveVersion', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (5, 5, 5, 6, '{"interventionLevel":0}', '{"interventionLevel":1}', 'DraftVersion', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (6, 6, 6, 7, '{"readingLevel":3}', '{"readingLevel":4}', 'ActiveVersion', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (7, 7, 7, 8, '{"pronunciationSupport":false}', '{"pronunciationSupport":true}', 'ActiveVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (8, 8, 8, 9, '{"discussionLevel":"basic"}', '{"discussionLevel":"advanced"}', 'DraftVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (9, 9, 9, 10, '{"reminderEnabled":false}', '{"reminderEnabled":true}', 'Superseded', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 10, 2, '{"preferredGenre":"mixed"}', '{"preferredGenre":"sports"}', 'ActiveVersion', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 38. intervention_cases (10)
INSERT INTO intervention_cases ("Id", "ChildProfileId", "RecommendationId", "TriggerType", "Status", "OpenedAt", "ResolvedAt", "ResolvedByUserId", "SkillGapNotes", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, NULL, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '6 days', NULL, NULL, 'Can theo doi them ve toc do doc', NOW() - INTERVAL '6 days', false),
    (2, 2, 2, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '5 days', NULL, NULL, 'Can bo sung bai tap tu vung', NOW() - INTERVAL '5 days', false),
    (3, 5, 5, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '4 days', NULL, NULL, 'Ty le hoan thanh bai giao qua thap', NOW() - INTERVAL '4 days', false),
    (4, 7, 7, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '3 days', NULL, NULL, 'Loi phat am lap lai nhieu lan', NOW() - INTERVAL '3 days', false),
    (5, 9, 9, 'AutoBelowThreshold', 'ResolvedUnlocked', NOW() - INTERVAL '10 days', NOW() - INTERVAL '2 days', 3, 'Da cai thien sau khi nhac nho', NOW() - INTERVAL '10 days', false),
    (6, 3, NULL, 'RecommendedByAi', 'ResolvedUnlocked', NOW() - INTERVAL '12 days', NOW() - INTERVAL '9 days', 4, 'Da giai quyet, tre tien bo tot', NOW() - INTERVAL '12 days', false),
    (7, 4, NULL, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '2 days', NULL, NULL, 'Theo doi kha nang suy luan', NOW() - INTERVAL '2 days', false),
    (8, 6, NULL, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '1 days', NULL, NULL, 'Danh gia them ve toc do tien bo', NOW() - INTERVAL '1 days', false),
    (9, 8, NULL, 'AutoBelowThreshold', 'ResolvedUnlocked', NOW() - INTERVAL '8 days', NOW() - INTERVAL '3 days', 9, 'Tre da vuot qua kho khan ban dau', NOW() - INTERVAL '8 days', false),
    (10, 10, NULL, 'RecommendedByAi', 'OpenHoldMode', NOW() - INTERVAL '1 days', NULL, NULL, 'Can quan sat them ve so thich doc', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 39. o2o_assessments (10)
INSERT INTO o2o_assessments ("Id", "AssignmentId", "ChildProfileId", "TeacherUserId", "BonusPoints", "Notes", "AssessedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 7, 10, 'Hoan thanh bai tap tot, can khich le them', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (2, 2, 4, 8, 5, 'Can co gang hon o phan doc hieu', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (3, 3, 10, 9, 15, 'Xuat sac, hoan thanh truoc thoi han', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (4, 4, 6, 10, 0, 'Chua nop bai dung han', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (5, 5, 8, 7, 8, 'Tien bo ro ret so voi lan truoc', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (6, 6, 2, 8, 12, 'Tra loi tot cac cau hoi thao luan', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (7, 7, 4, 9, 7, 'Hoan thanh dung han, chat luong on', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (8, 8, 6, 10, 9, 'Co su sang tao trong cau tra loi', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (9, 9, 8, 7, 6, 'Can luyen tap them ve tu vung', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 10, 8, 20, 'Xuat sac toan dien, danh dau la hoc sinh tieu bieu', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 40. shared_stories (10)
INSERT INTO shared_stories ("Id", "StoryId", "ClassGroupId", "SharedByUserId", "ShareMode", "TeacherStatus", "ReviewedByUserId", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 7, 'Broadcast', 'Approved', 7, NOW() - INTERVAL '14 days', NOW() - INTERVAL '15 days', false),
    (2, 2, 2, 8, 'OneToOne', 'Pending', NULL, NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 3, 9, 'Broadcast', 'Approved', 9, NOW() - INTERVAL '12 days', NOW() - INTERVAL '13 days', false),
    (4, 4, 4, 10, 'OneToOne', 'Rejected', 10, NOW() - INTERVAL '11 days', NOW() - INTERVAL '12 days', false),
    (5, 5, 5, 7, 'Broadcast', 'Approved', 7, NOW() - INTERVAL '10 days', NOW() - INTERVAL '11 days', false),
    (6, 6, 1, 8, 'Broadcast', 'Pending', NULL, NULL, NOW() - INTERVAL '10 days', false),
    (7, 7, 2, 9, 'OneToOne', 'Approved', 9, NOW() - INTERVAL '8 days', NOW() - INTERVAL '9 days', false),
    (8, 8, 3, 10, 'Broadcast', 'Approved', 10, NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (9, 9, 4, 7, 'OneToOne', 'Rejected', 7, NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (10, 10, 5, 8, 'Broadcast', 'Approved', 8, NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 41. shared_story_recipients (10)
INSERT INTO shared_story_recipients ("Id", "SharedStoryId", "RecipientUserId", "Status", "RespondedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Accepted', NOW() - INTERVAL '13 days', NOW() - INTERVAL '14 days', false),
    (2, 2, 3, 'Pending', NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 4, 'Accepted', NOW() - INTERVAL '11 days', NOW() - INTERVAL '12 days', false),
    (4, 4, 5, 'Declined', NOW() - INTERVAL '10 days', NOW() - INTERVAL '11 days', false),
    (5, 5, 6, 'Accepted', NOW() - INTERVAL '9 days', NOW() - INTERVAL '10 days', false),
    (6, 6, 2, 'Pending', NULL, NOW() - INTERVAL '9 days', false),
    (7, 7, 3, 'Accepted', NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (8, 8, 4, 'Accepted', NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (9, 9, 5, 'Revoked', NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false),
    (10, 10, 6, 'Accepted', NOW() - INTERVAL '4 days', NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 42. supervision_invitations (10)
INSERT INTO supervision_invitations ("Id", "ChildProfileId", "InviterUserId", "InviteeUserId", "InviteeEmail", "InvitationCode", "Status", "RespondedAt", "UsedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, NULL, 'supervisor1.demo@example.com', 'INVITE-DEMO-0001', 'Accepted', NOW() - INTERVAL '39 days', NOW() - INTERVAL '39 days', NOW() - INTERVAL '40 days', false),
    (2, 2, 2, NULL, 'supervisor2.demo@example.com', 'INVITE-DEMO-0002', 'Pending', NULL, NULL, NOW() - INTERVAL '38 days', false),
    (3, 3, 3, NULL, 'supervisor3.demo@example.com', 'INVITE-DEMO-0003', 'Accepted', NOW() - INTERVAL '37 days', NOW() - INTERVAL '37 days', NOW() - INTERVAL '38 days', false),
    (4, 4, 3, NULL, 'supervisor4.demo@example.com', 'INVITE-DEMO-0004', 'Rejected', NOW() - INTERVAL '36 days', NULL, NOW() - INTERVAL '37 days', false),
    (5, 5, 4, NULL, 'supervisor5.demo@example.com', 'INVITE-DEMO-0005', 'Expired', NULL, NULL, NOW() - INTERVAL '36 days', false),
    (6, 6, 4, NULL, 'supervisor6.demo@example.com', 'INVITE-DEMO-0006', 'Accepted', NOW() - INTERVAL '34 days', NOW() - INTERVAL '34 days', NOW() - INTERVAL '35 days', false),
    (7, 7, 5, NULL, 'supervisor7.demo@example.com', 'INVITE-DEMO-0007', 'Pending', NULL, NULL, NOW() - INTERVAL '34 days', false),
    (8, 8, 5, NULL, 'supervisor8.demo@example.com', 'INVITE-DEMO-0008', 'Accepted', NOW() - INTERVAL '32 days', NOW() - INTERVAL '32 days', NOW() - INTERVAL '33 days', false),
    (9, 9, 6, NULL, 'supervisor9.demo@example.com', 'INVITE-DEMO-0009', 'Revoked', NOW() - INTERVAL '31 days', NULL, NOW() - INTERVAL '32 days', false),
    (10, 10, 6, NULL, 'supervisor10.demo@example.com', 'INVITE-DEMO-0010', 'Accepted', NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 43. supervision_relationships (10)
INSERT INTO supervision_relationships ("Id", "ChildProfileId", "SupervisorUserId", "SupervisorRole", "SupervisionInvitationId", "RevokedAt", "RevokedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Owner', 1, NULL, NULL, NOW() - INTERVAL '39 days', false),
    (2, 2, 2, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '39 days', false),
    (3, 3, 3, 'Owner', 3, NULL, NULL, NOW() - INTERVAL '37 days', false),
    (4, 4, 3, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '37 days', false),
    (5, 5, 4, 'AdditionalSupervisor', NULL, NULL, NULL, NOW() - INTERVAL '36 days', false),
    (6, 6, 4, 'Owner', 6, NULL, NULL, NOW() - INTERVAL '34 days', false),
    (7, 7, 5, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '34 days', false),
    (8, 8, 5, 'Owner', 8, NULL, NULL, NOW() - INTERVAL '32 days', false),
    (9, 9, 6, 'AdditionalSupervisor', NULL, NOW() - INTERVAL '20 days', 6, NOW() - INTERVAL '31 days', false),
    (10, 10, 6, 'Owner', 10, NULL, NULL, NOW() - INTERVAL '17 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 44. supervision_permissions
INSERT INTO supervision_permissions ("Id", "SupervisionRelationshipId", "Permission", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'ViewProgress', NOW() - INTERVAL '39 days', false),
    (2, 2, 'ViewResults', NOW() - INTERVAL '39 days', false),
    (3, 3, 'AssignActivity', NOW() - INTERVAL '37 days', false),
    (4, 4, 'ReceiveReport', NOW() - INTERVAL '37 days', false),
    (5, 5, 'ApproveReadingLevel', NOW() - INTERVAL '36 days', false),
    (6, 6, 'ApproveStory', NOW() - INTERVAL '34 days', false),
    (7, 7, 'ManageSafetySettings', NOW() - INTERVAL '34 days', false),
    (8, 8, 'ViewProgress', NOW() - INTERVAL '32 days', false),
    (9, 9, 'ViewResults', NOW() - INTERVAL '31 days', false),
    (10, 10, 'AssignActivity', NOW() - INTERVAL '17 days', false),
    (11, 1, 'GenerateStory', NOW() - INTERVAL '39 days', false),
    (12, 2, 'GenerateStory', NOW() - INTERVAL '39 days', false),
    (13, 3, 'GenerateStory', NOW() - INTERVAL '37 days', false),
    (14, 4, 'GenerateStory', NOW() - INTERVAL '37 days', false),
    (15, 5, 'GenerateStory', NOW() - INTERVAL '36 days', false),
    (16, 6, 'GenerateStory', NOW() - INTERVAL '34 days', false),
    (17, 7, 'GenerateStory', NOW() - INTERVAL '34 days', false),
    (18, 8, 'GenerateStory', NOW() - INTERVAL '32 days', false),
    (19, 10, 'GenerateStory', NOW() - INTERVAL '17 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 45. data_requests (10)
INSERT INTO data_requests ("Id", "ChildProfileId", "RequestedByUserId", "RequestType", "Status", "ResolvedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Export', 'Resolved', NOW() - INTERVAL '1 days', NOW() - INTERVAL '2 days', false),
    (2, 2, 2, 'Delete', 'Pending', NULL, NOW() - INTERVAL '2 days', false),
    (3, 3, 3, 'Export', 'Resolved', NOW() - INTERVAL '3 days', NOW() - INTERVAL '4 days', false),
    (4, 4, 3, 'Delete', 'Pending', NULL, NOW() - INTERVAL '3 days', false),
    (5, 5, 4, 'Export', 'Resolved', NOW() - INTERVAL '4 days', NOW() - INTERVAL '5 days', false),
    (6, 6, 4, 'Export', 'Pending', NULL, NOW() - INTERVAL '4 days', false),
    (7, 7, 5, 'Delete', 'Resolved', NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false),
    (8, 8, 5, 'Export', 'Pending', NULL, NOW() - INTERVAL '5 days', false),
    (9, 9, 6, 'Delete', 'Resolved', NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (10, 10, 6, 'Export', 'Pending', NULL, NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 46. audit_logs (10)
INSERT INTO audit_logs ("Id", "ActorUserId", "Action", "EntityType", "EntityId", "AfterState", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'CREATE_STORY', 'Story', 1, '{"status":"Draft"}', NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false),
    (2, 2, 'CREATE_CHILD_PROFILE', 'ChildProfile', 1, '{"status":"Active"}', NOW() - INTERVAL '40 days', NOW() - INTERVAL '40 days', false),
    (3, 7, 'ASSIGN_STORY', 'Assignment', 1, '{"status":"Assigned"}', NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', false),
    (4, 3, 'UPDATE_STORY', 'Story', 3, '{"status":"Ready"}', NOW() - INTERVAL '26 days', NOW() - INTERVAL '26 days', false),
    (5, 1, 'VERIFY_ORGANIZATION', 'Organization', 1, '{"verificationStatus":"Active"}', NOW() - INTERVAL '58 days', NOW() - INTERVAL '58 days', false),
    (6, 4, 'REVIEW_RECOMMENDATION', 'Recommendation', 4, '{"status":"Applied"}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 9, 'CREATE_CLASS_GROUP', 'ClassGroup', 3, '{"status":"Active"}', NOW() - INTERVAL '36 days', NOW() - INTERVAL '36 days', false),
    (8, 8, 'SHARE_STORY', 'SharedStory', 2, '{"teacherStatus":"Pending"}', NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false),
    (9, 5, 'REQUEST_DATA_EXPORT', 'DataRequest', 8, '{"status":"Pending"}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (10, 6, 'REVOKE_SUPERVISION', 'SupervisionRelationship', 9, '{"revoked":true}', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- 47. business_reports (10 - 10 thang gan nhat)
INSERT INTO business_reports ("Id", "PeriodStart", "PeriodEnd", "StoriesGenerated", "StoriesApproved", "StoriesRejected", "ReadingSessionsCompleted", "Status", "PublishedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, DATE_TRUNC('month', NOW()) - INTERVAL '10 month', DATE_TRUNC('month', NOW()) - INTERVAL '9 month' - INTERVAL '1 day', 12, 10, 2, 40, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '9 month', DATE_TRUNC('month', NOW()) - INTERVAL '9 month', false),
    (2, DATE_TRUNC('month', NOW()) - INTERVAL '9 month', DATE_TRUNC('month', NOW()) - INTERVAL '8 month' - INTERVAL '1 day', 15, 12, 3, 55, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '8 month', DATE_TRUNC('month', NOW()) - INTERVAL '8 month', false),
    (3, DATE_TRUNC('month', NOW()) - INTERVAL '8 month', DATE_TRUNC('month', NOW()) - INTERVAL '7 month' - INTERVAL '1 day', 18, 15, 3, 62, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '7 month', DATE_TRUNC('month', NOW()) - INTERVAL '7 month', false),
    (4, DATE_TRUNC('month', NOW()) - INTERVAL '7 month', DATE_TRUNC('month', NOW()) - INTERVAL '6 month' - INTERVAL '1 day', 20, 16, 4, 70, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '6 month', DATE_TRUNC('month', NOW()) - INTERVAL '6 month', false),
    (5, DATE_TRUNC('month', NOW()) - INTERVAL '6 month', DATE_TRUNC('month', NOW()) - INTERVAL '5 month' - INTERVAL '1 day', 22, 18, 4, 75, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '5 month', DATE_TRUNC('month', NOW()) - INTERVAL '5 month', false),
    (6, DATE_TRUNC('month', NOW()) - INTERVAL '5 month', DATE_TRUNC('month', NOW()) - INTERVAL '4 month' - INTERVAL '1 day', 25, 20, 5, 80, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '4 month', DATE_TRUNC('month', NOW()) - INTERVAL '4 month', false),
    (7, DATE_TRUNC('month', NOW()) - INTERVAL '4 month', DATE_TRUNC('month', NOW()) - INTERVAL '3 month' - INTERVAL '1 day', 24, 19, 5, 78, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '3 month', DATE_TRUNC('month', NOW()) - INTERVAL '3 month', false),
    (8, DATE_TRUNC('month', NOW()) - INTERVAL '3 month', DATE_TRUNC('month', NOW()) - INTERVAL '2 month' - INTERVAL '1 day', 28, 23, 5, 90, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '2 month', DATE_TRUNC('month', NOW()) - INTERVAL '2 month', false),
    (9, DATE_TRUNC('month', NOW()) - INTERVAL '2 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 month' - INTERVAL '1 day', 30, 25, 5, 95, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '1 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 month', false),
    (10, DATE_TRUNC('month', NOW()) - INTERVAL '1 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 day', 10, 8, 2, 50, 'Compiling', NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- 48. ai_governance_metrics (10 - 10 ngay gan nhat)
INSERT INTO ai_governance_metrics ("Id", "MetricDate", "AvgLatencyMs", "GenerationSuccessRate", "RegenerationRate", "SafetyFlagRate", "ApprovalRate", "CreatedAt", "IsDeleted")
VALUES
    (1, NOW() - INTERVAL '10 day', 1300, 92.500, 4.200, 2.100, 89.000, NOW() - INTERVAL '10 day', false),
    (2, NOW() - INTERVAL '9 day', 1280, 93.000, 4.000, 1.900, 90.000, NOW() - INTERVAL '9 day', false),
    (3, NOW() - INTERVAL '8 day', 1250, 93.500, 3.800, 1.700, 90.500, NOW() - INTERVAL '8 day', false),
    (4, NOW() - INTERVAL '7 day', 1220, 94.000, 3.600, 1.600, 91.000, NOW() - INTERVAL '7 day', false),
    (5, NOW() - INTERVAL '6 day', 1200, 94.500, 3.400, 1.400, 91.500, NOW() - INTERVAL '6 day', false),
    (6, NOW() - INTERVAL '5 day', 1180, 94.800, 3.300, 1.300, 91.800, NOW() - INTERVAL '5 day', false),
    (7, NOW() - INTERVAL '4 day', 1170, 95.000, 3.200, 1.200, 92.000, NOW() - INTERVAL '4 day', false),
    (8, NOW() - INTERVAL '3 day', 1150, 95.200, 3.100, 1.150, 92.200, NOW() - INTERVAL '3 day', false),
    (9, NOW() - INTERVAL '2 day', 1220, 95.300, 3.150, 1.120, 92.100, NOW() - INTERVAL '2 day', false),
    (10, NOW() - INTERVAL '1 day', 1200, 95.500, 3.200, 1.100, 92.000, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- ---------------------------------------------------------------------------
-- Reset lai cac sequence (identity) de tranh trung Id khi ung dung insert tiep
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'user_accounts','organizations','content_categories','organization_memberships',
        'organization_permissions','org_safety_policy_templates','child_profiles',
        'org_safety_policy_categories','org_consent_records','safety_policies',
        'safety_policy_categories','learning_profiles','learning_profile_topics',
        'class_groups','class_group_members','stories','story_categories','story_versions',
        'discussion_questions','quiz_items','media_assets','story_vocabulary',
        'prompt_catalog_versions','story_generation_jobs','assignments','assignment_recipients',
        'reading_sessions','reading_progress','quiz_attempts','telemetry_logs',
        'vocabulary_notebook_entries','achievements','badges','learning_insights',
        'recommendations','recommendation_reviews','child_profile_version_history',
        'intervention_cases','o2o_assessments','shared_stories','shared_story_recipients',
        'supervision_invitations','supervision_relationships','supervision_permissions',
        'data_requests','audit_logs','business_reports','ai_governance_metrics'
    ];
    seq text;
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        seq := pg_get_serial_sequence(tbl, 'Id');
        IF seq IS NOT NULL THEN
            EXECUTE format('SELECT setval(%L, COALESCE((SELECT MAX("Id") FROM %I), 1))', seq, tbl);
        END IF;
    END LOOP;
END $$;

COMMIT;
'@

$tempFile = Join-Path $env:TEMP "seed-data-$(Get-Date -Format 'yyyyMMddHHmmss').sql"
# Luu y: Set-Content -Encoding utf8 tren Windows PowerShell 5.1 luon ghi kem BOM,
# khien psql doc nham 3 byte BOM thanh ky tu la dinh vao dau file ("ï»¿BEGIN") va bao loi cu phap.
# Dung .NET UTF8Encoding voi encoderShouldEmitUTF8Identifier=$false de ghi UTF-8 khong BOM.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempFile, $sql, $utf8NoBom)

$env:PGPASSWORD = $Password
try {
    Write-Host "Ket noi toi PostgreSQL: Host=$PgHost Port=$Port Database=$Database Username=$Username" -ForegroundColor Cyan
    & $psqlPath -h $PgHost -p $Port -U $Username -d $Database -v ON_ERROR_STOP=1 -f $tempFile

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Insert du lieu mau thanh cong cho toan bo 48 bang." -ForegroundColor Green
    } else {
        Write-Error "psql tra ve loi (exit code $LASTEXITCODE). Xem log ben tren de biet chi tiet."
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
