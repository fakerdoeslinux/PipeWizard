using System;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return RunSelfTest();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int RunSelfTest()
    {
        const string searchRecordJson =
            """{"_idRow":250256,"_sName":"Somari","_aTags":["Aesthetic: Pixelated","Animation: Default"]}""";
        const string modDetailsJson =
            """{"_idRow":250256,"_sName":"Somari","_aTags":[{"_sTitle":"Character","_sValue":"Mario"}]}""";
        var searchRecord = JsonSerializer.Deserialize<GbModRecord>(searchRecordJson);
        var modDetails = JsonSerializer.Deserialize<GbModDetails>(modDetailsJson);
        if (searchRecord?.Tags is not { Count: 2 } ||
            searchRecord.Tags[0].ValueKind != JsonValueKind.String ||
            modDetails?.Tags is not { Count: 1 } ||
            modDetails.Tags[0].ValueKind != JsonValueKind.Object)
        {
            Console.Error.WriteLine("PipeWizard GameBanana JSON self-test failed.");
            return 1;
        }

        var supported = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'1', 0, 2 };
        var wrongGame = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'2', 0, 1 };
        var unsupportedRevision = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'1', 0, 3 };

        if (RustNative.ValidateDiscHeader(supported, (nuint)supported.Length) != RustNative.Valid ||
            RustNative.ValidateDiscHeader(wrongGame, (nuint)wrongGame.Length) != RustNative.WrongGame ||
            RustNative.ValidateDiscHeader(unsupportedRevision, (nuint)unsupportedRevision.Length) !=
            RustNative.UnsupportedRevision)
        {
            Console.Error.WriteLine("PipeWizard self-test failed.");
            return 1;
        }

        var temporaryMod = Path.Combine(
            Path.GetTempPath(), "pipewizard-riivolution-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var riivolution = Path.Combine(temporaryMod, "riivolution");
            Directory.CreateDirectory(riivolution);
            File.WriteAllText(
                Path.Combine(riivolution, "Skin.xml"),
                """
                <wiidisc version="1">
                  <id game="SMN" />
                  <options><section name="Skin"><option name="Mario">
                    <choice name="Disabled" /><choice name="Enabled"><patch id="skin" /></choice>
                  </option><option name="Optional Animations (optional)">
                    <choice name="Enabled"><patch id="animation" /></choice>
                  </option></section></options>
                  <patch id="skin"><folder external="/Skin" disc="/" recursive="true" /></patch>
                  <patch id="code"><memory offset="0x80000000" value="00" /></patch>
                </wiidisc>
                """);

            var notes = new System.Collections.Generic.List<string>();
            RiivolutionConfigWriter.Prepare(temporaryMod, null, notes);
            var generatedConfig = Path.Combine(riivolution, "config", "SMNE.xml");
            var generatedChoice = XDocument.Load(generatedConfig).Root?
                .Elements("option")
                .SingleOrDefault(option => (string?)option.Attribute("id") == "SkinMario");
            var generatedOptionalChoice = XDocument.Load(generatedConfig).Root?
                .Elements("option")
                .SingleOrDefault(option => (string?)option.Attribute("id") == "SkinOptional Animations (optional)");
            if ((string?)generatedChoice?.Attribute("default") != "2" ||
                (string?)generatedOptionalChoice?.Attribute("default") != "0" ||
                !File.Exists(Path.Combine(riivolution, "config", "SMNP.xml")) ||
                !notes.Exists(note => note.Contains("does not apply memory patches", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("PipeWizard Riivolution option self-test failed.");
                return 1;
            }

            File.WriteAllText(
                generatedConfig,
                """<riivolution version="2"><option id="SkinMario" default="0" /></riivolution>""");
            RiivolutionConfigWriter.Prepare(temporaryMod, null, notes);
            generatedChoice = XDocument.Load(generatedConfig).Root?
                .Elements("option")
                .SingleOrDefault(option => (string?)option.Attribute("id") == "SkinMario");
            if ((string?)generatedChoice?.Attribute("default") != "0")
            {
                Console.Error.WriteLine("PipeWizard did not preserve a saved Riivolution choice.");
                return 1;
            }
        }
        finally
        {
            if (Directory.Exists(temporaryMod))
            {
                Directory.Delete(temporaryMod, recursive: true);
            }
        }

        var temporaryGame = Path.Combine(
            Path.GetTempPath(), "pipewizard-self-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dataFolder = Path.Combine(temporaryGame, "DATA");
            Directory.CreateDirectory(Path.Combine(dataFolder, "files"));
            Directory.CreateDirectory(Path.Combine(dataFolder, "sys"));
            File.WriteAllBytes(Path.Combine(dataFolder, "sys", "boot.bin"), new byte[8]);
            File.WriteAllBytes(Path.Combine(dataFolder, "sys", "main.dol"), Array.Empty<byte>());
            if (!string.Equals(
                    PortService.NormalizeGameFolder(temporaryGame),
                    Path.GetFullPath(dataFolder),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Console.Error.WriteLine("PipeWizard game-folder normalization self-test failed.");
                return 1;
            }
        }
        finally
        {
            if (Directory.Exists(temporaryGame))
            {
                Directory.Delete(temporaryGame, recursive: true);
            }
        }

        Console.WriteLine("PipeWizard self-test passed.");
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();

        // UsePlatformDetect gives you the session's native backend (Wayland on a
        // Wayland desktop, X11 otherwise). Set PIPEWIZARD_FORCE_X11=1 to pin the
        // X11 backend, which is useful for debugging and screen capture.
        builder = Environment.GetEnvironmentVariable("PIPEWIZARD_FORCE_X11") == "1"
            ? builder.UseX11().UseSkia().UseHarfBuzz()
            : builder.UsePlatformDetect();

        return builder
            .WithInterFont()
            .LogToTrace();
    }
}
