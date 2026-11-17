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

- `src/DumpFinder.Mac.Core`: probes (`DiagnosticReports`, `SystemProbe`), rules (`DiagnosticsEngine`), text report, self-test. No UI dependency.
- `src/DumpFinder.Mac`: Avalonia window (Findings, Crashes, Extensions, Hardware), light and dark themes.

## Not done yet

Translations (reuse `src/Locales`), AI assistant and MCP server, history, stress tests (CPU / memory portable; GPU through Metal), temperature, notarization (needs an Apple Developer account).

Update check: it cannot use `/releases/latest` (always the Windows release). It will list the releases and take the newest `mac-v*` tag that has a `DumpFinder-mac-*.zip` asset.
