# Dump Finder for Mac (prototype)

Apple Silicon version of Dump Finder: it explains why a Mac crashes or restarts and points at the usual culprits. Read-only for now: it changes nothing on the Mac. Network access: the update check (GitHub releases API, at launch, can be turned off in Settings) and, only when you click "Ask the AI", the AI provider you chose (Google Gemini, Anthropic Claude, or Ollama on this Mac). What goes to the AI goes through `Privacy.Redact` first.

## What it reads

| Source | What it tells |
|---|---|
| `/Library/Logs/DiagnosticReports/*.panic` (and `.ips` with `bug_type` 210) | Kernel panics: message, panicked process, **non-Apple extensions in the backtrace** |
| `sysctl kern.shutdownreason`, `kern.bootreason`, `kern.boottime` | How the previous session ended (watchdog, forced restart…). Apple does not document these codes: the reading is approximate |
| `~/Library/Logs/DiagnosticReports/*.ips`, `/Library/Logs/DiagnosticReports/*.ips` | App crashes (`bug_type` 309) and low-memory kills (Jetsam, 298) |
| `kmutil showloaded`, `/Library/Extensions` | Third-party kernel extensions, loaded or left behind (kexts that macOS installs itself are skipped with `pkgutil`) |
| `systemextensionsctl list` | System extensions (VPN, security, network filters…) |
| `system_profiler -json -detailLevel mini` | Model, chip, GPU and Metal version, memory type, macOS, SSD SMART status, startup disk free space, battery health, screens, USB and Thunderbolt devices (display serial numbers are never read into the app) |
| `sysctl hw.perflevel*`, `vm.swapusage`, `memory_pressure -Q`, `pmset -g therm` | Performance / efficiency cores, swap, free memory, thermal throttling |
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
| `DumpFinder --mcp` | MCP server for AI agents (JSON-RPC over stdio, headless, English) |
| `DumpFinder --tray` | Login start of the Sentinel: no window, menu bar icon, a notification if a severe crash happened meanwhile (quits at once when the Sentinel is off) |
| `DumpFinder --notify-test` | Posts a test notification (run it from the .app: notifications need the bundle) |
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

- `src/DumpFinder.Mac.Core`: probes (`DiagnosticReports`, `SystemProbe`), rules (`DiagnosticsEngine`), Hardware cards and extensions (`HardwareChecks`, levels taken from the rules), crash timeline and score (`CrashTimeline`), crash detail (`CrashDetails`), known issues (`KnowledgeBase`), exports (`ReportBuilder`: HTML, JSON, Markdown, text), `Privacy`, update check (`UpdateChecker`), AI assistant (`AiAssistant`, `ClaudeAdvisor`, key in the Keychain through `SecretStore`), MCP server (`McpServer`), texts (`Loc`), settings and history (`AppStore`), self-test. No UI dependency.
- `src/DumpFinder.Mac`: Avalonia window with the same shell as Windows (title bar, sidebar with the 7 sections and ⌘1…7, canvas, status line, toasts), Home, Crashes (History with filters and detail panel, Common crashes), Hardware (Hardware · Conflicts · Extensions), Tools (macOS tools · Report & export · History), Settings (language, theme, updates, introduction, AI assistant, MCP integrations), "Ask the AI" in the crash panel and the AI check-up from Home, the 3-step introduction and the new-version card.
- `Locales/`: Mac texts (`mac.*` keys, 14 languages) and the Mac wording of shared keys that name Windows or the PC. Every check, explanation and article is a key: `--selftest` fails when a language misses a key or a placeholder.

## Same app as on Windows

The two apps have their own interface (WPF on Windows, Avalonia here) but look and behave the same, and share what does not depend on the system:

| Shared | How |
|---|---|
| Design | `src/DumpFinder.Mac/Themes/Tokens.axaml` has the same colour tokens as `src/UI/ThemeManager.cs` (light and dark), `Controls.axaml` the same sizes (cards 14, pills 10, buttons 8, type scale 11–28). Icons: Fluent UI System Icons, the SVG family of Segoe Fluent Icons. Change both sides together. |
| Texts | The very same `src/Locales/*.json` files are embedded (14 languages, same plural rules). `mac/Locales/<code>.json` adds Mac texts and wins over a shared key. Lookup: Mac file of the language, shared file of the language, Mac English, shared English. |
| Logic | `src/Core/Stability.cs` (score, levels, 30-day timeline) is compiled from the Windows source file (`SharedWindowsSource` in the Core project). A shared file must stay free of Windows APIs: the Mac CI job fails otherwise. Mac crashes map onto the Windows categories: kernel panic = blue screen, watchdog / forced restart = unexpected reboot, Jetsam = memory exhaustion. |
| Data | Settings and history in `~/Library/Application Support/DumpFinder` (`settings.json`, `history/scans.json`, `history/seen_crashes.json`), same fields as `%LOCALAPPDATA%\DumpFinder`. |

`DumpFinder --screenshot <folder> [--theme light|dark] [--lang xx] [--collapsed]` captures every section and sub-tab, the introduction and the new-version card (with a sample release), like the Windows capture harness; it never goes online. `DumpFinder --report [--lang xx] [--format text|html|json|md]` prints a report. Every export goes through `Privacy.Redact` (account name, computer name, web paths).

## Not done yet

Next steps, in order: Fixes and Optimize (journal and undo; Tools › History shows an empty journal until then), stability test (CPU / memory / disk, GPU through Metal; the Hardware pill comes with it). Notarization needs an Apple Developer account.

Update check: it cannot use `/releases/latest` (always the Windows release). It lists the releases, keeps the newest published `mac-v*` one and opens its `DumpFinder-mac-*.zip` in the browser: nothing is downloaded or installed by the app. Unlike Windows, it does not refresh the detection data (that file is embedded on the Mac).

Font: the app uses Helvetica Neue, not SF. SF is a variable font and Avalonia's renderer drops the accent of composite capitals (É, Ü…) at SemiBold and Bold weights.

## AI assistant and MCP

Same as Windows: Settings › AI assistant picks Google Gemini (newest "flash" model), Anthropic Claude (official Anthropic SDK, Claude Opus 5.5 by default, effort medium, server-side refusal fallback) or Ollama on this Mac; "Ask the AI" streams the answer into the crash panel, a second click stops it. The key is stored in the login Keychain through `/usr/bin/security` (written on stdin, never as an argument), not in `settings.json`.

`DumpFinder --mcp` is the Model Context Protocol server (same protocol layer as Windows). Its tools read this Mac: `mac_scan_system`, `mac_check_hardware_and_conflicts`, `mac_analyze_panic_report`, `mac_get_crash_detail`, `mac_export_report`; every answer is redacted. The Windows fix tools (`list_fixes`, `apply_fix` with on-screen approval) come with the Mac fixes. Settings › Integrations adds it to Claude Desktop (`~/Library/Application Support/Claude/claude_desktop_config.json`) or Antigravity (`~/.gemini/config/mcp_config.json`, the Windows location under the home folder), keeping the other servers and a `.bak` copy, or copies the `claude mcp add` / `gemini mcp add` command. The command points at the running binary, so register again after moving the app.

## Sentinel

Settings › Monitoring, same as Windows: with the Sentinel on, a menu bar icon (Open · Rescan · Quit) stays after the window is closed (the red button hides it, ⌘Q quits), and a notification comes as soon as macOS writes a kernel panic or a low-memory (Jetsam) report in the diagnostic report folders (Windows: GPU, WHEA, NVMe and commit events of the System log). "Open at login" writes a per-user LaunchAgent (`~/Library/LaunchAgents/io.github.lucasredjaimia.dumpfinder.sentinel.plist`, `--tray`), kept pointing at the app when it moves, refused while macOS runs the app from a translocated copy. Notifications go through `NSUserNotificationCenter`; a click brings the app forward on what the alert was about.
