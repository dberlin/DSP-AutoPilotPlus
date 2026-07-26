<#
.SYNOPSIS
    Cut a release for a LivingInstinkt DSP mod: bump versions, validate, build, package,
    commit, tag, push, and publish to Thunderstore.

.DESCRIPTION
    The single sanctioned way to release. Reads the mod name from manifest.json, so one
    identical copy of this script lives at the root of each mod repo.

    -Version    is the source of truth for the release. It is written to the .csproj
                <Version>, manifest.json version_number, and Plugin.cs Version constant.
    -DepVersion is required only for mods that depend on CruiseAssistPlus; it re-pins the
                manifest dependency and the built DLL is verified to exist.
    -NoPublish  builds, packages, commits, tags, pushes, and cuts the GitHub release but
                skips the Thunderstore upload.
    -DryRun     runs validation + build + package only (no git, no release, no publish).

.EXAMPLE
    ./release.ps1 -Version 0.3.4
    ./release.ps1 -Version 0.3.2 -DepVersion 0.3.4
    ./release.ps1 -Version 0.3.4 -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$DepVersion,

    [switch]$NoPublish,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Author = 'LivingInstinkt'
$Root   = $PSScriptRoot

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Info($m) { Write-Host "    $m" -ForegroundColor DarkGray }
function Fail($m) { Write-Host "RELEASE ABORTED: $m" -ForegroundColor Red; exit 1 }

# Every source file in these repos is UTF-8 without a BOM. Windows PowerShell 5.1's Get-Content/
# Set-Content are the wrong tools for round-tripping them: Get-Content -Raw falls back to the ANSI
# codepage on a BOM-less file (so 'x' comes back as three mojibake characters) and Set-Content
# -Encoding UTF8 writes a BOM. Between them, a version bump used to corrupt every accented character,
# arrow, and dash in Plugin.cs and the .csproj, and prepend a BOM to manifest.json that Thunderstore's
# parser can reject. Read and write explicitly instead.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Read-Text($path)        { [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) }
function Write-Text($path, $text) { [System.IO.File]::WriteAllText($path, $text, $Utf8NoBom) }

# ---------------------------------------------------------------------------
# Resolve paths from the manifest
# ---------------------------------------------------------------------------
$manifestPath = Join-Path $Root 'manifest.json'
if (-not (Test-Path $manifestPath)) { Fail 'manifest.json not found next to this script.' }

$manifest = (Read-Text $manifestPath) | ConvertFrom-Json
$Mod = $manifest.name
if (-not $Mod) { Fail 'manifest.json has no "name".' }

$csprojPath    = Join-Path $Root "$Mod.csproj"
$pluginPath    = Join-Path $Root 'Plugin.cs'
$changelogPath = Join-Path $Root 'CHANGELOG.md'
$readmePath    = Join-Path $Root 'README.md'
$iconPath      = Join-Path $Root 'icon.png'
$tomlPath      = Join-Path $Root 'thunderstore.toml'
$distDir       = Join-Path $Root 'dist'
$zipName       = "$Author-$Mod-$Version.zip"
$zipPath       = Join-Path $distDir $zipName
$dllPath       = Join-Path $Root "bin/Release/$Mod.dll"

Write-Host ""
Step "Releasing $Mod $Version"
if ($DryRun)    { Info 'DRY RUN - no git, release, or publish steps will run.' }
if ($NoPublish) { Info 'NoPublish - Thunderstore upload will be skipped.' }

# ---------------------------------------------------------------------------
# 1. Pre-flight gate
# ---------------------------------------------------------------------------
Step '1/8  Pre-flight checks'

# 1a. Working tree: only release-prep files may be dirty; everything else must be committed.
$allowed = @('CHANGELOG.md', 'README.md', 'manifest.json', "$Mod.csproj", 'Plugin.cs', 'thunderstore.toml')
$porcelain = @(git status --porcelain)
$unexpected = @()
foreach ($line in $porcelain) {
    if (-not $line) { continue }
    $status = $line.Substring(0, 2)
    $path   = $line.Substring(3).Trim().Trim('"')
    if ($status -eq '??') { $unexpected += "untracked: $path"; continue }
    if ($allowed -notcontains $path) { $unexpected += "modified: $path" }
}
if ($unexpected.Count -gt 0) {
    Info ($unexpected -join "`n    ")
    Fail 'Working tree has changes outside the release-prep files. Commit your work first.'
}

