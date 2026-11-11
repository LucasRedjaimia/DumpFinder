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
            _btnExportHtml.Content = Localizer.L("📄 Rapport HTML", "📄 HTML report", "📄 Informe HTML", "📄 HTML-Bericht", "📄 Rapporto HTML", "📄 Relatório HTML");
            _btnExportJson.Content = Localizer.L("💾 JSON", "💾 JSON", "💾 JSON", "💾 JSON", "💾 JSON", "💾 JSON");
            _btnCopyMarkdown.Content = Localizer.L("📋 Markdown (forum)", "📋 Markdown (forum)", "📋 Markdown (foro)", "📋 Markdown (Forum)", "📋 Markdown (forum)", "📋 Markdown (fórum)");
            _btnCopyReport.Content = Localizer.L("📋 Texte brut", "📋 Plain text", "📋 Texto plano", "📋 Klartext", "📋 Testo semplice", "📋 Texto simples");
            _btnExportHtml.ToolTip = Localizer.L("Une page autonome, lisible dans n'importe quel navigateur et imprimable en PDF.", "A standalone page, readable in any browser and printable to PDF.", "Página autónoma, imprimible en PDF.", "Eigenständige Seite, als PDF druckbar.", "Pagina autonoma, stampabile in PDF.", "Página autônoma, imprimível em PDF.");
            _btnCopyMarkdown.ToolTip = Localizer.L("Résumé prêt à coller sur Reddit ou un forum constructeur.", "Summary ready to paste on Reddit or a vendor forum.", "Resumen listo para pegar en un foro.", "Zusammenfassung zum Einfügen in ein Forum.", "Riepilogo da incollare in un forum.", "Resumo pronto para colar em um fórum.");
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
                SetToast(Localizer.L("Rapport HTML enregistré : ", "HTML report saved: ", "Informe HTML guardado: ", "HTML-Bericht gespeichert: ", "Rapporto HTML salvato: ", "Relatório HTML salvo: ") + Path.GetFileName(path), "SuccessFg");
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
                SetToast(Localizer.L("Export JSON enregistré : ", "JSON export saved: ", "JSON guardado: ", "JSON gespeichert: ", "JSON salvato: ", "JSON salvo: ") + Path.GetFileName(path), "SuccessFg");
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
                SetToast(Localizer.L("Résumé Markdown copié, prêt à coller sur un forum.", "Markdown summary copied, ready to paste on a forum.", "Resumen Markdown copiado.", "Markdown-Zusammenfassung kopiert.", "Riepilogo Markdown copiato.", "Resumo Markdown copiado."), "SuccessFg");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Clipboard");
                SetToast(Localizer.L("Le presse-papiers est occupé, réessayez.", "Clipboard busy, try again.", "Portapapeles ocupado.", "Zwischenablage belegt.", "Appunti occupati.", "Área de transferência ocupada."), "WarnFg");
            }
        }

        /// <summary>Used by the --screenshot harness / tests: writes all three formats to a folder.</summary>
        public void ExportAllReportsTo(string dir)
        {
            var data = BuildReportData();
            if (data == null) return;
            File.WriteAllText(Path.Combine(dir, "report.html"), ReportBuilder.Html(data), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "report.json"), ReportBuilder.Json(data), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "report.md"), ReportBuilder.Markdown(data), new UTF8Encoding(false));
        }
    }
}
