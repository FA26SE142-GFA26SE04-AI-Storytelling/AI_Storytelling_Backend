-- 1. user_accounts (10) - Mat khau cho tat ca tai khoan demo: Demo@123
INSERT INTO user_accounts (
    "Id", "Username", "Email", "PasswordHash", "FullName", "PhoneNumber", 
    "AvatarUrl", "Role", "Status", "ResetTokenHash", "ResetTokenExpiresAt", 
    "RefreshTokenHash", "RefreshTokenExpiresAt", "LastLoginAt", 
    "FailedLoginAttempts", "LockedUntil", "MfaSecret", "MfaEnabled", "TokenVersion", "RoleBeforeAdmin",
    "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 'admin_demo', 'admin@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Nguyen Van Admin', '0900000001', 
     'https://ui-avatars.com/api/?name=Admin+Demo&background=0D8ABC&color=fff', 'Administrator', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-admin'::bytea), 'hex'), NOW() + INTERVAL '7 days', NOW() - INTERVAL '2 hours', 
     0, NULL, NULL, true, 1, NULL,
     NOW() - INTERVAL '60 days', NOW() - INTERVAL '2 hours', false),

    (2, 'parent_demo1', 'parent1@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Tran Thi Hoa', '0900000002', 
     'https://ui-avatars.com/api/?name=Hoa+Tran&background=F39C12&color=fff', 'Parent', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '5 days', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '55 days', NOW() - INTERVAL '5 days', false),

    (3, 'parent_demo2', 'parent2@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Nguyen Van Binh', '0900000003', 
     'https://ui-avatars.com/api/?name=Binh+Nguyen&background=27AE60&color=fff', 'Parent', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-parent2'::bytea), 'hex'), NOW() + INTERVAL '6 days', NOW() - INTERVAL '1 day', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '52 days', NOW() - INTERVAL '1 day', false),

    (4, 'parent_demo3', 'parent3@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Le Thi Cuc', '0900000004', 
     'https://ui-avatars.com/api/?name=Cuc+Le&background=8E44AD&color=fff', 'Parent', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '10 days', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '50 days', NOW() - INTERVAL '10 days', false),

    (5, 'parent_demo4', 'parent4@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Pham Van Dung', '0900000005', 
     NULL, 'Parent', 'Suspended', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '30 days', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '48 days', NOW() - INTERVAL '20 days', false),

    (6, 'parent_demo5', 'parent5@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Hoang Thi Em', '0900000006', 
     NULL, 'Parent', 'Registered', NULL, NULL, 
     NULL, NULL, NULL, 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '46 days', NULL, false),

    (7, 'teacher_demo1', 'teacher1@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Vu Van Phong', '0900000007', 
     'https://ui-avatars.com/api/?name=Phong+Vu&background=2980B9&color=fff', 'Teacher', 'LoggedIn', NULL, NULL, 
     encode(sha256('demo-refresh-token-teacher1'::bytea), 'hex'), NOW() + INTERVAL '7 days', NOW() - INTERVAL '3 hours', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '45 days', NOW() - INTERVAL '3 hours', false),

    (8, 'teacher_demo2', 'teacher2@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Do Thi Giang', '0900000008', 
     'https://ui-avatars.com/api/?name=Giang+Do&background=E74C3C&color=fff', 'Teacher', 'EmailVerified', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '3 days', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '44 days', NOW() - INTERVAL '3 days', false),

    (9, 'teacher_demo3', 'teacher3@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Bui Van Hai', '0900000009', 
     NULL, 'Teacher', 'PasswordResetPending', encode(sha256('demo-reset-token-teacher3'::bytea), 'hex'), NOW() + INTERVAL '1 hour', 
     NULL, NULL, NOW() - INTERVAL '2 days', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '43 days', NOW() - INTERVAL '1 hour', false),

    (10, 'teacher_demo4', 'teacher4@example.com', '$2a$11$uQ6RWu9dJN87HUij5gy3RedTZUibH1T4BB/QZBmvLntZBvkjQIR0S', 'Dang Thi Kim', '0900000010', 
     'https://ui-avatars.com/api/?name=Kim+Dang&background=34495E&color=fff', 'Teacher', 'LoggedOut', NULL, NULL, 
     NULL, NULL, NOW() - INTERVAL '12 hours', 
     0, NULL, NULL, false, 1, NULL,
     NOW() - INTERVAL '42 days', NOW() - INTERVAL '6 hours', false)
ON CONFLICT ("Id") DO NOTHING;
