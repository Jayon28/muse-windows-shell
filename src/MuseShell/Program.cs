// Muse Windows 壳（WebView2）
// 把 muse.ai 包进一个原生 Windows 应用：托盘常驻、全局快捷键、外链走默认浏览器。
// 登录状态保存在本机独立的 WebView2 数据目录里，不碰 Chrome 的登录态。
using System.Diagnostics;
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
        Controls.Add(_webView);
        BuildTray();
        FormClosing += OnFormClosing;
        Move += (_, _) => SaveWindowBounds();
        Resize += (_, _) => SaveWindowBounds();
        Shown += async (_, _) => await InitWebViewAsync();
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

    // WebView2 初始化：数据目录独立持久（登录态就存在这里），首页 muse.ai。
    private async Task InitWebViewAsync()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuseShell", "WebView2Data");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
        await _webView.EnsureCoreWebView2Async(env);
        var core = _webView.CoreWebView2;
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
        menu.Items.Add("打开 Muse for Windows", null, (_, _) => ShowWindow());
        menu.Items.Add("设置快捷键…（当前：" + _settings.HotkeyText() + "）", null, (_, _) => ShowHotkeySettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
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
            _tray.ShowBalloonTip(5000, "Muse for Windows 快捷键注册失败",
                "快捷键 " + _settings.HotkeyText() + " 可能被其他程序占用了。" +
                "右键托盘图标选「设置快捷键…」换一个组合。", ToolTipIcon.Warning);
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
        if (WindowState != FormWindowState.Normal || _exiting) return;
        _settings.WindowX = Location.X;
        _settings.WindowY = Location.Y;
        _settings.WindowWidth = Size.Width;
        _settings.WindowHeight = Size.Height;
        _settings.Save();
    }

    // 点 × 不是退出，是缩回托盘；真正的退出只走托盘右键菜单。
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ExitApp()
    {
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
        Text = "设置全局快捷键";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(360, 150);
        var label = new Label
        {
            Text = "在下方输入框里按一遍新组合键：",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 8, 8, 0),
        };
        _box.KeyDown += OnKeyDown;
        var save = new Button { Text = "保存", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
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
