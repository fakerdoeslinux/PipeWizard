using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PipeWizard.Services;

internal static class RiivolutionConfigWriter
{
    private static readonly string[] DefaultRegions = ["E", "P", "J", "K"];

    public static void Prepare(
        string overlayRoot,
        string? previousOverlayRoot,
        ICollection<string> notes)
    {
        var riivolutionDirectory = Path.Combine(overlayRoot, "riivolution");
        if (!Directory.Exists(riivolutionDirectory))
        {
            return;
        }

        var configDirectory = Path.Combine(riivolutionDirectory, "config");
        PreserveExistingConfigs(
            previousOverlayRoot is null ? null : Path.Combine(previousOverlayRoot, "riivolution", "config"),
            configDirectory);

        var defaultsByGame = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var hasMemoryPatches = false;
        foreach (var xmlPath in Directory.GetFiles(riivolutionDirectory, "*.xml", SearchOption.TopDirectoryOnly))
        {
            XDocument document;
            try
            {
                var text = File.ReadAllText(xmlPath);
                hasMemoryPatches |= text.Contains("<memory", StringComparison.OrdinalIgnoreCase);
                document = XDocument.Parse(text);
            }
            catch (XmlException)
            {
                notes.Add($"Riivolution XML could not be parsed: {Path.GetFileName(xmlPath)}.");
                continue;
            }

            var root = document.Root;
            if (root?.Name.LocalName != "wiidisc")
            {
                continue;
            }

            var gameIds = GetGameIds(root).ToArray();
            if (gameIds.Length == 0)
            {
                continue;
            }

            foreach (var section in root.Elements("options").Elements("section"))
            {
                var sectionName = (string?)section.Attribute("name") ?? string.Empty;
                foreach (var option in section.Elements("option"))
                {
                    var optionName = (string?)option.Attribute("name") ?? string.Empty;
                    var optionId = (string?)option.Attribute("id");
                    if (string.IsNullOrEmpty(optionId))
                    {
                        optionId = sectionName + optionName;
                    }

                    var choices = option.Elements("choice").ToArray();
                    if (optionId.Length == 0 || choices.Length == 0)
                    {
                        continue;
                    }

                    var enabledChoice = Array.FindIndex(
                        choices,
                        choice =>
                        {
                            var name = (string?)choice.Attribute("name");
                            return name is not null &&
                                   (name.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("yes", StringComparison.OrdinalIgnoreCase));
                        });
                    var selectedChoice = optionName.Contains("optional", StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : enabledChoice >= 0 ? enabledChoice + 1 : 1;
                    foreach (var gameId in gameIds)
                    {
                        if (!defaultsByGame.TryGetValue(gameId, out var gameDefaults))
                        {
                            gameDefaults = new Dictionary<string, int>(StringComparer.Ordinal);
                            defaultsByGame.Add(gameId, gameDefaults);
                        }

                        gameDefaults.TryAdd(optionId, selectedChoice);
                    }
                }
            }
        }

        var wroteDefaults = false;
        var configWarnings = new List<string>();
        foreach (var (gameId, defaults) in defaultsByGame)
        {
            var configPath = Path.Combine(configDirectory, gameId + ".xml");
            if (MergeDefaults(configPath, defaults, out var warning))
            {
                wroteDefaults = true;
            }

            if (warning is not null)
            {
                configWarnings.Add(warning);
            }
        }

        if (wroteDefaults)
        {
            notes.Add("Initial Riivolution choices were selected; existing saved choices were preserved.");
        }

        foreach (var warning in configWarnings)
        {
            notes.Add(warning);
        }
        if (hasMemoryPatches)
        {
            notes.Add("This pack contains Riivolution code patches; the native port does not apply memory patches, so those features will not work.");
        }
    }

    private static IEnumerable<string> GetGameIds(XElement root)
    {
        foreach (var id in root.Elements("id"))
        {
            var game = (string?)id.Attribute("game");
            if (string.IsNullOrWhiteSpace(game))
            {
                continue;
            }

            var regions = id.Elements("region")
                .Select(region => (string?)region.Attribute("type"))
                .Where(region => !string.IsNullOrWhiteSpace(region))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (regions.Length == 0 && game.Length == 3)
            {
                regions = DefaultRegions;
            }

            if (game.Length == 3)
            {
                foreach (var region in regions)
                {
                    yield return game + region;
                }
            }
            else if (game.Length >= 4)
            {
                yield return game[..4];
            }
        }
    }

    private static void PreserveExistingConfigs(string? previousDirectory, string configDirectory)
    {
        if (previousDirectory is null || !Directory.Exists(previousDirectory))
        {
            return;
        }

        foreach (var previousConfig in Directory.GetFiles(previousDirectory, "*.xml"))
        {
            var destination = Path.Combine(configDirectory, Path.GetFileName(previousConfig));
            if (File.Exists(destination))
            {
                continue;
            }

            Directory.CreateDirectory(configDirectory);
            File.Copy(previousConfig, destination);
        }
    }

    private static bool MergeDefaults(
        string configPath,
        IReadOnlyDictionary<string, int> defaults,
        out string? warning)
    {
        warning = null;
        XDocument config;
        if (File.Exists(configPath))
        {
            try
            {
                config = XDocument.Load(configPath);
            }
            catch (XmlException)
            {
                warning = $"Saved Riivolution choices could not be parsed: {Path.GetFileName(configPath)}.";
                return false;
            }

            if (config.Root?.Name.LocalName != "riivolution" ||
                (string?)config.Root.Attribute("version") != "2")
            {
                warning = $"Saved Riivolution choices have an unsupported format: {Path.GetFileName(configPath)}.";
                return false;
            }
        }
        else
        {
            config = new XDocument(new XElement("riivolution", new XAttribute("version", "2")));
        }

        var root = config.Root!;
        var existing = root.Elements("option")
            .Select(option => (string?)option.Attribute("id"))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        var added = false;
        foreach (var (id, choice) in defaults)
        {
            if (!existing.Add(id))
            {
                continue;
            }

            root.Add(new XElement(
                "option",
                new XAttribute("id", id),
                new XAttribute("default", choice)));
            added = true;
        }

        if (!added)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        config.Save(configPath);
        return true;
    }
}
