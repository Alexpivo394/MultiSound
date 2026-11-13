using System.Collections.Generic;

namespace MultiSound.App.Settings;

/// <summary>Saved choices for one Windows audio device.</summary>
/// <param name="Volume">Copy volume, 0–1.</param>
/// <param name="OffsetMs">User offset for the copy, 0–500 ms.</param>
public sealed record DeviceSettings(bool Enabled = false, float Volume = 1f, double OffsetMs = 0);

public sealed class AppSettings
{
    /// <summary>Per-device settings keyed by the Windows device ID.</summary>
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    /// <summary>Whether the "still running in the tray" notice has been shown once.</summary>
    public bool TrayHintShown { get; set; }
}
