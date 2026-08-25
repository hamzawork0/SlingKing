<#
.SYNOPSIS
  Dumps the SlingKing database to a timestamped .sql file.
.DESCRIPTION
  Run this before any migration that touches live data, and before the first real
  opening-stock import. The Supabase free tier keeps no automated backups, so this
  file is the only copy that exists outside the hosted database.

  The dump contains every row, including users.password_hash. backups/ is gitignored
  and must stay that way.
.PARAMETER OutputDirectory
  Where to write the dump. Defaults to backups/ beside this script.
.PARAMETER PgDumpSearchRoot
  Where to look for a bundled PostgreSQL install when pg_dump is not on PATH.
  Exposed so the not-found path can be exercised without uninstalling anything.
#>
param(
    [string]$OutputDirectory   = (Join-Path $PSScriptRoot "backups"),
    [string]$PgDumpSearchRoot  = (Join-Path $env:ProgramFiles "PostgreSQL")
)

$ErrorActionPreference = "Stop"

# --- Locate pg_dump ---------------------------------------------------------
# PATH first; otherwise the highest-numbered PostgreSQL install under the search
# root. Version 17 client tools read a 15, 16 or 17 server; an older pg_dump
# refuses a newer server, so the newest one we can find is always the right pick.
function Resolve-PgDump {
    param([string]$SearchRoot)

    $onPath = Get-Command pg_dump -CommandType Application -ErrorAction SilentlyContinue |
              Select-Object -First 1
    if ($onPath) { return $onPath.Source }

    if (Test-Path -LiteralPath $SearchRoot) {
        $candidate =
            Get-ChildItem -LiteralPath $SearchRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $exe = Join-Path $_.FullName "bin\pg_dump.exe"
                if (Test-Path -LiteralPath $exe) {
                    $major = 0
                    [void][int]::TryParse(($_.Name -split '\.')[0], [ref]$major)
                    [pscustomobject]@{ Major = $major; Path = $exe }
                }
            } |
            Sort-Object Major -Descending |
            Select-Object -First 1

        if ($candidate) { return $candidate.Path }
    }

    return $null
}

$pgDump = Resolve-PgDump -SearchRoot $PgDumpSearchRoot
if (-not $pgDump) {
    throw @"
pg_dump was not found.

Looked on PATH, and under: $PgDumpSearchRoot

Install the PostgreSQL 17 command-line tools:
  1. Download the Windows installer from https://www.postgresql.org/download/windows/
  2. Run it and deselect everything except "Command Line Tools" -- no server, no
     service, no port.
  3. Either add the install's bin folder (typically
     C:\Program Files\PostgreSQL\17\bin) to PATH, or re-run this script with
     -PgDumpSearchRoot pointing at the install root.

Do not install 15 or 16: an older pg_dump refuses to read a newer server.
"@
}

$versionLine = (& $pgDump --version) -join " "
Write-Host "Using $pgDump  ($versionLine)"

# --- Read the connection string --------------------------------------------
$envFile = Join-Path $PSScriptRoot "backend/.env"
if (-not (Test-Path -LiteralPath $envFile)) {
    throw "backend/.env not found. Copy backend/.env.example and fill it in."
}

# Read SLINGKING_DB_MIGRATION without disturbing the rest of the environment.
$connection = $null
foreach ($line in Get-Content -LiteralPath $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith("SLINGKING_DB_MIGRATION=")) {
        $connection = $trimmed.Substring("SLINGKING_DB_MIGRATION=".Length).Trim('"')
        break
    }
}

if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "SLINGKING_DB_MIGRATION is not set in backend/.env"
}

# Npgsql keyword form (Key=Value;Key=Value) -> libpq parameters pg_dump understands.
$parts = @{}
foreach ($pair in $connection.Split(';')) {
    $index = $pair.IndexOf('=')
    if ($index -gt 0) {
        $parts[$pair.Substring(0, $index).Trim().ToLower()] = $pair.Substring($index + 1).Trim()
    }
}

