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

# ── Helper: backup all cache/ subtrees, clean output dir, publish all projects, restore ──
#
# Accepts multiple ProjectPaths — all are published into the same OutDir after a
# single backup/wipe cycle, so CLI and GUI share one directory (and one cache).
#
function Invoke-PublishWithCacheBackup {
    param(
        [string[]] $ProjectPaths,
        [string]   $OutDir,
        [string[]] $ExtraArgs
    )

    # Backup lives NEXT TO $OutDir, never inside it.
    $backupRoot  = "${OutDir}__cache_bak"
    $restoreList = [System.Collections.Generic.List[pscustomobject]]::new()

    if (Test-Path $OutDir) {
        $absOut = (Resolve-Path $OutDir).Path.TrimEnd('\')

        # Move every cache/ subtree aside before the wipe.
        Get-ChildItem $absOut -Recurse -Directory -Filter "cache" -ErrorAction SilentlyContinue |
            ForEach-Object {
                $relPath = $_.FullName.Substring($absOut.Length).TrimStart('\')
                $dest    = Join-Path $backupRoot $relPath
                Write-Host "  [cache] Backing up $($_.FullName)"
                $null = New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force
                if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
                Move-Item $_.FullName $dest
                $restoreList.Add([pscustomobject]@{ Target = $_.FullName; Source = $dest })
            }

        Write-Host "  [clean] Wiping $absOut"
        Remove-Item $absOut -Recurse -Force
    }

    try {
        foreach ($proj in $ProjectPaths) {
            dotnet publish $proj @commonArgs @ExtraArgs -o $OutDir
        }
    }
    finally {
        # Restore all caches regardless of publish success/failure.
        foreach ($item in $restoreList) {
            Write-Host "  [cache] Restoring $($item.Target)"
            $null = New-Item -ItemType Directory -Path (Split-Path $item.Target -Parent) -Force
            if (Test-Path $item.Target) { Remove-Item $item.Target -Recurse -Force }
            Move-Item $item.Source $item.Target
        }
        if (Test-Path $backupRoot) {
            Remove-Item $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ── 1. Framework-dependent — CLI + GUI share one directory ──────────────────
$fdOut = "publish\framework-dependent"
Write-Host ""
Write-Host "==> framework-dependent  (CLI + GUI)  ->  $fdOut"
Invoke-PublishWithCacheBackup `
    -ProjectPaths @(".\BFGDL.NET.csproj", ".\GUI\BFGDL.NET.GUI.csproj") `
    -OutDir       $fdOut `
    -ExtraArgs    @("/p:SelfContained=false")

# ── 2. Self-contained / release — CLI + GUI share one directory ─────────────
$scOut = "publish\release"
Write-Host ""
Write-Host "==> self-contained       (CLI + GUI)  ->  $scOut"
Invoke-PublishWithCacheBackup `
    -ProjectPaths @(".\BFGDL.NET.csproj", ".\GUI\BFGDL.NET.GUI.csproj") `
    -OutDir       $scOut `
    -ExtraArgs    @("/p:SelfContained=true")

Write-Host ""
Write-Host "Done."
Write-Host "  framework-dependent : $fdOut"
Write-Host "  self-contained      : $scOut"