# 1b. CHANGELOG top section must be for this version.
if (-not (Test-Path $changelogPath)) { Fail 'CHANGELOG.md not found.' }
$changelogText = Read-Text $changelogPath
$firstHeading = ([regex]::Match($changelogText, '(?m)^##\s+.*$')).Value
if (-not $firstHeading) { Fail 'CHANGELOG.md has no "## " version section.' }
if ($firstHeading -notmatch [regex]::Escape($Version)) {
    Fail "CHANGELOG top section '$firstHeading' does not mention $Version. Add the new entry first."
}

# 1c. Manifest description + dependencies present.
if ([string]::IsNullOrWhiteSpace($manifest.description)) { Fail 'manifest.json description is empty.' }
if (-not $manifest.dependencies) { Fail 'manifest.json has no dependencies array.' }

# 1d. CruiseAssistPlus-dependent mods: re-pin and verify the referenced DLL.
$capDep = @($manifest.dependencies | Where-Object { $_ -match 'CruiseAssistPlus' })
if ($capDep.Count -gt 0) {
    if (-not $DepVersion) { Fail 'This mod depends on CruiseAssistPlus - pass -DepVersion <cap-version>.' }
    $capDll = Join-Path $Root '..\CruiseAssistPlus\bin\Release\CruiseAssistPlus.dll'
    if (-not (Test-Path $capDll)) { Fail "Referenced CruiseAssistPlus.dll not found at $capDll (build CruiseAssistPlus first)." }
}

# 1e. README + icon present, icon is a 256x256 PNG.
if (-not (Test-Path $readmePath)) { Fail 'README.md not found.' }
if (-not (Test-Path $iconPath))   { Fail 'icon.png not found.' }
$png = [System.IO.File]::ReadAllBytes($iconPath)
# The [int] casts are load-bearing: in Windows PowerShell 5.1 -shl keeps the operand's width, so
# shifting a [byte] left by 8 or more yields 0 and every icon measures 0x0. IHDR width/height are the
# four-byte big-endian fields at offsets 16 and 20.
$pngW = ([int]$png[16] -shl 24) -bor ([int]$png[17] -shl 16) -bor ([int]$png[18] -shl 8) -bor [int]$png[19]
$pngH = ([int]$png[20] -shl 24) -bor ([int]$png[21] -shl 16) -bor ([int]$png[22] -shl 8) -bor [int]$png[23]
if ($pngW -ne 256 -or $pngH -ne 256) { Fail "icon.png must be 256x256 (found ${pngW}x${pngH})." }

# 1f. Attribution scan: no tool/AI names in any tracked text file or commit message.
#     Terms are assembled from fragments so the words never appear literally in this file.
$terms = @( ('cl' + 'aude'), ('anthro' + 'pic'), ('co-auth' + 'ored-by'), ('gener' + 'ated with') )
$pattern = ($terms -join '|')
$binaryExt = @('.png', '.dll', '.zip', '.ico', '.jpg', '.jpeg', '.gif')
$tracked = @(git ls-files)
$hits = @()
foreach ($f in $tracked) {
    if ($binaryExt -contains ([System.IO.Path]::GetExtension($f)).ToLower()) { continue }
    $full = Join-Path $Root $f
    if (-not (Test-Path $full)) { continue }
    $m = Select-String -Path $full -Pattern $pattern -List -ErrorAction SilentlyContinue
    if ($m) { $hits += $f }
}
$logHit = (git log --format=%B) | Select-String -Pattern $pattern -List -ErrorAction SilentlyContinue
if ($hits.Count -gt 0 -or $logHit) {
    if ($hits.Count) { Info ("tracked files: " + ($hits -join ', ')) }
    if ($logHit)     { Info 'a commit message matches.' }
    Fail 'Attribution/tool-name scan matched. Remove the reference before releasing.'
}
Info 'Pre-flight OK.'

# ---------------------------------------------------------------------------
# 2. Bump versions
# ---------------------------------------------------------------------------
Step "2/8  Bumping version to $Version"

