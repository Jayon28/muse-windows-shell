// Muse Windows 壳（WebView2）
// 把 muse.ai 包进一个原生 Windows 应用：托盘常驻、全局快捷键、外链走默认浏览器。
// 登录状态保存在本机独立的 WebView2 数据目录里，不碰 Chrome 的登录态。
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MuseShell;

internal static class Program
{
    // 入口：只允许一个实例；第二个实例只唤醒第一个实例的窗口就退出。
    [STAThread]
    private static void Main(string[] args)
    {
        bool createdNew;
        using var mutex = new Mutex(true, "MuseShellSingleInstance", out createdNew);
        if (!createdNew)
        {
            EventWaitHandle.OpenExisting("MuseShellShowEvent").Set();
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool autostart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(autostart));
    }
}

// ---------- 设置 ----------

// 设置存在 %LOCALAPPDATA%\MuseShell\settings.json，只记窗口和快捷键，不记任何账号信息。
internal sealed class Settings
{
    public int HotkeyModifiers { get; set; } = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT;
    public int HotkeyKey { get; set; } = (int)Keys.N;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    // 页面缩放（WebView2 的 ZoomFactor 不会自动持久化，必须自己存，自己恢复）
    public double ZoomFactor { get; set; } = 1.0;
    // 退出时窗口是不是最大化，和位置大小分开记
    public bool WindowMaximized { get; set; } = false;
    // 运行时检测只在第一次成功启动时跑，跑过就不再打扰；托盘菜单可手动重检
    public bool RuntimeCheckDone { get; set; } = false;
    // 界面语言："system" 跟随系统（中文系统用中文、其他用英文），或手动指定 "zh" / "en"
    public string Language { get; set; } = "system";

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuseShell", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null) return s;
            }
        }
        catch { /* 设置损坏时直接用默认，绝不因此打不开 */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不打扰用户，下次仍用内存里的值 */ }
    }

    public string HotkeyText()
    {
        var parts = new List<string>();
        if ((HotkeyModifiers & Hotkey.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((HotkeyModifiers & Hotkey.MOD_ALT) != 0) parts.Add("Alt");
        if ((HotkeyModifiers & Hotkey.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((HotkeyModifiers & Hotkey.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(((Keys)HotkeyKey).ToString());
        return string.Join("+", parts);
    }
}


// ---------- 界面语言 ----------

// 全部界面文字集中在这里，按当前语言取。默认跟随系统：系统界面语言是中文就用中文，
// 其他任何语言一律用英文兜底；用户也可以在托盘菜单里手动指定，存进 Settings.Language。
internal static class Strings
{
    private static readonly Dictionary<string, string> Zh = new()
    {
        ["TrayOpen"] = "打开 Muse for Windows",
        ["TrayHotkey"] = "设置快捷键…（当前：{0}）",
        ["TrayRecheck"] = "重新检测运行时",
        ["TrayLangSystem"] = "跟随系统",
        ["TrayExit"] = "退出",
        ["BalloonFailTitle"] = "Muse for Windows 快捷键注册失败",
        ["BalloonFailBody"] = "快捷键 {0} 可能被其他程序占用了。右键托盘图标选「设置快捷键…」换一个组合。",
        ["HotkeyTitle"] = "设置全局快捷键",
        ["HotkeyPrompt"] = "在下方输入框里按一遍新组合键：",
        ["BtnSave"] = "保存",
        ["BtnCancel"] = "取消",
        ["CheckTitle"] = "Muse for Windows 运行时检测",
        ["CheckIntro"] = "正在检测运行所需的组件…",
        ["CheckDownload"] = "去微软官网下载 WebView2",
        ["CheckBusy"] = "检测中…",
        ["CheckStepDotNet"] = "检测 .NET 运行时…",
        ["CheckDotNetOk"] = "✓ .NET 运行时正常（{0}）",
        ["CheckStepWv2"] = "检测 WebView2 运行时…",
        ["CheckWv2Ok"] = "✓ WebView2 运行时正常（版本 {0}）",
        ["CheckWv2Missing1"] = "✗ 没有检测到 WebView2 运行时，页面将无法打开。",
        ["CheckWv2Missing2"] = "  请点下面的按钮去微软官网下载安装，装好后重新打开本程序。",
        ["CheckWv2Missing3"] = "  （.NET 6 桌面运行时下载：{0}）",
        ["CheckFailed"] = "检测未通过",
        ["BtnExit"] = "退出",
        ["CheckAllOk"] = "检测完成，一切正常",
        ["BtnDone"] = "完成",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["TrayOpen"] = "Open Muse for Windows",
        ["TrayHotkey"] = "Set hotkey… (current: {0})",
        ["TrayRecheck"] = "Re-check runtimes",
        ["TrayLangSystem"] = "Follow system",
        ["TrayExit"] = "Exit",
        ["BalloonFailTitle"] = "Muse for Windows: hotkey registration failed",
        ["BalloonFailBody"] = "The hotkey {0} may already be used by another app. Right-click the tray icon and choose \"Set hotkey…\" to pick a different combination.",
        ["HotkeyTitle"] = "Set global hotkey",
        ["HotkeyPrompt"] = "Press the new key combination in the box below:",
        ["BtnSave"] = "Save",
        ["BtnCancel"] = "Cancel",
        ["CheckTitle"] = "Muse for Windows runtime check",
        ["CheckIntro"] = "Checking the components this app needs…",
        ["CheckDownload"] = "Download WebView2 from Microsoft",
        ["CheckBusy"] = "Checking…",
        ["CheckStepDotNet"] = "Checking the .NET runtime…",
        ["CheckDotNetOk"] = "✓ .NET runtime OK ({0})",
        ["CheckStepWv2"] = "Checking the WebView2 runtime…",
        ["CheckWv2Ok"] = "✓ WebView2 runtime OK (version {0})",
        ["CheckWv2Missing1"] = "✗ WebView2 runtime not found — pages cannot be displayed.",
        ["CheckWv2Missing2"] = "  Use the button below to download and install it from Microsoft, then reopen this app.",
        ["CheckWv2Missing3"] = "  (.NET 6 Desktop Runtime download: {0})",
        ["CheckFailed"] = "Check failed",
        ["BtnExit"] = "Exit",
        ["CheckAllOk"] = "All checks passed",
        ["BtnDone"] = "Done",
    };

    // 当前生效的语言："zh" 或 "en"
    public static string Current { get; private set; } = "en";

    public static void Init(string setting)
    {
        Current = setting switch
        {
            "zh" => "zh",
            "en" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en",
        };
    }

    public static string Get(string key) =>
        (Current == "zh" ? Zh : En).TryGetValue(key, out var v) ? v
        : En.TryGetValue(key, out var fallback) ? fallback
        : key;

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}

// ---------- 快捷键 ----------

internal static class Hotkey
{
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

// ---------- 主窗体 ----------

internal sealed class MainForm : Form
{
    private const string HomeUrl = "https://muse.ai";
    private const int HotkeyId = 1;

    private readonly Settings _settings;
    private readonly bool _startHidden;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon _tray = new();
    private readonly EventWaitHandle _showEvent = new(false, EventResetMode.AutoReset, "MuseShellShowEvent");
    private bool _exiting;
    private bool _firstShow = true;

    public MainForm(bool autostart)
    {
        _settings = Settings.Load();
        Strings.Init(_settings.Language);
        _startHidden = autostart;
        Text = "Muse for Windows";
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
        StartPosition = FormStartPosition.Manual;
        if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
        {
            Location = new Point(_settings.WindowX, _settings.WindowY);
            Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        }
        else
        {
            Size = new Size(1200, 800);
        }
        if (_settings.WindowMaximized)
            WindowState = FormWindowState.Maximized;
        Controls.Add(_webView);
        BuildTray();
        FormClosing += OnFormClosing;
        Move += (_, _) => SaveWindowBounds();
        Resize += (_, _) => SaveWindowBounds();
        Shown += async (_, _) => await StartupAsync();
        // 第二个实例（重复点快捷方式）会发信号，让已经开着的这份把窗口呼出来。
        Task.Run(() =>
        {
            while (!_exiting)
            {
                _showEvent.WaitOne();
                BeginInvoke((Action)ShowWindow);
            }
        });
    }

    // --autostart 启动时第一次不显示窗口，只留在托盘里。
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && _firstShow)
        {
            _firstShow = false;
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterCurrentHotkey();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Hotkey.UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Hotkey.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleWindow();
            return;
        }
        base.WndProc(ref m);
    }

    // 启动流程：第一次启动先做运行时检测（有进度条的窗口），通过后才初始化 WebView2。
    // 检测没过（缺 WebView2）就退出，不留一个打不开页面的空壳在托盘里。
    private async Task StartupAsync()
    {
        if (!_settings.RuntimeCheckDone)
        {
            bool ok = await RuntimeCheckDialog.RunAsync(this);
            if (!ok)
            {
                ExitApp();
                return;
            }
            _settings.RuntimeCheckDone = true;
            _settings.Save();
        }
        await InitWebViewAsync();
    }

    // WebView2 初始化：数据目录独立持久（登录态就存在这里），首页 muse.ai。
    private async Task InitWebViewAsync()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuseShell", "WebView2Data");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
        await _webView.EnsureCoreWebView2Async(env);
        var core = _webView.CoreWebView2;
        // 恢复上次的页面缩放；用户在页面里 Ctrl +/- 改了缩放就立刻记下来，下次不丢。
        if (_settings.ZoomFactor > 0.1 && _settings.ZoomFactor < 5.0)
            _webView.ZoomFactor = _settings.ZoomFactor;
        _webView.ZoomFactorChanged += (_, _) =>
        {
            _settings.ZoomFactor = _webView.ZoomFactor;
            _settings.Save();
        };
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.Navigate(HomeUrl);
    }

    // 外站链接（新窗口打开的）用系统默认浏览器打开；muse.ai 自己的留在壳里。
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        string url = e.Uri;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("muse.ai", StringComparison.OrdinalIgnoreCase))
        {
            _webView.CoreWebView2.Navigate(url);
            ShowWindow();
        }
        else
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    // 网页通知允许，其余权限按默认处理。
    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
    }

    private void BuildTray()
    {
        _tray.Icon = Icon;
        _tray.Text = "Muse for Windows";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ToggleWindow();
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Get("TrayOpen"), null, (_, _) => ShowWindow());
        menu.Items.Add(Strings.Format("TrayHotkey", _settings.HotkeyText()), null, (_, _) => ShowHotkeySettings());
        menu.Items.Add(Strings.Get("TrayRecheck"), null, async (_, _) => await RuntimeCheckDialog.RunAsync(this));
        // 语言菜单标题双语并排，保证任何语言下都找得到这一项；语言名用各自原文。
        var langMenu = new ToolStripMenuItem("语言 / Language");
        langMenu.DropDownItems.Add(LanguageItem(Strings.Get("TrayLangSystem"), "system"));
        langMenu.DropDownItems.Add(LanguageItem("简体中文", "zh"));
        langMenu.DropDownItems.Add(LanguageItem("English", "en"));
        menu.Items.Add(langMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Get("TrayExit"), null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private ToolStripMenuItem LanguageItem(string text, string value)
    {
        var item = new ToolStripMenuItem(text) { Checked = _settings.Language == value };
        item.Click += (_, _) =>
        {
            _settings.Language = value;
            _settings.Save();
            Strings.Init(value);
            BuildTray(); // 菜单文字立刻换成新语言
        };
        return item;
    }

    private void ShowHotkeySettings()
    {
        using var dlg = new HotkeyDialog(_settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Save();
            Hotkey.UnregisterHotKey(Handle, HotkeyId);
            RegisterCurrentHotkey();
            BuildTray(); // 刷新托盘菜单里显示的当前快捷键
        }
    }

    private void RegisterCurrentHotkey()
    {
        bool ok = Hotkey.RegisterHotKey(Handle, HotkeyId, _settings.HotkeyModifiers, _settings.HotkeyKey);
        int err = ok ? 0 : Marshal.GetLastWin32Error();
        LogHotkey(ok, err);
        if (!ok)
        {
            // 注册失败多半是被别的程序占用（错误 1409），绝不能默默吞掉。
            _tray.ShowBalloonTip(5000, Strings.Get("BalloonFailTitle"),
                Strings.Format("BalloonFailBody", _settings.HotkeyText()), ToolTipIcon.Warning);
        }
    }

    // 快捷键注册结果留一行日志，方便事后排查冲突（%LOCALAPPDATA%\MuseShell\hotkey.log）。
    private void LogHotkey(bool ok, int err)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuseShell");
            Directory.CreateDirectory(dir);
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " RegisterHotKey " + _settings.HotkeyText() +
                (ok ? " => OK" : " => FAIL err=" + err) + Environment.NewLine;
            File.AppendAllText(Path.Combine(dir, "hotkey.log"), line);
        }
        catch { /* 日志写不了不影响使用 */ }
    }

    private void ToggleWindow()
    {
        if (Visible && WindowState != FormWindowState.Minimized) Hide();
        else ShowWindow();
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void SaveWindowBounds()
    {
        if (_exiting) return;
        SaveAllState();
    }

    // 退出时（关窗缩托盘、托盘真退出都算）把当前状态整份快照存盘：
    // 窗口位置大小、最大化标记直接读窗口当前值，缩放直接问 WebView 要当前值，
    // 不依赖任何变化事件有没有触发过——事件会漏，快照不会。
    private void SaveAllState()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            _settings.WindowMaximized = true;
            _settings.WindowX = RestoreBounds.X;
            _settings.WindowY = RestoreBounds.Y;
            _settings.WindowWidth = RestoreBounds.Width;
            _settings.WindowHeight = RestoreBounds.Height;
        }
        else if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowMaximized = false;
            _settings.WindowX = Location.X;
            _settings.WindowY = Location.Y;
            _settings.WindowWidth = Size.Width;
            _settings.WindowHeight = Size.Height;
        }
        // 最小化时不动窗口记录：那不是用户想要的常态样子。
        try
        {
            if (_webView.CoreWebView2 != null)
                _settings.ZoomFactor = _webView.ZoomFactor;
        }
        catch { /* WebView 还没初始化就跳过缩放 */ }
        _settings.Save();
    }

    // 点 × 不是退出，是缩回托盘；真正的退出只走托盘右键菜单。
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveAllState();
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ExitApp()
    {
        SaveAllState();
        _exiting = true;
        _tray.Visible = false;
        Close();
    }
}

