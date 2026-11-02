using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PipeWizard.Models;

namespace PipeWizard.Services;

public sealed class GameBananaException : Exception
{
    public GameBananaException(string message) : base(message)
    {
    }
}

/// <summary>
/// Read-only client for the GameBanana API v11, pinned to the NSMBW game row (6247).
/// All mod browsing in PipeWizard goes through here; no other mod source is used.
/// </summary>
public sealed class GameBananaClient
{
    public const int GameId = 6247;
    public const string GameName = "New Super Mario Bros. Wii";
    public const string GameUrl = "https://gamebanana.com/games/6247";

    private const string ApiRoot = "https://gamebanana.com/apiv11/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, GbPage> _pageCache = new();
    private readonly ConcurrentDictionary<int, GbModDetails> _detailCache = new();
    private readonly ConcurrentDictionary<int, List<GbFile>> _fileCache = new();
    private readonly ConcurrentDictionary<string, Bitmap?> _imageCache = new();

    public GameBananaClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiRoot),
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PipeWizard/1.0 (+https://github.com/fakerdoeslinux/PipeWizard)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<GbPage> GetModsAsync(int page, string sort, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var key = $"subfeed:{sort}:{page}";
        if (!forceRefresh && _pageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var url = $"Game/{GameId}/Subfeed?_nPage={page}&_sSort={Uri.EscapeDataString(sort)}&_csvModelInclusions=Mod";
        var response = await GetAsync<GbFeedResponse>(url, cancellationToken);
        var result = new GbPage
        {
            Records = response.Records ?? new List<GbModRecord>(),
            TotalCount = response.Metadata?.RecordCount ?? 0,
            PerPage = response.Metadata?.PerPage is > 0 ? response.Metadata.PerPage : 15,
            IsComplete = response.Metadata?.IsComplete ?? false,
        };

        _pageCache[key] = result;
        return result;
    }

    public async Task<GbPage> SearchAsync(string query, int page, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetModsAsync(page, "default", forceRefresh, cancellationToken);
        }

        page = Math.Max(1, page);
        var normalized = query.Trim();
        var key = $"search:{normalized.ToLowerInvariant()}:{page}";
        if (!forceRefresh && _pageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var url = "Util/Search/Results?_sSearchString=" + Uri.EscapeDataString(normalized) +
                  $"&_sModelName=Mod&_idGameRow={GameId}&_nPage={page}";
        var response = await GetAsync<GbFeedResponse>(url, cancellationToken);
        var result = new GbPage
        {
            Records = response.Records ?? new List<GbModRecord>(),
            TotalCount = response.Metadata?.RecordCount ?? 0,
            PerPage = response.Metadata?.PerPage is > 0 ? response.Metadata.PerPage : 15,
            IsComplete = response.Metadata?.IsComplete ?? false,
        };

        _pageCache[key] = result;
        return result;
    }

    public async Task<GbModDetails> GetModDetailsAsync(int modId, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _detailCache.TryGetValue(modId, out var cached))
        {
            return cached;
        }

        var details = await GetAsync<GbModDetails>($"Mod/{modId}/ProfilePage", cancellationToken);
        if (details.Files is null || details.Files.Count == 0)
        {
            details.Files = await GetFilesAsync(modId, forceRefresh, cancellationToken);
        }

        _detailCache[modId] = details;
        return details;
    }

    public async Task<List<GbFile>> GetFilesAsync(int modId, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _fileCache.TryGetValue(modId, out var cached))
        {
            return cached;
        }

        var files = await GetAsync<List<GbFile>>($"Mod/{modId}/Files", cancellationToken) ?? new List<GbFile>();
        _fileCache[modId] = files;
        return files;
    }

    public async Task DownloadAsync(
        GbFile file,
        string destination,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? file.Size;
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total > 0)
            {
                progress?.Report(Math.Clamp((double)received / total, 0, 1));
            }
        }

        progress?.Report(1);
        Log.Info($"Downloaded {file.FileName} ({received} bytes)");
    }

    public async Task<Bitmap?> GetImageAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (_imageCache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
            var cachePath = Path.Combine(AppPaths.ImageCacheDirectory, hash + ".img");
            byte[] bytes;

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            }
            else
            {
                using var response = await _http.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                try
                {
                    Directory.CreateDirectory(AppPaths.ImageCacheDirectory);
                    var temporary = cachePath + ".tmp";
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                    File.Move(temporary, cachePath, true);
                }
                catch (Exception exception)
                {
                    Log.Warn($"Could not cache image: {exception.Message}");
                }
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _imageCache[url] = bitmap;
            return bitmap;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn($"Could not load image {url}: {exception.Message}");
            _imageCache[url] = null;
            return null;
        }
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GameBananaException(
                $"GameBanana returned {(int)response.StatusCode} {response.ReasonPhrase} for {relativeUrl}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("_sErrorCode", out var errorCode))
        {
            var detail = document.RootElement.TryGetProperty("_aErrorData", out var errorData)
                ? errorData.ToString()
                : string.Empty;
            throw new GameBananaException($"GameBanana error {errorCode.GetString()}: {detail}");
        }

        return document.RootElement.Deserialize<T>(JsonOptions)
               ?? throw new GameBananaException("GameBanana returned an empty response.");
    }

    private sealed class GbFeedResponse
    {
        [JsonPropertyName("_aMetadata")] public GbMetadata? Metadata { get; set; }
        [JsonPropertyName("_aRecords")] public List<GbModRecord>? Records { get; set; }
    }

    private sealed class GbMetadata
    {
        [JsonPropertyName("_nRecordCount")] public int RecordCount { get; set; }
        [JsonPropertyName("_nPerpage")] public int PerPage { get; set; }
        [JsonPropertyName("_bIsComplete")] public bool IsComplete { get; set; }
    }
}
