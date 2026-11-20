using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using WinCrashDoctor.Optimize;

namespace WinCrashDoctor
{
    /// <summary>
    /// Optimiser › Storage: what takes space, as installed programs, a drill-down of the user folder or a drive, and
    /// the largest files. Show in Explorer, Move to Recycle Bin (journaled: Undo in Tools › History puts it back) and,
    /// for programs, their own uninstaller. Essentials (Windows, programs, Documents, AppData…) only get Show in Explorer.
    /// </summary>
    public partial class MainWindow
    {
        private const int OptSubStorage = 4;

        private List<OptSpace.Place> _spacePlaces;
        private int _spacePlace;
        private readonly Dictionary<string, SpaceScan> _spaceScans = new Dictionary<string, SpaceScan>(StringComparer.OrdinalIgnoreCase);
        private List<InstalledProgram> _spacePrograms;
        private SpaceNode _spaceFolder;
        private int _spaceMode = 1;                 // 0 Programs · 1 Folders · 2 Largest files
        private bool _spaceScanning;
        private CancellationTokenSource _spaceCts;
        private string _spaceStatus = "";
        private TextBlock _spaceStatusText;

        private OptSpace.Place CurrentPlace { get { return _spacePlaces[Math.Min(_spacePlace, _spacePlaces.Count - 1)]; } }

        private SpaceScan CurrentScan
        {
            get { SpaceScan s; return _spaceScans.TryGetValue(CurrentPlace.Root, out s) ? s : null; }
        }