// ---------- 快捷键设置对话框 ----------

// 在输入框里直接按一遍新组合键（例如 Ctrl+Shift+M），点保存立即生效。
internal sealed class HotkeyDialog : Form
{
    private readonly Settings _settings;
    private readonly TextBox _box = new() { Dock = DockStyle.Top, ReadOnly = true, Height = 30 };
    private int _modifiers;
    private int _key;

    public HotkeyDialog(Settings settings)
    {
        _settings = settings;
        _modifiers = settings.HotkeyModifiers;
        _key = settings.HotkeyKey;
        Text = Strings.Get("HotkeyTitle");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(360, 150);
        var label = new Label
        {
            Text = Strings.Get("HotkeyPrompt"),
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 8, 8, 0),
        };
        _box.KeyDown += OnKeyDown;
        var save = new Button { Text = Strings.Get("BtnSave"), DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = Strings.Get("BtnCancel"), DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        Controls.Add(_box);
        Controls.Add(label);
        Controls.Add(panel);
        RefreshBox();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        _modifiers = 0;
        if (e.Control) _modifiers |= Hotkey.MOD_CONTROL;
        if (e.Alt) _modifiers |= Hotkey.MOD_ALT;
        if (e.Shift) _modifiers |= Hotkey.MOD_SHIFT;
        if (e.KeyCode is Keys.LWin or Keys.RWin) _modifiers |= Hotkey.MOD_WIN;
        _key = (int)e.KeyCode;
        RefreshBox();
    }

