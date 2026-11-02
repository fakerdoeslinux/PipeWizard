using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PipeWizard.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace PipeWizard.Services;

public sealed record InstallProgress(double Fraction, string Stage);

public sealed record InstallResult(bool Success, string Message, InstalledMod? Mod = null);

/// <summary>
/// Downloads a GameBanana file, unpacks it and turns it into an overlay root the
/// port understands. NSMBW mods come in three shapes and all three are handled:
/// Riivolution packs (riivolution/*.xml), archives with files/, and plain
/// disc-shaped folders (Stage/, Object/, Layout/, ...).
/// </summary>
public sealed class ModInstaller
{
    public const string ManifestFileName = "pipewizard-mod.json";

    private static readonly string[] DiscFolderNames =
    {
        "Stage", "Object", "Layout", "WorldMap", "Character", "Sound", "Message",
        "US", "EU", "Movie", "Effect", "Scene", "Env", "Demo", "OpeningTitle",
    };

    private static readonly string[] DiscFileExtensions =
    {
        ".arc", ".brres", ".brstm", ".breff", ".breft", ".bti", ".dol", ".rel", ".bin", ".bmd", ".bdl",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _modsDirectory;
    private readonly string _downloadCache;
    private readonly GameBananaClient _client;

    public ModInstaller(string modsDirectory, string downloadCache, GameBananaClient client)
    {
        _modsDirectory = modsDirectory;
        _downloadCache = downloadCache;
        _client = client;
    }

    public IReadOnlyList<InstalledMod> LoadInstalled()
    {
        var result = new List<InstalledMod>();
        if (!Directory.Exists(_modsDirectory))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(_modsDirectory))
        {
            if (Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var mod = JsonSerializer.Deserialize<InstalledMod>(File.ReadAllText(manifestPath), JsonOptions);
                if (mod is null || mod.ModId == 0)
                {
                    continue;
                }

                // The folder is the source of truth for the overlay path, in case the
                // PipeWizard data directory was moved to another machine or drive.
                mod.InstallDirectory = directory;
                mod.OverlayRoot = directory;
                result.Add(mod);
            }
            catch (Exception exception)
            {
                Log.Warn($"Ignoring unreadable mod manifest {manifestPath}: {exception.Message}");
            }
        }

        return result;
    }

    public Task<InstallResult> InstallAsync(
        ModSummary summary,
        GbFile file,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return InstallCoreAsync(summary, file, progress, cancellationToken);
    }

    private async Task<InstallResult> InstallCoreAsync(
        ModSummary summary,
        GbFile file,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_modsDirectory);
        Directory.CreateDirectory(_downloadCache);
        Directory.CreateDirectory(AppPaths.StagingDirectory);

        var folderName = $"{summary.Id}-{Slug(summary.Name)}";
        var target = Path.Combine(_modsDirectory, folderName);
        var staging = Path.Combine(AppPaths.StagingDirectory, Guid.NewGuid().ToString("N"));
        var backup = target + ".old-" + Guid.NewGuid().ToString("N")[..8];
        var archivePath = Path.Combine(
            _downloadCache,
            $"{file.Id}-{SanitizeFileName(file.FileName)}");