$csproj = Read-Text $csprojPath
$csproj = [regex]::Replace($csproj, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
Write-Text $csprojPath $csproj

$plugin = Read-Text $pluginPath
$plugin = [regex]::Replace($plugin, '(public const string Version\s*=\s*")[^"]*(")', "`${1}$Version`${2}")
Write-Text $pluginPath $plugin

$manifestRaw = Read-Text $manifestPath
$manifestRaw = [regex]::Replace($manifestRaw, '("version_number"\s*:\s*")[^"]*(")', "`${1}$Version`${2}")
if ($DepVersion) {
    $manifestRaw = [regex]::Replace($manifestRaw, '(LivingInstinkt-CruiseAssistPlus-)\d+\.\d+\.\d+', "`${1}$DepVersion")
}
Write-Text $manifestPath $manifestRaw

# thunderstore.toml is the identity source for `tcli publish --file`; keep it in lockstep.
if (Test-Path $tomlPath) {
    $toml = Read-Text $tomlPath
    $toml = [regex]::Replace($toml, '(?m)^(versionNumber\s*=\s*")[^"]*(")', "`${1}$Version`${2}")
    if ($DepVersion) {
        $toml = [regex]::Replace($toml, '(LivingInstinkt-CruiseAssistPlus\s*=\s*")\d+\.\d+\.\d+(")', "`${1}$DepVersion`${2}")
    }
    Write-Text $tomlPath $toml
}
Info 'csproj, Plugin.cs, manifest.json, thunderstore.toml updated.'

# ---------------------------------------------------------------------------
# 3. Build Release
# ---------------------------------------------------------------------------
Step '3/8  dotnet build -c Release'
dotnet build $csprojPath -c Release --nologo
if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
if (-not (Test-Path $dllPath)) { Fail "Built DLL not found at $dllPath." }

# ---------------------------------------------------------------------------
# 4. Package the Thunderstore zip (files at archive root)
# ---------------------------------------------------------------------------
Step "4/8  Packaging $zipName"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$payload = @($manifestPath, $iconPath, $readmePath, $changelogPath, $dllPath)
Compress-Archive -Path $payload -DestinationPath $zipPath -CompressionLevel Optimal -Force
Info "Wrote $zipPath"

if ($DryRun) {
    Write-Host ""
    Step 'DRY RUN complete - stopped before git/release/publish.'
    exit 0
}

# ---------------------------------------------------------------------------
# 5. Commit + tag (source only; the zip stays gitignored)
# ---------------------------------------------------------------------------
Step '5/8  Commit + tag'
$toCommit = @($csprojPath, $pluginPath, $manifestPath, $changelogPath, $readmePath)
if (Test-Path $tomlPath) { $toCommit += $tomlPath }
git add -- $toCommit
git commit -m "Release $Mod v$Version"
if ($LASTEXITCODE -ne 0) { Fail 'git commit failed.' }
git tag -a "v$Version" -m "$Mod v$Version"
if ($LASTEXITCODE -ne 0) { Fail 'git tag failed.' }

# ---------------------------------------------------------------------------
# 6. Push branch + tag
# ---------------------------------------------------------------------------
Step '6/8  Push'
git push origin HEAD --follow-tags
if ($LASTEXITCODE -ne 0) { Fail 'git push failed.' }

# ---------------------------------------------------------------------------
# 7. GitHub Release (attach the zip)
# ---------------------------------------------------------------------------
Step '7/8  GitHub Release'
# Release notes = the top CHANGELOG section.
$sections = [regex]::Split($changelogText, '(?m)^(?=##\s)') | Where-Object { $_ -match '^##\s' }
$notes = ($sections | Select-Object -First 1).Trim()
$notesFile = Join-Path $env:TEMP "$Mod-$Version-notes.md"
Write-Text $notesFile $notes
gh release create "v$Version" $zipPath --title "$Mod $Version" --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed.' }
Remove-Item $notesFile -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# 8. Thunderstore publish
# ---------------------------------------------------------------------------
if ($NoPublish) {
    Step '8/8  Thunderstore publish skipped (-NoPublish).'
} else {
    Step '8/8  Thunderstore publish'
    if (-not $env:THUNDERSTORE_API_TOKEN) { Fail 'THUNDERSTORE_API_TOKEN is not set in the environment.' }
    tcli publish --file $zipPath --token $env:THUNDERSTORE_API_TOKEN
    if ($LASTEXITCODE -ne 0) { Fail 'tcli publish failed.' }
}

Write-Host ""
Step "$Mod $Version released."
