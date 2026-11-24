# Muse for Windows

把 [muse.ai](https://muse.ai) 包进一个原生 Windows 应用的小壳子。
Muse 官方目前只有网页版、iOS、Android 和 Mac 应用，没有 Windows 版——这个项目就是补上这一块的自制替代品。

基于 WebView2（Edge 内核）+ .NET 6 WinForms，功能刻意保持简单：一个窗口、一个托盘、一个快捷键。

## 功能

- **原生窗口**：独立的应用窗口和任务栏图标，打开就是 muse.ai，不带浏览器地址栏那一圈东西。
- **托盘常驻**：开机登录后静默自启到系统托盘；点窗口的 × 只是缩回托盘，托盘右键菜单才能真正退出。
- **全局快捷键**：默认 `Ctrl+Alt+N`，在任何软件里按一下呼出窗口、再按一下收起。可在托盘菜单「设置快捷键…」里改，改完立即生效。
- **缩放记忆**：在页面里用 Ctrl +/- 调的缩放会记下来，退出重开不丢失。
- **运行时检测**：第一次启动会弹出带进度条的检测窗，确认 .NET 与 WebView2 运行时正常；通过后不再打扰。托盘菜单里有「重新检测运行时」可随时手动重检；缺组件时检测窗会引导去微软官网下载。
- **独立登录**：登录状态保存在本机独立的 WebView2 数据目录里，第一次打开时登录一次，之后持久保持；不跟 Chrome 的登录态互相干扰。
- **外链分流**：muse.ai 站内跳转留在壳里；以新窗口打开的外站链接自动用系统默认浏览器打开。
- **记住窗口**：窗口大小和位置会被记住，下次打开还是上次的样子。
- **单实例**：重复点快捷方式不会开第二个进程，只会把已开着的窗口呼出来。

## 环境要求

- Windows 10/11
- WebView2 运行时（Windows 11 自带；没有的话装 Microsoft Edge 即可）
- 编译需要 .NET 6 SDK

## 运行前置条件

- Windows 10/11
- **.NET 6 桌面运行时**：缺了程序双击打不开（系统弹窗会引导下载），也可去微软官网下载：https://dotnet.microsoft.com/download/dotnet/6.0
- **WebView2 运行时**：Win11 自带；没有的话第一次启动的检测窗会引导，或去微软官网下载：https://developer.microsoft.com/microsoft-edge/webview2/

仓库根目录的 `Redist\` 里备有一份这两个运行时的官方安装包（体积大，不进 git）：在本仓库里运行 `install.ps1` 时若检测到缺失，会自动先用 Redist 里的装；没有 Redist 时按上面的官网地址下载即可。

## 安装

```powershell
# 在项目根目录
.\build.ps1     # 编译 + 发布
.\install.ps1   # 装到 %LOCALAPPDATA%\Programs\MuseShell，建桌面和开始菜单快捷方式，注册开机自启
```

装好后从桌面或开始菜单的 **Muse for Windows** 图标启动；第一次打开需要在壳里登录一次 muse.ai。

## 卸载

```powershell
.\uninstall.ps1   # 删程序、快捷方式和自启项；登录数据默认保留在 %LOCALAPPDATA%\MuseShell，手动删即全清
```

## 数据都放在哪

| 内容 | 位置 |
| --- | --- |
| 程序本体 | `%LOCALAPPDATA%\Programs\MuseShell\` |
| 设置（快捷键、窗口位置） | `%LOCALAPPDATA%\MuseShell\settings.json` |
| WebView2 数据（登录态、缓存） | `%LOCALAPPDATA%\MuseShell\WebView2Data\` |
| 开机自启 | 注册表 `HKCU\...\Run` 的 `Muse for Windows` 项（值为 `Muse.exe --autostart`） |

这些数据**都不进本仓库**（见 `.gitignore`），仓库里只有源码和文档。

## 项目结构

```
muse-windows-shell/
├─ README.md            本文件
├─ docs/
│  └─ SPEC.md           需求定版（做这个壳之前逐项确认过的功能清单）
├─ src/
│  └─ MuseShell/
│     ├─ MuseShell.csproj
│     ├─ Program.cs     全部源码（入口、设置、主窗体、托盘、快捷键，均带注释）
│     └─ app.ico        应用图标
├─ build.ps1            编译并发布到 publish\ 目录
├─ install.ps1          安装到本机（复制程序、建快捷方式、注册自启）
└─ uninstall.ps1        卸载
```

## 已知取舍

- 同一个标签页里直接跳转到外站的链接会留在壳内打开（保登录跳转流程不断）；只有「新窗口打开」的外链才分流到默认浏览器。
- 网页通知已放行，其余网页权限请求按 WebView2 默认处理。
