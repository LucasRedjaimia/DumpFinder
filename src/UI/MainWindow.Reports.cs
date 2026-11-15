using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    /// <summary>Report exports (HTML / JSON / Markdown) wired into Outils › Rapport and the home page.</summary>
    public partial class MainWindow
    {
        private Button _btnExportHtml, _btnExportJson, _btnCopyMarkdown;

        private void AddReportButtons()
        {
            var bar = _btnCopyReport.Parent as Panel;
            if (bar == null) return;

            // HTML becomes the primary action; the plain-text copy becomes secondary.
            _btnExportHtml = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(16, 7, 16, 7), 13);
            _btnExportHtml.Margin = new Thickness(0, 0, 8, 0);
            _btnExportHtml.Click += (s, e) => ExportHtmlReport();
            _btnExportJson = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnExportJson.Margin = new Thickness(0, 0, 8, 0);
            _btnExportJson.Click += (s, e) => ExportJsonReport();
            _btnCopyMarkdown = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnCopyMarkdown.Margin = new Thickness(0, 0, 8, 0);
            _btnCopyMarkdown.Click += (s, e) => CopyMarkdownReport();

            bar.Children.Insert(0, _btnCopyMarkdown);
            bar.Children.Insert(0, _btnExportJson);
            bar.Children.Insert(0, _btnExportHtml);

            _btnCopyReport.SetResourceReference(Button.BackgroundProperty, "BgCard");
            _btnCopyReport.SetResourceReference(Button.ForegroundProperty, "TextPrimary");
            _btnCopyReport.SetResourceReference(Button.BorderBrushProperty, "BorderStrong");
        }

        private void RefreshReportLabels()
        {
            if (_btnExportHtml == null) return;
            _btnExportHtml.Content = Localizer.S("reports.html_report");
            _btnExportJson.Content = Localizer.S("reports.json");
            _btnCopyMarkdown.Content = Localizer.S("reports.markdown_forum");
            _btnCopyReport.Content = Localizer.S("reports.plain_text");
            _btnExportHtml.ToolTip = Localizer.S("reports.a_standalone_page_readable_in_any");
            _btnCopyMarkdown.ToolTip = Localizer.S("reports.summary_ready_to_paste_on_reddit");
        }

        private ReportData BuildReportData()
        {
            if (_lastResult == null) return null;
            var window = (_lastResult.Events ?? new System.Collections.Generic.List<CrashEventItem>())
                .Where(e => (DateTime.Now - e.Timestamp).TotalDays <= Stability.WindowDays).ToList();
            return new ReportData
            {
                Scan = _lastResult,
                Crashes = _crashEntries,
                Score = _homeScore,
                CrashScore = Stability.Score(window, DateTime.Now),
                WindowsIndex = Reliability.Latest(_lastResult.Reliability),
                History = History.Scans(),
                Journal = History.Journal()
            };
        }

        private string AskSavePath(string defaultName, string filter)
        {
            var sfd = new SaveFileDialog { FileName = defaultName, Filter = filter };
            return sfd.ShowDialog(this) == true ? sfd.FileName : null;
        }

        private void ExportHtmlReport()
        {
            var data = BuildReportData();
            if (data == null) return;
            string path = AskSavePath("DumpFinder_Rapport_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html", "HTML (*.html)|*.html");
            if (path == null) return;
            try
            {
                File.WriteAllText(path, ReportBuilder.Html(data), new UTF8Encoding(false));
                SetToast(Localizer.S("reports.html_report_saved") + Path.GetFileName(path), "SuccessFg");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, path);
                SetToast(ex.Message, "DangerFg");
            }
        }

        private void ExportJsonReport()
        {
            var data = BuildReportData();
            if (data == null) return;
            string path = AskSavePath("DumpFinder_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json", "JSON (*.json)|*.json");
            if (path == null) return;
            try
            {
                File.WriteAllText(path, ReportBuilder.Json(data), new UTF8Encoding(false));
                SetToast(Localizer.S("reports.json_export_saved") + Path.GetFileName(path), "SuccessFg");
            }
            catch (Exception ex)
            {
                Log.Error(ex, path);
                SetToast(ex.Message, "DangerFg");
            }
        }

        private void CopyMarkdownReport()
        {
            var data = BuildReportData();
            if (data == null) return;
            try
            {
                Clipboard.SetText(ReportBuilder.Markdown(data));
                SetToast(Localizer.S("reports.markdown_summary_copied_ready_to_paste"), "SuccessFg");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Clipboard");
                SetToast(Localizer.S("ai.clipboard_busy_try_again"), "WarnFg");
            }
        }

        /// <summary>Used by the --screenshot harness / tests: writes all three formats to a folder.</summary>
        /// <summary>
        /// Capture harness export (--screenshot): the three report formats, plus nav_selftest.txt, the check that
        /// every nav: command and tray alert click lands on the right tab / card (navigation state is restored).
        /// </summary>
        public void ExportAllReportsTo(string dir)
        {
            if (Localizer.ReadOnlySettings) RunNavSelfCheck(Path.Combine(dir, "nav_selftest.txt"));
            var data = BuildReportData();
            if (data == null) return;
            File.WriteAllText(Path.Combine(dir, "report.html"), ReportBuilder.Html(data), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "report.json"), ReportBuilder.Json(data), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "report.md"), ReportBuilder.Markdown(data), new UTF8Encoding(false));
        }
    }
}
