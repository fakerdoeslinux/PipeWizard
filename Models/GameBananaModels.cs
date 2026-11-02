using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipeWizard.Models;

/// <summary>One row from GameBanana's Subfeed or Search endpoints.</summary>
public sealed class GbModRecord
{
    [JsonPropertyName("_idRow")] public int Id { get; set; }
    [JsonPropertyName("_sName")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; set; }
    [JsonPropertyName("_sVersion")] public string? Version { get; set; }
    [JsonPropertyName("_tsDateAdded")] public long DateAdded { get; set; }
    [JsonPropertyName("_tsDateModified")] public long DateModified { get; set; }
    [JsonPropertyName("_nViewCount")] public int ViewCount { get; set; }
    [JsonPropertyName("_bHasFiles")] public bool HasFiles { get; set; }
    [JsonPropertyName("_bIsObsolete")] public bool IsObsolete { get; set; }
    [JsonPropertyName("_aPreviewMedia")] public GbPreviewMedia? PreviewMedia { get; set; }
    [JsonPropertyName("_aSubmitter")] public GbSubmitter? Submitter { get; set; }
    [JsonPropertyName("_aRootCategory")] public GbCategory? RootCategory { get; set; }
    [JsonPropertyName("_aSubCategory")] public GbCategory? SubCategory { get; set; }
    [JsonPropertyName("_aCategory")] public GbCategory? Category { get; set; }
    [JsonPropertyName("_aSuperCategory")] public GbCategory? SuperCategory { get; set; }
    [JsonPropertyName("_aTags")] public List<JsonElement>? Tags { get; set; }
}

public sealed class GbCategory
{
    [JsonPropertyName("_idRow")] public int Id { get; set; }
    [JsonPropertyName("_sName")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; set; }
}

public sealed class GbSubmitter
{
    [JsonPropertyName("_idRow")] public int Id { get; set; }
    [JsonPropertyName("_sName")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; set; }
    [JsonPropertyName("_sAvatarUrl")] public string? AvatarUrl { get; set; }
}

public sealed class GbPreviewMedia
{
    [JsonPropertyName("_aImages")] public List<GbImage>? Images { get; set; }
}

public sealed class GbImage
{
    [JsonPropertyName("_sType")] public string? Type { get; set; }
    [JsonPropertyName("_sBaseUrl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("_sUrl")] public string? Url { get; set; }
    [JsonPropertyName("_sFile")] public string? File { get; set; }
    [JsonPropertyName("_sFile100")] public string? File100 { get; set; }
    [JsonPropertyName("_sFile220")] public string? File220 { get; set; }
    [JsonPropertyName("_sFile530")] public string? File530 { get; set; }
    [JsonPropertyName("_sCaption")] public string? Caption { get; set; }

    public string? ThumbnailUrl => Build(File530 ?? File220 ?? File100 ?? File);

    public string? FullUrl => Build(File ?? File530 ?? Url);

    public string? ThumbnailSmallUrl => Build(File220 ?? File100 ?? File530 ?? File);

    private string? Build(string? file)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(file))
        {
            return BaseUrl.TrimEnd('/') + "/" + file;
        }

        return Url;
    }
}

/// <summary>Mod/{id}/ProfilePage. Also carries the current file list when GameBanana includes it.</summary>
public sealed class GbModDetails
{
    [JsonPropertyName("_idRow")] public int Id { get; set; }
    [JsonPropertyName("_sName")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; set; }
    [JsonPropertyName("_sVersion")] public string? Version { get; set; }
    [JsonPropertyName("_sText")] public string? Text { get; set; }
    [JsonPropertyName("_tsDateAdded")] public long DateAdded { get; set; }
    [JsonPropertyName("_tsDateModified")] public long DateModified { get; set; }
    [JsonPropertyName("_tsDateUpdated")] public long DateUpdated { get; set; }
    [JsonPropertyName("_nViewCount")] public int ViewCount { get; set; }
    [JsonPropertyName("_nLikeCount")] public int LikeCount { get; set; }
    [JsonPropertyName("_nDownloadCount")] public int DownloadCount { get; set; }
    [JsonPropertyName("_bIsObsolete")] public bool IsObsolete { get; set; }
    [JsonPropertyName("_aPreviewMedia")] public GbPreviewMedia? PreviewMedia { get; set; }
    [JsonPropertyName("_aSubmitter")] public GbSubmitter? Submitter { get; set; }
    [JsonPropertyName("_aCategory")] public GbCategory? Category { get; set; }
    [JsonPropertyName("_aSuperCategory")] public GbCategory? SuperCategory { get; set; }
    [JsonPropertyName("_aFiles")] public List<GbFile>? Files { get; set; }
    [JsonPropertyName("_aTags")] public List<JsonElement>? Tags { get; set; }
}