    private void RefreshBox()
    {
        _box.Text = new Settings { HotkeyModifiers = _modifiers, HotkeyKey = _key }.HotkeyText();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _key != 0)
        {
            _settings.HotkeyModifiers = _modifiers;
            _settings.HotkeyKey = _key;
        }
        base.OnFormClosed(e);
    }
}

// ---------- 运行时检测窗口 ----------

// 第一次启动时检查依赖的运行时是否正常：带进度条，每项结果都列出来。
// 缺 WebView2 时给出微软官网下载入口；.NET 缺失时程序根本起不来，
// Windows 自带的 apphost 弹窗会引导去微软下载页（README 里也写了地址）。
internal sealed class RuntimeCheckDialog : Form
{
    private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/6.0";

    private readonly Label _step = new();
    private readonly ProgressBar _bar = new();
    private readonly ListBox _log = new();
    private readonly Button _downloadButton = new();
    private readonly Button _closeButton = new();
    private bool _success;

    private RuntimeCheckDialog()
    {
        Text = Strings.Get("CheckTitle");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 300);
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = Strings.Get("CheckIntro"),
            Location = new Point(16, 14),
            Size = new Size(408, 22),
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
        };
        _step.Location = new Point(16, 44);
        _step.Size = new Size(408, 20);
        _bar.Location = new Point(16, 68);
        _bar.Size = new Size(408, 16);
        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _log.Location = new Point(16, 94);
        _log.Size = new Size(408, 130);
        _downloadButton.Text = Strings.Get("CheckDownload");
        _downloadButton.Location = new Point(16, 238);
        _downloadButton.Size = new Size(200, 30);
        _downloadButton.Visible = false;
        _downloadButton.Click += (_, _) => Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true });
        _closeButton.Text = Strings.Get("CheckBusy");
        _closeButton.Location = new Point(324, 238);
        _closeButton.Size = new Size(100, 30);
        _closeButton.Enabled = false;
        _closeButton.Click += (_, _) => Close();
        Controls.Add(title);
        Controls.Add(_step);
        Controls.Add(_bar);
        Controls.Add(_log);
        Controls.Add(_downloadButton);
        Controls.Add(_closeButton);
        Shown += async (_, _) => await RunChecksAsync();
    }

    // 显示检测窗口并等它关掉；返回检测是否全部通过。
    public static Task<bool> RunAsync(IWin32Window owner)
    {
        using var dialog = new RuntimeCheckDialog();
        dialog.ShowDialog(owner);
        return Task.FromResult(dialog._success);
    }

    private async Task RunChecksAsync()
    {
        // 1) .NET 运行时：程序能跑到这里就说明它在，报一下实际版本。
        _step.Text = Strings.Get("CheckStepDotNet");
        await Task.Delay(350);
        _log.Items.Add(Strings.Format("CheckDotNetOk", RuntimeInformation.FrameworkDescription));
        _bar.Value = 40;

        // 2) WebView2 运行时：查不到版本号就是没装。
        _step.Text = Strings.Get("CheckStepWv2");
        await Task.Delay(350);
        string? version = null;
        try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch { /* 没装时这里抛异常，按缺失处理 */ }
        if (string.IsNullOrEmpty(version))
        {
            _log.Items.Add(Strings.Get("CheckWv2Missing1"));
            _log.Items.Add(Strings.Get("CheckWv2Missing2"));
            _log.Items.Add(Strings.Format("CheckWv2Missing3", DotNetDownloadUrl));
            _bar.Value = 100;
            _step.Text = Strings.Get("CheckFailed");
            _downloadButton.Visible = true;
            _closeButton.Text = Strings.Get("BtnExit");
            _closeButton.Enabled = true;
            _success = false;
            return;
        }
        _log.Items.Add(Strings.Format("CheckWv2Ok", version));
        _bar.Value = 80;

        await Task.Delay(300);
        _bar.Value = 100;
        _step.Text = Strings.Get("CheckAllOk");
        _success = true;
        _closeButton.Text = Strings.Get("BtnDone");
        _closeButton.Enabled = true;
        await Task.Delay(900);
        if (!IsDisposed) Close();
    }
}