        try
        {
            progress?.Report(new InstallProgress(0.01, "Checking download"));
            if (!IsCachedDownloadValid(archivePath, file))
            {
                var downloadProgress = new Progress<double>(fraction =>
                    progress?.Report(new InstallProgress(0.02 + fraction * 0.78, "Downloading")));
                await _client.DownloadAsync(file, archivePath + ".part", downloadProgress, cancellationToken);
                File.Move(archivePath + ".part", archivePath, true);

                if (!string.IsNullOrWhiteSpace(file.Md5))
                {
                    var actual = await Md5Async(archivePath, cancellationToken);
                    if (!actual.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(archivePath);
                        return new InstallResult(false, "The download did not match GameBanana's checksum. Try again.");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress(0.82, "Extracting"));
            var extracted = Path.Combine(staging, "extracted");
            Directory.CreateDirectory(extracted);

            var entryCount = CountEntries(archivePath);
            var extractedCount = 0;
            await Task.Run(
                () => ExtractArchive(
                    archivePath,
                    extracted,
                    () =>
                    {
                        extractedCount++;
                        if (entryCount > 0)
                        {
                            progress?.Report(new InstallProgress(
                                0.82 + 0.13 * Math.Clamp((double)extractedCount / entryCount, 0, 1),
                                "Extracting"));
                        }
                    }),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress(0.96, "Finding game files"));
            var payload = ResolvePayload(extracted, out var notes);
            if (payload is null)
            {
                return new InstallResult(false, "The archive does not look like an NSMBW mod: no Riivolution XML, files/ folder or disc folders were found.");
            }

            progress?.Report(new InstallProgress(0.98, "Installing"));
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
            }

            try
            {
                Directory.Move(payload, target);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }

                throw;
            }

            if (Directory.Exists(backup))
            {
                TryDeleteDirectory(backup);
            }

            var kind = DetectKind(target);
            var size = DirectorySize(target);
            var installed = new InstalledMod
            {
                ModId = summary.Id,
                Name = summary.Name,
                Author = summary.Author,
                Category = summary.Category,
                Version = string.IsNullOrWhiteSpace(file.Version) ? summary.Version : file.Version,
                SourceUrl = summary.ProfileUrl,
                FileId = file.Id,
                FileName = file.FileName,
                Md5 = file.Md5,
                DownloadUrl = file.DownloadUrl,
                InstallDirectory = target,
                OverlayRoot = target,
                Kind = kind,
                InstalledAt = DateTimeOffset.Now,
                SizeBytes = size,
            };

            File.WriteAllText(
                Path.Combine(target, ManifestFileName),
                JsonSerializer.Serialize(installed, JsonOptions));

            progress?.Report(new InstallProgress(1, "Installed"));
            var note = notes.Count == 0 ? string.Empty : " " + string.Join(" ", notes);
            Log.Info($"Installed mod {summary.Id} ({summary.Name}) as {kind} at {target}.{note}");
            return new InstallResult(
                true,
                kind == "riivolution"
                    ? "Installed as a Riivolution pack." + note
                    : "Installed as a disc overlay." + note,
                installed);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Install cancelled.");
        }
        catch (Exception exception)
        {
            Log.Error($"Install of mod {summary.Id} failed.", exception);
            return new InstallResult(false, $"Install failed: {exception.Message}");
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(backup);
            try
            {
                if (File.Exists(archivePath + ".part"))
                {
                    File.Delete(archivePath + ".part");
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public bool Delete(InstalledMod mod)
    {
        try
        {
            TryDeleteDirectory(mod.InstallDirectory);
            return !Directory.Exists(mod.InstallDirectory);
        }
        catch (Exception exception)
        {
            Log.Error($"Could not delete {mod.InstallDirectory}.", exception);
            return false;
        }
    }

    private static bool IsCachedDownloadValid(string path, GbFile file)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var length = new FileInfo(path).Length;
            return length > 0 && (file.Size <= 0 || length == file.Size);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> Md5Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CountEntries(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            return archive.Entries.Count(entry => !entry.IsDirectory);
        }
        catch
        {
            return 0;
        }
    }

    private static void ExtractArchive(string archivePath, string destination, Action onEntry)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var destinationFull = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            var key = entry.Key.Replace('\\', '/').TrimStart('/');
            if (key.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment == "." || segment == "..") || segments.Length == 0)
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, Path.Combine(segments)));
            if (!targetPath.StartsWith(destinationFull, StringComparison.Ordinal))
            {
                Log.Warn($"Skipping archive entry outside the target folder: {entry.Key}");
                continue;
            }

            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.WriteToFile(targetPath, new ExtractionOptions { Overwrite = true });
            onEntry();
        }
    }

    /// <summary>
    /// Works out which folder should be handed to the port's [paths] overlay_roots.
    /// Returns null when nothing game-shaped was found.
    /// </summary>
    private static string? ResolvePayload(string extracted, out List<string> notes)
    {
        notes = new List<string>();
        var root = DescendWrapper(extracted);

        NormalizeRiivolutionFolder(root);

        var riivo = Path.Combine(root, "riivolution");
        if (Directory.Exists(riivo) && FindRiivolutionXmls(riivo, topLevelOnly: true).Count > 0)
        {
            return root;
        }

        // Some packs ship the XML next to the data instead of in riivolution/.
        // Copy those XMLs into riivolution/ so the port's Dolphin-style discovery
        // finds them. Externals that start with "/" (the NSMBW norm) still resolve
        // against this root.
        var strayXmls = FindRiivolutionXmls(root, topLevelOnly: false)
            .Where(xml => !IsInsideDirectory(xml, riivo))
            .ToList();
        if (strayXmls.Count > 0)
        {
            Directory.CreateDirectory(riivo);
            foreach (var xml in strayXmls)
            {
                var name = Path.GetFileName(xml);
                var target = Path.Combine(riivo, name);
                var suffix = 1;
                while (File.Exists(target))
                {
                    target = Path.Combine(riivo, $"{Path.GetFileNameWithoutExtension(name)}-{suffix++}.xml");
                }

                File.Copy(xml, target);
            }

            notes.Add("The Riivolution XML was moved into riivolution/.");
            return root;
        }

        var files = Path.Combine(root, "files");
        if (Directory.Exists(files))
        {
            if (Directory.Exists(Path.Combine(root, "sys")))
            {
                notes.Add("Only files/ is used; a mod cannot replace sys/.");
            }

            return files;
        }

        if (LooksLikeDiscOverlay(root))
        {
            return root;
        }

        return null;
    }

    private static string DescendWrapper(string extracted)
    {
        var current = extracted;
        for (var depth = 0; depth < 6; depth++)
        {
            var directories = Directory.GetDirectories(current)
                .Where(directory => !Path.GetFileName(directory).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var files = Directory.GetFiles(current)
                .Where(file => !IsJunkFile(file))
                .ToArray();

            if (directories.Length != 1)
            {
                return current;
            }

            // A lone folder next to a readme is a wrapper; a lone folder next to an
            // XML or a files/ + sys/ pair is the payload itself.
            var hasXml = files.Any(file => file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (hasXml ||
                Directory.Exists(Path.Combine(current, "files")) ||
                Directory.Exists(Path.Combine(current, "sys")))
            {
                return current;
            }

            current = directories[0];
        }

        return current;
    }

    private static void NormalizeRiivolutionFolder(string root)
    {
        foreach (var directory in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(directory).Equals("riivolution", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(directory).Equals("riivolution", StringComparison.Ordinal))
            {
                var target = Path.Combine(root, "riivolution");
                if (!Directory.Exists(target))
                {
                    Directory.Move(directory, target);
                }

                return;
            }
        }
    }

    private static List<string> FindRiivolutionXmls(string root, bool topLevelOnly)
    {
        if (!Directory.Exists(root))
        {
            return new List<string>();
        }

        var files = topLevelOnly
            ? Directory.GetFiles(root, "*.xml", SearchOption.TopDirectoryOnly)
            : Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories);

        var result = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length is <= 0 or > 4 * 1024 * 1024)
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (text.Contains("<wiidisc", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file);
                }
            }
            catch
            {
                // An unreadable XML is not a Riivolution pack we can use.
            }
        }

        return result;
    }

    private static bool LooksLikeDiscOverlay(string root)
    {
        foreach (var name in DiscFolderNames)
        {
            if (Directory.Exists(Path.Combine(root, name)))
            {
                return true;
            }
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(5000))
            {
                var extension = Path.GetExtension(file);
                if (DiscFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string DetectKind(string target)
    {
        var riivo = Path.Combine(target, "riivolution");
        return Directory.Exists(riivo) && FindRiivolutionXmls(riivo, topLevelOnly: true).Count > 0
            ? "riivolution"
            : "disc";
    }

    private static bool IsInsideDirectory(string file, string directory)
    {
        var fileFull = Path.GetFullPath(file);
        var directoryFull = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        return fileFull.StartsWith(directoryFull, StringComparison.Ordinal);
    }

    private static bool IsJunkFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("._", StringComparison.Ordinal);
    }

    private static long DirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        var previousDash = false;
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }

            if (builder.Length >= 48)
            {
                break;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "mod" : slug;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception exception)
        {
            Log.Warn($"Could not delete {directory}: {exception.Message}");
        }
    }
}
