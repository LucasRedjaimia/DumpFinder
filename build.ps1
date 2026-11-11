# Builds Dump Finder (.NET Framework 4.8, single exe) with the .NET SDK.
# Usage: .\build.ps1 [-Configuration Release|Debug] [-NoZip] [-Icon]
#   -Icon  regenerates src\Assets\dumpfinder.ico from branding\make-icon.ps1 first
param(
    [ValidateSet("Release", "Debug")][string]$Configuration = "Release",
    [switch]$NoZip,
    [switch]$Icon
)

$ErrorActionPreference = "Stop"
$projDir   = $PSScriptRoot
$csproj    = Join-Path $projDir "src\DumpFinder.csproj"
$outExe    = Join-Path $projDir "DumpFinder.exe"
# Kept for existing MCP client configs that still point at the legacy name.
$compatExe = Join-Path $projDir "WinCrashDoctor.exe"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -and (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe")) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not $dotnet) { throw ".NET SDK not found. Install it: winget install Microsoft.DotNet.SDK.8" }

# 1. Optional icon regeneration (WPF renderer, no dependency)
if ($Icon) {
    & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $projDir "branding\make-icon.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Icon generation failed" }
    Copy-Item (Join-Path $projDir "branding\out\dumpfinder.ico") (Join-Path $projDir "src\Assets\dumpfinder.ico") -Force
}

# 2. Stop running instances so the executables are not locked
Get-Process DumpFinder, WinCrashDoctor -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# 3. Build
& $dotnet build $csproj -c $Configuration -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

$built = Join-Path $projDir "src\bin\$Configuration\DumpFinder.exe"
if (-not (Test-Path $built)) { throw "Build output not found: $built" }

# 4. Publish next to the script (+ legacy mirror) and zip
foreach ($target in $outExe, $compatExe) {
    try { Copy-Item $built $target -Force }
    catch {
        $stale = [System.IO.Path]::ChangeExtension($target, ".old_" + [System.IO.Path]::GetRandomFileName() + ".exe")
        Move-Item $target $stale -Force
        Copy-Item $built $target -Force
    }
}

$version = (Get-Item $outExe).VersionInfo.ProductVersion
if (-not $NoZip) {
    $zipPath = Join-Path $projDir ("DumpFinder_v{0}.zip" -f $version)
    Compress-Archive -Path $outExe -DestinationPath $zipPath -Force
}

$fi = Get-Item $outExe
Write-Output ("SUCCESS! Built {0} v{1} ({2} KB, {3})" -f $fi.FullName, $version, [math]::Round($fi.Length / 1KB, 1), $Configuration)
