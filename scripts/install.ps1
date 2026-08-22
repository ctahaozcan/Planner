param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "dist\Yaver"
$target = Join-Path $env:LOCALAPPDATA "Programs\Yaver"

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "publish.ps1")
}

if (-not (Test-Path (Join-Path $source "Yaver.exe"))) {
    throw "Yayımlanan exe bulunamadı. Önce scripts\publish.ps1 çalıştırın."
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

$exe = Join-Path $target "Yaver.exe"
$programs = [Environment]::GetFolderPath("Programs")
$desktop = [Environment]::GetFolderPath("Desktop")

$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @($programs, $desktop)) {
    $shortcut = $shell.CreateShortcut((Join-Path $dir "Yaver.lnk"))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $target
    $shortcut.WindowStyle = 1
    $shortcut.Description = "Yaver — Günlük asistan"
    $icon = Join-Path $target "Yaver.exe"
    $shortcut.IconLocation = "$icon,0"
    $shortcut.Save()
}

Write-Host "Kuruldu: $exe"
Write-Host "Başlat menüsü ve masaüstü kısayolları oluşturuldu."