$pgHost = $parts["host"]
$port   = if ($parts.ContainsKey("port")) { $parts["port"] } else { "5432" }
$db     = $parts["database"]
$user   = $parts["username"]

foreach ($required in @("host", "database", "username", "password")) {
    if ([string]::IsNullOrWhiteSpace($parts[$required])) {
        throw "SLINGKING_DB_MIGRATION is missing '$required'. Expected Npgsql keyword form: Host=...;Port=...;Database=...;Username=...;Password=..."
    }
}

# Npgsql SSL Mode -> libpq sslmode. Supabase requires TLS; the pooler presents a
# certificate this machine does not have a CA for, which is what
# Trust Server Certificate=true means -- 'require' encrypts without verifying.
$sslMode = "require"
if ($parts.ContainsKey("ssl mode")) {
    switch ($parts["ssl mode"].ToLower()) {
        "disable"    { $sslMode = "disable" }
        "allow"      { $sslMode = "allow" }
        "prefer"     { $sslMode = "prefer" }
        "require"    { $sslMode = "require" }
        "verifyca"   { $sslMode = "verify-ca" }
        "verifyfull" { $sslMode = "verify-full" }
        default      { $sslMode = "require" }
    }
}
if ($parts.ContainsKey("trust server certificate") -and
    $parts["trust server certificate"] -match '^(true|yes|1)$' -and
    $sslMode -like "verify-*") {
    $sslMode = "require"
}

# --- Dump -------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$stamp  = Get-Date -Format "yyyy-MM-dd-HHmmss"
$target = Join-Path $OutputDirectory "slingking-$stamp.sql"

# The host and username are deliberately not printed: the pooler username carries
# the Supabase project reference, and this output gets pasted into notes and logs.
Write-Host "Dumping database '$db' to $target ..."

# The password goes in the environment, never on the command line -- command lines
# are readable by any other process on this machine.
$env:PGPASSWORD = $parts["password"]
$env:PGSSLMODE  = $sslMode
try {
    & $pgDump --host=$pgHost --port=$port --username=$user --dbname=$db `
              --schema=public --no-owner --no-privileges --file=$target
    $exitCode = $LASTEXITCODE
}
finally {
    # Never leave the password sitting in the session environment.
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:PGSSLMODE  -ErrorAction SilentlyContinue
}

# --- Prove the dump is whole ------------------------------------------------
# A truncated file that reports success is worse than no backup at all, so a
# failed or incomplete dump is deleted rather than left to be trusted later.
function Assert-DumpOrDiscard {
    param([string]$Path, [string]$Reason)
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    throw "Backup FAILED: $Reason. No file was kept."
}

if ($exitCode -ne 0) {
    Assert-DumpOrDiscard -Path $target -Reason "pg_dump exited with code $exitCode"
}
if (-not (Test-Path -LiteralPath $target)) {
    throw "Backup FAILED: pg_dump reported success but wrote no file to $target."
}

$bytes = (Get-Item -LiteralPath $target).Length
if ($bytes -eq 0) {
    Assert-DumpOrDiscard -Path $target -Reason "pg_dump wrote an empty file"
}

# pg_dump writes this only after the dump has fully completed. It is near, but not
# always exactly at, the end of the file -- 17.x follows it with an \unrestrict line.
$tail = Get-Content -LiteralPath $target -Tail 20
if (-not ($tail -match "PostgreSQL database dump complete")) {
    Assert-DumpOrDiscard -Path $target -Reason "the dump is truncated (no completion marker)"
}

$tables = @(Select-String -LiteralPath $target -Pattern '^CREATE TABLE ' -AllMatches).Count
if ($tables -eq 0) {
    Assert-DumpOrDiscard -Path $target -Reason "the dump contains no CREATE TABLE statements"
}

$sizeKb = [math]::Round($bytes / 1KB, 1)
Write-Host "Backup complete: $target ($sizeKb KB, $tables tables)" -ForegroundColor Green
Write-Host "Keep a copy somewhere that is not this laptop." -ForegroundColor Yellow
