using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipeWizard.Services;

/// <summary>PipeWizard's own preferences. The port's own settings live in Config.toml.</summary>
public sealed class AppSettings
{
    [JsonPropertyName("portExecutable")]
    public string? PortExecutable { get; set; }

    [JsonPropertyName("gameFolder")]
    public string? GameFolder { get; set; }

    /// <summary>Set only when the user overrides the auto-detected Config.toml.</summary>
    [JsonPropertyName("configFile")]
    public string? ConfigFile { get; set; }

    [JsonPropertyName("useMangoHud")]
    public bool UseMangoHud { get; set; }

    /// <summary>Port binary built with Newer Super Mario Bros. Wii code linked in.</summary>
    [JsonPropertyName("newerPortExecutable")]
    public string? NewerPortExecutable { get; set; }

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 1240;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 800;

    [JsonPropertyName("firstRunSetupPrompted")]
    public bool FirstRunSetupPrompted { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var text = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(text, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Could not read settings: {exception.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception)
        {
            Log.Error("Could not save settings.", exception);
        }
    }
}
