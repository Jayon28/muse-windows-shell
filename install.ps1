# 安装 Muse for Windows 到本机：
# 1) 复制 publish\ 到 %LOCALAPPDATA%\Programs\MuseShell
# 2) 在桌面和开始菜单建快捷方式
# 3) 注册开机自启（静默起到托盘：Muse.exe --autostart）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root 'publish'
if (-not (Test-Path (Join-Path $publish 'Muse.exe'))) {
    throw '没找到 publish\Muse.exe，请先运行 build.ps1'
}
$dest = Join-Path $env:LOCALAPPDATA 'Programs\MuseShell'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $publish '*') $dest -Recurse -Force
$exe = Join-Path $dest 'Muse.exe'

# 运行时检查：缺 .NET 6 桌面运行时或 WebView2 时，先用 Redist\ 里的官方安装包静默装；
# Redist 里没有就从微软官方直链下载安装；全失败才打印官网地址让用户自己下。
$redist = Join-Path $root 'Redist'
function Test-DotNet6Desktop {
    $base = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    return (Test-Path $base) -and [bool](Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '6.*' })
}
function Test-WebView2 {
    foreach ($p in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                   'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') {
        $pv = (Get-ItemProperty -Path $p -Name pv -ErrorAction SilentlyContinue).pv
        if ($pv -and $pv -ne '0.0.0.0') { return $true }
    }
    return $false
}
function Install-Runtime([string]$label, [string]$redistFile, [string]$url, [string]$silentArgs, [scriptblock]$test) {
    if (& $test) { return }
    $pkg = Join-Path $redist $redistFile
    if (-not (Test-Path $pkg)) {
        Write-Host "未检测到 $label，正在从微软官网下载…"
        $pkg = Join-Path $env:TEMP $redistFile
        try { Invoke-WebRequest -Uri $url -OutFile $pkg } catch { $pkg = $null }
    }
    if ($pkg -and (Test-Path $pkg)) {
        Write-Host "正在安装 $label …"
        Start-Process $pkg -ArgumentList $silentArgs -Wait
    }
    if (-not (& $test)) { Write-Host "仍缺 $label，请手动下载安装：$url" }
}
Install-Runtime '.NET 6 桌面运行时' 'windowsdesktop-runtime-6.0-win-x64.exe' 'https://aka.ms/dotnet/6.0/windowsdesktop-runtime-win-x64.exe' '/install /quiet /norestart' { Test-DotNet6Desktop }
Install-Runtime 'WebView2 运行时' 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe' 'https://go.microsoft.com/fwlink/?linkid=2124701' '/silent /install' { Test-WebView2 }

$shell = New-Object -ComObject WScript.Shell
# 桌面实际路径从注册表读，兼容 OneDrive 重定向
$desktop = $shell.SpecialFolders.Item('Desktop')
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# 改名迁移：清掉旧名 Muse 的快捷方式和自启项（存在才删）
Remove-Item (Join-Path $desktop 'Muse.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $startMenu 'Muse.lnk') -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'Muse' -ErrorAction SilentlyContinue

# 桌面快捷方式
$lnk = $shell.CreateShortcut((Join-Path $desktop 'Muse for Windows.lnk'))
$lnk.TargetPath = $exe
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Muse for Windows'
$lnk.Save()
# 开始菜单快捷方式
$lnk2 = $shell.CreateShortcut((Join-Path $startMenu 'Muse for Windows.lnk'))
$lnk2.TargetPath = $exe
$lnk2.IconLocation = "$exe,0"
$lnk2.Description = 'Muse for Windows'
$lnk2.Save()
# 开机自启
Set-ItemProperty -Path $runKey -Name 'Muse for Windows' -Value "`"$exe`" --autostart"
Write-Host "安装完成：$exe"
