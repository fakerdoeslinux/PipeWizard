# PipeWizard

PipeWizard is a desktop launcher and mod manager for the NSMBW native PC port. Its mod browser and downloads use the GameBanana API for [New Super Mario Bros. Wii (game 6247)](https://gamebanana.com/games/6247).

## What it does

- Finds and launches the `WiiCompiled` executable.
- Validates the selected extracted disc as `SMNE01`, revision 1 or 2.
- Browses, searches, and downloads mods from GameBanana game 6247.
- Installs Riivolution packs and disc overlays into PipeWizard's own data directory.
- Enables, disables, and orders overlays through the port's `Config.toml`.
- Stores launcher settings and downloads outside the game directory.
- Features the GameBanana WIP [Mario's Final Adventure Wii](https://gamebanana.com/wips/65286); WIPs are linked to their author page and are not treated as installable mods.

PipeWizard does not include the game, game data, the native port executable, or downloaded mods. You must provide your own compatible port build and legally obtained extracted disc data. GameBanana remains the source of mod listings and downloads; PipeWizard does not mirror mod files.

## First-run native port setup

On first launch, if PipeWizard cannot find `WiiCompiled`, it asks you to choose your own extracted SMNE01 disc folder, then automatically clones [NSMB-Wii-Native-Port](https://github.com/fakerdoeslinux/NSMB-Wii-Native-Port), installs the .NET 8 SDK in its user data directory if needed, downloads the port's pinned compiler toolchain, builds the executable, and writes `Config.toml`. Cancel the folder picker to configure an existing port manually; Home also offers a retry button. Game data is never downloaded. The source checkout and generated build are kept under `Documents/PipeWizard/NSMB-Wii-Native-Port`.

Automatic source builds currently support Linux x64. The port's system development dependencies must already be installed; on Fedora these include `alsa-lib-devel`, `vulkan-loader-devel`, `libX11-devel`, `libxkbcommon-devel`, and `wayland-devel`. The compiler toolchain download is about 2 GB and the first build can take a long time. Windows/macOS users can build the port using its [install guide](https://github.com/fakerdoeslinux/NSMB-Wii-Native-Port/blob/main/INSTALL.md), then locate `WiiCompiled` in PipeWizard.

## Run from source

Requirements: .NET 10 SDK, Rust stable with Cargo, and a desktop environment supported by Avalonia.

```sh
dotnet run --project PipeWizard.csproj
```

The app's build automatically compiles its Rust native library for the current target. The Rust component validates the disc header; the UI, GameBanana integration, installer, configuration editor, and launcher are implemented in C#.

Run the bundled native interop check with:

```sh
dotnet run --project PipeWizard.csproj -- --self-test
```

Supported Rust/.NET release targets are `linux-x64`, `win-x64`, `osx-x64`, and `osx-arm64`. Use a matching host toolchain for desktop releases.
Linux x64 releases include a `.rpm` package and an `.AppImage`, as well as the portable `.tar.gz` bundle.

On Fedora, install the RPM with `sudo dnf install ./PipeWizard-linux-x64.rpm`. For the AppImage, make it executable with `chmod +x PipeWizard-linux-x64.AppImage`, then run `./PipeWizard-linux-x64.AppImage`.

## Data locations

PipeWizard keeps its own settings, logs, downloads, image cache, and installed mods in the platform's per-user application data directory. It does not write mod files into the game installation. Settings for the port itself are saved to its `Config.toml`.

## GameBanana and WheelWizard

PipeWizard is an independent project and is not affiliated with Nintendo, GameBanana, or Team Wheel Wizard. WheelWizard is a design reference; PipeWizard does not include WheelWizard source code. Mod authors retain rights to their submissions, and downloaded mods remain subject to their authors' terms.

## License

PipeWizard is licensed under GPL-3.0-only. See [LICENSE](LICENSE). Third-party libraries retain their own licenses and notices.
