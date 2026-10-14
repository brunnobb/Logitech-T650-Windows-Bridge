using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using T650Bridge.Config;

namespace T650Bridge.UI;

public sealed class LogWindowForm : Form
{
    private readonly TabControl _tabControl;
    private readonly TabPage _tabSettings;
    private readonly TabPage _tabLogs;

    // Log Controls
    private readonly RichTextBox _logBox;
    private readonly Label _telemetryLabel;
    private readonly Button _btnRewake;
    private readonly Button _btnClear;
    private readonly Button _btnHide;

    // Settings Controls
    private readonly ComboBox _comboIntegrationMode;
    private readonly Label _lblIntegrationHelp;
    private readonly Label _lblActiveEngine;
    private readonly CheckBox _chkStartMinimized;
    private readonly CheckBox _chkAutoHideOnConnect;
    private readonly CheckBox _chkNotifications;
    private readonly CheckBox _chkLogFrames;
    private readonly CheckBox _chkLogFramesSettings;
    private readonly CheckBox _chkDebounceEnabled;
    private readonly TrackBar _trackDebounceMs;
    private readonly Label _lblDebounceDelay;
    private readonly CheckBox _chkSuppressLandingConfidence;
    private readonly CheckBox _chkTapToDragEnabled;
    private readonly NumericUpDown _numDoubleClickTimeout;

    // Cursor Sensitivity & Scrolling Controls
    private readonly TrackBar _trackSensitivity;
    private readonly Label _lblSensitivity;
    private readonly CheckBox _chkInvertVerticalScroll;

    // Gesture Shortcut Controls
    private readonly TextBox _txtG3Click;
    private readonly TextBox _txtG3DragUp;
    private readonly TextBox _txtG3DragDown;
    private readonly TextBox _txtG3DragLeft;
    private readonly TextBox _txtG3DragRight;

    private readonly TextBox _txtG4Click;
    private readonly TextBox _txtG4DragUp;
    private readonly TextBox _txtG4DragDown;
    private readonly TextBox _txtG4DragLeft;
    private readonly TextBox _txtG4DragRight;

    private readonly Label _lblSettingsSavedFeedback;
    private readonly System.Windows.Forms.Timer _feedbackTimer;

    private BridgeSettings _settings;
    private bool _allowClose;
    private const int MaxLogChars = 64_000;

    public event Action? RequestRewake;
    public event Action? RequestHideToTray;
    public event Action? RequestExit;
    public event Action<BridgeSettings>? SettingsChanged;

    public LogWindowForm(string title, Icon? appIcon, BridgeSettings settings)
    {
        _settings = settings;

        Text = title;
        Size = new Size(820, 620);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 20, 24);
        ForeColor = Color.FromArgb(224, 230, 237);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        KeyPreview = true;

        if (appIcon != null)
        {
            Icon = appIcon;
        }