        private void RenderOptSpace()
        {
            if (_spacePlaces == null) _spacePlaces = OptSpace.Places();
            var p = _optPanels[OptSubStorage];
            p.Children.Clear();
            OptSpace.Place place = CurrentPlace;
            SpaceScan scan = CurrentScan;

            // ---- Disk bar, status, Analyze / Stop
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            string diskText; double used;
            DiskUsage(place.Root, out diskText, out used);
            left.Children.Add(ResFg(new TextBlock { Text = diskText, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
            left.Children.Add(SizeBar(used, 8, "Accent", new Thickness(0, 6, 0, 6)));
            _spaceStatusText = ResFg(new TextBlock { Text = _spaceStatus, FontSize = 12, TextWrapping = TextWrapping.Wrap }, "TextMuted");
            left.Children.Add(_spaceStatusText);
            top.Children.Add(left);

            Button analyze;
            if (_spaceScanning)
            {
                analyze = CreateFlatButton(Localizer.S("space.stop"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 12.5);
                analyze.Click += (s, e) => { if (_spaceCts != null) _spaceCts.Cancel(); };
            }
            else
            {
                analyze = CreateFlatButton(Localizer.S(scan != null ? "space.rescan" : "space.analyze"), "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
                analyze.Click += (s, e) => { _ = ScanSpaceAsync(); };
            }
            analyze.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(analyze, 1);
            top.Children.Add(analyze);
            p.Children.Add(OptCard(top));

            // ---- Where to look: the user folder or a whole drive
            var places = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            places.Children.Add(ResFg(new TextBlock { Text = Localizer.S("space.where"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 6) }, "TextSecondary"));
            for (int i = 0; i < _spacePlaces.Count; i++)
            {
                int idx = i;
                var b = CreateFlatButton(_spacePlaces[i].Label, "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 5, 12, 5), 12);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.IsEnabled = !_spaceScanning;
                StyleTogglePill(b, i == _spacePlace);
                b.Click += (s, e) =>
                {
                    _spacePlace = idx;
                    SpaceScan sc = CurrentScan;
                    _spaceFolder = sc != null ? sc.Root : null;
                    if (_spaceMode == 0 && _spacePrograms == null) _spaceMode = 1;
                    _spaceStatus = sc != null ? ScannedText(sc) : "";
                    RenderOptSpace();
                };
                places.Children.Add(b);
            }
            p.Children.Add(places);
            p.Children.Add(OptNote(Localizer.S("space.note")));

            if (scan == null && _spacePrograms == null) return;

            // ---- Programs | Folders | Largest files, crumb and parent folder
            var bar = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            string[] modeNames = { Localizer.S("space.programs"), Localizer.S("space.folders"), Localizer.S("space.files") };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = CreateFlatButton(modeNames[i], "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 5, 12, 5), 12);
                b.Margin = new Thickness(0, 0, 6, 0);
                StyleTogglePill(b, i == _spaceMode);
                b.Click += (s, e) => { _spaceMode = idx; RenderOptSpace(); };
                modes.Children.Add(b);
            }
            bar.Children.Add(modes);

            var crumbRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (_spaceMode == 1 && scan != null && _spaceFolder != null && _spaceFolder.Parent != null)
            {
                var up = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(10, 4, 10, 4), 12);
                up.Content = Ds.Icon(Glyph.Up, 12);
                up.ToolTip = Localizer.S("space.up");
                AutomationProperties.SetName(up, Localizer.S("space.up"));
                up.Margin = new Thickness(0, 0, 8, 0);
                up.Click += (s, e) => { _spaceFolder = _spaceFolder.Parent ?? _spaceFolder; RenderOptSpace(); };
                crumbRow.Children.Add(up);
            }
            string crumb = CrumbText(scan, place);
            var crumbText = ResFg(new TextBlock { Text = crumb, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, ToolTip = crumb }, "TextMuted");
            if (_spaceMode == 1) crumbText.FontFamily = Ds.MonoFont;
            crumbRow.Children.Add(crumbText);
            Grid.SetColumn(crumbRow, 1);
            bar.Children.Add(crumbRow);
            p.Children.Add(bar);

            // ---- Rows
            var rows = new StackPanel();
            Grid.SetIsSharedSizeScope(rows, true);   // sizes and buttons line up from row to row
            if (_spaceMode == 0) AddProgramRows(rows);
            else if (scan == null) rows.Children.Add(OptNote(Localizer.F("space.analyze_place", place.Label)));
            else if (_spaceMode == 1) AddNodeRows(rows, SpaceScan.ChildrenOf(_spaceFolder ?? scan.Root).Where(k => k.Bytes > 0).Take(200).ToList(), false);
            else AddNodeRows(rows, scan.LargestFiles, true);
            if (rows.Children.Count == 0) rows.Children.Add(OptNote(Localizer.S("space.empty")));
            p.Children.Add(OptCard(rows));
        }

        private string CrumbText(SpaceScan scan, OptSpace.Place place)
        {
            if (_spaceMode == 0) return Localizer.S("space.programs_note");
            if (scan == null) return place.Root;
            if (_spaceMode == 2) return Localizer.F("space.largest_files_note", scan.Root.Name);
            SpaceNode f = _spaceFolder ?? scan.Root;
            return f.FullPath + "  ·  " + OptSpace.Human(f.Bytes);
        }

        private static void DiskUsage(string root, out string text, out double usedShare)
        {
            text = ""; usedShare = 0;
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(root));
                long used = d.TotalSize - d.TotalFreeSpace;
                usedShare = d.TotalSize > 0 ? 100.0 * used / d.TotalSize : 0;
                text = Localizer.F("space.disk_usage", d.Name.TrimEnd('\\'), OptSpace.Human(used), OptSpace.Human(d.TotalSize), OptSpace.Human(d.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
        }

        /// <summary>A thin bar filled to <paramref name="share"/> % (theme brushes, no template needed).</summary>
        private static Grid SizeBar(double share, double height, string fillKey, Thickness margin)
        {
            share = Math.Max(0, Math.Min(100, share));
            var g = new Grid { Height = height, Margin = margin };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.0001), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - share, 0.0001), GridUnitType.Star) });
            var track = ResBg(new Border { CornerRadius = new CornerRadius(height / 2) }, "BgSubtle");
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            g.Children.Add(ResBg(new Border { CornerRadius = new CornerRadius(height / 2), MinWidth = share > 0 ? height : 0 }, fillKey));
            return g;
        }

        private static string ScannedText(SpaceScan s)
        {
            return Localizer.F("space.scanned", OptSpace.Human(s.ScannedBytes), s.ScannedFiles, s.Root.Name);
        }

        private void SetSpaceStatus(string text)
        {
            _spaceStatus = text;
            if (_spaceStatusText != null) _spaceStatusText.Text = text;
        }

        /// <summary>Measures the installed programs (once) and the chosen place, then shows the current view.</summary>
        private async Task ScanSpaceAsync()
        {
            if (_spaceScanning) return;
            _spaceScanning = true;
            var cts = _spaceCts = new CancellationTokenSource();
            OptSpace.Place place = CurrentPlace;
            RenderOptSpace();
            try
            {
                SetSpaceStatus(Localizer.S("space.measuring_programs"));
                _spacePrograms = await Task.Run(() => OptSpace.Programs(cts.Token));
                if (cts.IsCancellationRequested) { SetSpaceStatus(Localizer.S("space.stopped")); return; }

                SetSpaceStatus(Localizer.F("space.measuring", place.Root, OptSpace.Human(0), 0));
                var dispatcher = Dispatcher;
                SpaceScan scan = await Task.Run(() => SpaceScan.Scan(place.Root, cts.Token,
                    (bytes, files) => dispatcher.BeginInvoke(new Action(() => { if (_spaceScanning) SetSpaceStatus(Localizer.F("space.measuring", place.Root, OptSpace.Human(bytes), files)); }))));
                if (cts.IsCancellationRequested) { SetSpaceStatus(Localizer.S("space.stopped")); return; }
                _spaceScans[place.Root] = scan;
                if (CurrentPlace.Root.Equals(place.Root, StringComparison.OrdinalIgnoreCase)) _spaceFolder = scan.Root;
                SetSpaceStatus(ScannedText(scan));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "storage scan");
                SetSpaceStatus(ex.Message);
            }
            finally
            {
                _spaceScanning = false;
                _spaceCts = null;
                cts.Dispose();
                if (_optSub == OptSubStorage) RenderOptSpace();
            }
        }

