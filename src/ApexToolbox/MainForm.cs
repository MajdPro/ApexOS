using System.Diagnostics;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ApexToolbox;

internal sealed class MainForm : Form
{
    private sealed record DriverInventoryEntry(string Name, string Manufacturer, string DeviceClass, string Provider, string Version, string Date, string Status, string Details, string SearchText);
    private sealed record StartupEntry(string Name, string RegistryView, string Command)
    {
        public string Display => $"{Name} ({RegistryView})";
    }
    private sealed record PowerPlanEntry(string Guid, string Name, bool Active)
    {
        public string Display => $"{Name} ({Guid})";
    }

    private static readonly (string Label, string Category, string Glyph)[] Sections =
    [
        ("Home", "HOME", "\uE80F"),
        ("General Configuration", "General Configuration", "\uE713"),
        ("Windows Settings", "Windows", "\uE774"),
        ("Security", "Security", "\uE72E"),
        ("Troubleshooting", "Repair", "\uE90F"),
        ("Performance", "Performance", "\uE945"),
        ("Background Activity", "Background Activity", "\uE777"),
        ("Debloating", "Debloating", "\uE74D"),
        ("Gaming", "Gaming", "\uE7FC"),
        ("RAM Saver", "RAM Saver", "\uE950"),
        ("Power", "Power", "\uE7E8"),
        ("Drivers", "Drivers", "\uE839"),
        ("Network", "Network", "\uE774"),
        ("Interface Tweaks", "Personalization", "\uE790"),
        ("Explorer", "Explorer", "\uE8B7"),
        ("Privacy", "Privacy", "\uE72E"),
        ("Software", "Software", "\uE71D"),
        ("Storage", "Storage", "\uE7C3"),
        ("Startup", "Startup", "\uE777"),
        ("Diagnostics", "Diagnostics", "\uE9D9"),
        ("Compatibility Checker", "Compatibility", "\uE946"),
        ("Presets", "Presets", "\uE8D7"),
        ("Optimization History", "Optimization History", "\uE81C"),
        ("Advanced Configuration", "Advanced", "\uE713"),
        ("Backup & Restore", "Backup & Restore", "\uE8F1"),
        ("About", "About", "\uE946")
    ];
    private readonly string _root = AppContext.BaseDirectory;
    private readonly Panel _pageHost = new();
    private readonly Dictionary<string, FlowLayoutPanel> _pageViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Control>> _actionCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<ScriptResult>>> _statusReads = new(StringComparer.Ordinal);
    private readonly List<DriverInventoryEntry> _driverInventory = [];
    private Panel? _startupPanel;
    private ComboBox? _startupChoice;
    private Label? _startupCommandDetails;
    private readonly List<StartupEntry> _startupInventory = [];
    private Panel? _powerPlanPanel;
    private ComboBox? _powerPlanChoice;
    private readonly List<PowerPlanEntry> _powerPlanInventory = [];
    private FlowLayoutPanel _activePage = new();
    private Control? _wallpaperCard;
    private DataGridView? _driverGrid;
    private TextBox? _driverSearch;
    private ComboBox? _driverClassFilter;
    private ComboBox? _driverStatusFilter;
    private TextBox? _driverDetails;
    private bool _driverDetailsVisible;
    private FlowLayoutPanel _actions => _activePage;
    private readonly Label _heading = new();
    private readonly Label _footer = new();
    private readonly Panel _sidebar = new();
    private readonly PictureBox _brandIcon = new();
    private readonly TextBox _searchBox = new();
    private readonly ToolTip _toolTips = new();
    private readonly ContextMenuStrip _searchResults = new();
    private readonly Dictionary<string, Panel> _navigationButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Panel _notification = new();
    private readonly Label _notificationText = new();
    private readonly System.Windows.Forms.Timer _notificationTimer = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _navigationHoverTimer = new() { Interval = 16 };
    private int _notificationTicks;
    private bool _notificationAnimate;
    private Panel? _hoveredNavigationButton;
    private int _hoverOutlineAlpha;
    private bool _hoverOutlineTarget;
    private ToolboxConfiguration _config = new();
    private string _category = "HOME";
    private readonly bool _isWindows = OperatingSystem.IsWindows();
    private readonly WindowsDetails _windows = ReadWindowsDetails();
    private bool _lightTheme;
    private JsonElement? _homeSnapshot;

    private Color PageColor => _lightTheme ? Color.FromArgb(242, 245, 247) : Color.FromArgb(18, 22, 27);
    private Color SurfaceColor => _lightTheme ? Color.White : Color.FromArgb(27, 33, 40);
    private Color SidebarColor => _lightTheme ? Color.FromArgb(232, 237, 240) : Color.FromArgb(23, 28, 34);
    private Color TextColor => _lightTheme ? Color.FromArgb(31, 39, 45) : Color.FromArgb(235, 239, 242);
    private Color MutedColor => _lightTheme ? Color.FromArgb(92, 105, 114) : Color.FromArgb(155, 168, 177);
    private Color AccentColor => Color.FromArgb(76, 190, 155);

