$ErrorActionPreference = "Stop"
$target = Join-Path $env:LOCALAPPDATA "Programs\Yaver"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name "Yaver","Planlayici" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name "Yaver" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $runKey -Name "Planlayici" -ErrorAction SilentlyContinue
}

$programs = [Environment]::GetFolderPath("Programs")
$desktop = [Environment]::GetFolderPath("Desktop")
foreach ($dir in @($programs, $desktop)) {
    foreach ($name in @("Yaver.lnk", "Planlayıcı.lnk")) {
        $lnk = Join-Path $dir $name
        if (Test-Path $lnk) { Remove-Item $lnk -Force }
    }
}

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}

$legacyInstall = Join-Path $env:LOCALAPPDATA "Programs\Planlayici"
if (Test-Path $legacyInstall) {
    Remove-Item $legacyInstall -Recurse -Force
}

Write-Host "Uygulama kaldırıldı. Görev ve kişi verileri duruyor: $env:LOCALAPPDATA\Yaver"
Write-Host "Eski Planlayıcı verisi duruyorsa: $env:LOCALAPPDATA\Planlayici"
Write-Host "Verileri de silmek için bu klasörleri elle silin."