        // Initialize TabControl
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(150, 32),
            Padding = new Point(12, 4)
        };
        _tabControl.DrawItem += OnTabControlDrawItem;

        _tabSettings = new TabPage("Settings")
        {
            BackColor = Color.FromArgb(18, 20, 24),
            ForeColor = Color.FromArgb(224, 230, 237),
            Padding = new Padding(16)
        };

        _tabLogs = new TabPage("Diagnostic Log")
        {
            BackColor = Color.FromArgb(18, 20, 24),
            ForeColor = Color.FromArgb(224, 230, 237),
            Padding = new Padding(0)
        };

        // --- Build Diagnostic Log Tab ---
        var topTelemetryPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(26, 30, 36),
            Padding = new Padding(10, 0, 10, 0)
        };

        _chkLogFrames = new CheckBox
        {
            Dock = DockStyle.Right,
            Width = 145,
            Text = "Log frames to list",
            Checked = _settings.LogTouchFrames,
            ForeColor = Color.FromArgb(170, 185, 205),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleRight,
            CheckAlign = ContentAlignment.MiddleRight,
            Cursor = Cursors.Hand
        };
        _chkLogFrames.CheckedChanged += (s, e) =>
        {
            _settings.LogTouchFrames = _chkLogFrames.Checked;
            if (_chkLogFramesSettings != null && _chkLogFramesSettings.Checked != _chkLogFrames.Checked)
            {
                _chkLogFramesSettings.Checked = _chkLogFrames.Checked;
            }
            SaveCurrentSettings();
        };

        _telemetryLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "[Telemetry] Waiting for touch frames...",
            ForeColor = Color.FromArgb(0, 220, 130),
            Font = new Font("Consolas", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        topTelemetryPanel.Controls.Add(_chkLogFrames);
        topTelemetryPanel.Controls.Add(_telemetryLabel);

        var bottomLogPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            BackColor = Color.FromArgb(26, 30, 36),
            Padding = new Padding(10, 6, 10, 6)
        };

        var shortcutHint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Shortcuts: [R] Re-wake pad   [H / Esc] Hide to tray   [Q] Quit daemon",
            ForeColor = Color.FromArgb(140, 150, 165),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _btnHide = CreateActionButton("Hide to Tray", Color.FromArgb(45, 52, 64));
        _btnHide.Click += (s, e) => RequestHideToTray?.Invoke();

        _btnClear = CreateActionButton("Clear Log", Color.FromArgb(45, 52, 64));
        _btnClear.Click += (s, e) => _logBox?.Clear();

        _btnRewake = CreateActionButton("Re-wake Pad", Color.FromArgb(0, 120, 90));
        _btnRewake.Click += (s, e) => RequestRewake?.Invoke();

        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        buttonFlow.Controls.Add(_btnHide);
        buttonFlow.Controls.Add(_btnClear);
        buttonFlow.Controls.Add(_btnRewake);

        bottomLogPanel.Controls.Add(shortcutHint);
        bottomLogPanel.Controls.Add(buttonFlow);

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(18, 20, 24),
            ForeColor = Color.FromArgb(220, 226, 234),
            Font = new Font("Consolas", 9.75F, FontStyle.Regular),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = false,
            DetectUrls = false,
            HideSelection = false
        };

        _tabLogs.Controls.Add(_logBox);
        _tabLogs.Controls.Add(topTelemetryPanel);
        _tabLogs.Controls.Add(bottomLogPanel);

        // --- Build Settings Tab ---
        var settingsContentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        // 1. Integration Mode Group
        var grpIntegration = CreateSettingsCard("Integration Mode", 150);

        var lblIntegrationType = new Label
        {
            Text = "Type of integration:",
            Location = new Point(16, 30),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 240, 245)
        };

        _comboIntegrationMode = new ComboBox
        {
            Location = new Point(16, 56),
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(32, 36, 44),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
        };
        _comboIntegrationMode.Items.AddRange(new object[]
        {
            "Automatic",
            "Standalone",
            "PTP Driver"
        });
        _comboIntegrationMode.SelectedIndex = (int)_settings.IntegrationMode;
        _comboIntegrationMode.SelectedIndexChanged += OnIntegrationModeChanged;

        _lblActiveEngine = new Label
        {
            Location = new Point(350, 58),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Italic),
            ForeColor = Color.FromArgb(0, 220, 130),
            Text = "Active: Initializing..."
        };

        _lblIntegrationHelp = new Label
        {
            Location = new Point(16, 92),
            Size = new Size(680, 48),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(160, 174, 192)
        };
        UpdateIntegrationHelpText();

        grpIntegration.Controls.Add(lblIntegrationType);
        grpIntegration.Controls.Add(_comboIntegrationMode);
        grpIntegration.Controls.Add(_lblActiveEngine);
        grpIntegration.Controls.Add(_lblIntegrationHelp);

        // 2. Preferences Group
        var grpPreferences = CreateSettingsCard("Startup & Diagnostics Preferences", 155);
        grpPreferences.Top = grpIntegration.Bottom + 12;

        _chkStartMinimized = new CheckBox
        {
            Text = "Start minimized to system tray",
            Location = new Point(16, 30),
            AutoSize = true,
            Checked = _settings.StartMinimized,
            ForeColor = Color.FromArgb(220, 226, 234)
        };
        _chkStartMinimized.CheckedChanged += (s, e) => SaveCurrentSettings();

        _chkAutoHideOnConnect = new CheckBox
        {
            Text = "Auto-hide window when touchpad connects and gestures become active",
            Location = new Point(16, 58),
            AutoSize = true,
            Checked = _settings.AutoHideOnConnect,
            ForeColor = Color.FromArgb(220, 226, 234)
        };
        _chkAutoHideOnConnect.CheckedChanged += (s, e) => SaveCurrentSettings();

        _chkNotifications = new CheckBox
        {
            Text = "Show desktop notifications (balloons) for connection events",
            Location = new Point(16, 86),
            AutoSize = true,
            Checked = _settings.EnableNotifications,
            ForeColor = Color.FromArgb(220, 226, 234)
        };
        _chkNotifications.CheckedChanged += (s, e) => SaveCurrentSettings();

        _chkLogFramesSettings = new CheckBox
        {
            Text = "Add raw touch frames directly to Diagnostic Log list (detailed debugging)",
            Location = new Point(16, 114),
            AutoSize = true,
            Checked = _settings.LogTouchFrames,
            ForeColor = Color.FromArgb(220, 226, 234)
        };
        _chkLogFramesSettings.CheckedChanged += (s, e) =>
        {
            if (_chkLogFrames != null && _chkLogFrames.Checked != _chkLogFramesSettings.Checked)
            {
                _chkLogFrames.Checked = _chkLogFramesSettings.Checked;
            }
            SaveCurrentSettings();
        };

        grpPreferences.Controls.Add(_chkStartMinimized);
        grpPreferences.Controls.Add(_chkAutoHideOnConnect);
        grpPreferences.Controls.Add(_chkNotifications);
        grpPreferences.Controls.Add(_chkLogFramesSettings);

        // 3. Multi-Touch Landing Debounce Group
        var grpDebounce = CreateSettingsCard("Multi-Touch Landing Debounce & Stagger Protection", 175);
        grpDebounce.Top = grpPreferences.Bottom + 12;

        _chkDebounceEnabled = new CheckBox
        {
            Text = "Enable finger landing debouncer (prevents accidental Start / 3-finger taps before 4-finger swipes)",
            Location = new Point(16, 28),
            AutoSize = true,
            Checked = _settings.DebounceEnabled,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 240, 245)
        };
        _chkDebounceEnabled.CheckedChanged += (s, e) =>
        {
            if (_trackDebounceMs != null) _trackDebounceMs.Enabled = _chkDebounceEnabled.Checked;
            if (_chkSuppressLandingConfidence != null) _chkSuppressLandingConfidence.Enabled = _chkDebounceEnabled.Checked;
            SaveCurrentSettings();
        };

        _lblDebounceDelay = new Label
        {
            Text = $"Stabilization window: {_settings.LandingDebounceMs} ms ({GetDebounceLevelName(_settings.LandingDebounceMs)})",
            Location = new Point(16, 56),
            AutoSize = true,
            ForeColor = Color.FromArgb(0, 220, 130),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };

        _trackDebounceMs = new TrackBar
        {
            Location = new Point(16, 78),
            Width = 320,
            Height = 30,
            Minimum = 10,
            Maximum = 75,
            TickFrequency = 5,
            SmallChange = 5,
            LargeChange = 10,
            Value = Math.Clamp(_settings.LandingDebounceMs, 10, 75),
            Enabled = _settings.DebounceEnabled
        };
        _trackDebounceMs.Scroll += (s, e) =>
        {
            _lblDebounceDelay.Text = $"Stabilization window: {_trackDebounceMs.Value} ms ({GetDebounceLevelName(_trackDebounceMs.Value)})";
            SaveCurrentSettings();
        };

        var lblDebounceHelp = new Label
        {
            Text = "Buffers initial touchdown for 15–40 ms so 3-finger and 4-finger contacts arrive together before triggering tap gestures. Movement > 1.5mm flushes immediately with zero lag.",
            Location = new Point(345, 62),
            Size = new Size(370, 48),
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        _chkSuppressLandingConfidence = new CheckBox
        {
            Text = "Suppress tap confidence during initial touchdown (Hardware Confidence = 0 until movement or stabilization)",
            Location = new Point(16, 126),
            AutoSize = true,
            Checked = _settings.SuppressLandingConfidence,
            Enabled = _settings.DebounceEnabled,
            ForeColor = Color.FromArgb(220, 226, 234)
        };
        _chkSuppressLandingConfidence.CheckedChanged += (s, e) => SaveCurrentSettings();

        grpDebounce.Controls.Add(_chkDebounceEnabled);
        grpDebounce.Controls.Add(_lblDebounceDelay);
        grpDebounce.Controls.Add(_trackDebounceMs);
        grpDebounce.Controls.Add(lblDebounceHelp);
        grpDebounce.Controls.Add(_chkSuppressLandingConfidence);

        // 4. Tap Gestures & Tap to Drag Group
        var grpTapToDrag = CreateSettingsCard("Tap Gestures & Tap to Drag", 138);
        grpTapToDrag.Top = grpDebounce.Bottom + 12;

        _chkTapToDragEnabled = new CheckBox
        {
            Text = "Enable Tap to Drag (Double-tap and hold to drag windows, files, and selections)",
            Location = new Point(16, 26),
            AutoSize = true,
            Checked = _settings.TapToDragEnabled,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 240, 245)
        };

        var lblTapToDragHelp = new Label
        {
            Text = "Quickly tap twice with one finger and keep it on the glass to drag without pressing the physical button.\r\nSupported natively in both Precision Touchpad (Tier 2) and Standalone C# Engine (Tier 1) modes.",
            Location = new Point(16, 52),
            Size = new Size(690, 34),
            ForeColor = Color.FromArgb(160, 174, 192),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        var lblDoubleClick = new Label
        {
            Text = "Double click timeout:",
            Location = new Point(16, 96),
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 220, 235),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };

        _numDoubleClickTimeout = new NumericUpDown
        {
            Location = new Point(155, 93),
            Width = 75,
            Minimum = 80,
            Maximum = 1000,
            Increment = 10,
            Value = Math.Clamp(_settings.DoubleClickTimeoutMs, 80, 1000),
            BackColor = Color.FromArgb(32, 36, 44),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Enabled = _settings.TapToDragEnabled
        };
        _numDoubleClickTimeout.ValueChanged += (s, e) => SaveCurrentSettings();

        var lblDoubleClickUnit = new Label
        {
            Text = "ms (Recommended: 200–300 ms, Default: 250 ms) — Maximum delay between taps to register double-tap / tap-to-drag.",
            Location = new Point(238, 96),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        _chkTapToDragEnabled.CheckedChanged += (s, e) =>
        {
            _numDoubleClickTimeout.Enabled = _chkTapToDragEnabled.Checked;
            SaveCurrentSettings();
        };

        grpTapToDrag.Controls.Add(_chkTapToDragEnabled);
        grpTapToDrag.Controls.Add(lblTapToDragHelp);
        grpTapToDrag.Controls.Add(lblDoubleClick);
        grpTapToDrag.Controls.Add(_numDoubleClickTimeout);
        grpTapToDrag.Controls.Add(lblDoubleClickUnit);

        // 5. Cursor Tracking Sensitivity & Scrolling
        var grpSensitivity = CreateSettingsCard("Cursor Speed & Scrolling (Standalone Mode)", 150);
        grpSensitivity.Top = grpTapToDrag.Bottom + 12;

        _lblSensitivity = new Label
        {
            Text = $"Cursor sensitivity: {_settings.CursorSensitivity:0.00}x{(_settings.CursorSensitivity == 1.15 ? " (Default)" : "")}",
            Location = new Point(16, 26),
            AutoSize = true,
            ForeColor = Color.FromArgb(0, 220, 130),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };

        _trackSensitivity = new TrackBar
        {
            Location = new Point(16, 48),
            Width = 320,
            Height = 30,
            Minimum = 40,
            Maximum = 250,
            TickFrequency = 15,
            SmallChange = 5,
            LargeChange = 15,
            Value = Math.Clamp((int)Math.Round(_settings.CursorSensitivity * 100), 40, 250)
        };
        _trackSensitivity.Scroll += (s, e) =>
        {
            double sens = Math.Round(_trackSensitivity.Value / 100.0, 2);
            _lblSensitivity.Text = $"Cursor sensitivity: {sens:0.00}x{(sens == 1.15 ? " (Default)" : "")}";
            SaveCurrentSettings();
        };

        var lblSensHelp = new Label
        {
            Text = "Adjusts 1-finger tracking speed and software cursor acceleration multiplier.\r\nDefault: 1.15x for balanced precision. Lower for fine precision, higher for multi-monitors.",
            Location = new Point(345, 34),
            Size = new Size(370, 44),
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        _chkInvertVerticalScroll = new CheckBox
        {
            Text = "Reverse vertical scrolling direction (Invert 2-finger scroll in Standalone mode)",
            Location = new Point(16, 88),
            AutoSize = true,
            Checked = _settings.InvertVerticalScroll,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 240, 245)
        };
        _chkInvertVerticalScroll.CheckedChanged += (s, e) => SaveCurrentSettings();

        var lblScrollHelp = new Label
        {
            Text = "Default: Standard scrolling (finger down scrolls page down). Check to invert scroll direction (natural scrolling).\r\nNote: In Virtual PTP mode, configure scrolling direction natively in Windows Settings -> Touchpad.",
            Location = new Point(16, 114),
            Size = new Size(690, 30),
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        grpSensitivity.Controls.Add(_lblSensitivity);
        grpSensitivity.Controls.Add(_trackSensitivity);
        grpSensitivity.Controls.Add(lblSensHelp);
        grpSensitivity.Controls.Add(_chkInvertVerticalScroll);
        grpSensitivity.Controls.Add(lblScrollHelp);

        // 6. Gesture Shortcuts (3 & 4-Finger Actions)
        var grpShortcuts = CreateSettingsCard("Customizable Gesture Shortcuts (3 & 4-Finger Actions)", 245);
        grpShortcuts.Top = grpSensitivity.Bottom + 12;

        // --- 3-Finger Column ---
        var lbl3Header = new Label
        {
            Text = "3-Finger Gestures",
            Location = new Point(16, 28),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 220, 130)
        };
        grpShortcuts.Controls.Add(lbl3Header);

        _txtG3Click = CreateShortcutTextBox(115, 52, _settings.Gesture3Click);
        _txtG3DragUp = CreateShortcutTextBox(115, 80, _settings.Gesture3DragUp);
        _txtG3DragDown = CreateShortcutTextBox(115, 108, _settings.Gesture3DragDown);
        _txtG3DragLeft = CreateShortcutTextBox(115, 136, _settings.Gesture3DragLeft);
        _txtG3DragRight = CreateShortcutTextBox(115, 164, _settings.Gesture3DragRight);

        grpShortcuts.Controls.Add(CreateShortcutRowLabel("3 Click:", 16, 54));
        grpShortcuts.Controls.Add(_txtG3Click);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("3 Drag Up:", 16, 82));
        grpShortcuts.Controls.Add(_txtG3DragUp);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("3 Drag Down:", 16, 110));
        grpShortcuts.Controls.Add(_txtG3DragDown);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("3 Left:", 16, 138));
        grpShortcuts.Controls.Add(_txtG3DragLeft);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("3 Right:", 16, 166));
        grpShortcuts.Controls.Add(_txtG3DragRight);

        // --- 4-Finger Column ---
        var lbl4Header = new Label
        {
            Text = "4-Finger Gestures",
            Location = new Point(360, 28),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 220, 130)
        };
        grpShortcuts.Controls.Add(lbl4Header);

        _txtG4Click = CreateShortcutTextBox(465, 52, _settings.Gesture4Click);
        _txtG4DragUp = CreateShortcutTextBox(465, 80, _settings.Gesture4DragUp);
        _txtG4DragDown = CreateShortcutTextBox(465, 108, _settings.Gesture4DragDown);
        _txtG4DragLeft = CreateShortcutTextBox(465, 136, _settings.Gesture4DragLeft);
        _txtG4DragRight = CreateShortcutTextBox(465, 164, _settings.Gesture4DragRight);

        grpShortcuts.Controls.Add(CreateShortcutRowLabel("4 Click:", 360, 54));
        grpShortcuts.Controls.Add(_txtG4Click);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("4 Drag Up:", 360, 82));
        grpShortcuts.Controls.Add(_txtG4DragUp);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("4 Drag Down:", 360, 110));
        grpShortcuts.Controls.Add(_txtG4DragDown);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("4 Left:", 360, 138));
        grpShortcuts.Controls.Add(_txtG4DragLeft);
        grpShortcuts.Controls.Add(CreateShortcutRowLabel("4 Right:", 360, 166));
        grpShortcuts.Controls.Add(_txtG4DragRight);

        var lblShortcutHelp = new Label
        {
            Text = "Syntax: Modifiers (Win, Ctrl, Alt, Shift) + Key (e.g. Win+Tab, Alt+Tab). Leave empty to disable.",
            Location = new Point(16, 202),
            Size = new Size(520, 32),
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        var btnResetShortcuts = CreateActionButton("Reset Defaults", Color.FromArgb(45, 52, 64));
        btnResetShortcuts.Location = new Point(545, 198);
        btnResetShortcuts.Click += (s, e) =>
        {
            _txtG3Click.Text = "Win+S";
            _txtG3DragUp.Text = "Win+Tab";
            _txtG3DragDown.Text = "Win+D";
            _txtG3DragLeft.Text = "Alt+Shift+Tab";
            _txtG3DragRight.Text = "Alt+Tab";

            _txtG4Click.Text = "Win+A";
            _txtG4DragUp.Text = "Win+Tab";
            _txtG4DragDown.Text = "Win+A";
            _txtG4DragLeft.Text = "Ctrl+Win+Left";
            _txtG4DragRight.Text = "Ctrl+Win+Right";
            SaveCurrentSettings();
        };

        grpShortcuts.Controls.Add(lblShortcutHelp);
        grpShortcuts.Controls.Add(btnResetShortcuts);

        // 7. File Info & Actions Group
        var grpFile = CreateSettingsCard("Configuration Storage", 90);
        grpFile.Top = grpShortcuts.Bottom + 12;

        var lblFilePath = new Label
        {
            Text = $"Configuration File: {BridgeSettings.GetSettingsPath()}",
            Location = new Point(16, 30),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Consolas", 8.5F, FontStyle.Regular)
        };

        var btnOpenFolder = CreateActionButton("Open Config Folder", Color.FromArgb(45, 52, 64));
        btnOpenFolder.Location = new Point(16, 54);
        btnOpenFolder.Click += (s, e) =>
        {
            try
            {
                Directory.CreateDirectory(BridgeSettings.GetSettingsDirectory());
                Process.Start(new ProcessStartInfo
                {
                    FileName = BridgeSettings.GetSettingsDirectory(),
                    UseShellExecute = true
                });
            }
            catch { }
        };

        _lblSettingsSavedFeedback = new Label
        {
            Location = new Point(btnOpenFolder.Right + 16, 58),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 220, 130),
            Text = "✓ Settings saved",
            Visible = false
        };

        _feedbackTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _feedbackTimer.Tick += (s, e) =>
        {
            _lblSettingsSavedFeedback.Visible = false;
            _feedbackTimer.Stop();
        };

        grpFile.Controls.Add(lblFilePath);
        grpFile.Controls.Add(btnOpenFolder);
        grpFile.Controls.Add(_lblSettingsSavedFeedback);

        settingsContentPanel.Controls.Add(grpIntegration);
        settingsContentPanel.Controls.Add(grpPreferences);
        settingsContentPanel.Controls.Add(grpDebounce);
        settingsContentPanel.Controls.Add(grpTapToDrag);
        settingsContentPanel.Controls.Add(grpSensitivity);
        settingsContentPanel.Controls.Add(grpShortcuts);
        settingsContentPanel.Controls.Add(grpFile);

        _tabSettings.Controls.Add(settingsContentPanel);

        _tabControl.TabPages.Add(_tabSettings);
        _tabControl.TabPages.Add(_tabLogs);

        Controls.Add(_tabControl);

        KeyDown += OnFormKeyDown;
    }

    private static Panel CreateSettingsCard(string title, int height)
    {
        var panel = new Panel
        {
            Width = 730,
            Height = height,
            BackColor = Color.FromArgb(24, 28, 34),
            Margin = new Padding(0, 0, 0, 12),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var header = new Label
        {
            Text = title,
            UseMnemonic = false,
            Location = new Point(12, 6),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 135, 155)
        };
        panel.Controls.Add(header);

        return panel;
    }

    private void OnTabControlDrawItem(object? sender, DrawItemEventArgs e)
    {
        var tab = _tabControl.TabPages[e.Index];
        bool isSelected = (_tabControl.SelectedIndex == e.Index);

        var bgBrush = new SolidBrush(isSelected ? Color.FromArgb(32, 38, 48) : Color.FromArgb(20, 23, 28));
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        if (isSelected)
        {
            using var activeBarPen = new Pen(Color.FromArgb(0, 200, 120), 2.5f);
            e.Graphics.DrawLine(activeBarPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        var textBrush = new SolidBrush(isSelected ? Color.White : Color.FromArgb(150, 160, 175));
        var stringFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        using var font = new Font(Font, isSelected ? FontStyle.Bold : FontStyle.Regular);
        e.Graphics.DrawString(tab.Text, font, textBrush, e.Bounds, stringFormat);
    }

    private void OnIntegrationModeChanged(object? sender, EventArgs e)
    {
        UpdateIntegrationHelpText();
        SaveCurrentSettings();
    }

    private void UpdateIntegrationHelpText()
    {
        var mode = (IntegrationMode)_comboIntegrationMode.SelectedIndex;
        switch (mode)
        {
            case IntegrationMode.Automatic:
                _lblIntegrationHelp.ForeColor = Color.FromArgb(160, 174, 192);
                _lblIntegrationHelp.Text = "• Automatic: Attempts to connect to the Virtual PTP driver first. If the driver is not installed or available, automatically falls back to standalone software gestures.";
                break;

            case IntegrationMode.Standalone:
                _lblIntegrationHelp.ForeColor = Color.FromArgb(160, 174, 192);
                _lblIntegrationHelp.Text = "• Standalone: Always uses our C# software gesture engine directly via SendInput. Does not connect to the PTP driver.";
                break;

            case IntegrationMode.PtpDriver:
                _lblIntegrationHelp.ForeColor = Color.FromArgb(255, 180, 60);
                _lblIntegrationHelp.Text = "• PTP Driver: Multi-touch frames are injected directly into the Virtual PTP driver.\r\n⚠️ Notice: The Virtual PTP driver must be installed. If not connected, touch injection is paused and only logs will be recorded.";
                break;
        }
    }

    private void SaveCurrentSettings()
    {
        _settings.IntegrationMode = (IntegrationMode)_comboIntegrationMode.SelectedIndex;
        _settings.StartMinimized = _chkStartMinimized.Checked;
        _settings.AutoHideOnConnect = _chkAutoHideOnConnect.Checked;
        _settings.EnableNotifications = _chkNotifications.Checked;
        _settings.DebounceEnabled = _chkDebounceEnabled.Checked;
        _settings.LandingDebounceMs = _trackDebounceMs.Value;
        _settings.SuppressLandingConfidence = _chkSuppressLandingConfidence.Checked;
        _settings.TapToDragEnabled = _chkTapToDragEnabled.Checked;
        if (_numDoubleClickTimeout != null)
        {
            _settings.DoubleClickTimeoutMs = (int)_numDoubleClickTimeout.Value;
        }
        _settings.LogTouchFrames = _chkLogFrames.Checked;

        if (_trackSensitivity != null)
        {
            _settings.CursorSensitivity = Math.Round(_trackSensitivity.Value / 100.0, 2);
        }
        if (_chkInvertVerticalScroll != null)
        {
            _settings.InvertVerticalScroll = _chkInvertVerticalScroll.Checked;
        }

        if (_txtG3Click != null) _settings.Gesture3Click = _txtG3Click.Text.Trim();
        if (_txtG3DragUp != null) _settings.Gesture3DragUp = _txtG3DragUp.Text.Trim();
        if (_txtG3DragDown != null) _settings.Gesture3DragDown = _txtG3DragDown.Text.Trim();
        if (_txtG3DragLeft != null) _settings.Gesture3DragLeft = _txtG3DragLeft.Text.Trim();
        if (_txtG3DragRight != null) _settings.Gesture3DragRight = _txtG3DragRight.Text.Trim();

        if (_txtG4Click != null) _settings.Gesture4Click = _txtG4Click.Text.Trim();
        if (_txtG4DragUp != null) _settings.Gesture4DragUp = _txtG4DragUp.Text.Trim();
        if (_txtG4DragDown != null) _settings.Gesture4DragDown = _txtG4DragDown.Text.Trim();
        if (_txtG4DragLeft != null) _settings.Gesture4DragLeft = _txtG4DragLeft.Text.Trim();
        if (_txtG4DragRight != null) _settings.Gesture4DragRight = _txtG4DragRight.Text.Trim();

        if (_settings.Save())
        {
            _lblSettingsSavedFeedback.Visible = true;
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }

        SettingsChanged?.Invoke(_settings);
    }

    private static string GetDebounceLevelName(int ms) => ms switch
    {
        <= 15 => "Minimal",
        <= 25 => "Fast",
        <= 40 => "Recommended",
        <= 55 => "High Protection",
        _ => "Aggressive"
    };

    public void UpdateEngineStatus(string statusText, Color color)
    {
        if (IsDisposed) return;
        _lblActiveEngine.Text = $"Active Engine: {statusText}";
        _lblActiveEngine.ForeColor = color;
    }

    private static Button CreateActionButton(string text, Color backColor)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(6, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 95);
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    private TextBox CreateShortcutTextBox(int x, int y, string initialValue)
    {
        var txt = new TextBox
        {
            Location = new Point(x, y),
            Width = 190,
            Text = initialValue,
            BackColor = Color.FromArgb(32, 36, 44),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        txt.TextChanged += (s, e) => SaveCurrentSettings();
        return txt;
    }

    private static Label CreateShortcutRowLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 210, 225),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control || e.Alt) return;

        if (e.KeyCode == Keys.R)
        {
            e.Handled = true;
            RequestRewake?.Invoke();
        }
        else if (e.KeyCode == Keys.H || e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            RequestHideToTray?.Invoke();
        }
        else if (e.KeyCode == Keys.Q)
        {
            e.Handled = true;
            RequestExit?.Invoke();
        }
    }

    public void AppendLogLine(string text)
    {
        if (IsDisposed) return;

        if (_logBox.TextLength > MaxLogChars)
        {
            _logBox.Select(0, _logBox.TextLength - (MaxLogChars / 2));
            _logBox.SelectedText = string.Empty;
        }

        Color lineColor = ResolveLineColor(text);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = lineColor;
        _logBox.AppendText(text + Environment.NewLine);
        _logBox.SelectionColor = _logBox.ForeColor;
        _logBox.ScrollToCaret();
    }

    public void UpdateTelemetry(string telemetryText)
    {
        if (IsDisposed) return;
        _telemetryLabel.Text = telemetryText;
    }

    private static Color ResolveLineColor(string line)
    {
        if (line.Contains("===") || line.Contains("Logitech T650 Bridge"))
            return Color.FromArgb(80, 220, 255);
        if (line.Contains("[Active]") || line.Contains("live!"))
            return Color.FromArgb(0, 230, 140);
        if (line.Contains("[Tier 2]") || line.Contains("[PTP Driver]"))
            return Color.FromArgb(210, 140, 255);
        if (line.Contains(">>>") || line.Contains("[Security] Elevation prompt") || line.Contains("Waiting for T650"))
            return Color.FromArgb(255, 210, 80);
        if (line.Contains("Waiting for Logitech Unifying") || line.Contains("Error") || line.Contains("Failed") || line.Contains("DISABLED"))
            return Color.FromArgb(255, 100, 100);
        return Color.FromArgb(220, 226, 234);
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            RequestHideToTray?.Invoke();
            return;
        }

        base.OnFormClosing(e);
    }
}

