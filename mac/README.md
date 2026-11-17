# Dump Finder for Mac (prototype)

Apple Silicon version of Dump Finder: it explains why a Mac crashes or restarts and points at the usual culprits. Read-only for now: it changes nothing on the Mac.

## What it reads

| Source | What it tells |
|---|---|
| `/Library/Logs/DiagnosticReports/*.panic` (and `.ips` with `bug_type` 210) | Kernel panics: message, panicked process, **non-Apple extensions in the backtrace** |
| `sysctl kern.shutdownreason`, `kern.bootreason`, `kern.boottime` | How the previous session ended (watchdog, forced restart…). Apple does not document these codes: the reading is approximate |
| `~/Library/Logs/DiagnosticReports/*.ips`, `/Library/Logs/DiagnosticReports/*.ips` | App crashes (`bug_type` 309) and low-memory kills (Jetsam, 298) |
| `kmutil showloaded`, `/Library/Extensions` | Third-party kernel extensions, loaded or left behind (kexts that macOS installs itself are skipped with `pkgutil`) |
| `systemextensionsctl list` | System extensions (VPN, security, network filters…) |
| `system_profiler -json -detailLevel mini` | Model, chip, macOS, SSD SMART status, startup disk free space, battery health |
| `csrutil status`, `spctl --status` | SIP and Gatekeeper |

The rules and their texts live in [`data/mac-detection.json`](data/mac-detection.json) (data only, like `data/detection.json` on Windows): panic patterns, shutdown codes, kext vendor categories, thresholds, supported macOS versions.

## Build and run

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), an Apple Silicon Mac.

```bash
dotnet build mac/src/DumpFinder.Mac
dotnet run --project mac/src/DumpFinder.Mac
```

Command-line modes:

| Command | Does |
|---|---|
| `DumpFinder --report` | Scans and prints a text report. Exit code: number of critical findings |
| `DumpFinder --selftest` | Parsers and rules on synthetic inputs (no access to the Mac). Exit code: failed checks |
| `DumpFinder --screenshot <folder>` | Scans, saves every tab as a PNG, quits (the PNGs show this Mac's data) |

## Package and release

The Mac app has its own version (`<Version>` in `src/DumpFinder.Mac/DumpFinder.Mac.csproj`) and its own tags, `mac-v<version>`, independent of the Windows exe (`v6.x.x`).

```bash
mac/build.sh
```
Runs the self-test, then writes `mac/out/Dump Finder.app` (self-contained: no .NET needed on the user's Mac) and `mac/out/DumpFinder-mac-<version>.zip`. It is signed ad hoc, so a downloaded copy needs right-click › Open the first time. With an Apple Developer account, set `DEVELOPER_ID="Developer ID Application: Name (TEAMID)"` to sign it properly, plus `NOTARY_PROFILE=<profile>` (created with `xcrun notarytool store-credentials`) to notarize it.

```bash
mac/release.sh 0.2.0
```
From `main`. The first run creates `mac/release-notes/v0.2.0.md` and stops; fill it in, run it again. The script bumps the version, builds, commits `mac/`, tags `mac-v0.2.0`, pushes and publishes the GitHub release with the zip. Mac releases are never marked **Latest**: that flag stays on the Windows release, which installed Windows apps check for updates.

## Layout

- `src/DumpFinder.Mac.Core`: probes (`DiagnosticReports`, `SystemProbe`), rules (`DiagnosticsEngine`), crash timeline and score (`CrashTimeline`), texts (`Loc`), settings and history (`AppStore`), text report, self-test. No UI dependency.
- `src/DumpFinder.Mac`: Avalonia window with the same shell as Windows (title bar, sidebar with the 7 sections and ⌘1…7, canvas, status line, toasts), Home, Crashes, Hardware, Settings.
- `Locales/`: Mac texts (`mac.*` keys) and the Mac wording of shared keys that name Windows or the PC.

## Same app as on Windows

The two apps have their own interface (WPF on Windows, Avalonia here) but look and behave the same, and share what does not depend on the system:

| Shared | How |
|---|---|
| Design | `src/DumpFinder.Mac/Themes/Tokens.axaml` has the same colour tokens as `src/UI/ThemeManager.cs` (light and dark), `Controls.axaml` the same sizes (cards 14, pills 10, buttons 8, type scale 11–28). Icons: Fluent UI System Icons, the SVG family of Segoe Fluent Icons. Change both sides together. |
| Texts | The very same `src/Locales/*.json` files are embedded (14 languages, same plural rules). `mac/Locales/<code>.json` adds Mac texts and wins over a shared key. Lookup: Mac file of the language, shared file of the language, Mac English, shared English. |
| Logic | `src/Core/Stability.cs` (score, levels, 30-day timeline) is compiled from the Windows source file (`SharedWindowsSource` in the Core project). A shared file must stay free of Windows APIs: the Mac CI job fails otherwise. Mac crashes map onto the Windows categories: kernel panic = blue screen, watchdog / forced restart = unexpected reboot, Jetsam = memory exhaustion. |
| Data | Settings and history in `~/Library/Application Support/DumpFinder` (`settings.json`, `history/scans.json`, `history/seen_crashes.json`), same fields as `%LOCALAPPDATA%\DumpFinder`. |

`DumpFinder --screenshot <folder> [--theme light|dark] [--lang xx] [--collapsed]` captures every section, like the Windows capture harness.

## Not done yet

Next steps, in order: Crashes (filters, detail panel, Mac known issues, reports), Hardware sub-tabs + Tools + update check + introduction, AI assistant and MCP server, Sentinel (menu bar, notifications, login item), Fixes and Optimize (journal and undo), stability test (CPU / memory / disk, GPU through Metal). Mac check texts (`DiagnosticsEngine`) are English only for now, and the `mac.*` keys exist in English and French (other languages fall back to English). Notarization needs an Apple Developer account.

Update check: it cannot use `/releases/latest` (always the Windows release). It will list the releases and take the newest `mac-v*` tag that has a `DumpFinder-mac-*.zip` asset.
