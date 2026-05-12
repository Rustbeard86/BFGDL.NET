param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$commonArgs = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:PublishTrimmed=false"
)

# ── Helper: backup cache/, publish, restore cache/ ──────────────────────────
#
# Each publish output folder may contain a cache/ directory with downloaded
# metadata and images that we don't want to nuke on every publish run.
# This function moves it aside before the publish (which clobbers the output
# dir), runs dotnet publish, then moves it back in a finally block.
#
function Invoke-PublishWithCacheBackup {
    param(
        [string]   $ProjectPath,
        [string]   $OutDir,
        [string[]] $ExtraArgs
    )

    $cacheDir  = Join-Path $OutDir "cache"
    $backupDir = Join-Path $OutDir ".cache_backup_tmp"

    # Move existing cache out of the way
    if (Test-Path $cacheDir) {
        Write-Host "  [cache] Backing up $cacheDir"
        if (Test-Path $backupDir) { Remove-Item $backupDir -Recurse -Force }
        Move-Item $cacheDir $backupDir
    }

    try {
        dotnet publish $ProjectPath @commonArgs @ExtraArgs -o $OutDir
    }
    finally {
        # Restore cache regardless of publish success/failure
        if (Test-Path $backupDir) {
            Write-Host "  [cache] Restoring $cacheDir"
            if (Test-Path $cacheDir) { Remove-Item $cacheDir -Recurse -Force }
            Move-Item $backupDir $cacheDir
        }
    }
}

# ── 1. CLI — framework-dependent (small, requires .NET runtime installed) ──
$fdOut = "publish\framework-dependent"
Write-Host ""
Write-Host "==> CLI  framework-dependent  ->  $fdOut"
Invoke-PublishWithCacheBackup `
    -ProjectPath ".\BFGDL.NET.csproj" `
    -OutDir      $fdOut `
    -ExtraArgs   @("/p:SelfContained=false")

# ── 2. CLI — self-contained / release (single fat binary, no runtime needed) ──
$scOut = "publish\release"
Write-Host ""
Write-Host "==> CLI  self-contained        ->  $scOut"
Invoke-PublishWithCacheBackup `
    -ProjectPath ".\BFGDL.NET.csproj" `
    -OutDir      $scOut `
    -ExtraArgs   @("/p:SelfContained=true")

# ── 3. GUI — framework-dependent ──
$guiFdOut = "publish\framework-dependent\gui"
Write-Host ""
Write-Host "==> GUI  framework-dependent  ->  $guiFdOut"
Invoke-PublishWithCacheBackup `
    -ProjectPath ".\GUI\BFGDL.NET.GUI.csproj" `
    -OutDir      $guiFdOut `
    -ExtraArgs   @("/p:SelfContained=false")

# ── 4. GUI — self-contained / release ──
$guiScOut = "publish\release\gui"
Write-Host ""
Write-Host "==> GUI  self-contained        ->  $guiScOut"
Invoke-PublishWithCacheBackup `
    -ProjectPath ".\GUI\BFGDL.NET.GUI.csproj" `
    -OutDir      $guiScOut `
    -ExtraArgs   @("/p:SelfContained=true")

Write-Host ""
Write-Host "Done."
Write-Host "  CLI  framework-dependent : $fdOut"
Write-Host "  CLI  self-contained      : $scOut"
Write-Host "  GUI  framework-dependent : $guiFdOut"
Write-Host "  GUI  self-contained      : $guiScOut"