public sealed class BridgeConsoleRedirectionWriter : TextWriter
{
    private readonly TextWriter _originalOut;
    private readonly Action<string> _onLogLine;
    private readonly Action<string> _onTelemetryUpdate;
    private readonly StringBuilder _lineBuffer = new();
    private readonly object _sync = new();

    public override Encoding Encoding => Encoding.UTF8;

    public BridgeConsoleRedirectionWriter(
        TextWriter originalOut,
        Action<string> onLogLine,
        Action<string> onTelemetryUpdate)
    {
        _originalOut = originalOut;
        _onLogLine = onLogLine;
        _onTelemetryUpdate = onTelemetryUpdate;
    }

    public override void Write(char value)
    {
        lock (_sync)
        {
            try { _originalOut.Write(value); } catch { }

            if (value == '\r')
            {
                FlushBuffer(isCarriageReturnOnly: true);
            }
            else if (value == '\n')
            {
                FlushBuffer(isCarriageReturnOnly: false);
            }
            else
            {
                _lineBuffer.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        lock (_sync)
        {
            try { _originalOut.Write(value); } catch { }

            // Fast path for telemetry updates starting with '\r' without newline
            if (value.StartsWith('\r') && !value.Contains('\n'))
            {
                _lineBuffer.Clear();
                _onTelemetryUpdate(value.TrimStart('\r').TrimEnd());
                return;
            }

            foreach (char ch in value)
            {
                if (ch == '\r')
                {
                    continue;
                }
                if (ch == '\n')
                {
                    FlushBuffer(isCarriageReturnOnly: false);
                }
                else
                {
                    _lineBuffer.Append(ch);
                }
            }
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            try { _originalOut.WriteLine(value); } catch { }

            if (!string.IsNullOrEmpty(value))
            {
                _lineBuffer.Append(value);
            }

            string full = _lineBuffer.ToString();
            _lineBuffer.Clear();

            foreach (string line in full.Split('\n'))
            {
                string cleaned = line.TrimEnd('\r');
                _onLogLine(cleaned);
            }
        }
    }

    private void FlushBuffer(bool isCarriageReturnOnly)
    {
        if (_lineBuffer.Length == 0) return;

        string text = _lineBuffer.ToString();
        _lineBuffer.Clear();

        if (isCarriageReturnOnly && text.StartsWith("[Frame"))
        {
            _onTelemetryUpdate(text.Trim());
        }
        else
        {
            _onLogLine(text);
        }
    }
}
