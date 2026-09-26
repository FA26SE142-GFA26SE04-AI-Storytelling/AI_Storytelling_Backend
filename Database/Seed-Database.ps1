<#
.SYNOPSIS
    Kiem tra va nap du lieu mau cho toan bo schema PostgreSQL cua StoryPlatform.

.DESCRIPTION
    Script doc cac file Database/Seed/*.sql theo thu tu ten file, kiem tra moi
    bang trong EF Core model snapshot deu co dung mot file seed, ghep tat ca
    lenh vao mot transaction va thuc thi bang psql.

    Moi file seed phai co ten "<thu-tu-2-chu-so>_<ten-bang>.sql" va cac so
    phai lien tuc tu 01 den dung tong so bang trong EF Core model snapshot.
    Sau khi nap, identity sequence duoc reset tu chinh danh sach bang da seed,
    khong can duy tri mot danh sach bang viet tay.

.PARAMETER PgHost
    Dia chi PostgreSQL. Mac dinh: localhost.

.PARAMETER Port
    Cong PostgreSQL. Mac dinh: 5432.

.PARAMETER Database
    Ten database. Mac dinh: AIStorytellingDB.

.PARAMETER Username
    Tai khoan PostgreSQL. Mac dinh: postgres.

.PARAMETER Password
    Mat khau PostgreSQL. Neu bo trong, dung gia tri PGPASSWORD hien tai.

.PARAMETER ValidateOnly
    Chi kiem tra coverage/ten/thu tu file seed so voi EF model snapshot;
    khong tim psql, khong ket noi va khong thay doi database.

.EXAMPLE
    ./Seed-Database.ps1 -ValidateOnly

.EXAMPLE
    ./Seed-Database.ps1

.EXAMPLE
    ./Seed-Database.ps1 -PgHost "localhost" -Port 5432 -Database "AIStorytellingDB" -Username "postgres" -Password "your_password"
#>

[CmdletBinding()]
param(
    [string]$PgHost = "localhost",
    [ValidateRange(1, 65535)]
    [int]$Port = 5432,
    [string]$Database = "AIStorytellingDB",
    [string]$Username = "postgres",
    [string]$Password = $env:PGPASSWORD,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-Psql {
    $command = Get-Command psql.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $postgresRoot = "C:\Program Files\PostgreSQL"
    if (-not (Test-Path -LiteralPath $postgresRoot -PathType Container)) {
        return $null
    }

    $candidates = @(Get-ChildItem -LiteralPath $postgresRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } -Descending)

    foreach ($directory in $candidates) {
        $candidate = Join-Path $directory.FullName "bin\psql.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function Get-SeedTableName {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$SeedFile
    )

    $content = [System.IO.File]::ReadAllText($SeedFile.FullName)
    $insertPattern = '(?im)^\s*INSERT\s+INTO\s+(?:"(?<quoted>[a-z][a-z0-9_]*)"|(?<plain>[a-z][a-z0-9_]*))'
    $matches = [regex]::Matches($content, $insertPattern)
    if ($matches.Count -eq 0) {
        throw "File seed '$($SeedFile.Name)' khong co lenh INSERT INTO hop le."
    }

    $targets = @($matches | ForEach-Object {
        if ($_.Groups['quoted'].Success) {
            $_.Groups['quoted'].Value
        }
        else {
            $_.Groups['plain'].Value
        }
    } | Sort-Object -Unique)

    if ($targets.Count -ne 1) {
        throw "File seed '$($SeedFile.Name)' ghi vao nhieu bang: $($targets -join ', '). Hay tach moi bang thanh mot file."
    }

    return $targets[0]
}

function Get-SeedManifest {
    param(
        [Parameter(Mandatory)]
        [string]$SeedDirectory
    )

    if (-not (Test-Path -LiteralPath $SeedDirectory -PathType Container)) {
        throw "Khong tim thay thu muc seed: $SeedDirectory"
    }

    $seedFiles = @(Get-ChildItem -LiteralPath $SeedDirectory -Filter "*.sql" -File | Sort-Object Name)
    if ($seedFiles.Count -eq 0) {
        throw "Khong tim thay file .sql nao trong thu muc: $SeedDirectory"
    }

    $manifest = @()
    foreach ($seedFile in $seedFiles) {
        if ($seedFile.Name -notmatch '^(?<order>\d{2})_(?<table>[a-z][a-z0-9_]*)\.sql$') {
            throw "Ten file seed '$($seedFile.Name)' khong dung mau '<STT-2-chu-so>_<ten_bang>.sql'."
        }

        $expectedOrder = $manifest.Count + 1
        if ([int]$Matches['order'] -ne $expectedOrder) {
            throw "Thu tu file seed khong lien tuc: mong doi $($expectedOrder.ToString('00')) nhung gap '$($seedFile.Name)'."
        }

        $tableFromFileName = $Matches['table']
        $tableFromSql = Get-SeedTableName -SeedFile $seedFile
        if ($tableFromFileName -ne $tableFromSql) {
            throw "File '$($seedFile.Name)' khai bao bang '$tableFromFileName' nhung INSERT vao '$tableFromSql'."
        }

        $manifest += [PSCustomObject]@{
            File = $seedFile
            TableName = $tableFromSql
        }
    }

    $duplicateTables = @($manifest | Group-Object TableName | Where-Object Count -gt 1)
    if ($duplicateTables.Count -gt 0) {
        $details = $duplicateTables | ForEach-Object { "$($_.Name) ($($_.Count) files)" }
        throw "Moi bang chi duoc co mot file seed. Bi trung: $($details -join ', ')."
    }

    return $manifest
}

function Assert-EfModelSeedCoverage {
    param(
        [Parameter(Mandatory)]
        [object[]]$Manifest,
        [Parameter(Mandatory)]
        [string]$SnapshotPath
    )

    if (-not (Test-Path -LiteralPath $SnapshotPath -PathType Leaf)) {
        throw "Khong tim thay EF model snapshot de kiem tra coverage: $SnapshotPath"
    }

    $snapshot = [System.IO.File]::ReadAllText($SnapshotPath)
    $mappedTables = @([regex]::Matches($snapshot, '\.ToTable\("(?<table>[a-z][a-z0-9_]*)"') |
        ForEach-Object { $_.Groups['table'].Value } |
        Sort-Object -Unique)

    if ($mappedTables.Count -eq 0) {
        throw "Khong doc duoc ten bang nao tu EF model snapshot: $SnapshotPath"
    }

    $seedTables = @($Manifest | ForEach-Object TableName | Sort-Object -Unique)
    $missingSeedTables = @($mappedTables | Where-Object { $_ -notin $seedTables })
    $unknownSeedTables = @($seedTables | Where-Object { $_ -notin $mappedTables })

    if ($missingSeedTables.Count -gt 0 -or $unknownSeedTables.Count -gt 0) {
        $problems = @()
        if ($missingSeedTables.Count -gt 0) {
            $problems += "Thieu file seed: $($missingSeedTables -join ', ')"
        }
        if ($unknownSeedTables.Count -gt 0) {
            $problems += "File seed khong con trong EF model: $($unknownSeedTables -join ', ')"
        }

        throw "Seed data khong khop EF model snapshot. $($problems -join '. ')."
    }
}

function Assert-SeedForeignKeyOrder {
    param(
        [Parameter(Mandatory)]
        [object[]]$Manifest,
        [Parameter(Mandatory)]
        [string]$MigrationsDirectory
    )

    $migrationFile = Get-ChildItem -LiteralPath $MigrationsDirectory -Filter "*_Init.cs" -File |
        Where-Object Name -NotLike "*.Designer.cs" |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $migrationFile) {
        throw "Khong tim thay Init migration de kiem tra thu tu khoa ngoai trong: $MigrationsDirectory"
    }

    $tableOrder = @{}
    for ($index = 0; $index -lt $Manifest.Count; $index++) {
        $tableOrder[$Manifest[$index].TableName] = $index
    }

    $foreignKeys = @()
    $currentTable = $null
    $waitingForTableName = $false
    foreach ($line in [System.IO.File]::ReadLines($migrationFile.FullName)) {
        if ($line -match 'migrationBuilder\.CreateTable\(') {
            $waitingForTableName = $true
            $currentTable = $null
            continue
        }

        if ($waitingForTableName -and $line -match 'name:\s*"(?<table>[a-z][a-z0-9_]*)"') {
            $currentTable = $Matches['table']
            $waitingForTableName = $false
            continue
        }

        if ($currentTable -and $line -match 'principalTable:\s*"(?<parent>[a-z][a-z0-9_]*)"') {
            $foreignKeys += [PSCustomObject]@{
                Child = $currentTable
                Parent = $Matches['parent']
            }
        }
    }

    # Hai bang nay tham chieu vong nhau. Request duoc insert voi HandoffJobId=NULL,
    # job duoc insert sau, roi file job UPDATE HandoffJobId trong cung transaction.
    $deferredForeignKeys = @('story_generation_requests->story_generation_jobs')
    $violations = @($foreignKeys | Where-Object {
        $edge = "$($_.Child)->$($_.Parent)"
        $tableOrder.ContainsKey($_.Child) -and
        $tableOrder.ContainsKey($_.Parent) -and
        $tableOrder[$_.Parent] -gt $tableOrder[$_.Child] -and
        $edge -notin $deferredForeignKeys
    } | Sort-Object Child, Parent -Unique)

    if ($violations.Count -gt 0) {
        $details = $violations | ForEach-Object { "$($_.Child) phai sau $($_.Parent)" }
        throw "Thu tu file seed vi pham khoa ngoai: $($details -join '; ')."
    }
}

$seedDirectory = Join-Path $PSScriptRoot "Seed"
$migrationsDirectory = Join-Path $PSScriptRoot "..\src\Core\StoryPlatform.Infrastructure\Migrations"
$snapshotPath = Join-Path $migrationsDirectory "ApplicationDbContextModelSnapshot.cs"
$manifest = @(Get-SeedManifest -SeedDirectory $seedDirectory)
Assert-EfModelSeedCoverage -Manifest $manifest -SnapshotPath $snapshotPath
Assert-SeedForeignKeyOrder -Manifest $manifest -MigrationsDirectory $migrationsDirectory

if ($ValidateOnly) {
    Write-Host "Seed validation thanh cong: $($manifest.Count) file cho $($manifest.Count) bang EF Core." -ForegroundColor Green
    return
}

$psqlPath = Find-Psql
if (-not $psqlPath) {
    throw "Khong tim thay psql.exe. Hay them thu muc PostgreSQL bin vao PATH hoac cai dat PostgreSQL."
}

Write-Host "Dung psql tai: $psqlPath" -ForegroundColor Cyan

$seedTableLiterals = ($manifest | ForEach-Object { "'$($_.TableName)'" }) -join ', '

$sqlHeaderTemplate = @'
\set ON_ERROR_STOP on
BEGIN;

-- Fail fast neu database chua duoc migrate dung EF model hien tai.
DO $seed_preflight$
DECLARE
    table_name text;
    missing_tables text[] := ARRAY[]::text[];
    seed_tables text[] := ARRAY[__SEED_TABLES__];
BEGIN
    FOREACH table_name IN ARRAY seed_tables LOOP
        IF to_regclass(format('%I.%I', current_schema(), table_name)) IS NULL THEN
            missing_tables := array_append(missing_tables, table_name);
        END IF;
    END LOOP;

    IF cardinality(missing_tables) > 0 THEN
        RAISE EXCEPTION 'Database schema is missing seeded tables: %', array_to_string(missing_tables, ', ');
    END IF;
END
$seed_preflight$;

'@
$sqlHeader = $sqlHeaderTemplate.Replace('__SEED_TABLES__', $seedTableLiterals)

$sqlFooterTemplate = @'

-- Reset identity sequences tu chinh cac bang co file seed.
DO $seed_sequences$
DECLARE
    table_name text;
    sequence_name text;
    schema_name text := current_schema();
    maximum_id bigint;
    seed_tables text[] := ARRAY[__SEED_TABLES__];
BEGIN
    FOREACH table_name IN ARRAY seed_tables LOOP
        sequence_name := pg_get_serial_sequence(format('%I.%I', schema_name, table_name), 'Id');
        IF sequence_name IS NOT NULL THEN
            EXECUTE format('SELECT MAX("Id") FROM %I.%I', schema_name, table_name) INTO maximum_id;
            IF maximum_id IS NULL THEN
                PERFORM setval(sequence_name, 1, false);
            ELSE
                PERFORM setval(sequence_name, maximum_id, true);
            END IF;
        END IF;
    END LOOP;
END
$seed_sequences$;

COMMIT;
'@
$sqlFooter = $sqlFooterTemplate.Replace('__SEED_TABLES__', $seedTableLiterals)

# Doc UTF-8 nguyen ban de PowerShell khong noi suy cac ky tu '$' trong BCrypt hash.
$seedSql = ($manifest | ForEach-Object {
    [System.IO.File]::ReadAllText($_.File.FullName)
}) -join "`r`n`r`n"

$sql = $sqlHeader + $seedSql + $sqlFooter
$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("story-platform-seed-{0}.sql" -f [guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempFile, $sql, $utf8NoBom)

$hadPgPassword = Test-Path Env:PGPASSWORD
$previousPgPassword = $env:PGPASSWORD
if (-not [string]::IsNullOrEmpty($Password)) {
    $env:PGPASSWORD = $Password
}

try {
    Write-Host "Ket noi PostgreSQL: Host=$PgHost Port=$Port Database=$Database Username=$Username" -ForegroundColor Cyan
    $psqlArguments = @(
        '-X',
        "--host=$PgHost",
        "--port=$Port",
        "--username=$Username",
        "--dbname=$Database",
        '--set=ON_ERROR_STOP=1',
        "--file=$tempFile"
    )

    & $psqlPath @psqlArguments
    if ($LASTEXITCODE -ne 0) {
        throw "psql tra ve loi (exit code $LASTEXITCODE). Transaction seed da duoc rollback."
    }

    Write-Host "Nap du lieu mau thanh cong cho $($manifest.Count) bang." -ForegroundColor Green
}
finally {
    if ($hadPgPassword) {
        $env:PGPASSWORD = $previousPgPassword
    }
    else {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $tempFile -PathType Leaf) {
        Remove-Item -LiteralPath $tempFile -Force
    }
}