        // ---------- Rows ----------
        private Grid SpaceRow(string glyph, UIElement title, string sub, long bytes, long max, string sizeText, bool first, params Button[] actions)
        {
            var row = new Grid { Margin = new Thickness(0, first ? 2 : 8, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90, SharedSizeGroup = "SpaceSize" });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "SpaceActions" });

            var tile = MakeIconTile(glyph, "BgSubtle", "TextSecondary");
            tile.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(tile);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            mid.Children.Add(title);
            if (!string.IsNullOrEmpty(sub))
                mid.Children.Add(ResFg(new TextBlock { Text = sub, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sub }, "TextMuted"));
            mid.Children.Add(SizeBar(max > 0 ? 100.0 * bytes / max : 0, 4, "AccentText", new Thickness(0, 5, 0, 0)));
            Grid.SetColumn(mid, 1);
            row.Children.Add(mid);

            var size = ResFg(new TextBlock { Text = sizeText, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, "TextPrimary");
            Grid.SetColumn(size, 2);
            row.Children.Add(size);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (Button b in actions.Where(a => a != null))
            {
                b.Margin = new Thickness(6, 0, 0, 0);
                buttons.Children.Add(b);
            }
            Grid.SetColumn(buttons, 3);
            row.Children.Add(buttons);
            return row;
        }

