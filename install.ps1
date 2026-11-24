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
