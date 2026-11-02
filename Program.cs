using System;
using Avalonia;
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
        var supported = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'1', 0, 2 };
        var wrongGame = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'2', 0, 1 };
        var unsupportedRevision = new byte[] { (byte)'S', (byte)'M', (byte)'N', (byte)'E', (byte)'0', (byte)'1', 0, 3 };

        if (RustNative.ValidateDiscHeader(supported, (nuint)supported.Length) != RustNative.Valid ||
            RustNative.ValidateDiscHeader(wrongGame, (nuint)wrongGame.Length) != RustNative.WrongGame ||
            RustNative.ValidateDiscHeader(unsupportedRevision, (nuint)unsupportedRevision.Length) !=
            RustNative.UnsupportedRevision)
        {
            Console.Error.WriteLine("PipeWizard native self-test failed.");
            return 1;
        }

        Console.WriteLine("PipeWizard native self-test passed.");
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();

        // UsePlatformDetect gives you the session's native backend (Wayland on a
        // Wayland desktop, X11 otherwise). Set PIPEWIZARD_FORCE_X11=1 to pin the
        // X11 backend, which is useful for debugging and screen capture.
        builder = Environment.GetEnvironmentVariable("PIPEWIZARD_FORCE_X11") == "1"
            ? builder.UseX11()
            : builder.UsePlatformDetect();

        return builder
            .WithInterFont()
            .LogToTrace();
    }
}
