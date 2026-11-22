# Publishes a new Dump Finder version on GitHub: bumps the version, builds, commits, tags, pushes
# and creates the GitHub release with DumpFinder.exe attached. Users get the "new version" card at next launch.
#
# Usage:  .\release.ps1 -Version 6.1.0
#   Release notes are read from release-notes\v<Version>.md (Markdown). If the file does not exist yet,
#   a template is created and the script stops so you can fill it in.
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

# Runs git / gh and fails only on their exit code. Windows PowerShell 5.1 turns anything a native program writes
# to stderr (git's "LF will be replaced by CRLF" warnings, git push progress) into an error when the output is
# captured, which stopped a release half-way under $ErrorActionPreference = "Stop".
function Invoke-Native([string]$What, [scriptblock]$Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $Command 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $saved }
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh -and (Test-Path "$env:ProgramFiles\GitHub CLI\gh.exe")) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not $gh) { throw "GitHub CLI not found. Install it: winget install GitHub.cli" }
try { Invoke-Native "gh auth status" { & $gh auth status } | Out-Null }
catch { throw "GitHub CLI is not signed in. Run: gh auth login" }

# 1. Release notes
$notesDir = Join-Path $root "release-notes"
$notes = Join-Path $notesDir "v$Version.md"
if (-not (Test-Path $notes)) {
    New-Item -ItemType Directory -Force $notesDir | Out-Null
    $template = "## What's new`n- `n`n## Fixes`n- `n"
    [IO.File]::WriteAllText($notes, $template, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Release notes template created: $notes"
    Write-Output "Fill it in (what is new / what is fixed), then run this script again."
    exit 1
}

# 2. The version must be newer than every existing Windows tag (v*; the Mac app has its own mac-v* tags)
$current = [version]$Version
$tags = git tag --list "v*" | ForEach-Object { try { [version]($_.TrimStart('v')) } catch { $null } } | Where-Object { $_ }
$latest = $tags | Sort-Object -Descending | Select-Object -First 1
if ($latest -and $current -le $latest) { throw "Version $Version is not newer than the latest tag v$latest" }

# 3. Bump the version in the sources (UTF-8, no BOM, like the files themselves)
$utf8 = New-Object Text.UTF8Encoding($false)
function Set-InFile([string]$path, [string]$pattern, [string]$replacement) {
    $full = Join-Path $root $path
    $text = [IO.File]::ReadAllText($full, $utf8)
    $new = [regex]::Replace($text, $pattern, $replacement)
    if ($new -eq $text -and -not ($text -match [regex]::Escape($replacement))) { throw "Version pattern not found in $path" }
    [IO.File]::WriteAllText($full, $new, $utf8)
}
Set-InFile "src\Program.cs" 'AssemblyVersion\("[\d\.]+"\)' "AssemblyVersion(""$Version.0"")"
Set-InFile "src\Program.cs" 'AssemblyFileVersion\("[\d\.]+"\)' "AssemblyFileVersion(""$Version.0"")"
Set-InFile "src\Core\McpServer.cs" 'ServerVersion = "[\d\.]+"' "ServerVersion = ""$Version"""

# 4. Build
& (Join-Path $root "build.ps1") -NoZip
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Build failed" }
$exe = Join-Path $root "DumpFinder.exe"
$built = (Get-Item $exe).VersionInfo.FileVersion
if ($built -ne "$Version.0") { throw "Built exe reports $built, expected $Version.0" }

# 5. Commit, tag, push
Invoke-Native "git add" { git add -A }
Invoke-Native "git commit" { git commit -m "Version $Version" }
Invoke-Native "git tag" { git tag -a "v$Version" -m "Dump Finder $Version" }
Invoke-Native "git push" { git push origin HEAD }
Invoke-Native "git push (tag)" { git push origin "v$Version" }

# 6. GitHub release with the exe, named after its version (DumpFinder-6.4.1.exe, like the Mac zip). With one name
# for every release, browsers saved updates as "DumpFinder (1).exe", "DumpFinder (2).exe"… next to the old
# DumpFinder.exe, and opening DumpFinder.exe started the old version again. Installed apps take the first .exe
# asset whatever its name, so every version (6.0 and later) still finds it.
# --latest: installed Windows apps read /releases/latest, so it must always be a Windows release
# (Mac releases are published with --latest=false by mac/release.sh).
$assetDir = Join-Path ([IO.Path]::GetTempPath()) ("dumpfinder-release-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $assetDir | Out-Null
$asset = Join-Path $assetDir "DumpFinder-$Version.exe"
Copy-Item $exe $asset
try
{
    $ghArgs = @("release", "create", "v$Version", $asset, "--title", "Dump Finder $Version", "--notes-file", $notes, "--verify-tag", "--latest")
    if ($Draft) { $ghArgs += "--draft" }
    Invoke-Native "gh release create" { & $gh @ghArgs }
}
finally { Remove-Item -Recurse -Force $assetDir -ErrorAction SilentlyContinue }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Output ""
Write-Output "Released Dump Finder $Version"
Write-Output "SHA-256 of DumpFinder-$Version.exe: $hash"
