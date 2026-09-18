<#
.SYNOPSIS
    Insert du lieu mau (seed data) cho toan bo cac bang cua database AIStorytellingDB (PostgreSQL).

.DESCRIPTION
    Script nay ket noi toi PostgreSQL da cai san tren may (dung psql.exe) va chay 1 file .sql
    chua cac cau lenh INSERT cho cac bang theo dung thu tu phu thuoc khoa ngoai (FK).
    Moi INSERT xu ly "ON CONFLICT (\"Id\")" nen co the chay lai nhieu lan an toan.
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
    Mat khau PostgreSQL. Mac dinh: doc tu bien moi truong PGPASSWORD neu khong truyen tham so.

.EXAMPLE
    ./Seed-Database.ps1

.EXAMPLE
    ./Seed-Database.ps1 -PgHost "localhost" -Port 5432 -Database "AIStorytellingDB" -Username "postgres" -Password "your_password"
#>

[CmdletBinding()]
param(
    [string]$PgHost = "localhost",
    [int]$Port = 5432,
    [string]$Database = "AIStorytellingDB",
    [string]$Username = "postgres",
    [string]$Password = $env:PGPASSWORD
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
# Noi dung SQL seed - doc va ghep cac file trong thu muc .\Seed theo dung
# thu tu phu thuoc khoa ngoai (FK). Moi file la 1 bang, dat ten dang
# "<STT>_<ten_bang>.sql" (vi du: 01_user_accounts.sql). Hau to a/b/c duoc
# dung khi chen bang moi vao giua thu tu FK ma khong doi ten cac file cu.
# ---------------------------------------------------------------------------
$sqlHeader = @'
BEGIN;

'@

$sqlFooter = @'

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
        'media_contexts','story_scenes','discussion_questions','quiz_items','media_assets','story_vocabulary',
        'prompt_catalog_versions','story_generation_requests','story_generation_jobs','assignments','assignment_recipients',
        'reading_sessions','reading_progress','quiz_attempts','telemetry_logs',
        'vocabulary_notebook_entries','achievements','badges','learning_insights',
        'recommendations','recommendation_reviews','child_profile_version_history',
        'intervention_cases','o2o_assessments','shared_stories','shared_story_recipients',
        'supervision_invitations','supervision_relationships','supervision_permissions',
        'supervision_permission_requests','supervision_permission_request_items','ownership_transfer_requests',
        'data_requests','audit_logs','business_reports','ai_governance_metrics',
        'refresh_tokens','notifications','child_access_credentials','content_reports',
        'subscription_plans','token_quota_configs','payment_transactions'
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

$seedDir = Join-Path $PSScriptRoot "Seed"
$seedFiles = Get-ChildItem -Path $seedDir -Filter "*.sql" | Sort-Object Name
if ($seedFiles.Count -eq 0) {
    Write-Error "Khong tim thay file .sql nao trong thu muc: $seedDir"
    exit 1
}

# Dung .NET de doc file UTF-8 nguyen ban (khong qua interpolation cua PowerShell),
# vi noi dung co the chua ky tu '$' trong bcrypt hash (vd: $2a$11$...) hoac dollar-quote.
$seedSql = ($seedFiles | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n`n"

$sql = $sqlHeader + $seedSql + $sqlFooter
$tempFile = Join-Path $env:TEMP "seed-data-$(Get-Date -Format 'yyyyMMddHHmmss').sql"
# Luu y: Set-Content -Encoding utf8 tren Windows PowerShell 5.1 luon ghi kem BOM,
# khien psql doc nham 3 byte BOM thanh ky tu la dinh vao dau file ("ï»¿BEGIN") va bao loi cu phap.
# Dung .NET UTF8Encoding voi encoderShouldEmitUTF8Identifier=$false de ghi UTF-8 khong BOM.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempFile, $sql, $utf8NoBom)

if (-not [string]::IsNullOrEmpty($Password)) {
    $env:PGPASSWORD = $Password
}
try {
    Write-Host "Ket noi toi PostgreSQL: Host=$PgHost Port=$Port Database=$Database Username=$Username" -ForegroundColor Cyan
    & $psqlPath -h $PgHost -p $Port -U $Username -d $Database -v ON_ERROR_STOP=1 -f $tempFile

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Insert du lieu mau thanh cong cho toan bo $($seedFiles.Count) bang." -ForegroundColor Green
    } else {
        Write-Error "psql tra ve loi (exit code $LASTEXITCODE). Xem log ben tren de biet chi tiet."
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
