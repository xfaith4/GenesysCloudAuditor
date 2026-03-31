<#
.SYNOPSIS
    Standalone Genesys Cloud Extension Audit script.

.DESCRIPTION
    Fetches all active users and telephony extension assignments from Genesys Cloud,
    then runs a cross-reference audit to identify:
      - Duplicate extensions across user profiles
      - Duplicate extensions in the telephony assignment pool
      - Extensions present on user profiles but missing from telephony assignments
      - Extensions whose telephony assignment is owned by the wrong user

    Authenticates via OAuth2 Client Credentials (no browser required).
    Outputs findings to the console and optionally exports CSV files.

.PARAMETER ClientId
    Genesys Cloud OAuth Client ID (Client Credentials grant).

.PARAMETER ClientSecret
    Genesys Cloud OAuth Client Secret.

.PARAMETER Region
    Genesys Cloud region domain, e.g. "mypurecloud.com", "usw2.pure.cloud",
    "mypurecloud.ie", "euw2.pure.cloud". Defaults to "mypurecloud.com".

.PARAMETER PageSize
    Number of records per API page (1-500). Defaults to 100.

.PARAMETER IncludeInactive
    When specified, inactive users are included in the audit.

.PARAMETER OutputPath
    Optional folder path to write CSV report files into.
    If omitted, findings are printed to the console only.

.PARAMETER IncludeAssignedMissingFromProfiles
    When specified, also reports telephony extensions that have no matching user profile.

.EXAMPLE
    .\Invoke-ExtensionAudit.ps1 -ClientId "<client-id>" -ClientSecret "<client-secret>" -Region "usw2.pure.cloud"

.EXAMPLE
    .\Invoke-ExtensionAudit.ps1 -ClientId "<client-id>" -ClientSecret "<client-secret>" -OutputPath "C:\Reports"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ClientId,

    [Parameter(Mandatory)]
    [string]$ClientSecret,

    [string]$Region = "mypurecloud.com",

    [ValidateRange(1, 500)]
    [int]$PageSize = 100,

    [switch]$IncludeInactive,

    [string]$OutputPath,

    [switch]$IncludeAssignedMissingFromProfiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────

function Write-Step([string]$Message) {
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "    $Message" -ForegroundColor Green
}

function Write-Warn([string]$Message) {
    Write-Host "    WARN: $Message" -ForegroundColor Yellow
}

# Normalize an extension string to digits only (1-20 chars).
# Returns $null if the value is invalid/empty.
function Normalize-Extension([string]$Raw) {
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $digits = [string]::new(($Raw.Trim().ToCharArray() | Where-Object { [char]::IsDigit($_) }))
    if ($digits.Length -eq 0 -or $digits.Length -gt 20) { return $null }
    return $digits
}

# ─────────────────────────────────────────────────────────────
# Step 1 — Authenticate
# ─────────────────────────────────────────────────────────────

Write-Step "Authenticating with Genesys Cloud ($Region) ..."

$loginUrl  = "https://login.$Region/oauth/token"
$apiBase   = "https://api.$Region"
$b64Creds  = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${ClientId}:${ClientSecret}"))

