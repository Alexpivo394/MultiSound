using System;
using System.IO;
using System.Text.Json;

namespace MultiSound.App.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON.</summary>
/// <remarks>A file that cannot be read is kept as <c>settings.json.bak</c> and defaults are used.</remarks>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiSound", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                // The backup is a courtesy; defaults still apply.
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
