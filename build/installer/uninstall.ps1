# WinNavi Dock 제거
#   프로그램 파일, 바로가기, 시작 시 실행 등록, '앱 및 기능' 항목을 지웁니다.
#   사용자 설정(%APPDATA%\WinNavi)은 기본적으로 남겨 두며, -PurgeSettings 로 함께 지울 수 있습니다.
param(
    [switch]$Silent,
    [switch]$PurgeSettings
)
$ErrorActionPreference = 'Continue'

$procName = 'WinNavi.Dock'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\WinNavi Dock'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinNavi Dock.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'WinNavi Dock.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinNaviDock'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$settingsDir = Join-Path $env:APPDATA 'WinNavi'

if (-not $Silent -and -not $PurgeSettings -and (Test-Path $settingsDir)) {
    $a = Read-Host "저장된 설정과 아이콘 구성($settingsDir)도 지울까요? (y/N)"
    if ($a.Trim().ToLower().StartsWith('y')) { $PurgeSettings = $true }
}

Write-Host 'WinNavi Dock 제거 중...'
$running = Get-Process -Name $procName -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Remove-Item $startMenu, $desktopLnk -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'WinNaviDock' -ErrorAction SilentlyContinue
Remove-Item $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
if ($PurgeSettings) { Remove-Item $settingsDir -Recurse -Force -ErrorAction SilentlyContinue }

# 이 스크립트도 설치 폴더 안에 있으므로, 종료된 뒤에 폴더를 지우도록 따로 실행
if (Test-Path $dest) {
    Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "ping -n 3 127.0.0.1 >nul & rmdir /s /q `"$dest`""
}

Write-Host '제거했습니다.'
if (-not $Silent) { Read-Host 'Enter 키를 누르면 닫습니다' | Out-Null }
