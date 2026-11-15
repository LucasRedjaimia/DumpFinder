# Dump Finder

Dump Finder helps you understand why a Windows PC crashes (blue screens, GPU timeouts, sudden reboots) and suggests fixes.

**[Download the latest version](https://github.com/LucasRedjaimia/DumpFinder/releases/latest)**: a single `DumpFinder.exe`, no installation needed (Windows 10 / 11, 64-bit).

## Features

- **Crashes**: reads the Windows event logs and crash dumps (minidumps), identifies the driver or component involved and explains its common causes.
- **Hardware**: BIOS, memory (EXPO/XMP), GPU, SSDs (SMART), risky drivers, Windows version.
- **Stress test**: CPU (core by core), memory, GPU (OpenCL) and disk, with verified results and CPU temperature.
- **Fixes**: well-known Windows settings that stabilise a PC (TDR, MPO, NVMe SSD HMB…), all reversible.
- **Optimise**: official Windows settings, preinstalled apps, startup programs, temporary file cleanup.
- **AI assistant** (optional, with your own API key) and an MCP server.
- Interface in 14 languages: English, French, Spanish, German, Italian, Portuguese, Polish, Dutch, Turkish, Russian, Ukrainian, Simplified Chinese, Japanese and Korean.

At launch, the app checks GitHub for a newer version and shows what's new with a download link. Nothing is downloaded or installed automatically, and the check can be turned off in **Settings › Updates**.

## Build

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download) (the project targets .NET Framework 4.8).

```powershell
.\build.ps1
```

The executable is written to the repository root: `DumpFinder.exe`.

## Translations

All interface texts live in `src/Locales/`, one JSON file per language (embedded in the exe at build time):

- `<code>.json`: interface texts, `"key": "text"`. `en.json` is the reference; a key missing in a language falls back to English.
- `<code>.engine.json`: texts built by the diagnostic engine, keyed by their exact English sentence (`en.engine.json` lists them all).

When translating:

- Keep placeholders such as `{0}`, `{1:HH:mm}` (they may move inside the sentence), emoji, `\r\n` line breaks and technical names (BSOD, TDR, NVMe, XMP…).
- Values written as `one: … | other: …` depend on a count. Use the plural forms of your language (`one`, `few`, `many`, `other` for Polish, Russian, Ukrainian; a single plain sentence for Chinese, Japanese, Korean).

To add a language: add a value to `AppLang` (`src/Core/Models.cs`, append only), a row to the `Codes`, `Cultures` and `Natives` tables in `src/Infrastructure/Localizer.cs`, and the two JSON files.

Several translations (Polish, Dutch, Turkish, Russian, Ukrainian, Chinese, Japanese, Korean) were machine-assisted: corrections from native speakers are welcome.

## Publish a new version

Requirements: [GitHub CLI](https://cli.github.com/), signed in (`gh auth login`).

1. Run `.\release.ps1 -Version 6.1.0`. The first time, the script creates `release-notes\v6.1.0.md` and stops.
2. Write the new features and fixes in that file (Markdown).
3. Run `.\release.ps1 -Version 6.1.0` again. The script bumps the version number, builds, commits, tags, pushes and publishes the release with `DumpFinder.exe`.

Users see the new version the next time they open the app.

## Third-party components

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0): CPU temperature, through the [PawnIO](https://pawnio.eu/) driver.
- [Anthropic SDK for C#](https://github.com/anthropics/anthropic-sdk-csharp) (MIT).
- [Costura.Fody](https://github.com/Fody/Costura) (MIT): bundles the dependencies into a single executable.
