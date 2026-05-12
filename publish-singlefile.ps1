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

# ── 1. CLI — framework-dependent (small, requires .NET runtime installed) ──
$fdOut = "publish\framework-dependent"
Write-Host ""
Write-Host "==> CLI  framework-dependent  ->  $fdOut"
dotnet publish .\BFGDL.NET.csproj @commonArgs `
    /p:SelfContained=false `
    -o $fdOut

# ── 2. CLI — self-contained / release (single fat binary, no runtime needed) ──
$scOut = "publish\release"
Write-Host ""
Write-Host "==> CLI  self-contained        ->  $scOut"
dotnet publish .\BFGDL.NET.csproj @commonArgs `
    /p:SelfContained=true `
    -o $scOut

# ── 3. GUI — framework-dependent ──
$guiFdOut = "publish\framework-dependent\gui"
Write-Host ""
Write-Host "==> GUI  framework-dependent  ->  $guiFdOut"
dotnet publish .\GUI\BFGDL.NET.GUI.csproj @commonArgs `
    /p:SelfContained=false `
    -o $guiFdOut

# ── 4. GUI — self-contained / release ──
$guiScOut = "publish\release\gui"
Write-Host ""
Write-Host "==> GUI  self-contained        ->  $guiScOut"
dotnet publish .\GUI\BFGDL.NET.GUI.csproj @commonArgs `
    /p:SelfContained=true `
    -o $guiScOut

Write-Host ""
Write-Host "Done."
Write-Host "  CLI  framework-dependent : $fdOut"
Write-Host "  CLI  self-contained      : $scOut"
Write-Host "  GUI  framework-dependent : $guiFdOut"
Write-Host "  GUI  self-contained      : $guiScOut"
