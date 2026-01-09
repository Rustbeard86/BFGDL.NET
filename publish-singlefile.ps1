param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "publish",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$sc = if ($SelfContained.IsPresent) { "true" } else { "false" }

Write-Host "Publishing ($Configuration) for $Runtime (SelfContained=$sc) ..."

# Note: WebView2 + WinForms implies Windows TFM; single-file is supported.
# Default is framework-dependent since it keeps output smaller.
dotnet publish .\BFGDL.NET.csproj `
    -c $Configuration `
    -r $Runtime `
    -o $Output `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:SelfContained=$sc `
    /p:PublishTrimmed=false

Write-Host "Done. Output: $Output"
