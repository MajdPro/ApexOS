using System.Drawing;

namespace ApexToolbox;

internal sealed record FirstRunSelection(
    string PowerPlan,
    string Theme,
    string RamMode,
    string ExplorerMenu,
    string Wallpaper,
    string Browser,
    string GamingApp,
    string RecordingApp,
    string UtilityApp,
    bool EnableGaming,
    bool CreateRestorePoint,
    bool SetChromeDefault,
    bool ConfigureNanaZip);

internal sealed class FirstRunForm : Form
{
    private readonly ComboBox _power = Choice("Keep current", "Apex Balanced", "Apex Performance", "Apex Maximum Performance", "Apex Ultimate Performance", "Apex Power Saver", "Apex Laptop Performance", "Apex Custom");
    private readonly ComboBox _theme = Choice("Keep current", "Dark", "Light");
    private readonly ComboBox _ram = Choice("Keep current", "OFF / restore saved baseline", "BALANCED", "AGGRESSIVE");
    private readonly ComboBox _explorer = Choice("Keep current", "Classic Windows context menu", "Windows 11 context menu");
    private readonly ComboBox _browser = Choice("Keep existing browser", "Google Chrome", "Mozilla Firefox", "Brave");
    private readonly ComboBox _gamingApp = Choice("None", "Steam", "Epic Games Launcher", "Minecraft Launcher");
    private readonly ComboBox _recordingApp = Choice("None", "OBS Studio");
    private readonly ComboBox _utilityApp = Choice("None", "NanaZip", "7-Zip");
    private readonly ComboBox _wallpaper;
    private readonly CheckBox _gaming = new() { Text = "Enable Apex Gaming Mode", AutoSize = true };
    private readonly CheckBox _restorePoint = new() { Text = "Request a Windows restore point first", AutoSize = true };
    private readonly CheckBox _chrome = new() { Text = "Open Chrome default-app settings", AutoSize = true };
    private readonly CheckBox _nanaZip = new() { Text = "Open NanaZip archive-association settings", AutoSize = true };

    public FirstRunSelection Selection { get; private set; } = new("Keep current", "Keep current", "Keep current", "Keep current", "None", "Keep existing browser", "None", "None", "None", false, false, false, false);

    public FirstRunForm(string apexRoot)
    {
        Text = "Welcome to Apex OS";
        Size = new Size(700, 760);
        MinimumSize = new Size(640, 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(19, 23, 28);
        ForeColor = Color.FromArgb(235, 239, 242);
        Font = new Font("Segoe UI", 9.5F);

        var wallpaperDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ApexDesktop", "Wallpapers");
        var availableWallpapers = Directory.Exists(wallpaperDirectory)
            ? Directory.EnumerateFiles(wallpaperDirectory)
                .Where(path => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("Apex-LockScreen", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray()
            : [];
        _wallpaper = Choice(new[] { "None" }.Concat(availableWallpapers).ToArray());
        _browser.SelectedIndex = 1;
        var defaultWallpaper = Array.FindIndex(availableWallpapers, name => string.Equals(name, "Apex-Default-Dark.jpg", StringComparison.OrdinalIgnoreCase));
        if (defaultWallpaper >= 0) _wallpaper.SelectedIndex = defaultWallpaper + 1;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 22, 26, 18), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Welcome to Apex OS", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 20), ForeColor = Color.FromArgb(116, 219, 186) }, 0, 0);

        var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(0, 6, 0, 8) };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddChoice(choices, 0, "Power plan", _power, 1);
        AddChoice(choices, 1, "Theme", _theme, 0);
        AddChoice(choices, 2, "RAM Saver", _ram, 0);
        AddChoice(choices, 3, "Explorer context menu", _explorer, 1);
        AddChoice(choices, 4, "Wallpaper", _wallpaper, 0);
        AddChoice(choices, 5, "Default browser install", _browser, 0);
        AddChoice(choices, 6, "Optional game launcher", _gamingApp, 0);
        AddChoice(choices, 7, "Optional recording app", _recordingApp, 0);
        AddChoice(choices, 8, "Optional archive utility", _utilityApp, 0);
        layout.Controls.Add(choices, 0, 1);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        options.Controls.Add(_restorePoint);
        options.Controls.Add(_gaming);
        options.Controls.Add(_chrome);
        options.Controls.Add(_nanaZip);
        layout.Controls.Add(options, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var apply = new Button { Text = "Apply selection", Width = 130, Height = 34, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(116, 219, 186), FlatStyle = FlatStyle.Flat };
        apply.Click += (_, _) => CaptureSelection();
        var skip = new Button { Text = "Skip setup", Width = 100, Height = 34, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(49, 59, 66), ForeColor = Color.White };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(skip);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = apply;
        CancelButton = skip;
    }

    private void CaptureSelection()
    {
        Selection = new FirstRunSelection(
            _power.SelectedItem?.ToString() ?? "Keep current",
            _theme.SelectedItem?.ToString() ?? "Keep current",
            _ram.SelectedItem?.ToString() ?? "Keep current",
            _explorer.SelectedItem?.ToString() ?? "Keep current",
            _wallpaper.SelectedItem?.ToString() ?? "None",
            _browser.SelectedItem?.ToString() ?? "Keep existing browser",
            _gamingApp.SelectedItem?.ToString() ?? "None",
            _recordingApp.SelectedItem?.ToString() ?? "None",
            _utilityApp.SelectedItem?.ToString() ?? "None",
            _gaming.Checked,
            _restorePoint.Checked,
            _chrome.Checked,
            _nanaZip.Checked);
    }

    private static ComboBox Choice(params string[] items)
    {
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(29, 35, 41), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static void AddChoice(TableLayoutPanel table, int row, string label, ComboBox choice, int selectedIndex)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(190, 199, 205) }, 0, row);
        table.Controls.Add(choice, 1, row);
        choice.SelectedIndex = Math.Min(selectedIndex, choice.Items.Count - 1);
    }
}