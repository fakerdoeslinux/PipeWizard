using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PipeWizard.Services;

public sealed class NativePortSetupService
{
    private const string RepositoryUrl = "https://github.com/fakerdoeslinux/NSMB-Wii-Native-Port.git";
    private static readonly HttpClient HttpClient = new();

    public async Task<string> InstallAndBuildAsync(
        string gameFolder,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException(
                "Automatic source builds are currently supported on Linux x64. " +
                "On other systems, follow the port's install guide and locate WiiCompiled in Settings.");
        }

        var normalizedGameFolder = PortService.NormalizeGameFolder(gameFolder);
        var validation = PortService.ValidateGameFolder(gameFolder);
        if (!validation.IsValid || normalizedGameFolder is null)
        {
            throw new InvalidOperationException(validation.Error ?? "Choose a valid extracted game folder.");
        }

        EnsureLinuxPrerequisites();
        Directory.CreateDirectory(AppPaths.NativePortWorkspace);

        progress.Report("Preparing the NSMBW native-port source...");
        await EnsureRepositoryAsync(progress, cancellationToken);

        var dotnet = await EnsureDotnetSdkAsync(progress, cancellationToken);
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_ROOT"] = GetDotnetRoot(dotnet),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["PATH"] = $"{Path.GetDirectoryName(dotnet)}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}",
        };

        var source = AppPaths.NativePortSourceDirectory;
        progress.Report("Downloading and preparing the pinned native compiler toolchain (this is a large download)...");
        await RunAsync(
            "bash",
            [Path.Combine(source, "Launcher", "prepare-portable-tools.sh"), "--arch", "x86_64"],
            source,
            environment,
            progress,
            cancellationToken);

        progress.Report("Preparing local game-code inputs from your extracted disc...");
        await RunAsync(
            "python3",
            [
                Path.Combine(source, "projects", "nsmbw", "tools", "unpack_game.py"),
                normalizedGameFolder,
            ],
            source,
            environment,
            progress,
            cancellationToken);
        await RunAsync(
            "python3",
            [Path.Combine(source, "projects", "nsmbw", "tools", "prelink_rels.py")],
            source,
            environment,
            progress,
            cancellationToken);

        progress.Report("Translating the game and compiling WiiCompiled. This can take a long time...");
        await RunAsync(
            "bash",
            [
                Path.Combine(source, "projects", "nsmbw", "tools", "build-linux.sh"),
                "--game",
                normalizedGameFolder,
            ],
            source,
            environment,
            progress,
            cancellationToken);

        var executable = Path.Combine(AppPaths.NativePortWorkspace, "nsmbw-local-build", "WiiCompiled");
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"The build completed without producing the expected executable: {executable}");
        }

        progress.Report("Native port built. Creating PipeWizard's Config.toml...");
        return executable;
    }

    private static void EnsureLinuxPrerequisites()
    {
        var missing = new[] { "git", "python3", "pkg-config", "curl", "tar", "sha256sum", "strip" }
            .Where(name => PortService.FindOnPath(name) is null)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Install these system tools and retry: {string.Join(", ", missing)}. " +
                "Fedora: sudo dnf install git python3 pkgconf-pkg-config curl tar xz coreutils binutils. " +
                "Debian/Ubuntu: sudo apt install git python3 pkg-config curl tar xz-utils coreutils binutils.");
        }

        var pkgConfig = PortService.FindOnPath("pkg-config")!;
        foreach (var package in new[] { "alsa", "vulkan", "x11", "xkbcommon", "wayland-client" })
        {
            if (RunExitCode(pkgConfig, ["--exists", package]) != 0)
            {
                throw new InvalidOperationException(
                    $"The native-port build needs the {package} development files. " +
                    "Fedora: sudo dnf install alsa-lib-devel vulkan-loader-devel libX11-devel " +
                    "libxkbcommon-devel wayland-devel. Debian/Ubuntu: sudo apt install libasound2-dev " +
                    "libvulkan-dev libx11-dev libxkbcommon-dev libwayland-dev.");
            }
        }
    }

    private static async Task EnsureRepositoryAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var source = AppPaths.NativePortSourceDirectory;
        if (Directory.Exists(source))
        {
            if (!Directory.Exists(Path.Combine(source, ".git")))
            {
                throw new InvalidOperationException(
                    $"The destination already exists and is not a Git checkout: {source}. " +
                    "Move it aside before retrying; PipeWizard will not overwrite it.");
            }

            var remote = await CaptureAsync(
                "git", ["-C", source, "remote", "get-url", "origin"], cancellationToken);
            var actualRemote = remote.Trim().TrimEnd('/');
            var expectedRemote = RepositoryUrl[..^".git".Length];
            if (!string.Equals(actualRemote, expectedRemote, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actualRemote, RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The existing checkout at {source} has a different origin. " +
                    "PipeWizard will not change or overwrite it.");
            }

            progress.Report("Reusing the existing native-port checkout without changing its files.");
            return;
        }

        progress.Report("Cloning the native-port source from GitHub...");
        await RunAsync(
            "git",
            ["clone", "--depth", "1", RepositoryUrl, source],
            AppPaths.NativePortWorkspace,
            null,
            progress,
            cancellationToken);
    }

    private static async Task<string> EnsureDotnetSdkAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var localInstall = Path.Combine(AppPaths.DataDirectory, "native-port-tools", "dotnet8");
        var localDotnet = Path.Combine(localInstall, "dotnet");
        if (HasDotnet8(localDotnet))
        {
            return localDotnet;
        }

        var systemDotnet = PortService.FindOnPath("dotnet");
        if (systemDotnet is not null && HasDotnet8(systemDotnet))
        {
            return systemDotnet;
        }

        progress.Report("Installing the .NET 8 SDK in PipeWizard's user data folder...");
        Directory.CreateDirectory(localInstall);
        var installer = Path.Combine(AppPaths.DataDirectory, "dotnet-install.sh");
        var script = await HttpClient.GetByteArrayAsync(
            "https://dot.net/v1/dotnet-install.sh", cancellationToken);
        await File.WriteAllBytesAsync(installer, script, cancellationToken);
        await RunAsync(
            "bash",
            [installer, "--channel", "8.0", "--install-dir", localInstall, "--no-path"],
            AppPaths.DataDirectory,
            null,
            progress,
            cancellationToken);

        if (!HasDotnet8(localDotnet))
        {
            throw new InvalidOperationException(
                "The .NET 8 SDK installer finished, but the SDK could not be verified.");
        }

        return localDotnet;
    }

    private static string GetDotnetRoot(string executable)
    {
        var target = File.ResolveLinkTarget(executable, returnFinalTarget: true);
        return Path.GetDirectoryName(Path.GetFullPath(target?.FullName ?? executable))!;
    }

    private static bool HasDotnet8(string executable)
    {
        if (!File.Exists(executable))
        {
            return false;
        }

        try
        {
            var output = CaptureAsync(executable, ["--list-sdks"], CancellationToken.None)
                .GetAwaiter().GetResult();
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.StartsWith("8.", StringComparison.Ordinal));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int RunExitCode(string executable, IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, arguments, Environment.CurrentDirectory, null),
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static async Task<string> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, arguments, Environment.CurrentDirectory, null),
        };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
        }

        return stdout;
    }

    private static async Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, arguments, workingDirectory, environment),
            EnableRaisingEvents = true,
        };

        var output = new Queue<string>();
        void ReportLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (output)
            {
                output.Enqueue(line);
                while (output.Count > 20)
                {
                    output.Dequeue();
                }
            }

            progress.Report(line);
        }

        process.OutputDataReceived += (_, eventArgs) => ReportLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => ReportLine(eventArgs.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string details;
            lock (output)
            {
                details = string.Join(Environment.NewLine, output);
            }

            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} exited with code {process.ExitCode}." +
                (details.Length == 0 ? string.Empty : $"{Environment.NewLine}{details}"));
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                info.Environment[pair.Key] = pair.Value;
            }
        }

        return info;
    }
}
