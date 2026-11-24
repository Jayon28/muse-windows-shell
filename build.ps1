# 编译并发布 Muse Windows 壳到 publish\ 目录
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\MuseShell\MuseShell.csproj'
$out = Join-Path $root 'publish'
dotnet publish $proj -c Release -o $out --nologo
Write-Host "发布完成：$out"