    public MainForm()
    {
        Text = "Apex Toolbox";
        Size = new Size(1240, 820);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PageColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI Variable Text", 9.5F);
        var iconPath = Path.Combine(_root, "Assets", "ApexToolboxApplication.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        _lightTheme = ReadThemePreference();
        BackColor = PageColor;
        ForeColor = TextColor;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        _sidebar.Dock = DockStyle.Fill;
        _sidebar.BackColor = SidebarColor;
        _sidebar.Padding = new Padding(14, 18, 12, 12);
        var sidebar = _sidebar;
        shell.Controls.Add(sidebar, 0, 0);
        var brand = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = SidebarColor, Padding = new Padding(0, 0, 0, 10) };
        _brandIcon.Dock = DockStyle.Left;
        _brandIcon.Size = new Size(46, 46);
        _brandIcon.SizeMode = PictureBoxSizeMode.Zoom;
        brand.Controls.Add(_brandIcon);
        brand.Controls.Add(new Label { Text = "APEX\nTOOLBOX", Dock = DockStyle.Left, Width = 124, Font = new Font("Segoe UI Semibold", 12F), ForeColor = AccentColor, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) });
        sidebar.Controls.Add(brand);
        var setupButton = new Button { Text = "Welcome setup", Dock = DockStyle.Top, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = SurfaceColor, ForeColor = TextColor };
        setupButton.FlatAppearance.BorderColor = _lightTheme ? Color.FromArgb(220, 226, 230) : Color.FromArgb(48, 57, 65);
        setupButton.FlatAppearance.BorderSize = 1;
        setupButton.Enabled = _windows.IsWindows11;
        setupButton.Click += async (_, _) => await ShowFirstRunSetupAsync();
        sidebar.Controls.Add(setupButton);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 10, 2, 0), BackColor = SidebarColor };
        sidebar.Controls.Add(nav);
        _navigationHoverTimer.Tick += (_, _) =>
        {
            _hoverOutlineAlpha = Math.Clamp(_hoverOutlineAlpha + (_hoverOutlineTarget ? 36 : -36), 0, 180);
            _hoveredNavigationButton?.Invalidate();
            if (_hoverOutlineTarget && _hoverOutlineAlpha == 180 || !_hoverOutlineTarget && _hoverOutlineAlpha == 0)
                _navigationHoverTimer.Stop();
        };
        foreach (var section in Sections)
        {
            var item = new Panel { Width = 196, Height = 34, BackColor = SidebarColor, Tag = section.Category, Margin = new Padding(0, 1, 0, 1), Cursor = Cursors.Hand, TabStop = true, AccessibleRole = AccessibleRole.PageTab, AccessibleName = section.Label };
            var glyph = new Label { Text = section.Glyph, Dock = DockStyle.Left, Width = 31, TextAlign = ContentAlignment.MiddleCenter, ForeColor = MutedColor, Font = new Font("Segoe MDL2 Assets", 12F), BackColor = Color.Transparent };
            var label = new Label { Text = section.Label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedColor, Font = new Font("Segoe UI", 9F), BackColor = Color.Transparent, AutoEllipsis = true };
            async void Navigate(object? _, EventArgs __) => await NavigateAsync(section.Category);
            item.Click += Navigate;
            glyph.Click += Navigate;
            label.Click += Navigate;
            item.KeyDown += async (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Space)
                {
                    e.Handled = true;
                    await NavigateAsync(section.Category);
                }
                else if (e.KeyCode is Keys.Up or Keys.Down)
                {
                    var index = nav.Controls.GetChildIndex(item);
                    var nextIndex = Math.Clamp(index + (e.KeyCode == Keys.Down ? 1 : -1), 0, nav.Controls.Count - 1);
                    nav.Controls[nextIndex].Focus();
                    e.Handled = true;
                }
            };
            item.Paint += (_, e) =>
            {
                if (!ReferenceEquals(_hoveredNavigationButton, item) || _hoverOutlineAlpha == 0) return;
                using var outline = new Pen(_lightTheme
                    ? Color.FromArgb(_hoverOutlineAlpha, 31, 39, 45)
                    : Color.FromArgb(_hoverOutlineAlpha, 245, 248, 250));
                e.Graphics.DrawRectangle(outline, 1, 1, item.Width - 3, item.Height - 3);
            };
            foreach (var control in new Control[] { item, glyph, label })
            {
                control.MouseEnter += (_, _) =>
                {
                    _hoveredNavigationButton?.Invalidate();
                    _hoveredNavigationButton = item;
                    _hoverOutlineTarget = true;
                    if (SystemInformation.HighContrast || !SystemInformation.IsMenuAnimationEnabled)
                    {
                        _hoverOutlineAlpha = 180;
                        item.Invalidate();
                    }
                    else _navigationHoverTimer.Start();
                };
                control.MouseLeave += (_, _) =>
                {
                    if (item.ClientRectangle.Contains(item.PointToClient(Cursor.Position))) return;
                    if (!ReferenceEquals(_hoveredNavigationButton, item)) return;
                    _hoverOutlineTarget = false;
                    if (SystemInformation.HighContrast || !SystemInformation.IsMenuAnimationEnabled)
                    {
                        _hoverOutlineAlpha = 0;
                        item.Invalidate();
                    }
                    else _navigationHoverTimer.Start();
                };
            }
            item.Controls.Add(label);
            item.Controls.Add(glyph);
            nav.Controls.Add(item);
            _navigationButtons[section.Category] = item;
        }
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(26, 22, 26, 12) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        shell.Controls.Add(body, 1, 0);
        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = PageColor };
        _heading.Dock = DockStyle.Left;
        _heading.Width = 390;
        _heading.Font = new Font("Segoe UI Variable Display", 21F, FontStyle.Bold);
        _heading.ForeColor = TextColor;
        _heading.TextAlign = ContentAlignment.MiddleLeft;
        toolbar.Controls.Add(_heading);
        var themeButton = new Button { Name = "theme-button", Text = _lightTheme ? "\uE708  Light" : "\uE708  Dark", Dock = DockStyle.Right, Width = 96, FlatStyle = FlatStyle.Flat, BackColor = SurfaceColor, ForeColor = TextColor, Font = new Font("Segoe MDL2 Assets", 9F), UseCompatibleTextRendering = true };
        themeButton.FlatAppearance.BorderSize = 0;
        themeButton.Click += (_, _) => SetTheme(!_lightTheme);
        toolbar.Controls.Add(themeButton);
        _searchBox.Dock = DockStyle.Right;
        _searchBox.Width = 260;
        _searchBox.Font = new Font("Segoe UI", 10F);
        _searchBox.BorderStyle = BorderStyle.FixedSingle;
        _searchBox.Margin = new Padding(0, 8, 10, 8);
        _searchBox.TextChanged += (_, _) => UpdateSearchResults();
        _searchBox.KeyDown += SearchBoxKeyDown;
        toolbar.Controls.Add(_searchBox);
        body.Controls.Add(toolbar, 0, 0);
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = PageColor;
        body.Controls.Add(_pageHost, 0, 1);
        _footer.Dock = DockStyle.Fill;
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.ForeColor = MutedColor;
        body.Controls.Add(_footer, 0, 2);
        _notification.Visible = false;
        _notification.Width = 360;
        _notification.Height = 0;
        _notification.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _notification.Padding = new Padding(14, 8, 8, 8);
        _notification.Controls.Add(_notificationText);
        _notificationText.Dock = DockStyle.Fill;
        _notificationText.TextAlign = ContentAlignment.MiddleLeft;
        _notificationText.AutoEllipsis = true;
        var dismissNotification = new Button { Text = "×", Dock = DockStyle.Right, Width = 28, FlatStyle = FlatStyle.Flat, TabStop = true };
        dismissNotification.FlatAppearance.BorderSize = 0;
        dismissNotification.Click += (_, _) => HideNotification();
        _notification.Controls.Add(dismissNotification);
        Controls.Add(_notification);
        Resize += (_, _) => PositionNotification();
        _notificationTimer.Tick += (_, _) =>
        {
            if (!_notificationAnimate)
            {
                HideNotification();
                return;
            }
            _notificationTicks++;
            if (_notificationTicks <= 3) _notification.Height = Math.Min(54, _notification.Height + 18);
            else if (_notificationTicks >= 75)
            {
                _notification.Height = Math.Max(0, _notification.Height - 18);
                if (_notification.Height == 0) HideNotification();
            }
        };
        _searchResults.ItemClicked += async (_, eventArgs) =>
        {
            if (eventArgs.ClickedItem?.Tag is ToolboxAction action)
            {
                _searchResults.Hide();
                _searchBox.Clear();
                await NavigateAsync(action.Category);
            }
            else if (eventArgs.ClickedItem?.Tag is ValueTuple<string, string, string> section)
            {
                _searchResults.Hide();
                _searchBox.Clear();
                await NavigateAsync(section.Item2);
            }
        };
        LoadConfig();
        UpdateBrandIcon();
        UpdateFooter();
        InitializePageStructures();
        Shown += async (_, _) =>
        {
            if (!_isWindows)
            {
                ShowNotification("Windows system actions are unavailable on this operating system.", true);
                await ShowCategoryAsync(_category);
                return;
            }
            if (!_windows.IsWindows11)
                ShowNotification($"Read-only mode: Windows {_windows.Product}, build {_windows.Build} is not supported for system changes.", true);
            else if (_windows.Build < 26100)
                ShowNotification($"System changes have not been validated on Windows build {_windows.Build}. Review each operation before applying it.", false);
            await ShowCategoryAsync(_category);
            _ = InitializeRuntimeStateAsync();
            if (_windows.IsWindows11 && !HasCompletedFirstRun()) await ShowFirstRunSetupAsync();
            if (!_windows.IsWindows11) ApplyUnsupportedReadOnlyGate();
        };
    }

    private void PositionNotification()
    {
        _notification.Location = new Point(Math.Max(12, ClientSize.Width - _notification.Width - 24), 18);
    }

    private void ShowNotification(string message, bool error)
    {
        _notificationText.Text = message;
        _notification.BackColor = error
            ? (_lightTheme ? Color.FromArgb(255, 239, 235) : Color.FromArgb(61, 39, 39))
            : (_lightTheme ? Color.FromArgb(229, 247, 239) : Color.FromArgb(34, 62, 54));
        _notificationText.ForeColor = error ? Color.FromArgb(200, 74, 60) : (_lightTheme ? Color.FromArgb(31, 39, 45) : Color.FromArgb(235, 239, 242));
        foreach (Control child in _notification.Controls)
            if (child is Button button) { button.BackColor = _notification.BackColor; button.ForeColor = _notificationText.ForeColor; }
        PositionNotification();
        _notification.Visible = true;
        _notification.BringToFront();
        _notificationTicks = 0;
        _notificationAnimate = !SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled;
        _notification.Height = _notificationAnimate ? 0 : 54;
        _notificationTimer.Interval = _notificationAnimate ? 30 : 2500;
        _notificationTimer.Stop();
        _notificationTimer.Start();
    }

    private void HideNotification()
    {
        _notificationTimer.Stop();
        _notification.Height = 0;
        _notification.Visible = false;
    }

    private static bool ReadThemePreference()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "toolbox-theme.json");
        try { return File.Exists(path) && File.ReadAllText(path).Trim().Equals("light", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void SetTheme(bool light)
    {
        _lightTheme = light;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "toolbox-theme.json");
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, light ? "light" : "dark"); }
        catch { }
        ApplyTheme(this);
        UpdateBrandIcon();
        UpdateFooter();
        foreach (var pair in _navigationButtons)
        {
            var selected = pair.Key.Equals(_category, StringComparison.OrdinalIgnoreCase);
            pair.Value.BackColor = selected ? AccentColor : SidebarColor;
            foreach (Control child in pair.Value.Controls) child.ForeColor = selected ? Color.FromArgb(17, 34, 29) : MutedColor;
        }
        var themeButton = Controls.Find("theme-button", true).FirstOrDefault() as Button;
        if (themeButton is not null) themeButton.Text = _lightTheme ? "\uE708  Light" : "\uE708  Dark";
    }

    private void ApplyTheme(Control parent)
    {
        parent.BackColor = parent == this ? PageColor : parent == _sidebar ? SidebarColor :
            parent == _actions ? PageColor : parent is Panel && parent.Tag?.ToString() == "card" ? SurfaceColor :
            parent is Panel ? (parent.Parent == _sidebar ? SidebarColor : PageColor) : parent.BackColor;
        if (parent is Label label) label.ForeColor = label.Name == "muted" ? MutedColor : TextColor;
        if (parent is Button button && button.Name != "theme-button")
        {
            button.ForeColor = TextColor;
            button.BackColor = SurfaceColor;
        }
        if (parent is TextBox textBox) { textBox.BackColor = SurfaceColor; textBox.ForeColor = TextColor; }
        foreach (Control child in parent.Controls) ApplyTheme(child);
    }

    private void UpdateBrandIcon()
    {
        var path = Path.Combine(_root, "Assets", _lightTheme ? "ApexMenuIcon.Light.png" : "ApexMenuIcon.Dark.png");
        if (!File.Exists(path)) return;
        var old = _brandIcon.Image;
        _brandIcon.Image = Image.FromFile(path);
        old?.Dispose();
    }

    private void ApplyUnsupportedReadOnlyGate()
    {
        var setupButton = _sidebar.Controls.OfType<Button>().FirstOrDefault(button => button.Text == "Welcome setup");
        if (setupButton is not null) setupButton.Enabled = false;
    }

    private async Task NavigateAsync(string category)
    {
        await ShowCategoryAsync(category);
    }

    private async Task InitializeRuntimeStateAsync()
    {
        if (!_isWindows) return;
        using var gate = new SemaphoreSlim(3, 3);
        var statusTasks = _config.Actions
            .Where(action => action.Id != "home-snapshot" && _actionCards.ContainsKey(action.Id))
            .Select(async action =>
            {
                await gate.WaitAsync();
                try
                {
                    foreach (var card in _actionCards[action.Id])
                        await RefreshStatusAsync(action, card);
                }
                finally { gate.Release(); }
            });
        var tasks = new List<Task>
        {
            Task.WhenAll(statusTasks),
            RenderHomeAsync(_pageViews["HOME"]),
            LoadDriverInventoryAsync()
        };
        if (_startupPanel is not null) tasks.Add(LoadStartupInventoryAsync());
        if (_powerPlanPanel is not null) tasks.Add(LoadPowerPlanInventoryAsync());
        var wallpaperAction = _config.Actions.FirstOrDefault(action => action.Id == "wallpaper-browser");
        if (wallpaperAction is not null && _wallpaperCard is not null)
            tasks.Add(RefreshWallpaperListAsync(wallpaperAction, _wallpaperCard));
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception exception)
        {
            var logPath = WriteLog("Startup state initialization", 1, exception.ToString(), "Diagnostics");
            ShowNotification($"Some system information could not be initialized. Details: {logPath}", true);
        }
    }

    private void UpdateSearchResults()
    {
        var query = _searchBox.Text.Trim();
        _searchResults.Items.Clear();
        if (query.Length < 2) { _searchResults.Hide(); return; }
        foreach (var action in _config.Actions
                     .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .Take(12))
        {
            var item = new ToolStripMenuItem($"{action.Title}    ·    {action.Category}") { Tag = action, ToolTipText = action.Description };
            _searchResults.Items.Add(item);
        }
        foreach (var section in Sections.Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
            _searchResults.Items.Add(new ToolStripMenuItem($"{section.Glyph}   {section.Label}    ·    Section") { Tag = section });
        if (_searchResults.Items.Count == 0) _searchResults.Items.Add(new ToolStripMenuItem("No matching Apex settings") { Enabled = false });
        _searchResults.Show(_searchBox, new Point(0, _searchBox.Height));
    }

    private void SearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { _searchBox.Clear(); _searchResults.Hide(); }
        if (e.KeyCode == Keys.Enter && _searchResults.Items.Count > 0 && _searchResults.Items[0].Enabled)
        {
            _searchResults.Items[0].PerformClick();
            e.Handled = true;
        }
    }

    private string FirstRunMarkerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "setup-completed.json");

    private bool HasCompletedFirstRun() => File.Exists(FirstRunMarkerPath);

    private void MarkFirstRunComplete()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FirstRunMarkerPath)!);
        File.WriteAllText(FirstRunMarkerPath, DateTimeOffset.Now.ToString("O"));
    }

    private async Task ShowFirstRunSetupAsync()
    {
        if (!_isWindows) return;
        using var dialog = new FirstRunForm(_root);
        var result = dialog.ShowDialog(this);
        MarkFirstRunComplete();
        if (result != DialogResult.OK) return;

        var selection = dialog.Selection;
        var tasks = new List<(ToolboxAction Action, IReadOnlyList<string> Arguments)>();
        void AddAction(string id, IReadOnlyList<string>? arguments = null)
        {
            var action = _config.Actions.FirstOrDefault(item => item.Id == id);
            if (action is not null) tasks.Add((action, arguments ?? action.ApplyArgs));
        }

        if (selection.CreateRestorePoint) AddAction("restore-point");
        var powerId = selection.PowerPlan switch
        {
            "Apex Balanced" => "power-balanced",
            "Apex Performance" => "power-performance",
            "Apex Maximum Performance" => "power-maximum",
            "Apex Ultimate Performance" => "power-ultimate",
            "Apex Power Saver" => "power-saver",
            "Apex Laptop Performance" => "power-laptop",
            "Apex Custom" => "power-custom",
            _ => null
        };
        if (powerId is not null) AddAction(powerId);
        if (selection.Theme == "Dark") AddAction("theme-dark");
        if (selection.Theme == "Light") AddAction("theme-light");
        if (selection.RamMode == "BALANCED") AddAction("ram-saver-balanced");
        if (selection.RamMode == "AGGRESSIVE") AddAction("ram-saver-aggressive");
        if (selection.RamMode == "OFF / restore saved baseline")
        {
            var ram = _config.Actions.FirstOrDefault(item => item.Id == "ram-saver-balanced");
            if (ram is not null) AddAction(ram.Id, ram.RestoreArgs);
        }
        if (selection.ExplorerMenu == "Classic Windows context menu") AddAction("explorer-context");
        if (selection.ExplorerMenu == "Windows 11 context menu") AddAction("explorer-context-windows11");
        if (selection.Wallpaper != "None")
            AddAction("wallpaper-browser", ["-Mode", "Set", "-Name", selection.Wallpaper]);
        var browserAction = selection.Browser switch
        {
            "Google Chrome" => "software-chrome",
            "Mozilla Firefox" => "software-firefox",
            "Brave" => "software-brave",
            _ => null
        };
        if (browserAction is not null) AddAction(browserAction);
        var gamingAppAction = selection.GamingApp switch
        {
            "Steam" => "software-steam",
            "Epic Games Launcher" => "software-epic",
            "Minecraft Launcher" => "software-minecraft",
            _ => null
        };
        if (gamingAppAction is not null) AddAction(gamingAppAction);
        if (selection.RecordingApp == "OBS Studio") AddAction("software-obs");
        var utilityAction = selection.UtilityApp switch
        {
            "NanaZip" => "software-nanazip",
            "7-Zip" => "software-7zip",
            _ => null
        };
        if (utilityAction is not null) AddAction(utilityAction);
        if (selection.EnableGaming) AddAction("gaming-mode");
        if (selection.SetChromeDefault && selection.Browser == "Google Chrome") AddAction("chrome-default");
        if (selection.ConfigureNanaZip) AddAction("nanazip-configure");

        if (tasks.Count == 0)
        {
            ShowNotification("No setup changes were selected. You can reopen Welcome setup from the sidebar.", false);
            return;
        }

        var completed = 0;
        foreach (var task in tasks)
        {
            _footer.Text = $"Applying {task.Action.Title} ({completed + 1} of {tasks.Count})...";
            var outcome = await ScriptRunner.RunAsync(ResolveScript(task.Action.Script), task.Arguments, task.Action.RequiresAdmin);
            var detail = $"STDOUT:\n{outcome.StandardOutput}\nSTDERR:\n{outcome.StandardError}";
            var logPath = WriteLog($"First run | {task.Action.Title} | {task.Action.Script}", outcome.ExitCode, detail);
            if (outcome.ExitCode != 0)
            {
                UpdateFooter();
                ShowNotification($"Setup stopped at {task.Action.Title}. Details: {logPath}", true);
                return;
            }
            completed++;
            await Task.Yield();
        }
        UpdateFooter();
        ShowNotification($"Completed {completed} selected setup actions.", false);
    }

    private void LoadConfig()
    {
        var path = Path.Combine(_root, "Toolbox", "config", "toolbox.json");
        try
        {
            _config = JsonSerializer.Deserialize<ToolboxConfiguration>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ToolboxConfiguration();
        }
        catch (Exception exception)
        {
            ShowNotification($"Toolbox configuration could not be loaded: {exception.Message}", true);
        }
    }

    private void InitializePageStructures()
    {
        foreach (var category in Sections.Select(section => section.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var page = new FlowLayoutPanel
            {
                Name = $"page-{category.Replace(' ', '-')}",
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = PageColor,
                Visible = false
            };
            _pageViews.Add(category, page);
            _pageHost.Controls.Add(page);
            page.SizeChanged += (_, _) =>
            {
                var contentWidth = Math.Max(480, page.ClientSize.Width - 36);
                foreach (Control child in page.Controls)
                    if (child.Width > 300) child.Width = contentWidth;
            };
            _category = category;
            _activePage = page;
            BuildCategoryStructure(category);
        }
        _category = "HOME";
        _activePage = _pageViews[_category];
        _activePage.Visible = true;
        _heading.Text = "Home";
        UpdateNavigationSelection(_category);
    }

    private void BuildCategoryStructure(string category)
    {
        var section = Sections.FirstOrDefault(item => item.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (!_isWindows)
        {
            _actions.Controls.Add(new Label { Text = "Windows-only functionality unavailable on this operating system.", AutoSize = true, ForeColor = Color.FromArgb(240, 179, 120), Margin = new Padding(0, 12, 0, 0) });
            return;
        }
        if (category.Equals("HOME", StringComparison.OrdinalIgnoreCase))
        {
            BuildHomeStructure();
            return;
        }
        if (category.Equals("About", StringComparison.OrdinalIgnoreCase))
        {
            RenderAbout();
            return;
        }
        if (category.Equals("Presets", StringComparison.OrdinalIgnoreCase))
        {
            _actions.Controls.Add(CreatePresetsPage());
            return;
        }
        if (category.Equals("Optimization History", StringComparison.OrdinalIgnoreCase))
        {
            _actions.Controls.Add(CreateOptimizationHistoryPage());
            return;
        }
        var actions = GetActionsForCategory(category);
        if (category == "Personalization")
        {
            var wallpaperAction = actions.FirstOrDefault(action => action.Id == "wallpaper-browser");
            if (wallpaperAction is not null)
            {
                _wallpaperCard = CreateWallpaperBrowser(wallpaperAction);
                _actions.Controls.Add(_wallpaperCard);
                actions.Remove(wallpaperAction);
            }
        }
        if (category is "Windows" or "General Configuration")
            _actions.Controls.Add(CreateWindowsSettingsShortcuts());
        if (category == "Security")
            _actions.Controls.Add(CreateSecurityShortcuts());
        if (category == "Drivers")
            _actions.Controls.Add(CreateDriverInventoryPanel());
        if (category == "Startup")
        {
            _startupPanel = CreateStartupManagerPanel();
            _actions.Controls.Add(_startupPanel);
        }
        if (category == "Power")
        {
            _powerPlanPanel = CreatePowerPlanPickerPanel();
            _actions.Controls.Add(_powerPlanPanel);
        }
        if (actions.Count == 0)
        {
            if (_actions.Controls.Count > 0) return;
            var page = new Panel { Width = Math.Max(480, _actions.ClientSize.Width - 36), Height = 132, BackColor = SurfaceColor, Padding = new Padding(18), Tag = "card" };
            page.Controls.Add(new Label { Text = "Coming Soon", Dock = DockStyle.Top, Height = 32, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 14F) });
            page.Controls.Add(new Label { Text = $"{section.Label ?? category} does not have a supported Apex action available yet. No controls are shown until functionality can be implemented and verified.", Dock = DockStyle.Fill, ForeColor = MutedColor, MaximumSize = new Size(Math.Max(400, _actions.ClientSize.Width - 72), 0) });
            _actions.Controls.Add(page);
            return;
        }
        foreach (var action in actions.Where(action => (category != "Drivers" || action.Id != "driver-list") && (category != "Startup" || action.Id != "startup-disable-entry") && (category != "Power" || action.Id != "power-select-installed")))
            _actions.Controls.Add(CreateCard(action));
    }

    private List<ToolboxAction> GetActionsForCategory(string category)
    {
        var configured = _config.Actions.Where(action => action.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        var ids = category switch
        {
            "General Configuration" => new HashSet<string>(["windows-update-settings", "storage-settings", "background-settings", "search-index-settings", "indexing-options", "optional-apps-list", "store-status", "edge-browser-status", "webview2-status", "windows-compatibility"], StringComparer.OrdinalIgnoreCase),
            "Background Activity" => new HashSet<string>(["startup-inventory", "startup-settings", "background-settings", "search-index-settings"], StringComparer.OrdinalIgnoreCase),
            "Startup" => new HashSet<string>(["startup-settings", "startup-disable-entry"], StringComparer.OrdinalIgnoreCase),
            "Storage" => new HashSet<string>(["storage-settings"], StringComparer.OrdinalIgnoreCase),
            "Debloating" => new HashSet<string>(["optional-apps-list", "remove-clipchamp", "remove-bing-news", "remove-gethelp", "remove-tips", "remove-solitaire", "remove-feedback-hub", "remove-maps", "remove-movies-tv", "remove-people", "remove-teams-personal", "remove-cortana", "remove-mail-calendar", "remove-copilot-app", "store-status", "store-remove"], StringComparer.OrdinalIgnoreCase),
            "Compatibility" => new HashSet<string>(["windows-compatibility", "edge-browser-status", "webview2-status", "gaming-services-diagnose", "xbox-signin-diagnose", "winre-diagnose"], StringComparer.OrdinalIgnoreCase),
            _ => null!
        };
        if (ids is not null) return _config.Actions.Where(action => ids.Contains(action.Id)).ToList();
        return configured.ToList();
    }

    private Panel CreateStartupManagerPanel()
    {
        var card = new Panel { Name = "startup-manager", Width = Math.Max(480, _actions.ClientSize.Width - 36), Height = 196, BackColor = SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12), Tag = "card" };
        card.Controls.Add(new Label { Text = "Current-user startup entries", Dock = DockStyle.Top, Height = 28, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 13F) });
        card.Controls.Add(new Label { Text = "Only HKCU Run registry values are managed. Services, scheduled tasks, other accounts, and Startup-folder items are not changed.", Dock = DockStyle.Top, Height = 42, ForeColor = MutedColor });
        _startupChoice = new ComboBox { Name = "startup-entry-choice", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(StartupEntry.Display), BackColor = PageColor, ForeColor = TextColor };
        _startupChoice.SelectedIndexChanged += (_, _) => UpdateStartupSelection();
        var selectionRow = new Panel { Dock = DockStyle.Top, Height = 34 };
        selectionRow.Controls.Add(_startupChoice);
        var refresh = ButtonFor("Refresh", false);
        refresh.Width = 82;
        refresh.Dock = DockStyle.Right;
        refresh.Click += async (_, _) => await LoadStartupInventoryAsync();
        selectionRow.Controls.Add(refresh);
        card.Controls.Add(selectionRow);
        _startupCommandDetails = new Label { Name = "startup-command-details", Text = "Command: inventory loading...", Dock = DockStyle.Top, Height = 32, ForeColor = MutedColor, AutoEllipsis = true };
        card.Controls.Add(_startupCommandDetails);
        var statusRow = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        statusRow.Controls.Add(new Label { Name = "status", Text = "Status: loading startup entries...", Dock = DockStyle.Left, Width = 190, ForeColor = MutedColor, TextAlign = ContentAlignment.MiddleLeft });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 284, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var restore = ButtonFor("Restore saved", false);
        restore.Name = "restore-button";
        restore.Width = 118;
        restore.Click += async (_, _) => await RunStartupRestoreAsync(card);
        var disable = ButtonFor("Disable selected", true);
        disable.Name = "action-button";
        disable.Width = 132;
        disable.Click += async (_, _) => await RunStartupDisableAsync(card);
        buttons.Controls.AddRange([restore, disable]);
        statusRow.Controls.Add(buttons);
        card.Controls.Add(statusRow);
        _toolTips.SetToolTip(refresh, "Rescan current-user startup Run registry entries.");
        return card;
    }

    private async Task LoadStartupInventoryAsync()
    {
        if (_startupChoice is null) return;
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "startup-inventory");
        if (action is null) return;
        var status = _startupPanel?.Controls.Find("status", true).FirstOrDefault() as Label;
        try
        {
            var previous = _startupChoice.SelectedItem as StartupEntry;
            var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), ["-Mode", "StartupList"], false);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
            using var document = JsonDocument.Parse(result.StandardOutput);
            _startupInventory.Clear();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                _startupInventory.Add(new StartupEntry(
                    GetJsonString(entry, "Name", "Unknown"),
                    GetJsonString(entry, "RegistryView", "Registry64"),
                    GetJsonString(entry, "Command", "")));
            }
            _startupChoice.BeginUpdate();
            _startupChoice.Items.Clear();
            foreach (var entry in _startupInventory) _startupChoice.Items.Add(entry);
            var restoreIndex = previous is null ? -1 : _startupInventory.FindIndex(entry => entry.Name == previous.Name && entry.RegistryView == previous.RegistryView);
            _startupChoice.SelectedIndex = restoreIndex >= 0 ? restoreIndex : _startupInventory.Count > 0 ? 0 : -1;
            _startupChoice.EndUpdate();
            if (status is not null) status.Text = $"Status: {_startupInventory.Count} current-user entries";
            UpdateStartupSelection();
        }
        catch (Exception exception)
        {
            var logPath = WriteLog("Startup Inventory", 1, exception.ToString(), "Startup");
            if (status is not null) { status.Text = "Status: inventory failed"; status.ForeColor = Color.FromArgb(226, 124, 104); }
            ShowNotification($"Startup inventory failed: {exception.Message} Details: {logPath}", true);
        }
    }

    private void UpdateStartupSelection()
    {
        if (_startupCommandDetails is null) return;
        _startupCommandDetails.Text = _startupChoice?.SelectedItem is StartupEntry entry
            ? $"Command: {entry.Command}"
            : "No current-user Run entries were found.";
    }

    private async Task RunStartupDisableAsync(Control card)
    {
        if (_startupChoice?.SelectedItem is not StartupEntry entry)
        {
            ShowNotification("Select a current-user startup entry first.", false);
            return;
        }
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "startup-disable-entry");
        if (action is null) { ShowNotification("Startup management is unavailable in this Toolbox build.", true); return; }
        await RunActionAsync(action, ["-Mode", "DisableStartup", "-Name", entry.Name, "-RegistryView", entry.RegistryView], card);
    }

    private async Task RunStartupRestoreAsync(Control card)
    {
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "startup-disable-entry");
        if (action is null) { ShowNotification("Startup management is unavailable in this Toolbox build.", true); return; }
        await RunActionAsync(action, action.RestoreArgs, card);
    }

    private Panel CreatePowerPlanPickerPanel()
    {
        var card = new Panel { Name = "installed-power-plans", Width = Math.Max(480, _actions.ClientSize.Width - 36), Height = 148, BackColor = SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12), Tag = "card" };
        card.Controls.Add(new Label { Text = "Installed Windows power plans", Dock = DockStyle.Top, Height = 28, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 13F) });
        card.Controls.Add(new Label { Text = "Plans and GUIDs are enumerated from this Windows installation. Selecting a plan does not create or rename a scheme.", Dock = DockStyle.Top, Height = 38, ForeColor = MutedColor });
        var selectionRow = new Panel { Dock = DockStyle.Top, Height = 34 };
        _powerPlanChoice = new ComboBox { Name = "installed-power-plan-choice", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(PowerPlanEntry.Display), BackColor = PageColor, ForeColor = TextColor };
        selectionRow.Controls.Add(_powerPlanChoice);
        var refresh = ButtonFor("Refresh", false);
        refresh.Width = 82;
        refresh.Dock = DockStyle.Right;
        refresh.Click += async (_, _) => await LoadPowerPlanInventoryAsync();
        selectionRow.Controls.Add(refresh);
        card.Controls.Add(selectionRow);
        var statusRow = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        statusRow.Controls.Add(new Label { Name = "status", Text = "Status: loading installed plans...", Dock = DockStyle.Left, Width = 220, ForeColor = MutedColor, TextAlign = ContentAlignment.MiddleLeft });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var restore = ButtonFor("Restore previous", false);
        restore.Name = "restore-button";
        restore.Width = 126;
        restore.Click += async (_, _) => await RunInstalledPowerPlanRestoreAsync(card);
        var select = ButtonFor("Use selected plan", true);
        select.Name = "action-button";
        select.Width = 142;
        select.Click += async (_, _) => await RunInstalledPowerPlanSelectAsync(card);
        buttons.Controls.AddRange([restore, select]);
        statusRow.Controls.Add(buttons);
        card.Controls.Add(statusRow);
        _toolTips.SetToolTip(refresh, "Rescan the installed power schemes and their current active state.");
        return card;
    }

    private async Task LoadPowerPlanInventoryAsync()
    {
        if (_powerPlanChoice is null) return;
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "power-select-installed");
        if (action is null) return;
        var status = _powerPlanPanel?.Controls.Find("status", true).FirstOrDefault() as Label;
        try
        {
            var previous = _powerPlanChoice.SelectedItem as PowerPlanEntry;
            var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), ["-Mode", "List"], false);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
            using var document = JsonDocument.Parse(result.StandardOutput);
            _powerPlanInventory.Clear();
            foreach (var plan in document.RootElement.EnumerateArray())
            {
                _powerPlanInventory.Add(new PowerPlanEntry(
                    GetJsonString(plan, "Guid", ""),
                    GetJsonString(plan, "Name", "Unknown plan"),
                    plan.TryGetProperty("Active", out var active) && active.GetBoolean()));
            }
            _powerPlanChoice.BeginUpdate();
            _powerPlanChoice.Items.Clear();
            foreach (var plan in _powerPlanInventory) _powerPlanChoice.Items.Add(plan);
            var selectedIndex = previous is null ? -1 : _powerPlanInventory.FindIndex(plan => plan.Guid == previous.Guid);
            if (selectedIndex < 0) selectedIndex = _powerPlanInventory.FindIndex(plan => plan.Active);
            _powerPlanChoice.SelectedIndex = selectedIndex;
            _powerPlanChoice.EndUpdate();
            var activePlan = _powerPlanInventory.FirstOrDefault(plan => plan.Active);
            if (status is not null) status.Text = activePlan is null ? "Status: active plan unavailable" : $"Active: {activePlan.Name}";
        }
        catch (Exception exception)
        {
            var logPath = WriteLog("Installed Power Plan Inventory", 1, exception.ToString(), "Power");
            if (status is not null) { status.Text = "Status: plan scan failed"; status.ForeColor = Color.FromArgb(226, 124, 104); }
            ShowNotification($"Installed power plan scan failed: {exception.Message} Details: {logPath}", true);
        }
    }

    private async Task RunInstalledPowerPlanSelectAsync(Control card)
    {
        if (_powerPlanChoice?.SelectedItem is not PowerPlanEntry plan)
        {
            ShowNotification("Select an installed Windows power plan first.", false);
            return;
        }
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "power-select-installed");
        if (action is null) { ShowNotification("Installed power plan selection is unavailable in this Toolbox build.", true); return; }
        await RunActionAsync(action, ["-Mode", "SelectInstalled", "-Guid", plan.Guid], card);
    }

    private async Task RunInstalledPowerPlanRestoreAsync(Control card)
    {
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "power-select-installed");
        if (action is null) { ShowNotification("Installed power plan restore is unavailable in this Toolbox build.", true); return; }
        await RunActionAsync(action, action.RestoreArgs, card);
    }

    private Task ShowCategoryAsync(string category)
    {
        if (!_pageViews.TryGetValue(category, out var page))
        {
            ShowNotification($"The {category} page is unavailable in this Toolbox build.", true);
            return Task.CompletedTask;
        }
        if (_pageViews.TryGetValue(_category, out var currentPage)) currentPage.Visible = false;
        _category = category;
        _activePage = page;
        _heading.Text = Sections.FirstOrDefault(section => section.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).Label ?? category;
        page.Visible = true;
        page.BringToFront();
        UpdateNavigationSelection(category);
        return Task.CompletedTask;
    }

    private void UpdateNavigationSelection(string category)
    {
        foreach (var pair in _navigationButtons)
        {
            var selected = pair.Key.Equals(category, StringComparison.OrdinalIgnoreCase);
            pair.Value.BackColor = selected ? AccentColor : SidebarColor;
            foreach (Control child in pair.Value.Controls) child.ForeColor = selected ? Color.FromArgb(17, 34, 29) : MutedColor;
        }
    }

    private Control CreatePresetsPage()
    {
        var page = new Panel { Width = Math.Max(480, _actions.ClientSize.Width - 36), Height = 350, BackColor = SurfaceColor, Padding = new Padding(18), Tag = "card" };
        page.Controls.Add(new Label { Text = "Select a configuration bundle", Dock = DockStyle.Top, Height = 34, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 14F) });
        page.Controls.Add(new Label { Text = "Each checked item runs its existing Apex script. Settings remain individually restorable from their own pages.", Dock = DockStyle.Top, Height = 40, ForeColor = MutedColor });
        var choices = new FlowLayoutPanel { Name = "preset-choices", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SurfaceColor, Padding = new Padding(0, 6, 0, 0) };
        AddPresetChoice(choices, "visual-effects", "Reduce visual effects", "Favor performance for this user; restore the saved Windows defaults later.");
        AddPresetChoice(choices, "power-balanced", "Apex Balanced power plan", "Create from an available Windows plan if needed, verify it, then activate it.");
        AddPresetChoice(choices, "gaming-mode", "Apex Gaming Mode", "Enable Game Mode and disable Game DVR capture for this user; saved values are restorable.");
        AddPresetChoice(choices, "ram-saver-balanced", "RAM Saver Balanced", "Return supported Store apps to the normal Windows background policy.");
        var apply = ButtonFor("Apply selected", true);
        apply.Width = 140;
        apply.Dock = DockStyle.Bottom;
        apply.Click += async (_, _) => await ApplySelectedPresetAsync(choices);
        page.Controls.Add(apply);
        return page;
    }

    private void AddPresetChoice(FlowLayoutPanel choices, string actionId, string title, string description)
    {
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == actionId);
        var row = new Panel { Width = Math.Max(440, _actions.ClientSize.Width - 84), Height = 58, BackColor = PageColor, Margin = new Padding(0, 0, 0, 6), Padding = new Padding(8), Tag = actionId };
        var check = new CheckBox { Name = "preset-selected", Text = title, Checked = action is not null, AutoSize = true, ForeColor = TextColor, Dock = DockStyle.Top, Enabled = action is not null };
        row.Controls.Add(new Label { Text = action is null ? $"{description} (unavailable)" : description, Dock = DockStyle.Fill, ForeColor = MutedColor, AutoEllipsis = true });
        row.Controls.Add(check);
        _toolTips.SetToolTip(check, description);
        choices.Controls.Add(row);
    }

    private async Task ApplySelectedPresetAsync(FlowLayoutPanel choices)
    {
        var selectedIds = choices.Controls.OfType<Panel>()
            .Where(row => row.Controls.Find("preset-selected", false).FirstOrDefault() is CheckBox check && check.Checked)
            .Select(row => row.Tag?.ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        if (selectedIds.Length == 0)
        {
            ShowNotification("Select at least one Apex action for this preset.", false);
            return;
        }
        foreach (var id in selectedIds)
        {
            var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == id);
            if (action is null || !_actionCards.TryGetValue(id!, out var cards) || cards.Count == 0)
            {
                ShowNotification($"Preset action '{id}' is unavailable; no remaining preset actions were run.", true);
                return;
            }
            var card = cards[0];
            await RunActionAsync(action, action.ApplyArgs, card);
            if (card.Controls.Find("status", true).FirstOrDefault() is Label status && status.Text == "Status: failed")
                return;
        }
        ShowNotification("Selected Apex preset actions completed.", false);
    }

    private Control CreateOptimizationHistoryPage()
    {
        var page = new TableLayoutPanel
        {
            Name = "optimization-history-page",
            Width = Math.Max(480, _actions.ClientSize.Width - 36),
            Height = 520,
            BackColor = SurfaceColor,
            Padding = new Padding(16),
            RowCount = 3,
            ColumnCount = 1,
            Tag = "card"
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(new Label { Text = "Apex Operation History", Dock = DockStyle.Fill, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 14F) }, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = SurfaceColor };
        var refresh = ButtonFor("Refresh history", true);
        refresh.Width = 124;
        refresh.Click += (_, _) => RefreshOptimizationHistory(page);
        var openLogs = ButtonFor("Open logs folder", false);
        openLogs.Width = 132;
        openLogs.Click += (_, _) => OpenApexLogsFolder();
        toolbar.Controls.AddRange([refresh, openLogs]);
        page.Controls.Add(toolbar, 0, 1);
        var logView = new TextBox { Name = "history-log-view", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = PageColor, ForeColor = MutedColor, Font = new Font("Consolas", 9F) };
        page.Controls.Add(logView, 0, 2);
        RefreshOptimizationHistory(page);
        return page;
    }

    private void RefreshOptimizationHistory(Control page)
    {
        var view = page.Controls.Find("history-log-view", true).FirstOrDefault() as TextBox;
        if (view is null) return;
        try
        {
            var directories = new[]
            {
                Path.Combine(_root, "Logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "Logs")
            };
            var entries = directories.Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(path => File.ReadLines(path).Select(line => $"{Path.GetFileName(path)} | {line}"))
                .TakeLast(500)
                .ToArray();
            view.Text = entries.Length == 0 ? "No Apex Toolbox operation history is available yet." : string.Join(Environment.NewLine, entries);
            view.SelectionStart = view.TextLength;
            view.ScrollToCaret();
        }
        catch (Exception exception)
        {
            view.Text = $"Apex history could not be read: {exception.Message}";
        }
    }

    private void OpenApexLogsFolder()
    {
        var primary = Path.Combine(_root, "Logs");
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "Logs");
        var directory = Directory.Exists(primary) ? primary : fallback;
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowNotification($"Could not open the Apex logs folder: {exception.Message}", true);
        }
    }

    private Control CreateWindowsSettingsShortcuts()
    {
        var card = new Panel
        {
            Width = Math.Max(480, _actions.ClientSize.Width - 34),
            Height = 180,
            BackColor = SurfaceColor,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12),
            Tag = "card"
        };
        card.Controls.Add(new Label { Text = "Windows Settings", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12F), ForeColor = TextColor });
        card.Controls.Add(new Label { Text = "Open supported Windows settings pages. Availability depends on the installed Windows edition and build.", Dock = DockStyle.Top, Height = 34, ForeColor = MutedColor });
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = SurfaceColor, Padding = new Padding(0, 4, 0, 0) };
        (string Label, string Uri)[] settings =
        [
            ("Windows Update", "ms-settings:windowsupdate"),
            ("Activation", "ms-settings:activation"),
            ("Windows Security", "ms-settings:windowsdefender"),
            ("Display", "ms-settings:display"),
            ("Sound", "ms-settings:sound"),
            ("Network", "ms-settings:network-status"),
            ("Bluetooth", "ms-settings:bluetooth"),
            ("Personalization", "ms-settings:personalization"),
            ("Accounts", "ms-settings:yourinfo"),
            ("Privacy", "ms-settings:privacy"),
            ("Storage", "ms-settings:storagesense"),
            ("Power", "ms-settings:powersleep"),
            ("Apps", "ms-settings:appsfeatures"),
            ("Gaming", "ms-settings:gaming-gamemode"),
            ("Accessibility", "ms-settings:easeofaccess"),
            ("Recovery", "ms-settings:recovery"),
            ("System Information", "ms-settings:about")
        ];
        foreach (var setting in settings)
        {
            var button = ButtonFor(setting.Label, false);
            button.Width = 142;
            button.AccessibleName = $"Open {setting.Label} settings";
            _toolTips.SetToolTip(button, setting.Uri);
            button.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(setting.Uri) { UseShellExecute = true });
                    ShowNotification($"Opened {setting.Label} settings.", false);
                }
                catch (Exception exception)
                {
                    ShowNotification($"Could not open {setting.Label} settings: {exception.Message}", true);
                }
            };
            links.Controls.Add(button);
        }
        card.Controls.Add(links);
        return card;
    }

    private Control CreateSecurityShortcuts()
    {
        var card = new Panel { Width = Math.Max(480, _actions.ClientSize.Width - 34), Height = 94, BackColor = SurfaceColor, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12), Tag = "card" };
        card.Controls.Add(new Label { Text = "Windows protection", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI Semibold", 12F), ForeColor = TextColor });
        var security = ButtonFor("Windows Security", false);
        security.Width = 142;
        security.Dock = DockStyle.Right;
        security.Click += (_, _) => OpenSettingsUri("ms-settings:windowsdefender", "Windows Security");
        var firewall = ButtonFor("Firewall", false);
        firewall.Width = 100;
        firewall.Dock = DockStyle.Right;
        firewall.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "firewall.cpl") { UseShellExecute = true }); }
            catch (Exception exception) { ShowNotification($"Could not open Windows Firewall: {exception.Message}", true); }
        };
        card.Controls.Add(firewall);
        card.Controls.Add(security);
        return card;
    }

    private void OpenSettingsUri(string uri, string title)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            ShowNotification($"Opened {title}.", false);
        }
        catch (Exception exception)
        {
            ShowNotification($"Could not open {title}: {exception.Message}", true);
        }
    }

    private Control CreateDriverInventoryPanel()
    {
        var card = new TableLayoutPanel
        {
            Name = "driver-inventory-panel",
            Width = Math.Max(620, _actions.ClientSize.Width - 36),
            Height = 520,
            BackColor = SurfaceColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 14),
            RowCount = 4,
            ColumnCount = 1,
            Tag = "card"
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        card.Controls.Add(new Label { Text = "Installed Driver Inventory", Dock = DockStyle.Fill, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 13F) }, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = SurfaceColor, Padding = new Padding(0, 4, 0, 0) };
        _driverSearch = new TextBox { Width = 190, Height = 28, PlaceholderText = "Search drivers" };
        _driverClassFilter = new ComboBox { Width = 124, DropDownStyle = ComboBoxStyle.DropDownList };
        _driverClassFilter.Items.Add("All classes");
        _driverClassFilter.SelectedIndex = 0;
        _driverStatusFilter = new ComboBox { Width = 124, DropDownStyle = ComboBoxStyle.DropDownList };
        _driverStatusFilter.Items.AddRange(["All statuses", "Working", "Needs attention", "Unknown"]);
        _driverStatusFilter.SelectedIndex = 0;
        var sort = new ComboBox { Width = 132, DropDownStyle = ComboBoxStyle.DropDownList };
        sort.Items.AddRange(["Sort: Name", "Manufacturer", "Device class", "Provider", "Version", "Status"]);
        sort.SelectedIndex = 0;
        var refresh = ButtonFor("Refresh", true);
        refresh.Width = 82;
        refresh.Click += async (_, _) => await LoadDriverInventoryAsync();
        var copy = ButtonFor("Copy", false);
        copy.Width = 70;
        copy.Click += (_, _) => CopySelectedDriver();
        var detailsToggle = ButtonFor("Details", false);
        detailsToggle.Width = 78;
        toolbar.Controls.AddRange([_driverSearch, _driverClassFilter, _driverStatusFilter, sort, refresh, copy, detailsToggle]);
        card.Controls.Add(toolbar, 0, 1);

        _driverGrid = new DataGridView
        {
            Name = "driver-grid",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SurfaceColor,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = SidebarColor, ForeColor = TextColor },
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = SurfaceColor, ForeColor = TextColor, SelectionBackColor = AccentColor, SelectionForeColor = Color.FromArgb(17, 34, 29) }
        };
        _driverGrid.Columns.Add("Name", "Device");
        _driverGrid.Columns.Add("Manufacturer", "Manufacturer");
        _driverGrid.Columns.Add("Class", "Class");
        _driverGrid.Columns.Add("Provider", "Driver provider");
        _driverGrid.Columns.Add("Version", "Version");
        _driverGrid.Columns.Add("Date", "Driver date");
        _driverGrid.Columns.Add("Status", "Status");
        _driverGrid.Columns[0].FillWeight = 155;
        _driverGrid.Columns[1].FillWeight = 95;
        _driverGrid.Columns[2].FillWeight = 75;
        _driverGrid.Columns[3].FillWeight = 100;
        _driverGrid.Columns[4].FillWeight = 78;
        _driverGrid.Columns[5].FillWeight = 86;
        _driverGrid.Columns[6].FillWeight = 90;
        _driverGrid.SelectionChanged += (_, _) => UpdateSelectedDriverDetails();
        card.Controls.Add(_driverGrid, 0, 2);

        _driverDetails = new TextBox { Name = "driver-details", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = PageColor, ForeColor = MutedColor, Font = new Font("Consolas", 8.5F) };
        _driverDetails.Visible = false;
        card.Controls.Add(_driverDetails, 0, 3);
        card.RowStyles[3].Height = 0;
        detailsToggle.Click += (_, _) =>
        {
            _driverDetailsVisible = !_driverDetailsVisible;
            _driverDetails.Visible = _driverDetailsVisible;
            card.RowStyles[3].Height = _driverDetailsVisible ? 92 : 0;
            card.PerformLayout();
            detailsToggle.Text = _driverDetailsVisible ? "Hide details" : "Details";
        };
        _driverSearch.TextChanged += (_, _) => PopulateDriverGrid(sort.SelectedIndex);
        _driverClassFilter.SelectedIndexChanged += (_, _) => PopulateDriverGrid(sort.SelectedIndex);
        _driverStatusFilter.SelectedIndexChanged += (_, _) => PopulateDriverGrid(sort.SelectedIndex);
        sort.SelectedIndexChanged += (_, _) => PopulateDriverGrid(sort.SelectedIndex);
        PopulateDriverClassFilter();
        _toolTips.SetToolTip(_driverSearch, "Search device name, manufacturer, class, provider, version, and hardware IDs.");
        _toolTips.SetToolTip(sort, "Choose how the current filtered inventory is sorted.");
        return card;
    }

    private async Task LoadDriverInventoryAsync(ScriptResult? existingResult = null, string? existingLogPath = null)
    {
        if (!_isWindows || _driverGrid is null) return;
        var action = _config.Actions.FirstOrDefault(candidate => candidate.Id == "driver-list");
        if (action is null)
        {
            ShowNotification("Driver inventory is not configured in this Toolbox build.", true);
            return;
        }
        try
        {
            ShowNotification("Refreshing driver inventory...", false);
            var result = existingResult ?? await ScriptRunner.RunAsync(ResolveScript(action.Script), action.ApplyArgs, action.RequiresAdmin);
            var logPath = existingLogPath ?? WriteLog("Driver Inventory", result.ExitCode, $"STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
            if (result.ExitCode != 0)
            {
                var reason = result.StandardError.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Windows did not return driver inventory.";
                ShowNotification($"Driver inventory failed: {reason} Details: {logPath}", true);
                return;
            }
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var entries = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];
            _driverInventory.Clear();
            foreach (var entry in entries)
            {
                var name = GetJsonString(entry, "Name", GetJsonString(entry, "Device", "Unknown device"));
                var manufacturer = GetJsonString(entry, "Manufacturer", "Unknown");
                var deviceClass = GetJsonString(entry, "Class", "Unknown");
                var provider = GetJsonString(entry, "Provider", "Unknown");
                var version = GetJsonString(entry, "Version", "Unknown");
                var date = FormatDriverDate(GetJsonString(entry, "Date", "Unknown"));
                var status = GetJsonString(entry, "Status", "Unknown");
                var details = $"Device: {name}\r\nManufacturer: {manufacturer}\r\nClass: {deviceClass}\r\nProvider: {provider}\r\nVersion: {version}\r\nDriver date: {date}\r\nStatus: {status}\r\nHardware IDs: {GetJsonString(entry, "HardwareIds", "Unavailable")}\r\nDevice instance: {GetJsonString(entry, "InstanceId", "Unavailable")}\r\nINF: {GetJsonString(entry, "InfName", "Unavailable")}\r\nService: {GetJsonString(entry, "Service", "Unavailable")}\r\nSigned: {GetJsonString(entry, "IsSigned", "Unknown")}";
                _driverInventory.Add(new DriverInventoryEntry(name, manufacturer, deviceClass, provider, version, date, status, details, $"{name} {manufacturer} {deviceClass} {provider} {version} {status} {details}"));
            }
            PopulateDriverClassFilter();
            PopulateDriverGrid();
            ShowNotification($"Loaded {_driverInventory.Count} installed driver records.", false);
        }
        catch (Exception exception)
        {
            var logPath = WriteLog("Driver Inventory", 1, exception.ToString());
            ShowNotification($"Driver inventory could not be displayed: {exception.Message} Details: {logPath}", true);
        }
    }

    private void PopulateDriverClassFilter()
    {
        if (_driverClassFilter is null) return;
        var selected = _driverClassFilter.SelectedItem?.ToString() ?? "All classes";
        _driverClassFilter.BeginUpdate();
        _driverClassFilter.Items.Clear();
        _driverClassFilter.Items.Add("All classes");
        _driverClassFilter.Items.AddRange(_driverInventory.Select(entry => entry.DeviceClass).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        _driverClassFilter.SelectedItem = _driverClassFilter.Items.Contains(selected) ? selected : "All classes";
        _driverClassFilter.EndUpdate();
    }

    private void PopulateDriverGrid(int sortIndex = 0)
    {
        if (_driverGrid is null) return;
        var query = _driverSearch?.Text.Trim() ?? "";
        var classFilter = _driverClassFilter?.SelectedItem?.ToString() ?? "All classes";
        var statusFilter = _driverStatusFilter?.SelectedItem?.ToString() ?? "All statuses";
        var rows = _driverInventory.Where(entry =>
            (query.Length == 0 || entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (classFilter == "All classes" || entry.DeviceClass.Equals(classFilter, StringComparison.OrdinalIgnoreCase)) &&
            (statusFilter == "All statuses" ||
             statusFilter == "Needs attention" && entry.Status.StartsWith("Needs attention", StringComparison.OrdinalIgnoreCase) ||
             entry.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)));
        rows = sortIndex switch
        {
            1 => rows.OrderBy(entry => entry.Manufacturer, StringComparer.OrdinalIgnoreCase),
            2 => rows.OrderBy(entry => entry.DeviceClass, StringComparer.OrdinalIgnoreCase),
            3 => rows.OrderBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase),
            4 => rows.OrderBy(entry => entry.Version, StringComparer.OrdinalIgnoreCase),
            5 => rows.OrderBy(entry => entry.Status, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        };
        _driverGrid.Rows.Clear();
        foreach (var entry in rows)
        {
            var index = _driverGrid.Rows.Add(entry.Name, entry.Manufacturer, entry.DeviceClass, entry.Provider, entry.Version, entry.Date, entry.Status);
            _driverGrid.Rows[index].Tag = entry;
        }
        if (_driverGrid.Rows.Count == 0 && _driverInventory.Count == 0)
            _driverDetails!.Text = "Driver inventory has not been loaded. Use Refresh to query Windows device and signed-driver information.";
        UpdateSelectedDriverDetails();
    }

    private void UpdateSelectedDriverDetails()
    {
        if (_driverDetails is null || _driverGrid?.CurrentRow?.Tag is not DriverInventoryEntry entry) return;
        _driverDetails.Text = entry.Details;
    }

    private void CopySelectedDriver()
    {
        if (_driverGrid?.CurrentRow?.Tag is not DriverInventoryEntry entry)
        {
            ShowNotification("Select a driver row before copying its information.", false);
            return;
        }
        try
        {
            Clipboard.SetText(entry.Details);
            ShowNotification($"Copied {entry.Name} driver information.", false);
        }
        catch (Exception exception)
        {
            ShowNotification($"Could not copy driver information: {exception.Message}", true);
        }
    }

    private static string FormatDriverDate(string value)
    {
        return DateTime.TryParse(value, out var date) ? date.ToString("yyyy-MM-dd") : value;
    }

    private void BuildHomeStructure()
    {
        var intro = new Panel { Width = Math.Max(620, _actions.ClientSize.Width - 36), Height = 106, BackColor = PageColor, Margin = new Padding(0, 0, 0, 12) };
        intro.Controls.Add(new Label { Text = "Apex OS", Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI Variable Display", 25F, FontStyle.Bold), ForeColor = TextColor });
        intro.Controls.Add(new Label { Text = "Apex Playbook v0.3.0", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 11F), ForeColor = MutedColor });
        var refresh = ButtonFor("Refresh snapshot", true);
        refresh.Width = 142;
        refresh.Dock = DockStyle.Right;
        refresh.Click += async (_, _) => await RenderHomeAsync(_pageViews["HOME"]);
        intro.Controls.Add(refresh);
        _actions.Controls.Add(intro);

        var snapshotResults = new FlowLayoutPanel
        {
            Name = "home-snapshot-results",
            Width = Math.Max(620, _actions.ClientSize.Width - 36),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = PageColor,
            Margin = Padding.Empty
        };
        snapshotResults.Controls.Add(CreateNotice("System information will load shortly.", false));
        _actions.Controls.Add(snapshotResults);

        var quickTitle = new Label { Text = "Quick actions", Width = Math.Max(620, _actions.ClientSize.Width - 36), Height = 30, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 13F) };
        _actions.Controls.Add(quickTitle);
        var quick = new FlowLayoutPanel { Width = Math.Max(620, _actions.ClientSize.Width - 36), Height = 46, WrapContents = true, BackColor = PageColor };
        AddQuickAction(quick, "Performance", "power-performance");
        AddQuickAction(quick, "Gaming Mode", "gaming-mode");
        AddQuickAction(quick, "RAM Saver", "ram-saver-balanced");
        AddQuickAction(quick, "Network Repair", "network-repair");
        AddQuickAction(quick, "Windows Update", "windows-update-settings");
        AddQuickAction(quick, "Storage Cleanup", "storage-settings");
        _actions.Controls.Add(quick);
    }

    private async Task RenderHomeAsync(FlowLayoutPanel homePage)
    {
        var snapshotResults = homePage.Controls.Find("home-snapshot-results", false).FirstOrDefault() as FlowLayoutPanel;
        if (snapshotResults is null) return;
        snapshotResults.Controls.Clear();
        snapshotResults.Controls.Add(CreateNotice("Reading current system state...", false, snapshotResults.Width));
        var action = _config.Actions.FirstOrDefault(item => item.Id == "home-snapshot");
        if (action is null)
        {
            snapshotResults.Controls.Clear();
            snapshotResults.Controls.Add(CreateNotice("Home snapshot is not configured.", true, snapshotResults.Width));
            return;
        }
        var outcome = await ScriptRunner.RunAsync(ResolveScript(action.Script), action.ApplyArgs, false);
        var logPath = WriteLog($"Home snapshot | {action.Script}", outcome.ExitCode, $"STDOUT:\n{outcome.StandardOutput}\nSTDERR:\n{outcome.StandardError}");
        snapshotResults.Controls.Clear();
        if (outcome.ExitCode != 0)
        {
            snapshotResults.Controls.Add(CreateNotice($"Apex could not read the system snapshot. Details: {logPath}", true, snapshotResults.Width));
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(outcome.StandardOutput);
            _homeSnapshot = document.RootElement.Clone();
            RenderSnapshotCards(document.RootElement, snapshotResults);
        }
        catch (JsonException exception)
        {
            var parseLog = WriteLog("Home snapshot JSON", 1, exception.ToString());
            snapshotResults.Controls.Add(CreateNotice($"System snapshot was incomplete or invalid. Details: {parseLog}", true, snapshotResults.Width));
        }
    }

    private void RenderSnapshotCards(JsonElement snapshot, FlowLayoutPanel target)
    {
        var system = snapshot.GetProperty("Windows");
        var memory = snapshot.GetProperty("Memory");
        var storage = snapshot.GetProperty("Storage");
        var cpu = snapshot.GetProperty("Cpu");
        var gpu = snapshot.GetProperty("Gpu");
        var power = snapshot.GetProperty("Power");
        var status = snapshot.GetProperty("Status");
        var deviceType = snapshot.GetProperty("DeviceType").GetString() ?? "Unknown device";
        var systemSummary = new Label
        {
            Text = $"{deviceType}    ·    {system.GetProperty("Product").GetString()} {system.GetProperty("DisplayVersion").GetString()}  (build {system.GetProperty("Build").GetInt32()}, {system.GetProperty("Architecture").GetString()})\n{cpu.GetProperty("Name").GetString()}    ·    {gpu.GetProperty("Name").GetString()}    ·    {FormatBytes(memory.GetProperty("TotalBytes").GetInt64())} RAM",
            Width = Math.Max(620, target.ClientSize.Width - 20),
            Height = 58,
            ForeColor = MutedColor,
            Margin = new Padding(0, 0, 0, 10)
        };
        target.Controls.Add(systemSummary);

        var metrics = new FlowLayoutPanel { Width = Math.Max(620, target.ClientSize.Width - 20), Height = 112, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = PageColor, Margin = new Padding(0, 0, 0, 12) };
        AddMetric(metrics, "CPU", cpu.GetProperty("UsagePercent").ValueKind == JsonValueKind.Null ? "Unavailable" : $"{cpu.GetProperty("UsagePercent").GetInt32()}%", cpu.GetProperty("Name").GetString() ?? "Processor");
        AddMetric(metrics, "Memory", $"{memory.GetProperty("UsagePercent").GetDouble():0}% used", $"{FormatBytes(memory.GetProperty("AvailableBytes").GetInt64())} available · {memory.GetProperty("Pressure").GetString()} pressure");
        var gpuUsage = gpu.GetProperty("UsagePercent");
        AddMetric(metrics, "Graphics", gpuUsage.ValueKind == JsonValueKind.Null ? "Usage unavailable" : $"{gpuUsage.GetDouble():0}%", gpu.GetProperty("Name").GetString() ?? "Not detected");
        AddMetric(metrics, "Storage", $"{FormatBytes(storage.GetProperty("FreeBytes").GetInt64())} free", $"{storage.GetProperty("Drive").GetString()} · {FormatBytes(storage.GetProperty("TotalBytes").GetInt64())} total");
        target.Controls.Add(metrics);

        var planName = power.GetProperty("ActivePlan").GetString() ?? "Unknown";
        var startupCount = snapshot.GetProperty("StartupCount").GetInt32();
        var statusGrid = new FlowLayoutPanel { Width = Math.Max(620, target.ClientSize.Width - 20), Height = 142, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = PageColor, Margin = new Padding(0, 0, 0, 12) };
        AddStatus(statusGrid, "Optimization profile", "Individual settings", true);
        AddStatus(statusGrid, "Active power plan", planName, true);
        AddStatus(statusGrid, "RAM Saver", status.GetProperty("RamSaver").GetString() ?? "Unknown", true);
        AddStatus(statusGrid, "Gaming Mode", status.GetProperty("GameMode").GetBoolean() ? "On" : "Off", true);
        AddStatus(statusGrid, "Windows Update", status.GetProperty("WindowsUpdate").GetString() ?? "Unknown", true);
        AddStatus(statusGrid, "Windows Security", status.GetProperty("WindowsSecurity").GetString() ?? "Unknown", !string.Equals(status.GetProperty("WindowsSecurity").GetString(), "Real-time protection off", StringComparison.OrdinalIgnoreCase));
        var networkState = status.GetProperty("Network").GetString() ?? "Unknown";
        AddStatus(statusGrid, "Network", networkState, networkState.StartsWith("Internet reachable", StringComparison.OrdinalIgnoreCase));
        AddStatus(statusGrid, "Startup apps", startupCount.ToString(), true);
        AddStatus(statusGrid, "Restart Required", status.GetProperty("RestartRequired").GetBoolean() ? "Yes" : "No", !status.GetProperty("RestartRequired").GetBoolean());
        AddStatus(statusGrid, "Device errors", status.GetProperty("DeviceErrors").GetInt32().ToString(), status.GetProperty("DeviceErrors").GetInt32() == 0);
        target.Controls.Add(statusGrid);
    }

    private void AddMetric(FlowLayoutPanel row, string title, string value, string detail)
    {
        var panel = new Panel { Width = Math.Max(150, (row.ClientSize.Width - 30) / 4), Height = 98, BackColor = SurfaceColor, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(13), Tag = "card" };
        panel.Controls.Add(new Label { Text = detail, Dock = DockStyle.Bottom, Height = 33, ForeColor = MutedColor, Font = new Font("Segoe UI", 8.5F) });
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Top, Height = 30, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 14F) });
        panel.Controls.Add(new Label { Text = title.ToUpperInvariant(), Dock = DockStyle.Top, Height = 19, ForeColor = AccentColor, Font = new Font("Segoe UI Semibold", 8F) });
        row.Controls.Add(panel);
    }

    private void AddStatus(FlowLayoutPanel row, string title, string value, bool healthy)
    {
        var panel = new Panel { Width = Math.Max(180, (row.ClientSize.Width - 18) / 3), Height = 56, BackColor = SurfaceColor, Margin = new Padding(0, 0, 8, 7), Padding = new Padding(10, 7, 8, 5), Tag = "card" };
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Right, Width = 104, ForeColor = healthy ? AccentColor : Color.FromArgb(226, 124, 104), TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true });
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = MutedColor, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
        row.Controls.Add(panel);
    }

    private void AddQuickAction(FlowLayoutPanel row, string label, string actionId)
    {
        var button = ButtonFor(label, false);
        button.Width = label.Length > 13 ? 136 : 118;
        var action = _config.Actions.FirstOrDefault(item => item.Id == actionId);
        button.Enabled = action is not null && (_windows.IsWindows11 || action.ReadOnly);
        button.Click += async (_, _) =>
        {
            if (action is not null) await RunActionAsync(action, action.ApplyArgs, _actions);
        };
        row.Controls.Add(button);
    }

    private Control CreateNotice(string message, bool error, int? width = null)
    {
        return new Label { Text = message, Width = width ?? Math.Max(620, _actions.ClientSize.Width - 36), Height = 58, BackColor = SurfaceColor, ForeColor = error ? Color.FromArgb(226, 124, 104) : MutedColor, Padding = new Padding(16), Margin = new Padding(0, 10, 0, 12), Tag = "card" };
    }

    private void RenderAbout()
    {
        var panel = new Panel { Width = Math.Max(620, _actions.ClientSize.Width - 36), Height = 180, BackColor = SurfaceColor, Padding = new Padding(22), Tag = "card" };
        panel.Controls.Add(new Label { Text = "Apex OS", Dock = DockStyle.Top, Height = 44, Font = new Font("Segoe UI Variable Display", 24F, FontStyle.Bold), ForeColor = TextColor });
        panel.Controls.Add(new Label { Text = $"Apex Playbook v0.3.0\n{_windows.Product} · build {_windows.Build} · {RuntimeInformation.OSArchitecture}\nAME Wizard deployment · Apex Toolbox and reversible Windows configuration tools", Dock = DockStyle.Fill, ForeColor = MutedColor });
        _actions.Controls.Add(panel);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private Control CreateWallpaperBrowser(ToolboxAction action)
        {
            var card = new Panel { Width = Math.Max(480, _actions.ClientSize.Width - 34), Height = 388, BackColor = SurfaceColor, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16), Tag = "card" };
            card.Controls.Add(new Label { Text = action.Title, Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 12) });
            card.Controls.Add(new Label { Text = $"{action.Description} Supported formats: JPG, JPEG, PNG, BMP, and WebP where Windows supports it.", Dock = DockStyle.Top, Height = 44, ForeColor = MutedColor });

            var wallpaperChoice = new ComboBox { Name = "wallpaper-choice", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(25, 30, 36), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32 };
            wallpaperChoice.SelectedIndexChanged += async (_, _) => await RefreshWallpaperStatusAsync(action, card);
            card.Controls.Add(wallpaperChoice);

            var status = new Label { Name = "wallpaper-status", Text = "Status: scanning wallpaper folder...", Dock = DockStyle.Top, Height = 30, ForeColor = Color.FromArgb(116, 219, 186), TextAlign = ContentAlignment.MiddleLeft };
            card.Controls.Add(status);
            var actionDetails = new TextBox { Name = "action-details", Dock = DockStyle.Bottom, Height = 70, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false, BorderStyle = BorderStyle.FixedSingle, BackColor = PageColor, ForeColor = MutedColor, Font = new Font("Consolas", 8F) };
            var detailsToggle = new LinkLabel { Text = "Technical details", Dock = DockStyle.Bottom, Height = 18, LinkColor = AccentColor, Visible = false };
            detailsToggle.Click += (_, _) =>
            {
                var expanded = !actionDetails.Visible;
                actionDetails.Visible = expanded;
                detailsToggle.Text = expanded ? "Hide technical details" : "Technical details";
                AnimateControlHeight(card, expanded ? 476 : 388);
            };
            card.Controls.Add(actionDetails);
            card.Controls.Add(detailsToggle);
            var previewDetails = new Panel { Dock = DockStyle.Top, Height = 140, BackColor = PageColor, Padding = new Padding(4), Margin = new Padding(0, 4, 0, 4) };
            var preview = new PictureBox { Name = "wallpaper-preview", Dock = DockStyle.Left, Width = 224, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SurfaceColor };
            var fileDetails = new Label { Name = "wallpaper-file-details", Text = "Select an installed wallpaper to preview it.", Dock = DockStyle.Fill, ForeColor = MutedColor, Padding = new Padding(14, 8, 4, 4) };
            previewDetails.Controls.Add(fileDetails);
            previewDetails.Controls.Add(preview);
            card.Controls.Add(previewDetails);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 74, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            var refresh = ButtonFor("Refresh Wallpapers", false);
            refresh.Width = 150;
            refresh.Enabled = _windows.IsWindows11;
            refresh.Click += async (_, _) => await RefreshWallpaperListAsync(action, card);
            buttons.Controls.Add(refresh);

            var apply = ButtonFor("Set desktop", true);
            apply.Width = 110;
            apply.Enabled = _windows.IsWindows11;
            apply.Click += async (_, _) => await RunSelectedWallpaperAsync(action, card, "Set");
            buttons.Controls.Add(apply);

            var setDefault = ButtonFor("Apex default", false);
            setDefault.Width = 110;
            setDefault.Enabled = _windows.IsWindows11;
            setDefault.Click += async (_, _) => await RunWallpaperAsync(action, card, "Set", "Apex-Default-Dark.jpg");
            buttons.Controls.Add(setDefault);

            var lockScreen = ButtonFor("Set selected lock screen", false);
            lockScreen.Width = 158;
            lockScreen.Enabled = _windows.IsWindows11;
            lockScreen.Click += async (_, _) => await RunSelectedWallpaperAsync(action, card, "SetLockScreen");
            buttons.Controls.Add(lockScreen);

            var apexLockScreen = ButtonFor("Apex default lock screen", false);
            apexLockScreen.Width = 162;
            apexLockScreen.Enabled = _windows.IsWindows11;
            apexLockScreen.Click += async (_, _) => await RunWallpaperAsync(action, card, "SetLockScreen", "Apex-LockScreen-Dark.jpg");
            buttons.Controls.Add(apexLockScreen);

            var restore = ButtonFor("Restore previous", false);
            restore.Width = 130;
            restore.Enabled = _windows.IsWindows11;
            restore.Click += async (_, _) => await RunActionAsync(action, ["-Mode", "Restore"], card);
            buttons.Controls.Add(restore);
            card.Controls.Add(buttons);
            detailsToggle.Visible = true;
            return card;
        }

        private async Task RefreshWallpaperListAsync(ToolboxAction action, Control card)
        {
            var choice = card.Controls.Find("wallpaper-choice", true).FirstOrDefault() as ComboBox;
            var status = card.Controls.Find("wallpaper-status", true).FirstOrDefault() as Label;
            if (choice is null || status is null) return;
            try
            {
                var selected = choice.SelectedItem as string;
                var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), ["-Mode", "List"], false);
                if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError.Trim());
                var wallpapers = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                choice.BeginUpdate();
                choice.Items.Clear();
                choice.Items.AddRange(wallpapers);
                var index = Array.FindIndex(wallpapers, name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));
                if (index < 0) index = Array.FindIndex(wallpapers, name => string.Equals(name, "Apex-Default-Dark.jpg", StringComparison.OrdinalIgnoreCase));
                choice.SelectedIndex = index >= 0 ? index : wallpapers.Length > 0 ? 0 : -1;
                choice.EndUpdate();
                if (wallpapers.Length == 0) status.Text = "No supported image files found in the Wallpapers folder.";
                else await RefreshWallpaperStatusAsync(action, card);
            }
            catch (Exception exception)
            {
                status.Text = "Status: wallpaper scan failed.";
                status.ForeColor = Color.FromArgb(240, 147, 126);
                var logPath = WriteLog("Wallpaper library refresh", 1, exception.ToString());
                ShowNotification($"Could not scan the Apex Wallpapers folder. Details: {logPath}", true);
            }
        }

        private async Task RefreshWallpaperStatusAsync(ToolboxAction action, Control card)
        {
            var choice = card.Controls.Find("wallpaper-choice", true).FirstOrDefault() as ComboBox;
            var status = card.Controls.Find("wallpaper-status", true).FirstOrDefault() as Label;
            if (choice?.SelectedItem is not string name || status is null) return;
            var preview = card.Controls.Find("wallpaper-preview", true).FirstOrDefault() as PictureBox;
            var fileDetails = card.Controls.Find("wallpaper-file-details", true).FirstOrDefault() as Label;
            if (preview is not null && fileDetails is not null)
            {
                var wallpaperPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ApexDesktop", "Wallpapers", name);
                try
                {
                    using var image = Image.FromFile(wallpaperPath);
                    var replacement = new Bitmap(image);
                    var previous = preview.Image;
                    preview.Image = replacement;
                    previous?.Dispose();
                    fileDetails.Text = $"Filename: {name}\nType: {Path.GetExtension(name).TrimStart('.').ToUpperInvariant()}\nResolution: {image.Width} × {image.Height}";
                }
                catch
                {
                    var previous = preview.Image;
                    preview.Image = null;
                    previous?.Dispose();
                    fileDetails.Text = $"Filename: {name}\nType: {Path.GetExtension(name).TrimStart('.').ToUpperInvariant()}\nResolution: Preview unavailable";
                }
            }
            try
            {
                var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), ["-Mode", "Status", "-Name", name], false);
                var state = result.StandardOutput.Trim();
                status.Text = result.ExitCode == 0 ? $"Status: {state}" : $"Status: unavailable — {result.StandardError.Trim()}";
                status.ForeColor = result.ExitCode == 0 ? Color.FromArgb(116, 219, 186) : Color.FromArgb(240, 147, 126);
            }
            catch (Exception exception)
            {
                status.Text = $"Status: unavailable — {exception.Message}";
                status.ForeColor = Color.FromArgb(240, 147, 126);
            }
        }

        private async Task RunSelectedWallpaperAsync(ToolboxAction action, Control card, string mode)
        {
            var choice = card.Controls.Find("wallpaper-choice", true).FirstOrDefault() as ComboBox;
            if (choice?.SelectedItem is not string name)
            {
                ShowNotification("Select an installed wallpaper before applying it.", false);
                return;
            }
            await RunWallpaperAsync(action, card, mode, name);
        }

        private async Task RunWallpaperAsync(ToolboxAction action, Control card, string mode, string name)
        {
            await RunActionAsync(action, ["-Mode", mode, "-Name", name], card);
        }

        private Control CreateCard(ToolboxAction action)
    {
            var card = new Panel { Width = Math.Max(480, _actions.ClientSize.Width - 34), Height = 168, BackColor = SurfaceColor, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16), Tag = action.Id };
            var riskTier = GetRiskTier(action);
            var titleRow = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = SurfaceColor };
            titleRow.Controls.Add(new Label { Text = action.Title, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 12), ForeColor = TextColor, AutoEllipsis = true });
            var riskLabel = new Label { Text = riskTier, Dock = DockStyle.Right, Width = 96, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 8.5F), ForeColor = riskTier == "SAFE" ? AccentColor : riskTier == "EXPERIMENTAL" ? Color.FromArgb(226, 124, 104) : Color.FromArgb(226, 160, 94) };
            titleRow.Controls.Add(riskLabel);
            _toolTips.SetToolTip(riskLabel, riskTier switch
            {
                "EXPERIMENTAL" => "EXPERIMENTAL: requires a separate confirmation before every apply operation and may have uncertain system effects.",
                "ADVANCED" => "ADVANCED: review the action description and recovery implications before applying.",
                _ => "SAFE: designed to preserve normal Windows functionality. Review the action description before applying."
            });
            card.Controls.Add(titleRow);
        card.Controls.Add(new Label { Text = action.Description, Dock = DockStyle.Top, Height = 48, ForeColor = MutedColor });
        var details = new TextBox { Name = "action-details", Dock = DockStyle.Bottom, Height = 92, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false, BorderStyle = BorderStyle.FixedSingle, BackColor = PageColor, ForeColor = MutedColor, Font = new Font("Consolas", 8.5F) };
        card.Controls.Add(details);
        var lower = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        lower.Controls.Add(new Label { Name = "status", Text = "Status: loading...", Dock = DockStyle.Left, Width = 145, ForeColor = MutedColor, TextAlign = ContentAlignment.MiddleLeft });
        var detailsToggle = new LinkLabel { Name = "details-toggle", Text = "Technical details", Dock = DockStyle.Left, Width = 96, LinkColor = AccentColor, ActiveLinkColor = AccentColor, TextAlign = ContentAlignment.MiddleLeft };
        detailsToggle.Click += (_, _) =>
        {
            var expanded = !details.Visible;
            details.Visible = expanded;
            detailsToggle.Text = expanded ? "Hide details" : "Technical details";
            AnimateControlHeight(card, expanded ? 260 : 168);
        };
        lower.Controls.Add(detailsToggle);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var progress = new ProgressBar { Name = "progress", Width = 84, Height = 20, Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 25, Margin = new Padding(6, 5, 0, 0) };
        buttons.Controls.Add(progress);
        var apply = ButtonFor(string.IsNullOrWhiteSpace(action.ApplyText) ? "Apply" : action.ApplyText, true);
        apply.Name = "action-button";
        apply.Enabled = _windows.IsWindows11 || action.ReadOnly;
        apply.Click += async (_, _) => await RunActionAsync(action, action.ApplyArgs, card);
        buttons.Controls.Add(apply);
        if (action.RestoreArgs.Count > 0)
        {
            var restore = ButtonFor(string.IsNullOrWhiteSpace(action.RestoreText) ? "Restore" : action.RestoreText, false);
            restore.Name = "restore-button";
                        restore.Enabled = _windows.IsWindows11;
            restore.Click += async (_, _) => await RunActionAsync(action, action.RestoreArgs, card);
            buttons.Controls.Add(restore);
        }
        lower.Controls.Add(buttons);
        card.Controls.Add(lower);
        if (!_actionCards.TryGetValue(action.Id, out var cards))
            _actionCards[action.Id] = cards = [];
        cards.Add(card);
        return card;
    }

    private static Button ButtonFor(string text, bool primary)
    {
        var button = new Button { Text = text, Width = 88, Height = 31, FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(116, 219, 186) : Color.FromArgb(49, 59, 66), ForeColor = primary ? Color.FromArgb(18, 30, 27) : Color.White, Margin = new Padding(8, 0, 0, 0) };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void AnimateControlHeight(Control control, int targetHeight)
    {
        if (SystemInformation.HighContrast || !SystemInformation.IsMenuAnimationEnabled)
        {
            control.Height = targetHeight;
            return;
        }
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            var difference = targetHeight - control.Height;
            if (Math.Abs(difference) <= 12)
            {
                control.Height = targetHeight;
                timer.Stop();
                timer.Dispose();
            }
            else
                control.Height += Math.Sign(difference) * Math.Max(4, Math.Abs(difference) / 3);
        };
        timer.Start();
    }

    private async Task RefreshStatusAsync(ToolboxAction action, Control card, bool forceRefresh = false)
    {
        var label = card.Controls.Find("status", true).FirstOrDefault() as Label;
        if (label is null) return;
        try
        {
            var cacheKey = JsonSerializer.Serialize(new[] { action.Script }.Concat(action.StatusArgs));
            if (forceRefresh) _statusReads.TryRemove(cacheKey, out _);
            var resultTask = _statusReads.GetOrAdd(cacheKey, _ => new Lazy<Task<ScriptResult>>(
                () => ScriptRunner.RunAsync(ResolveScript(action.Script), action.StatusArgs, false),
                LazyThreadSafetyMode.ExecutionAndPublication));
            var result = await resultTask.Value;
            var state = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "Unknown";
            if (result.ExitCode == 0 && action.Category == "Power")
            {
                try
                {
                    using var json = JsonDocument.Parse(result.StandardOutput);
                    var root = json.RootElement;
                    if (root.TryGetProperty("Profile", out var profile))
                    {
                        var available = root.GetProperty("Available").GetBoolean();
                        var active = root.GetProperty("Active").GetBoolean();
                        state = available ? $"{(active ? "Active" : "Available")}: {profile.GetString()}" : $"Power plan unavailable: {profile.GetString()}";
                    }
                    else if (root.TryGetProperty("CurrentPlan", out var current))
                    {
                        var currentName = current.GetString() ?? "Unknown";
                        state = $"Active: {currentName}";
                    }
                }
                catch (JsonException) { }
            }
            if (result.ExitCode == 0 && action.Id == "security-status")
            {
                try
                {
                    using var json = JsonDocument.Parse(result.StandardOutput);
                    var root = json.RootElement;
                    var defender = root.GetProperty("Defender");
                    var firewall = root.GetProperty("Firewall");
                    var defenderState = defender.GetProperty("Available").GetBoolean()
                        ? defender.GetProperty("RealTimeProtection").GetBoolean() ? "Defender on" : "Defender off"
                        : "Defender status unavailable";
                    var profiles = firewall.GetProperty("Profiles").EnumerateArray().ToArray();
                    var firewallOn = profiles.Length > 0 && profiles.All(profile => profile.GetProperty("Enabled").GetBoolean());
                    var firewallState = firewall.GetProperty("Available").GetBoolean()
                        ? firewallOn ? "Firewall on" : "Firewall profile off"
                        : "Firewall status unavailable";
                    state = $"{defenderState}; {firewallState}";
                }
                catch (JsonException) { state = "Security status unavailable"; }
            }
            label.Text = result.ExitCode == 0 ? $"Status: {state}" : $"Status: unavailable (exit {result.ExitCode})";
            label.ForeColor = result.ExitCode != 0 || state.StartsWith("Power plan unavailable", StringComparison.OrdinalIgnoreCase) ||
                state.Contains("Defender off", StringComparison.OrdinalIgnoreCase) || state.Contains("Firewall profile off", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(240, 147, 126)
                : Color.FromArgb(116, 219, 186);
        }
        catch { label.Text = "Status: unavailable"; label.ForeColor = Color.FromArgb(240, 147, 126); }
    }

    private async Task RunActionAsync(ToolboxAction action, IReadOnlyList<string> arguments, Control card)
    {
        var owningPage = FindOwningPage(card) ?? _activePage;
        if (!_windows.IsWindows11 && !action.ReadOnly)
        {
            ShowNotification("This action is unavailable on the current Windows version.", true);
            return;
        }
        var isApply = MatchesActionMode(arguments, action.ApplyArgs);
        var isRestore = MatchesActionMode(arguments, action.RestoreArgs);
        if ((action.RequiresConfirmation || GetRiskTier(action) == "EXPERIMENTAL") && isApply)
        {
            var tierWarning = GetRiskTier(action) == "EXPERIMENTAL"
                ? "\n\nEXPERIMENTAL: this operation can have uncertain system effects. Apply only after reviewing a backup and recovery plan."
                : "";
            var choice = MessageBox.Show(this, $"{action.Description}{tierWarning}\n\nContinue?", action.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes) return;
        }
        if (action.OfferRestorePoint && isApply)
        {
            var choice = MessageBox.Show(this, "Create a Windows restore point before this change? Choose No to continue without one, or Cancel to stop.", action.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (choice == DialogResult.Cancel) return;
            if (choice == DialogResult.Yes)
            {
                var restoreAction = _config.Actions.FirstOrDefault(item => item.Id == "restore-point");
                if (restoreAction is null) { ShowNotification("Restore-point action is not configured; no change was made.", true); return; }
                card.Enabled = false;
                SetActionProgress(card, true);
                var restoreResult = await ScriptRunner.RunAsync(ResolveScript(restoreAction.Script), restoreAction.ApplyArgs, restoreAction.RequiresAdmin);
                var restoreLog = WriteLog($"Pre-change restore point | {restoreAction.Script}", restoreResult.ExitCode, restoreResult.StandardError);
                card.Enabled = true;
                SetActionProgress(card, false);
                if (restoreResult.ExitCode != 0)
                {
                    ShowNotification($"Restore point creation failed. No change was made. Details: {restoreLog}", true);
                    return;
                }
            }
        }
        card.Enabled = false;
        SetActionProgress(card, true);
        ShowNotification($"Running {action.Title}...", false);
        _notificationTimer.Stop();
        try
        {
            var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), arguments, action.RequiresAdmin);
            var detail = $"STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}";
            var logPath = WriteLog($"{action.Title} | {action.Script}", result.ExitCode, detail, action.Category);
            var applyLabel = isRestore
                ? (string.IsNullOrWhiteSpace(action.RestoreText) ? "Restore" : action.RestoreText.Trim())
                : (string.IsNullOrWhiteSpace(action.ApplyText) ? "Apply" : action.ApplyText.Trim());
            var isLockScreenRequest = action.Id == "wallpaper-browser" && arguments.Contains("SetLockScreen");
            var successVerb = isLockScreenRequest ? "Requested" : isRestore ? "Restored" :
                applyLabel.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "Opened" :
                applyLabel.Equals("Install", StringComparison.OrdinalIgnoreCase) ? "Installed" :
                applyLabel.Equals("Remove", StringComparison.OrdinalIgnoreCase) ? "Removed" :
                applyLabel.Equals("Enable", StringComparison.OrdinalIgnoreCase) ? "Enabled" :
                applyLabel.Equals("Disable", StringComparison.OrdinalIgnoreCase) ? "Disabled" :
                applyLabel is "Check" or "Diagnose" ? "Checked" : "Applied";
            var restartRequired = false;
            if (result.ExitCode == 0)
            {
                try
                {
                    using var resultJson = JsonDocument.Parse(result.StandardOutput);
                    restartRequired = resultJson.RootElement.TryGetProperty("RestartRequired", out var restart) && restart.GetBoolean();
                }
                catch (JsonException) { }
            }
            var actionButton = card.Controls.Find(isRestore ? "restore-button" : "action-button", true).FirstOrDefault() as Button;
            if (actionButton is not null) actionButton.Text = result.ExitCode == 0 ? restartRequired ? "✓ Restart required" : $"✓ {successVerb}" : "Failed";
            var status = card.Controls.Find("status", true).FirstOrDefault() as Label
                ?? card.Controls.Find("wallpaper-status", true).FirstOrDefault() as Label;
            if (status is not null)
            {
                status.Text = result.ExitCode == 0
                    ? restartRequired ? "Status: restart required" : isLockScreenRequest ? "Status: Windows accepted lock-screen assignment; exact active path is not exposed" : $"Status: {successVerb.ToLowerInvariant()}"
                    : "Status: failed";
                status.ForeColor = result.ExitCode != 0 ? Color.FromArgb(226, 124, 104) : restartRequired ? Color.FromArgb(226, 160, 94) : AccentColor;
            }
            var resultReason = result.StandardError.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                ?? result.StandardOutput.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                ?? "Windows did not provide additional error details.";
            UpdateActionDetails(card, action, arguments, result.ExitCode, result.StandardOutput, result.StandardError, logPath);
            ShowNotification(result.ExitCode != 0
                ? $"{action.Title} could not be completed. Expand Technical Details for the reason."
                : restartRequired ? $"{action.Title} request was accepted. Restart Windows before checking connectivity."
                : isLockScreenRequest ? "Windows accepted the lock-screen assignment and returned an active image stream; it does not expose the active source path for exact-file verification."
                : $"{action.Title}: {successVerb.ToLowerInvariant()} successfully.", result.ExitCode != 0 || restartRequired);
            if (action.Id == "power-select-installed") await LoadPowerPlanInventoryAsync();
            if (result.ExitCode == 0 && action.Category == "Drivers")
            {
                if (action.Id == "driver-list") await LoadDriverInventoryAsync(result, logPath);
                else RenderDriverResults(action, result.StandardOutput, logPath, owningPage);
                return;
            }
            if (result.ExitCode != 0 && action.Category == "Power" && arguments.Contains("Select"))
            {
                var details = card.Controls.Find("action-details", true).FirstOrDefault() as TextBox;
                if (details is not null) details.AppendText("\r\n\r\nSuggested recovery: select Use Available Plan to switch to an installed Windows scheme.");
            }
            else if (result.ExitCode == 0 && !restartRequired)
            {
                if (action.Id == "startup-disable-entry") await LoadStartupInventoryAsync();
                else if (action.Id != "power-select-installed")
                {
                    if (action.Id == "wallpaper-browser" && !isLockScreenRequest) await RefreshWallpaperStatusAsync(action, card);
                    else await RefreshStatusAsync(action, card, true);
                }
            }
            if (actionButton is not null && !actionButton.IsDisposed)
            {
                await Task.Delay(1800);
                if (!actionButton.IsDisposed) actionButton.Text = applyLabel;
            }
        }
        catch (Exception exception)
        {
            var logPath = WriteLog($"{action.Title} | {action.Script}", 1, exception.ToString(), action.Category);
            UpdateActionDetails(card, action, arguments, 1, "", exception.ToString(), logPath);
            ShowNotification($"{action.Title} could not be completed. Expand Technical Details for the reason.", true);
            var status = card.Controls.Find("status", true).FirstOrDefault() as Label
                ?? card.Controls.Find("wallpaper-status", true).FirstOrDefault() as Label;
            if (status is not null) { status.Text = "Status: failed"; status.ForeColor = Color.FromArgb(226, 124, 104); }
        }
        finally
        {
            card.Enabled = true;
            SetActionProgress(card, false);
        }
    }

    private static string GetRiskTier(ToolboxAction action)
    {
        var configured = action.RiskTier.Trim().ToUpperInvariant();
        if (configured is "SAFE" or "ADVANCED" or "EXPERIMENTAL") return configured;
        return action.RequiresConfirmation || action.RequiresAdmin ? "ADVANCED" : "SAFE";
    }

    private static bool MatchesActionMode(IReadOnlyList<string> arguments, IReadOnlyList<string> configuredArguments)
    {
        if (configuredArguments.Count == 0) return false;
        if (arguments.SequenceEqual(configuredArguments, StringComparer.OrdinalIgnoreCase)) return true;
        return configuredArguments.Count >= 2 && arguments.Count >= 2 &&
            arguments[0].Equals(configuredArguments[0], StringComparison.OrdinalIgnoreCase) &&
            arguments[1].Equals(configuredArguments[1], StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateActionDetails(Control card, ToolboxAction action, IReadOnlyList<string> arguments, int exitCode, string stdout, string stderr, string logPath)
    {
        var details = card.Controls.Find("action-details", true).FirstOrDefault() as TextBox;
        if (details is null) return;
        var scriptPath = ResolveScript(action.Script);
        details.Text = $"Action: {action.Title}\r\nScript: {scriptPath}\r\nArguments: {JsonSerializer.Serialize(arguments)}\r\nExit code: {exitCode}\r\nLog: {logPath}\r\n\r\nStandard error:\r\n{stderr.Trim()}\r\n\r\nStandard output:\r\n{stdout.Trim()}";
    }

    private FlowLayoutPanel? FindOwningPage(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (current is FlowLayoutPanel page && _pageViews.Values.Contains(page)) return page;
        return null;
    }

    private async Task RunPowerFallbackAsync(string actionId)
    {
        var action = _config.Actions.FirstOrDefault(item => item.Id == actionId);
        if (action is null)
        {
            ShowNotification("The selected power recovery action is not configured.", true);
            return;
        }
        try
        {
            var result = await ScriptRunner.RunAsync(ResolveScript(action.Script), action.ApplyArgs, action.RequiresAdmin);
            var detail = $"STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}";
            var log = WriteLog($"{action.Title} | {action.Script}", result.ExitCode, detail, action.Category);
            ShowNotification(result.ExitCode == 0 ? result.StandardOutput.Trim() : $"A compatible power plan could not be selected. Details: {log}", result.ExitCode != 0);
        }
        catch (Exception exception)
        {
            var log = WriteLog($"{action.Title} | {action.Script}", 1, exception.ToString(), action.Category);
            ShowNotification($"Power plan recovery failed: {exception.Message} Details: {log}", true);
        }
    }

    private void RenderDriverResults(ToolboxAction action, string json, string logPath, FlowLayoutPanel target)
    {
        var priorResults = target.Controls.Find("driver-results", false).FirstOrDefault();
        if (priorResults is not null) target.Controls.Remove(priorResults);
        var resultPanel = new FlowLayoutPanel
        {
            Name = "driver-results",
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = PageColor,
            Margin = new Padding(0, 4, 0, 0)
        };
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (action.Id == "driver-problems")
            {
                var problems = root.GetProperty("Problems");
                if (problems.GetArrayLength() == 0)
                    resultPanel.Controls.Add(CreateNotice("No devices are reporting a PnP driver error.", false));
                else
                    foreach (var problem in problems.EnumerateArray())
                    {
                        var friendlyName = GetJsonString(problem, "Name", "Device requiring attention");
                        var code = problem.TryGetProperty("ConfigManagerErrorCode", out var errorCode) ? errorCode.ToString() : "Unknown";
                        var instance = GetJsonString(problem, "PNPDeviceID", "Unavailable");
                        resultPanel.Controls.Add(CreateDriverCard(friendlyName, $"Device status: {GetJsonString(problem, "Status", "Unknown")} · Configuration code: {code}", instance));
                    }
            }
            else
            {
                var entries = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [];
                if (entries.Count == 0)
                    resultPanel.Controls.Add(CreateNotice("Windows did not return driver entries for this view.", false));
                foreach (var entry in entries)
                {
                    var name = GetJsonString(entry, "Name", GetJsonString(entry, "Device", "Unknown device"));
                    var provider = GetJsonString(entry, "Provider", GetJsonString(entry, "DriverProviderName", "Provider unavailable"));
                    var version = GetJsonString(entry, "Version", GetJsonString(entry, "DriverVersion", "Version unavailable"));
                    var date = GetJsonString(entry, "Date", GetJsonString(entry, "DriverDate", "Date unavailable"));
                    var status = GetJsonString(entry, "Status", "Unknown status");
                    var code = entry.TryGetProperty("ErrorCode", out var error) && error.ValueKind != JsonValueKind.Null ? $" · Error {error}" : "";
                    var instance = GetJsonString(entry, "InstanceId", GetJsonString(entry, "DeviceID", "Unavailable"));
                    var inf = GetJsonString(entry, "InfName", "Unavailable");
                    var signed = GetJsonString(entry, "IsSigned", "Unknown");
                    var details = $"Device instance: {instance}\nINF: {inf}\nSigned: {signed}\nDriver date: {date}";
                    resultPanel.Controls.Add(CreateDriverCard(name, $"{status}{code} · {provider} · {version}", details));
                }
            }
            resultPanel.Controls.Add(new Label { Text = $"Detailed output is logged at {logPath}", AutoSize = true, ForeColor = MutedColor, Margin = new Padding(0, 8, 0, 12) });
        }
        catch (Exception exception)
        {
            var parseLog = WriteLog("Driver results parsing", 1, exception.ToString());
            resultPanel.Controls.Add(CreateNotice($"Driver results could not be displayed. See log: {parseLog}", true));
        }
        target.Controls.Add(resultPanel);
    }

    private Panel CreateDriverCard(string name, string summary, string details)
    {
        var card = new Panel { Width = Math.Max(620, _actions.ClientSize.Width - 42), Height = 92, BackColor = SurfaceColor, Padding = new Padding(15, 10, 15, 8), Margin = new Padding(0, 0, 0, 8), Tag = "card" };
        var heading = new Label { Text = name, Dock = DockStyle.Top, Height = 25, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 11F), AutoEllipsis = true };
        var description = new Label { Text = summary, Dock = DockStyle.Top, Height = 23, ForeColor = MutedColor, AutoEllipsis = true };
        var advanced = new TextBox { Text = details, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Visible = false, BorderStyle = BorderStyle.None, BackColor = SurfaceColor, ForeColor = MutedColor, Font = new Font("Consolas", 8.5F) };
        var toggle = new LinkLabel { Text = "Advanced details", Dock = DockStyle.Bottom, Height = 18, LinkColor = AccentColor, ActiveLinkColor = AccentColor };
        toggle.Click += (_, _) =>
        {
            advanced.Visible = !advanced.Visible;
            card.Height = advanced.Visible ? 154 : 92;
            toggle.Text = advanced.Visible ? "Hide advanced details" : "Advanced details";
        };
        card.Controls.Add(advanced);
        card.Controls.Add(toggle);
        card.Controls.Add(description);
        card.Controls.Add(heading);
        return card;
    }

    private static string GetJsonString(JsonElement element, string property, string fallback)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }

    private static void SetActionProgress(Control card, bool running)
    {
        var label = card.Controls.Find("status", true).FirstOrDefault() as Label;
        if (label is not null && running)
        {
            label.Text = "Running...";
            label.ForeColor = Color.FromArgb(240, 179, 120);
        }
        var progress = card.Controls.Find("progress", true).FirstOrDefault() as ProgressBar;
        if (progress is not null) progress.Visible = running;
    }

    private string ResolveScript(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new FileNotFoundException("Configured script is missing or outside the Apex directory.", path);
        return path;
    }

    private string WriteLog(string action, int exitCode, string detail, string category = "Toolbox")
    {
        var line = $"{DateTimeOffset.Now:O} | category={category} | action={action} | {(exitCode == 0 ? "Success" : "Failed")} | exit={exitCode} | {detail}{Environment.NewLine}";
        var path = Path.Combine(_root, "Logs", "Toolbox.log");
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, line);
            return path;
        }
        catch
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexOS", "Logs", "Toolbox.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line);
            return path;
        }
    }

    private void UpdateFooter()
    {
        _footer.Text = _isWindows
            ? $"Apex OS    |    {_windows.Product} {_windows.DisplayVersion} (build {_windows.Build}, {RuntimeInformation.OSArchitecture})    |    Power plan: checking..."
            : "Apex OS    |    Windows-only actions unavailable";
        if (!_isWindows || !_windows.IsWindows11) return;
        try
        {
            var info = new ProcessStartInfo("powercfg.exe", "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var process = Process.Start(info);
            if (process is null) return;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            var plan = output.Split('(', ')').ElementAtOrDefault(1) ?? "Unknown";
            _footer.Text = $"Apex OS    |    {_windows.Product} {_windows.DisplayVersion} (build {_windows.Build}, {RuntimeInformation.OSArchitecture})    |    Power plan: {plan}";
        }
        catch { _footer.Text = $"Apex OS    |    {_windows.Product} {_windows.DisplayVersion} (build {_windows.Build}, {RuntimeInformation.OSArchitecture})    |    Power plan: unavailable"; }
    }

    private static WindowsDetails ReadWindowsDetails()
    {
        if (!OperatingSystem.IsWindows())
            return new WindowsDetails("Non-Windows", Environment.OSVersion.Version.Build, "unsupported", false);
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var release = key?.GetValue("DisplayVersion") as string ?? "unknown release";
        var buildValue = key?.GetValue("CurrentBuildNumber") as string;
        var build = int.TryParse(buildValue, out var parsed) ? parsed : Environment.OSVersion.Version.Build;
        var isWindows11 = Environment.OSVersion.Version.Major >= 10 && build >= 22000 && !product.Contains("Server", StringComparison.OrdinalIgnoreCase);
        if (isWindows11 && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
            product = "Windows 11" + product["Windows 10".Length..];
        return new WindowsDetails(product, build, release, isWindows11);
    }

    private sealed record WindowsDetails(string Product, int Build, string DisplayVersion, bool IsWindows11);
}