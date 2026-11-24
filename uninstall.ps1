# 卸载 Muse for Windows：停进程、删自启项、快捷方式和程序目录。
# 登录数据 %LOCALAPPDATA%\Muse 默认保留，要全清手动删该目录。
$ErrorActionPreference = 'SilentlyContinue'
Get-Process Muse -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Muse for Windows'
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Muse'
$shell = New-Object -ComObject WScript.Shell
$desktop = $shell.SpecialFolders.Item('Desktop')
Remove-Item (Join-Path $desktop 'Muse for Windows.lnk') -Force
Remove-Item (Join-Path $desktop 'Muse.lnk') -Force
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Muse for Windows.lnk') -Force
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\MuseShell') -Recurse -Force
Write-Host '卸载完成（登录数据保留在 %LOCALAPPDATA%\MuseShell，需要时手动删除）'
