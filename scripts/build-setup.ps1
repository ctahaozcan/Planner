param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "Planner.App\Planner.App.csproj"
$iss = Join-Path $PSScriptRoot "Yaver.iss"
$setupExe = Join-Path $root "dist\Yaver-Setup.exe"
$portableExe = Join-Path $root "dist\Yaver\Yaver.exe"

function Find-Iscc {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Install-InnoSetup {
    Write-Host "Inno Setup bulunamadı. winget ile kuruluyor..."
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "Inno Setup yok ve winget de yok. https://jrsoftware.org/isinfo.php adresinden Inno Setup 6 kurun."
    }
    & winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup winget kurulumu başarısız ($LASTEXITCODE)."
    }
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw "Inno Setup kuruldu ancak ISCC.exe bulunamadı. Oturumu yenileyip tekrar deneyin."
    }
    return $iscc
}

$iscc = Find-Iscc
if (-not $iscc) {
    $iscc = Install-InnoSetup
}

Write-Host "Derleyici: $iscc"

$version = "1.2.0"
[xml]$proj = Get-Content $csproj
$verNode = $proj.Project.PropertyGroup.Version | Select-Object -First 1
if ($verNode) { $version = "$verNode".Trim() }
Write-Host "Sürüm: $version"

& (Join-Path $PSScriptRoot "publish.ps1") -Configuration $Configuration -Runtime $Runtime

if (-not (Test-Path $portableExe)) {
    throw "Yayımlanan exe bulunamadı: $portableExe"
}

if (Test-Path $setupExe) {
    Remove-Item $setupExe -Force
}

Write-Host "Kurulum paketi derleniyor..."
& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup derlemesi başarısız ($LASTEXITCODE)."
}

if (-not (Test-Path $setupExe)) {
    throw "Kurulum dosyası oluşmadı: $setupExe"
}

$setupSizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
$portableSizeMb = [math]::Round((Get-Item $portableExe).Length / 1MB, 1)
if ($setupSizeMb -lt 10) {
    throw "Kurulum dosyası beklenenden küçük ($setupSizeMb MB). Self-contained yayın eksik olabilir."
}

Write-Host ""
Write-Host "Hazır."
Write-Host "  Taşınabilir uygulama: $portableExe  ($portableSizeMb MB exe)"
Write-Host "  Kurulum:              $setupExe  ($setupSizeMb MB)"
Write-Host "Kurmak için Setup.exe dosyasına çift tıklayın."
