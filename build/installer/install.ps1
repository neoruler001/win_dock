# WinNavi Dock 설치 (현재 사용자 전용, 관리자 권한 불필요)
#   %LOCALAPPDATA%\Programs\WinNavi Dock 에 복사하고 시작 메뉴 바로가기와 '앱 및 기능' 제거 항목을 등록합니다.
#   사용자 설정(%APPDATA%\WinNavi\dock.json)은 건드리지 않으므로 재설치해도 아이콘 구성이 유지됩니다.
param(
    [switch]$Silent,     # 질문 없이 기본값으로 설치 (바탕화면 바로가기: 아니오; 시작 시 실행은 항상 등록)
    [switch]$Desktop,    # 바탕화면 바로가기 만들기
    [switch]$NoStartup,  # Windows 시작 시 자동 실행 등록 안 함 (기본은 설치하면 항상 등록)
    [switch]$NoLaunch    # 설치 후 실행하지 않음
)
$ErrorActionPreference = 'Stop'

$appName = 'WinNavi Dock'
$procName = 'WinNavi.Dock'
$src = Join-Path $PSScriptRoot 'app'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\WinNavi Dock'
$exe = Join-Path $dest 'WinNavi.Dock.exe'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinNavi Dock.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'WinNavi Dock.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinNaviDock'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Ask([string]$question, [bool]$default) {
    if ($Silent) { return $default }
    $hint = if ($default) { 'Y/n' } else { 'y/N' }
    $a = Read-Host "$question ($hint)"
    if ([string]::IsNullOrWhiteSpace($a)) { return $default }
    return $a.Trim().ToLower().StartsWith('y')
}

function New-Shortcut([string]$path) {
    $sh = New-Object -ComObject WScript.Shell
    $l = $sh.CreateShortcut($path)
    $l.TargetPath = $exe
    $l.WorkingDirectory = $dest
    $l.IconLocation = "$exe,0"
    $l.Description = $appName
    $l.Save()
}

if (-not (Test-Path (Join-Path $src 'WinNavi.Dock.exe'))) {
    throw "app 폴더를 찾을 수 없습니다. 압축을 모두 푼 폴더에서 실행하세요: $src"
}

$wantDesktop = if ($Desktop) { $true } else { Ask '바탕화면 바로가기를 만들까요?' $false }
# 설치하면 항상 시작 프로그램에 등록합니다 (-NoStartup 으로만 끌 수 있음; 더 이상 설치 중에 묻지 않습니다).
$wantStartup = -not $NoStartup

Write-Host "$appName 설치 중..."

# 실행 중이면 종료 (파일이 잠겨 있으면 덮어쓸 수 없음)
$running = Get-Process -Name $procName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host '  실행 중인 WinNavi Dock을 종료합니다.'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
Copy-Item $src $dest -Recurse
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $dest
# 내려받은 zip에서 풀면 '인터넷에서 받은 파일' 표시가 붙어 SmartScreen 경고가 뜨므로 해제
Get-ChildItem $dest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

New-Shortcut $startMenu
if ($wantDesktop) { New-Shortcut $desktopLnk }

# Windows 부팅 시 자동 실행 (앱 설정의 'Windows 시작 시 실행'과 같은 값; 항상 등록, -NoStartup 시 제외)
if ($wantStartup) { Set-ItemProperty -Path $runKey -Name 'WinNaviDock' -Value "`"$exe`"" }
else { Remove-ItemProperty -Path $runKey -Name 'WinNaviDock' -ErrorAction SilentlyContinue }

# 설정 > 앱 > 설치된 앱 에 표시
$version = (Get-Item $exe).VersionInfo.ProductVersion
$sizeKb = [int]((Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
New-Item -Path $uninstallKey -Force | Out-Null
$props = @{
    DisplayName     = $appName
    DisplayVersion  = $version
    InstallLocation = $dest
    DisplayIcon     = $exe
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$dest\uninstall.ps1`""
    EstimatedSize   = $sizeKb
}
foreach ($k in $props.Keys) { Set-ItemProperty -Path $uninstallKey -Name $k -Value $props[$k] }
Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord

Write-Host "설치 완료: $dest"
if (-not $NoLaunch) {
    Start-Process $exe -WorkingDirectory $dest
    Write-Host '실행했습니다. 화면 가장자리의 독과 작업 표시줄 트레이 아이콘을 확인하세요.'
}
if (-not $Silent) { Read-Host 'Enter 키를 누르면 닫습니다' | Out-Null }