$tokenResponse = Invoke-RestMethod -Method Post -Uri $loginUrl `
    -Headers @{ Authorization = "Basic $b64Creds" } `
    -ContentType "application/x-www-form-urlencoded" `
    -Body "grant_type=client_credentials"

$accessToken = $tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Authentication failed — no access_token returned."
}

$authHeader = @{ Authorization = "Bearer $accessToken" }
Write-Ok "Authenticated successfully."

# ─────────────────────────────────────────────────────────────
# Step 2 — Fetch all users
# ─────────────────────────────────────────────────────────────

Write-Step "Fetching users ..."

$allUsers  = [System.Collections.Generic.List[object]]::new()
$pageNum   = 1
$pageCount = 1  # will be updated from first response

do {
    $stateParam = if ($IncludeInactive) { "" } else { "&state=active" }
    $usersUrl   = "$apiBase/api/v2/users?pageSize=$PageSize&pageNumber=$pageNum&expand=station$stateParam"

    $resp = Invoke-RestMethod -Method Get -Uri $usersUrl -Headers $authHeader
    if ($pageNum -eq 1) {
        $pageCount = [int]($resp.pageCount ?? 1)
        Write-Ok "Total users: $($resp.total ?? '?')  (pages: $pageCount)"
    }

    if ($resp.entities) { $allUsers.AddRange($resp.entities) }
    $pageNum++

} while ($pageNum -le $pageCount)

Write-Ok "Fetched $($allUsers.Count) users."

# ─────────────────────────────────────────────────────────────
# Step 3 — Fetch all telephony extension assignments
# ─────────────────────────────────────────────────────────────

Write-Step "Fetching telephony extension assignments ..."

$allAssignments = [System.Collections.Generic.List[object]]::new()
$pageNum        = 1
$pageCount      = 1

do {
    $extUrl = "$apiBase/api/v2/telephony/providers/edges/extensions?pageSize=$PageSize&pageNumber=$pageNum"

    $resp = Invoke-RestMethod -Method Get -Uri $extUrl -Headers $authHeader
    if ($pageNum -eq 1) {
        $pageCount = [int]($resp.pageCount ?? 1)
        Write-Ok "Total assignments: $($resp.total ?? '?')  (pages: $pageCount)"
    }

    if ($resp.entities) { $allAssignments.AddRange($resp.entities) }
    $pageNum++

} while ($pageNum -le $pageCount)

Write-Ok "Fetched $($allAssignments.Count) extension assignments."

# ─────────────────────────────────────────────────────────────
# Step 4 — Extract work-phone extensions from user profiles
# ─────────────────────────────────────────────────────────────
# Mirrors AuditOrchestrator.ExtractWorkPhoneExtension():
#   Look in primaryContactInfo for mediaType=PHONE entries,
#   prefer type="work", return the first non-empty .extension value.

function Extract-WorkPhoneExtension($User) {
    $contacts = $User.primaryContactInfo
    if (-not $contacts) { return $null }

    # Sort: work entries first
    $sorted = $contacts | Where-Object { $_.mediaType -eq 'PHONE' } |
        Sort-Object { if ($_.type -ieq 'work') { 0 } else { 1 } }

    foreach ($ci in $sorted) {
        if (-not [string]::IsNullOrWhiteSpace($ci.extension)) {
            return $ci.extension.Trim()
        }
    }
    return $null
}

# ─────────────────────────────────────────────────────────────
# Step 5 — Build indexes
# ─────────────────────────────────────────────────────────────

Write-Step "Building extension indexes ..."

# profileByExt: normalized-ext -> list of {UserId, UserName, State, RawExt}
$profileByExt    = @{}
$invalidProfiles = [System.Collections.Generic.List[object]]::new()

foreach ($u in $allUsers) {
    $rawExt = Extract-WorkPhoneExtension $u
    $normExt = Normalize-Extension $rawExt

    if ($null -eq $normExt) {
        if (-not [string]::IsNullOrWhiteSpace($rawExt)) {
            # Had something but it was invalid
            $invalidProfiles.Add([PSCustomObject]@{
                UserId   = $u.id
                UserName = $u.name
                State    = $u.state
                RawExt   = $rawExt
                Reason   = "Could not normalize (non-digit or length out of range)"
            })
        }
        continue
    }

    if (-not $profileByExt.ContainsKey($normExt)) { $profileByExt[$normExt] = @() }
    $profileByExt[$normExt] += [PSCustomObject]@{
        UserId   = $u.id
        UserName = $u.name
        State    = $u.state
        RawExt   = $rawExt
    }
}

# assignByExt: normalized-ext -> list of {AssignmentId, RawExt, TargetType, TargetId}
$assignByExt       = @{}
$invalidAssignments = [System.Collections.Generic.List[object]]::new()

foreach ($a in $allAssignments) {
    $rawExt  = $a.extension
    $normExt = Normalize-Extension $rawExt

    if ($null -eq $normExt) {
        if (-not [string]::IsNullOrWhiteSpace($rawExt)) {
            $invalidAssignments.Add([PSCustomObject]@{
                AssignmentId = $a.id
                RawExt       = $rawExt
                Reason       = "Could not normalize"
            })
        }
        continue
    }

    if (-not $assignByExt.ContainsKey($normExt)) { $assignByExt[$normExt] = @() }
    $assignByExt[$normExt] += [PSCustomObject]@{
        AssignmentId = $a.id
        RawExt       = $rawExt
        TargetType   = $a.assignedTo?.type
        TargetId     = $a.assignedTo?.id
    }
}

Write-Ok "Unique profile extensions:    $($profileByExt.Count)"
Write-Ok "Unique assignment extensions: $($assignByExt.Count)"

# ─────────────────────────────────────────────────────────────
# Step 6 — Run audit checks
# ─────────────────────────────────────────────────────────────

Write-Step "Running audit checks ..."

# (1) Duplicate extensions across user profiles
$dupProfiles = [System.Collections.Generic.List[object]]::new()
foreach ($ext in $profileByExt.Keys) {
    $users = $profileByExt[$ext]
    $distinctUserIds = $users | Select-Object -ExpandProperty UserId | Sort-Object -Unique
    if ($distinctUserIds.Count -ge 2) {
        foreach ($u in $users) {
            $dupProfiles.Add([PSCustomObject]@{
                FindingType = "DuplicateProfileExtension"
                ExtensionKey = $ext
                UserId   = $u.UserId
                UserName = $u.UserName
                State    = $u.State
                RawExt   = $u.RawExt
            })
        }
    }
}
$dupProfiles = $dupProfiles | Sort-Object ExtensionKey, UserName

# (2) Duplicate extensions in telephony assignment pool
$dupAssigned = [System.Collections.Generic.List[object]]::new()
foreach ($ext in $assignByExt.Keys) {
    $rows = $assignByExt[$ext]
    if ($rows.Count -ge 2) {
        foreach ($a in $rows) {
            $dupAssigned.Add([PSCustomObject]@{
                FindingType  = "DuplicateAssignedExtension"
                ExtensionKey = $ext
                AssignmentId = $a.AssignmentId
                TargetType   = $a.TargetType
                TargetId     = $a.TargetId
                RawExt       = $a.RawExt
            })
        }
    }
}
$dupAssigned = $dupAssigned | Sort-Object ExtensionKey, TargetId

# (3) Profile extensions not present in telephony assignments
$profileNotAssigned = [System.Collections.Generic.List[object]]::new()
foreach ($ext in $profileByExt.Keys) {
    if (-not $assignByExt.ContainsKey($ext)) {
        foreach ($u in $profileByExt[$ext]) {
            $profileNotAssigned.Add([PSCustomObject]@{
                FindingType  = "ProfileExtensionNotAssigned"
                ExtensionKey = $ext
                UserId   = $u.UserId
                UserName = $u.UserName
                State    = $u.State
                RawExt   = $u.RawExt
            })
        }
    }
}
$profileNotAssigned = $profileNotAssigned | Sort-Object ExtensionKey, UserName

# (4) Extensions assigned in telephony but not on any user profile (optional)
$assignedMissingProfiles = [System.Collections.Generic.List[object]]::new()
if ($IncludeAssignedMissingFromProfiles) {
    foreach ($ext in $assignByExt.Keys) {
        if (-not $profileByExt.ContainsKey($ext)) {
            foreach ($a in $assignByExt[$ext]) {
                $assignedMissingProfiles.Add([PSCustomObject]@{
                    FindingType  = "AssignedExtensionMissingFromProfiles"
                    ExtensionKey = $ext
                    AssignmentId = $a.AssignmentId
                    TargetType   = $a.TargetType
                    TargetId     = $a.TargetId
                    RawExt       = $a.RawExt
                })
            }
        }
    }
    $assignedMissingProfiles = $assignedMissingProfiles | Sort-Object ExtensionKey
}

# (5) Extension on profile exists in telephony but is assigned to the wrong entity
#     i.e. the telephony record's TargetId does not match this user's id
$wrongEntity = [System.Collections.Generic.List[object]]::new()
foreach ($ext in $profileByExt.Keys) {
    if (-not $assignByExt.ContainsKey($ext)) { continue }  # caught by (3)

    $extAssignments = $assignByExt[$ext]
    foreach ($u in $profileByExt[$ext]) {
        $correctlyAssigned = $extAssignments | Where-Object {
            $_.TargetType -ieq 'USER' -and $_.TargetId -ieq $u.UserId
        }
        if (-not $correctlyAssigned) {
            foreach ($a in $extAssignments) {
                $wrongEntity.Add([PSCustomObject]@{
                    FindingType         = "ExtensionAssignedToWrongEntity"
                    ExtensionKey        = $ext
                    ProfileUserId       = $u.UserId
                    ProfileUserName     = $u.UserName
                    ProfileState        = $u.State
                    AssignmentId        = $a.AssignmentId
                    AssignedTargetType  = $a.TargetType
                    AssignedTargetId    = $a.TargetId
                })
            }
        }
    }
}
$wrongEntity = $wrongEntity | Sort-Object ExtensionKey, ProfileUserName

# ─────────────────────────────────────────────────────────────
# Step 7 — Report findings
# ─────────────────────────────────────────────────────────────

function Write-FindingSummary([string]$Title, $Items, [string]$Color = 'Yellow') {
    $count = @($Items).Count
    Write-Host ""
    Write-Host "── $Title ($count finding$(if($count -ne 1){'s'})) ──" -ForegroundColor $Color
    if ($count -eq 0) {
        Write-Host "   None." -ForegroundColor Green
    } else {
        $Items | Format-Table -AutoSize | Out-String | Write-Host
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host "  GENESYS CLOUD EXTENSION AUDIT RESULTS" -ForegroundColor White
Write-Host "  Region: $Region  |  Users: $($allUsers.Count)  |  Assignments: $($allAssignments.Count)" -ForegroundColor White
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor White

Write-FindingSummary "1. Duplicate Extensions on User Profiles"    $dupProfiles
Write-FindingSummary "2. Duplicate Extensions in Telephony Pool"   $dupAssigned
Write-FindingSummary "3. Profile Extensions Not in Telephony"      $profileNotAssigned
Write-FindingSummary "4. Extension Assigned to Wrong Entity"        $wrongEntity 'Red'

if ($IncludeAssignedMissingFromProfiles) {
    Write-FindingSummary "5. Telephony Extensions with No Profile Match" $assignedMissingProfiles
}

# Summary table
Write-Host ""
Write-Host "── Summary ──" -ForegroundColor Cyan
[PSCustomObject]@{
    "Duplicate Profile Extensions"        = @($dupProfiles).Count
    "Duplicate Telephony Assignments"     = @($dupAssigned).Count
    "Profile Extensions Not Assigned"     = @($profileNotAssigned).Count
    "Extensions Assigned to Wrong Entity" = @($wrongEntity).Count
    "Invalid Profile Extensions"          = @($invalidProfiles).Count
    "Invalid Telephony Extensions"        = @($invalidAssignments).Count
} | Format-List

# ─────────────────────────────────────────────────────────────
# Step 8 — Export CSVs (optional)
# ─────────────────────────────────────────────────────────────

if ($OutputPath) {
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

    $exports = @(
        @{ Name = "DuplicateProfileExtensions";        Data = $dupProfiles           }
        @{ Name = "DuplicateAssignedExtensions";       Data = $dupAssigned           }
        @{ Name = "ProfileExtensionsNotAssigned";      Data = $profileNotAssigned    }
        @{ Name = "ExtensionsAssignedToWrongEntity";   Data = $wrongEntity           }
        @{ Name = "InvalidProfileExtensions";          Data = $invalidProfiles       }
        @{ Name = "InvalidAssignedExtensions";         Data = $invalidAssignments    }
    )

    if ($IncludeAssignedMissingFromProfiles) {
        $exports += @{ Name = "AssignedExtensionsMissingFromProfiles"; Data = $assignedMissingProfiles }
    }

    foreach ($export in $exports) {
        $file = Join-Path $OutputPath "${timestamp}_$($export.Name).csv"
        $rows = @($export.Data)
        if ($rows.Count -gt 0) {
            $rows | Export-Csv -Path $file -NoTypeInformation -Encoding UTF8
            Write-Ok "Exported $($rows.Count) rows → $file"
        }
    }

    Write-Ok "All CSVs written to: $OutputPath"
}

Write-Host ""
Write-Host "[*] Audit complete." -ForegroundColor Cyan