        private TextBlock SpaceTitle(string name, Action open)
        {
            var t = ResFg(new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name }, "TextPrimary");
            if (open == null) { t.Text = name; return t; }
            var link = new Hyperlink(new Run(name)) { TextDecorations = null };
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
            link.Click += (s, e) => open();
            AutomationProperties.SetName(link, Localizer.F("space.open_folder", name));
            t.Inlines.Add(link);
            return t;
        }

        private Button SpaceButton(string text, Action click, string automationName)
        {
            var b = CreateFlatButton(text, "BgCard", "TextPrimary", "BorderStrong", new Thickness(10, 5, 10, 5), 12);
            b.Click += (s, e) => click();
            AutomationProperties.SetName(b, automationName);
            return b;
        }

        private void AddNodeRows(StackPanel rows, List<SpaceNode> nodes, bool filesView)
        {
            long max = nodes.Count > 0 ? Math.Max(1, nodes.Max(n => n.Bytes)) : 1;
            bool first = true;
            foreach (SpaceNode n in nodes)
            {
                SpaceNode node = n;
                string path = node.FullPath;
                bool kept = OptSpace.IsProtected(path);
                string sub = filesView ? (Path.GetDirectoryName(path) ?? "") : kept ? Localizer.S("space.protected_short") : "";
                Action open = !filesView && node.IsFolder ? (Action)(() => { _spaceFolder = node; RenderOptSpace(); }) : null;
                Button show = SpaceButton(Localizer.S("space.show"), () => OptSpace.ShowInExplorer(path), Localizer.S("space.show") + " — " + node.Name);
                Button recycle = kept ? null : SpaceButton(Localizer.S("space.recycle"), () => RecycleNode(node), Localizer.S("space.recycle") + " — " + node.Name);
                rows.Children.Add(SpaceRow(node.IsFolder ? Glyph.Folder : Glyph.Document, SpaceTitle(node.Name, open), sub, node.Bytes, max, OptSpace.Human(node.Bytes), first, show, recycle));
                first = false;
            }
        }

        private void AddProgramRows(StackPanel rows)
        {
            if (_spacePrograms == null) { rows.Children.Add(OptNote(Localizer.F("space.analyze_place", Localizer.S("space.programs")))); return; }
            long max = _spacePrograms.Count > 0 ? Math.Max(1, _spacePrograms[0].Bytes) : 1;
            bool first = true;
            foreach (InstalledProgram pr in _spacePrograms)
            {
                InstalledProgram prog = pr;
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(prog.Publisher)) parts.Add(prog.Publisher);
                if (prog.Installed.HasValue) parts.Add(Localizer.F("space.installed_on", prog.Installed.Value));
                if (prog.Estimated) parts.Add(Localizer.S("space.size_reported"));
                Button show = prog.Location == null ? null : SpaceButton(Localizer.S("space.show"), () => OptSpace.ShowInExplorer(prog.Location), Localizer.S("space.show") + " — " + prog.Name);
                Button uninstall = SpaceButton(Localizer.S("space.uninstall"), () => UninstallProgram(prog), Localizer.S("space.uninstall") + " — " + prog.Name);
                rows.Children.Add(SpaceRow(Glyph.AllApps, SpaceTitle(prog.Name, null), string.Join("  ·  ", parts), prog.Bytes, max,
                    (prog.Estimated ? "≈ " : "") + OptSpace.Human(prog.Bytes), first, show, uninstall));
                first = false;
            }
            if (_spacePrograms.Count == 0) rows.Children.Add(OptNote(Localizer.S("space.no_programs")));

            // Store apps are not in the uninstall list: they are managed from Settings (and Optimiser › Apps).
            var store = new Grid { Margin = new Thickness(0, 12, 0, 2) };
            store.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            store.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var note = OptNote(Localizer.S("space.store_apps"));
            note.Margin = new Thickness(0, 0, 12, 0);
            note.VerticalAlignment = VerticalAlignment.Center;
            store.Children.Add(note);
            var open = SpaceButton(Localizer.S("optimize.open_apps"), () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:appsfeatures", UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex); }
            }, Localizer.S("optimize.open_apps"));
            Grid.SetColumn(open, 1);
            store.Children.Add(open);
            rows.Children.Add(store);
        }

        // ---------- Actions ----------
        private async void RecycleNode(SpaceNode node)
        {
            string path = node.FullPath;
            string size = OptSpace.Human(node.Bytes);
            if (MessageBox.Show(this, Localizer.F("space.confirm_recycle", node.Name, size), "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            SpaceScan scan = CurrentScan;
            OptSpace.RecycleResult result;
            string message = null;
            using (IDisposable fixLock = TryBeginFix(null))
            {
                if (fixLock == null) return;
                IntPtr owner = new WindowInteropHelper(this).Handle;
                using (BeginBusy(Localizer.S("space.recycling")))
                    result = await Task.Run(() => OptSpace.MoveToRecycleBin(path, node.Name, node.Bytes, owner, out message));
            }
            bool removed = result == OptSpace.RecycleResult.Recycled || result == OptSpace.RecycleResult.DeletedPermanently || result == OptSpace.RecycleResult.Gone;
            SetToast(message, result == OptSpace.RecycleResult.Recycled ? "SuccessFg" : "WarnFg");
            if (removed)
            {
                // Out of every measured tree that holds it (the user folder and its drive are measured separately).
                foreach (SpaceScan s in _spaceScans.Values)
                {
                    SpaceNode n = s == scan ? node : s.Find(path);
                    if (n != null) s.Forget(n);
                }
                if (_spaceFolder != null && IsInside(_spaceFolder, node)) _spaceFolder = node.Parent;
                if (scan != null) SetSpaceStatus(ScannedText(scan));
            }
            RenderJournal();
            if (_optSub == OptSubStorage) RenderOptSpace();
        }

        private static bool IsInside(SpaceNode n, SpaceNode ancestor)
        {
            for (var p = n; p != null; p = p.Parent) if (p == ancestor) return true;
            return false;
        }

        private void UninstallProgram(InstalledProgram prog)
        {
            if (MessageBox.Show(this, Localizer.F("space.confirm_uninstall", prog.Name), "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            string error;
            if (OptSpace.LaunchUninstaller(prog, out error)) SetToast(Localizer.F("space.uninstall_started", prog.Name), "SuccessFg");
            else SetToast(Localizer.F("space.uninstall_failed", error), "WarnFg");
        }

        /// <summary>--screenshot harness: a real (read-only) measure of the user folder, then the folder view.</summary>
        public async Task RunStorageScanForCapture()
        {
            SwitchOptimizeSub(OptSubStorage);
            _spacePlace = 0;
            await ScanSpaceAsync();
            _spaceMode = 1;
            RenderOptSpace();
        }

        public void ShowStorageModeForCapture(int mode)
        {
            _spaceMode = mode;
            RenderOptSpace();
        }
    }
}
