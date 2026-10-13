using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace T650Bridge.UI;

public sealed class LogWindowForm : Form
{
    private readonly RichTextBox _logBox;
    private readonly Label _telemetryLabel;
    private readonly Button _btnRewake;
    private readonly Button _btnClear;
    private readonly Button _btnHide;

    private bool _allowClose;
    private const int MaxLogChars = 64_000;

    public event Action? RequestRewake;
    public event Action? RequestHideToTray;
    public event Action? RequestExit;

    public LogWindowForm(string title, Icon? appIcon)
    {
        Text = title;
        Size = new Size(760, 480);
        MinimumSize = new Size(540, 320);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 20, 24);
        ForeColor = Color.FromArgb(224, 230, 237);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        KeyPreview = true;

        if (appIcon != null)
        {
            Icon = appIcon;
        }

        // Top telemetry status strip
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(26, 30, 36),
            Padding = new Padding(10, 0, 10, 0)
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
        topPanel.Controls.Add(_telemetryLabel);

        // Bottom control bar
        var bottomPanel = new Panel
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

        bottomPanel.Controls.Add(shortcutHint);
        bottomPanel.Controls.Add(buttonFlow);

        // Main log output view
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

        Controls.Add(_logBox);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);

        KeyDown += OnFormKeyDown;
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
        if (line.Contains("[Tier 2]"))
            return Color.FromArgb(210, 140, 255);
        if (line.Contains(">>>") || line.Contains("[Security] Elevation prompt") || line.Contains("Waiting for T650"))
            return Color.FromArgb(255, 210, 80);
        if (line.Contains("Waiting for Logitech Unifying") || line.Contains("Error") || line.Contains("Failed"))
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
