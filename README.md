# PipeWizard

PipeWizard is a desktop launcher and mod manager for the NSMBW native PC port. Its mod browser and downloads use the GameBanana API for [New Super Mario Bros. Wii (game 6247)](https://gamebanana.com/games/6247).

## What it does

- Finds and launches the `WiiCompiled` executable.
- Validates the selected extracted disc as `SMNE01`, revision 1 or 2.
- Browses, searches, and downloads mods from GameBanana game 6247.
- Installs Riivolution packs and disc overlays into PipeWizard's own data directory.
- Enables, disables, and orders overlays through the port's `Config.toml`.
- Stores launcher settings and downloads outside the game directory.

PipeWizard does not include the game, game data, the native port executable, or downloaded mods. You must provide your own compatible port build and legally obtained extracted disc data. GameBanana remains the source of mod listings and downloads; PipeWizard does not mirror mod files.

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

## Data locations

PipeWizard keeps its own settings, logs, downloads, image cache, and installed mods in the platform's per-user application data directory. It does not write mod files into the game installation. Settings for the port itself are saved to its `Config.toml`.

## GameBanana and WheelWizard

PipeWizard is an independent project and is not affiliated with Nintendo, GameBanana, or Team Wheel Wizard. WheelWizard is a design reference; PipeWizard does not include WheelWizard source code. Mod authors retain rights to their submissions, and downloaded mods remain subject to their authors' terms.

## License

PipeWizard is licensed under GPL-3.0-only. See [LICENSE](LICENSE). Third-party libraries retain their own licenses and notices.
