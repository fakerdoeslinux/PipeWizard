using System;
using System.Text.Json.Serialization;
using PipeWizard.Services;

namespace PipeWizard.Models;

/// <summary>
/// One installed mod. This is serialised next to the mod's files as
/// pipewizard-mod.json, so the mods folder is self-describing and survives
/// PipeWizard being reinstalled.
/// </summary>
public sealed class InstalledMod
{
    public int ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Md5 { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public string OverlayRoot { get; set; } = string.Empty;
    public string Kind { get; set; } = "disc";
    public bool RequiresNewer { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public long SizeBytes { get; set; }

    [JsonIgnore] public bool Enabled { get; set; }
    [JsonIgnore] public int Priority { get; set; } = int.MaxValue;

    [JsonIgnore]
    public bool IsRiivolution => Kind.Equals("riivolution", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public string KindLabel => IsRiivolution ? "Riivolution" : "Disc overlay";

    [JsonIgnore] public string SizeText => PlatformService.FormatSize(SizeBytes);

    [JsonIgnore] public string InstalledText => InstalledAt.LocalDateTime.ToString("d MMM yyyy");

    [JsonIgnore] public string PriorityText => Enabled ? $"Priority {Priority + 1}" : "Disabled";

    [JsonIgnore] public string VersionText => string.IsNullOrWhiteSpace(Version) ? "No version" : Version;
}
