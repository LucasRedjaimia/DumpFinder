# Dump Finder

[![CI](https://github.com/LucasRedjaimia/DumpFinder/actions/workflows/ci.yml/badge.svg)](https://github.com/LucasRedjaimia/DumpFinder/actions/workflows/ci.yml)

Dump Finder helps you understand why a Windows PC crashes (blue screens, GPU timeouts, sudden reboots) and suggests fixes.

**[Download the latest version](https://github.com/LucasRedjaimia/DumpFinder/releases/latest)**: a single `DumpFinder.exe`, no installation needed (Windows 10 / 11, 64-bit).

**Mac (preview)**: [Dump Finder for Mac](mac/README.md) for Apple Silicon is in early development, with its own versions: [Mac releases](https://github.com/LucasRedjaimia/DumpFinder/releases?q=mac-v&expanded=true).

## Features

- **Crashes**: reads the Windows event logs and crash dumps (minidumps), identifies the driver or component involved and explains its common causes.
- **Hardware**: BIOS, memory (EXPO/XMP), GPU, SSDs (SMART), risky drivers, Windows version.
- **Stress test**: CPU (core by core), memory, GPU (OpenCL) and disk, with verified results and CPU temperature.
- **Fixes**: well-known Windows settings that stabilise a PC (TDR, MPO, NVMe SSD HMB…), all reversible.
- **Optimise**: official Windows settings, preinstalled apps, startup programs, temporary file cleanup.
- **AI assistant** (optional, with your own API key) and an MCP server.
- Interface in 14 languages: English, French, Spanish, German, Italian, Portuguese, Polish, Dutch, Turkish, Russian, Ukrainian, Simplified Chinese, Japanese and Korean.
- Keyboard and screen readers: every control has a name for Narrator / NVDA, **Ctrl+1…7** open the sections, **F5** runs a new scan, **Esc** closes the crash details; Windows contrast themes are followed.

At launch, the app checks GitHub for a newer version and shows what's new with a download link. Nothing is downloaded or installed automatically, and the check can be turned off in **Settings › Updates**.

## Build

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download) (the project targets .NET Framework 4.8).

```powershell
.\build.ps1
```

The executable is written to the repository root: `DumpFinder.exe`.

## Tests

The app has built-in self-tests. Each one writes a report and returns the number of failed checks as its exit code:

| Command | Checks |
|---|---|
| `DumpFinder.exe --parse-selftest out.txt` | crash dump parsers on truncated / corrupted files (no exception, no hang) |
| `DumpFinder.exe --detect-selftest out.txt` | detection rules (drivers, Windows versions, BIOS, NVMe), read-only |
| `DumpFinder.exe --update-selftest out.txt` | version parsing, release link safety, one request to GitHub |
| `DumpFinder.exe --optimize-selftest out.txt` | Optimise page; applies then undoes one harmless setting (Game Mode) |
| `DumpFinder.exe --stress-selftest out.txt [cpu,ram,disk,gpu]` | each stress engine, clean (must pass) and with an injected fault (must fail) |
| `DumpFinder.exe --ui-selftest out.txt` | opens the window: text contrast of both themes (WCAG AA), a screen-reader name on every control, keyboard shortcuts |

`dotnet run --project tools/I18nTool -- --check src src/Locales` checks the translations (missing keys, placeholders, plural forms).

GitHub Actions runs the build, the translation check and the self-tests (without the GPU stress test) on every pull request and on `main`.

## Translations

All interface texts live in `src/Locales/`, one JSON file per language (embedded in the exe at build time):

- `<code>.json`: interface texts, `"key": "text"`. `en.json` is the reference; a key missing in a language falls back to English.
- `<code>.engine.json`: texts built by the diagnostic engine, keyed by their exact English sentence (`en.engine.json` lists them all).

When translating:

- Keep placeholders such as `{0}`, `{1:HH:mm}` (they may move inside the sentence), emoji, `\r\n` line breaks and technical names (BSOD, TDR, NVMe, XMP…).
- Values written as `one: … | other: …` depend on a count. Use the plural forms of your language (`one`, `few`, `many`, `other` for Polish, Russian, Ukrainian; a single plain sentence for Chinese, Japanese, Korean).

To add a language: add a value to `AppLang` (`src/Core/Models.cs`, append only), a row to the `Codes`, `Cultures` and `Natives` tables in `src/Infrastructure/Localizer.cs`, and the two JSON files.

To check that longer texts fit, `DumpFinder.exe --screenshot <folder> --lang qps` captures every page in a pseudo-language (English with accents, about 40% longer, each text in `[ ]`): a missing `]` shows a cut text.

`tools/I18nTool` helps with this work: `--check` validates every language, `--split` / `--merge` cut a language file into chunks for translators and put them back together, `--add` merges new keys. Its usage is described at the top of `tools/I18nTool/Program.cs`.

Several translations (Polish, Dutch, Turkish, Russian, Ukrainian, Chinese, Japanese, Korean) were machine-assisted: corrections from native speakers are welcome.

## Detection data

`data/detection.json` holds the detection tables: NVIDIA driver releases with a known problem (and minimum releases per GPU family), DRAM-less NVMe models and the Windows build affected by the HMB issue, Windows end-of-servicing dates per build and edition, and the catalog of risky kernel drivers.

- It is **data only**: version numbers, model tokens, dates, driver file names, and Locales keys (`detection_checks.*`, `risky_drivers.*`) for every explanation, never free text. Every field is validated; a file that does not match the schema is ignored as a whole.
- The copy in the repository is embedded in the exe as the default. When "check for updates at launch" is on (Settings › Updates), the app also downloads `https://raw.githubusercontent.com/LucasRedjaimia/DumpFinder/main/data/detection.json` (10 s timeout, 256 KB max) and uses it only if it is valid and its `version` is higher; it is cached in `%LOCALAPPDATA%\DumpFinder\data`.
- It cannot widen what the app acts on: only drivers of the **embedded** catalog can be disabled or reverted. A driver added by a downloaded copy is shown as a warning under Hardware › Conflicts, without a fix button.

To update it: edit the file, increase `version` by one and set `updated` (`yyyy-MM-dd`), add any new explanation as a key in all `src/Locales/<code>.json` files (a key that does not exist in `en.json` makes the file invalid), run `--update-selftest`, then merge to `main`. Installed apps pick it up at their next launch; a new key or a new disableable driver needs a new release of the app.

## Publish a new version

The Windows and Mac apps have **separate version numbers** and tags, because they are at different stages:

| App | Tags | Release title | Script |
|---|---|---|---|
| Windows | `v6.2.0` | Dump Finder 6.2.0 | `release.ps1` (below) |
| Mac | `mac-v0.1.0` | Dump Finder for Mac 0.1.0 | `mac/release.sh` (see [mac/README.md](mac/README.md)) |

The Windows release is always the one marked **Latest** on GitHub: installed Windows apps check `/releases/latest` for updates. `release.ps1` passes `--latest` and `mac/release.sh` passes `--latest=false`; a Mac release must never be marked Latest by hand either.

Requirements: [GitHub CLI](https://cli.github.com/), signed in (`gh auth login`).

1. Run `.\release.ps1 -Version 6.1.0`. The first time, the script creates `release-notes\v6.1.0.md` and stops.
2. Write the new features and fixes in that file (Markdown).
3. Run `.\release.ps1 -Version 6.1.0` again. The script bumps the version number, builds, commits, tags, pushes and publishes the release with `DumpFinder.exe`.

Users see the new version the next time they open the app.

## License

[MIT](LICENSE).

## Third-party components

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0): CPU temperature, through the [PawnIO](https://pawnio.eu/) driver.
- [Anthropic SDK for C#](https://github.com/anthropics/anthropic-sdk-csharp) (MIT).
- [Costura.Fody](https://github.com/Fody/Costura) (MIT): bundles the dependencies into a single executable.
