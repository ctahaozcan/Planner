param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Planner.App\Planner.App.csproj"
$outDir = Join-Path $root "dist\Yaver"

Write-Host "Yayımlanıyor: $project -> $outDir"
if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish başarısız ($LASTEXITCODE)."
}

Write-Host "Tamam. Taşınabilir çalışma: $outDir\Yaver.exe"
Write-Host "Kurulum paketi için: .\scripts\build-setup.ps1"