public sealed class GbFile
{
    [JsonPropertyName("_idRow")] public int Id { get; set; }
    [JsonPropertyName("_sFile")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("_nFilesize")] public long Size { get; set; }
    [JsonPropertyName("_tsDateAdded")] public long DateAdded { get; set; }
    [JsonPropertyName("_nDownloadCount")] public int DownloadCount { get; set; }
    [JsonPropertyName("_sDownloadUrl")] public string DownloadUrl { get; set; } = string.Empty;
    [JsonPropertyName("_sMd5Checksum")] public string? Md5 { get; set; }
    [JsonPropertyName("_sVersion")] public string? Version { get; set; }
    [JsonPropertyName("_sDescription")] public string? Description { get; set; }
    [JsonPropertyName("_bIsArchived")] public bool IsArchived { get; set; }
}

public sealed class GbPage
{
    public List<GbModRecord> Records { get; init; } = new();
    public int TotalCount { get; init; }
    public int PerPage { get; init; } = 15;
    public bool IsComplete { get; init; }
}

/// <summary>A flattened card/detail description of a GameBanana mod.</summary>
public sealed record ModSummary(
    int Id,
    string Name,
    string Author,
    string Category,
    string Version,
    DateTimeOffset Added,
    DateTimeOffset Updated,
    int ViewCount,
    string ProfileUrl,
    IReadOnlyList<GbImage> Images)
{
    public string? ThumbnailUrl => Images.Count == 0 ? null : Images[0].ThumbnailUrl;

    public string? FullImageUrl => Images.Count == 0 ? null : Images[0].FullUrl;

    public string AddedText => Added.UtcDateTime.ToString("d MMM yyyy");

    public string ViewsText => ViewCount == 1 ? "1 view" : $"{ViewCount:N0} views";

    public static ModSummary From(GbModRecord record)
    {
        var images = record.PreviewMedia?.Images ?? new List<GbImage>();
        var category = record.SubCategory?.Name ?? record.Category?.Name ?? record.RootCategory?.Name ?? "Mod";
        return new ModSummary(
            record.Id,
            record.Name,
            record.Submitter?.Name ?? "Unknown",
            category,
            record.Version ?? string.Empty,
            DateTimeOffset.FromUnixTimeSeconds(record.DateAdded),
            DateTimeOffset.FromUnixTimeSeconds(record.DateModified),
            record.ViewCount,
            record.ProfileUrl ?? $"https://gamebanana.com/mods/{record.Id}",
            images);
    }

    public static ModSummary From(GbModDetails details)
    {
        var images = details.PreviewMedia?.Images ?? new List<GbImage>();
        var category = details.Category?.Name ?? details.SuperCategory?.Name ?? "Mod";
        return new ModSummary(
            details.Id,
            details.Name,
            details.Submitter?.Name ?? "Unknown",
            category,
            details.Version ?? string.Empty,
            DateTimeOffset.FromUnixTimeSeconds(details.DateAdded),
            DateTimeOffset.FromUnixTimeSeconds(Math.Max(details.DateModified, details.DateUpdated)),
            details.ViewCount,
            details.ProfileUrl ?? $"https://gamebanana.com/mods/{details.Id}",
            images);
    }
}
