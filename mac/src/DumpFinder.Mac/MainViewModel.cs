using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

public sealed record FindingRow(Severity Severity, string Category, string Title, string Detail, string Advice)
{
    public string Badge => TextReport.Label(Severity).ToUpperInvariant();
    public bool IsCritical => Severity == Severity.Critical;
    public bool IsWarning => Severity == Severity.Warning;
    public bool IsOk => Severity == Severity.Ok;
    public bool HasAdvice => Advice.Length > 0;
}

public sealed record LineRow(string Primary, string Secondary = "")
{
    public bool HasSecondary => Secondary.Length > 0;
}

public sealed class RelayCommand(Func<Task> run, Func<bool>? canRun = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await run(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    readonly Func<string, Task> _copyToClipboard;
    ScanResult? _result;

    public MainViewModel(Func<string, Task> copyToClipboard)
    {
        _copyToClipboard = copyToClipboard;
        RescanCommand = new RelayCommand(ScanAsync, () => !IsScanning);
        CopyCommand = new RelayCommand(CopyAsync, () => _result is not null);
    }

    public RelayCommand RescanCommand { get; }
    public RelayCommand CopyCommand { get; }

    public ObservableCollection<FindingRow> Findings { get; } = new();
    public ObservableCollection<LineRow> Panics { get; } = new();
    public ObservableCollection<LineRow> Crashes { get; } = new();
    public ObservableCollection<LineRow> Kexts { get; } = new();
    public ObservableCollection<LineRow> SysExts { get; } = new();
    public ObservableCollection<LineRow> HardwareLines { get; } = new();

    string _machineLine = "Apple Silicon Mac";
    public string MachineLine { get => _machineLine; private set => Set(ref _machineLine, value); }

    string _statusLine = "";
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }

    bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set { Set(ref _isScanning, value); RescanCommand.Refresh(); }
    }

    public bool HasResult => _result is not null;

    public async Task ScanAsync()
    {
        IsScanning = true;
        StatusLine = "Scanning: crash reports, extensions, hardware…";
        try
        {
            var r = await DiagnosticsEngine.ScanAsync();
            _result = r;
            Fill(r);
            int crit = r.Findings.Count(f => f.Severity == Severity.Critical), warn = r.Findings.Count(f => f.Severity == Severity.Warning);
            StatusLine = $"Scanned {r.ScannedAt:HH:mm} · {crit} critical, {warn} warning(s)"
                         + (r.Errors.Count > 0 ? $" · {r.Errors.Count} source(s) not read" : "");
        }
        catch (Exception e)
        {
            StatusLine = "Scan failed: " + e.Message;
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(HasResult));
            CopyCommand.Refresh();
        }
    }

    async Task CopyAsync()
    {
        if (_result is null) return;
        await _copyToClipboard(TextReport.Build(_result));
        StatusLine = "Report copied to the clipboard.";
    }

    void Fill(ScanResult r)
    {
        var hw = r.Hardware;
        MachineLine = string.Join(" · ", new[] { hw.MachineName, hw.Chip, hw.Memory, hw.OsVersion }.Where(s => s.Length > 0));

        Findings.Clear();
        foreach (var f in r.Findings) Findings.Add(new FindingRow(f.Severity, f.Category, f.Title, f.Detail, f.Advice.Length > 0 ? "→ " + f.Advice : ""));

        Panics.Clear();
        foreach (var p in r.Panics)
            Panics.Add(new LineRow($"{p.Time:yyyy-MM-dd HH:mm} · {p.Message}",
                string.Join(" · ", new[]
                {
                    p.PanickedProcess.Length > 0 ? "process " + p.PanickedProcess : "",
                    p.BacktraceExtensions.Count > 0 ? "extensions: " + string.Join(", ", p.BacktraceExtensions) : "",
                    Path.GetFileName(p.File),
                }.Where(s => s.Length > 0))));

        Crashes.Clear();
        foreach (var g in r.Reports.Where(x => x.Kind is ReportKind.Crash or ReportKind.MemoryPressure)
                                   .GroupBy(x => (x.Kind, x.Process)).OrderByDescending(g => g.Count()))
        {
            var last = g.First();
            int n = g.Count();
            var kind = g.Key.Kind == ReportKind.MemoryPressure
                ? (n > 1 ? "low-memory events" : "low-memory event")
                : (n > 1 ? "crashes" : "crash");
            var why = string.Join(" / ", new[] { last.ExceptionType, last.Signal, last.Termination }.Where(s => s.Length > 0));
            Crashes.Add(new LineRow($"{g.Key.Process} — {n} {kind}",
                $"last {last.Time:yyyy-MM-dd HH:mm}{(last.FirstParty ? " · Apple process" : "")}{(why.Length > 0 ? " · " + why : "")}"));
        }

        Kexts.Clear();
        foreach (var k in r.KernelExtensions)
            Kexts.Add(new LineRow($"{k.BundleId} {k.Version}".Trim(), k.Loaded ? "loaded" : "installed, not loaded · " + k.Path));
        SysExts.Clear();
        foreach (var s in r.SystemExtensions)
            SysExts.Add(new LineRow($"{s.BundleId} {s.Version}", $"team {s.TeamId} · {s.State}"));

        HardwareLines.Clear();
        HardwareLines.Add(new LineRow($"{hw.MachineName} ({hw.ModelId})", $"{hw.Chip} · {hw.Memory}"));
        HardwareLines.Add(new LineRow(hw.OsVersion, hw.Kernel));
        if (r.Boot.BootTime is { } bt)
            HardwareLines.Add(new LineRow($"Started {bt:yyyy-MM-dd HH:mm} (boot reason: {Or(r.Boot.BootReason)})",
                "Last shutdown: " + Or(r.Boot.ShutdownReason) + (r.Boot.Interpretation.Count > 0 ? " → " + string.Join("; ", r.Boot.Interpretation) : "")));
        foreach (var d in hw.Drives)
            HardwareLines.Add(new LineRow(d.Name, $"{(d.Internal ? "internal" : "external")} · SMART {Or(d.SmartStatus)}"));
        if (hw.BootVolumeSize is long size && hw.BootVolumeFree is long free)
            HardwareLines.Add(new LineRow("Startup disk", $"{free / 1e9:0.#} GB free of {size / 1e9:0.#} GB ({100.0 * free / size:0.#} %)"));
        if (hw.HasBattery)
            HardwareLines.Add(new LineRow("Battery", $"{hw.BatteryMaxCapacityPct} % maximum capacity · {hw.BatteryCycles} cycles · {Or(hw.BatteryCondition)}"));
        HardwareLines.Add(new LineRow("Security", $"System Integrity Protection {OnOff(hw.SipEnabled)} · Gatekeeper {OnOff(hw.GatekeeperEnabled)}"));
        foreach (var e in r.Errors) HardwareLines.Add(new LineRow("Not read", e));
    }

    static string Or(string s) => s.Length > 0 ? s : "—";
    static string OnOff(bool? b) => b is null ? "unknown" : b.Value ? "on" : "off";

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
