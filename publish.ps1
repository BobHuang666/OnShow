param(
    [switch]$SelfContained
)

# OnShow publish script (ASCII only: Windows PowerShell 5.1 reads .ps1 as ANSI,
# non-ASCII text without BOM breaks parsing).
# Default : framework-dependent single file (target needs .NET 8 Desktop Runtime, small size)
# -SelfContained : self-contained single file (~130MB, no runtime needed)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$out = Join-Path $root "dist"

& $dotnet publish (Join-Path $root "src\OnShow.App\OnShow.App.csproj") `
    -c Release -r win-x64 `
    --self-contained:$SelfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Publish OK: $out\OnShow.exe" -ForegroundColor Green
    if (-not $SelfContained) {
        Write-Host "NOTE: framework-dependent build - target machine needs .NET 8 Desktop Runtime (x64)." -ForegroundColor Yellow
    }
}
