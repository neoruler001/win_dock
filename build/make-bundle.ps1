# Builds the distributable zip: dist\WinNavi-Dock-<version>-win-x64.zip
#
# The app is published framework-dependent, then the .NET 8 runtime that is installed on THIS machine
# (Microsoft.NETCore.App + Microsoft.WindowsDesktop.App) is copied next to it and the runtime config is
# switched to the self-contained form. The target PC then needs no .NET installation.
# (A regular `dotnet publish --self-contained` needs the runtime pack from nuget.org; this is the offline route.)
#
# Usage:  powershell -ExecutionPolicy Bypass -File build\make-bundle.ps1 [-RuntimeVersion 8.0.11]
param(
    [string]$RuntimeVersion = '8.0.11'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Compress-Archive and ZipFile.CreateFromDirectory on Windows PowerShell 5.1 store entry names with '\',
# which breaks folders in some unzip tools (macOS, Linux, 7-Zip on some settings). Write '/' ourselves.
function New-Zip([string]$srcDir, [string]$zipPath, [string]$prefix = '') {
    $srcDir = (Resolve-Path $srcDir).Path.TrimEnd('\')
    $fs = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $srcDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($srcDir.Length + 1).Replace('\', '/')
            $entry = $zip.CreateEntry($prefix + $rel, [IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            $in = [IO.File]::OpenRead($_.FullName)
            try { $in.CopyTo($es) } finally { $in.Dispose(); $es.Dispose() }
        }
    } finally { $zip.Dispose(); $fs.Dispose() }
}

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\WinNavi.Dock\WinNavi.Dock.csproj'
$dist = Join-Path $root 'dist'
$fd = Join-Path $dist 'fd'
$stage = Join-Path $dist 'stage'
$app = Join-Path $stage 'app'
$dotnet = Join-Path $env:ProgramFiles 'dotnet'

$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
$zip = Join-Path $dist "WinNavi-Dock-$version-win-x64.zip"

$netcore = Join-Path $dotnet "shared\Microsoft.NETCore.App\$RuntimeVersion"
$desktop = Join-Path $dotnet "shared\Microsoft.WindowsDesktop.App\$RuntimeVersion"
$fxr = Join-Path $dotnet "host\fxr\$RuntimeVersion\hostfxr.dll"
foreach ($p in $netcore, $desktop, $fxr) {
    if (-not (Test-Path $p)) { throw "Runtime $RuntimeVersion not found on this machine: $p" }
}

Remove-Item $fd, $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $app | Out-Null

Write-Host '[1/4] dotnet publish (Release, win-x64)'
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $fd | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host "[2/5] copy app + runtime $RuntimeVersion"
Copy-Item (Join-Path $fd 'WinNavi.Dock.exe'), (Join-Path $fd 'WinNavi.Dock.dll') $app
Copy-Item "$netcore\*" $app -Recurse -Force
Copy-Item "$desktop\*" $app -Recurse -Force
Copy-Item $fxr $app -Force
Remove-Item (Join-Path $app '*.deps.json') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $app '*.pdb') -Force -ErrorAction SilentlyContinue

Write-Host '[3/5] self-contained runtimeconfig + deps.json'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$rc = Get-Content (Join-Path $fd 'WinNavi.Dock.runtimeconfig.json') -Raw | ConvertFrom-Json
$opts = [ordered]@{
    includedFrameworks = @(
        [ordered]@{ name = 'Microsoft.NETCore.App'; version = $RuntimeVersion },
        [ordered]@{ name = 'Microsoft.WindowsDesktop.App'; version = $RuntimeVersion }
    )
    configProperties = $rc.runtimeOptions.configProperties
}
[IO.File]::WriteAllText((Join-Path $app 'WinNavi.Dock.runtimeconfig.json'),
    (@{ runtimeOptions = $opts } | ConvertTo-Json -Depth 8), $utf8)

# The host only treats the folder as self-contained when the app's deps.json is present, and then it loads
# exactly the assemblies listed there. So merge the two frameworks' deps (their runtime assets) into the app's.
$appDeps = Get-Content (Join-Path $fd 'WinNavi.Dock.deps.json') -Raw | ConvertFrom-Json
$tn = $appDeps.runtimeTarget.name
$appTarget = $appDeps.targets.PSObject.Properties[$tn].Value
foreach ($fw in "$netcore\Microsoft.NETCore.App.deps.json", "$desktop\Microsoft.WindowsDesktop.App.deps.json") {
    $d = Get-Content $fw -Raw | ConvertFrom-Json
    foreach ($p in $d.targets.PSObject.Properties[$tn].Value.PSObject.Properties) {
        if (-not $appTarget.PSObject.Properties[$p.Name]) { $appTarget | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value }
    }
    foreach ($p in $d.libraries.PSObject.Properties) {
        if (-not $appDeps.libraries.PSObject.Properties[$p.Name]) { $appDeps.libraries | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value }
    }
}
[IO.File]::WriteAllText((Join-Path $app 'WinNavi.Dock.deps.json'), ($appDeps | ConvertTo-Json -Depth 100), $utf8)

Write-Host '[4/5] installer zip'
Copy-Item (Join-Path $PSScriptRoot 'installer\*') $stage -Recurse -Force
Remove-Item $zip -Force -ErrorAction SilentlyContinue
New-Zip $stage $zip

Write-Host '[5/5] portable zip'
# Flat folder: the exe, the runtime and a portable.txt marker (settings then live in .\data).
$portableName = 'WinNavi-Dock-Portable'
$portable = Join-Path $dist "portable\$portableName"
$zipPortable = Join-Path $dist "WinNavi-Dock-$version-win-x64-portable.zip"
Remove-Item (Join-Path $dist 'portable') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item "$app\*" $portable -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'portable\README.txt') $portable
[IO.File]::WriteAllText((Join-Path $portable 'portable.txt'),
    "WinNavi Dock portable mode: settings are stored in the data folder next to this file. Delete this file to use %AppData% instead.`r`n", $utf8)
Remove-Item $zipPortable -Force -ErrorAction SilentlyContinue
New-Zip $portable $zipPortable "$portableName/"

foreach ($z in $zip, $zipPortable) {
    $mb = [math]::Round((Get-Item $z).Length / 1MB, 1)
    Write-Host "done: $z ($mb MB)"
}
