using System.Text.Json;
using System.Text.Json.Serialization;

namespace T650Bridge.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrationMode
{
    Automatic,
    Standalone,
    PtpDriver
}

public sealed class BridgeSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "T650-Bridge"
    );

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public IntegrationMode IntegrationMode { get; set; } = IntegrationMode.Automatic;
    public bool StartMinimized { get; set; } = false;
    public bool AutoHideOnConnect { get; set; } = true;
    public bool EnableNotifications { get; set; } = false;

    // Multi-touch Landing Debounce Settings
    public bool DebounceEnabled { get; set; } = true;
    public int LandingDebounceMs { get; set; } = 35;
    public bool SuppressLandingConfidence { get; set; } = true;
    public int MovementDisplacementThreshold { get; set; } = 40;

    // Tap to Drag & Double Click Settings
    public bool TapToDragEnabled { get; set; } = true;
    public int DoubleClickTimeoutMs { get; set; } = 250;

    [JsonPropertyName("tapDragTimeoutMs")]
    public int? LegacyTapDragTimeoutMs
    {
        get => null;
        set { if (value.HasValue) DoubleClickTimeoutMs = value.Value; }
    }

    [JsonIgnore]
    public int TapDragTimeoutMs
    {
        get => DoubleClickTimeoutMs;
        set => DoubleClickTimeoutMs = value;
    }

    // Cursor Sensitivity Setting (Default: 1.15)
    public double CursorSensitivity { get; set; } = 1.15;

    // Diagnostic Frame Logging
    public bool LogTouchFrames { get; set; } = false;

    // 3-Finger Gesture Shortcuts
    public string Gesture3Click { get; set; } = "Win+S";
    public string Gesture3DragUp { get; set; } = "Win+Tab";
    public string Gesture3DragDown { get; set; } = "Win+D";
    public string Gesture3DragLeft { get; set; } = "Alt+Shift+Tab";
    public string Gesture3DragRight { get; set; } = "Alt+Tab";

    // 4-Finger Gesture Shortcuts
    public string Gesture4Click { get; set; } = "Win+A";
    public string Gesture4DragUp { get; set; } = "Win+Tab";
    public string Gesture4DragDown { get; set; } = "Win+A";
    public string Gesture4DragLeft { get; set; } = "Ctrl+Win+Left";
    public string Gesture4DragRight { get; set; } = "Ctrl+Win+Right";

    public static string GetSettingsPath() => SettingsFilePath;
    public static string GetSettingsDirectory() => SettingsDirectory;

    public static BridgeSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<BridgeSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load settings from {SettingsFilePath} ({ex.Message}). Using defaults.");
        }

        var defaultSettings = new BridgeSettings();
        defaultSettings.Save();
        return defaultSettings;
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to save settings to {SettingsFilePath}: {ex.Message}");
            return false;
        }
    }
}
