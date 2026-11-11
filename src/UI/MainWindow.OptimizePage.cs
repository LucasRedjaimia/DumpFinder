using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCrashDoctor.Optimize;

namespace WinCrashDoctor
{
    /// <summary>
    /// Optimiser: Windows settings (ads, Bing, Copilot, privacy, gaming), preinstalled apps, startup programs,
    /// disk cleanup, and the "myths" we deliberately leave out. Everything goes through the fix pipeline
    /// (journal + exact undo) except app removal, which is reversible from the Microsoft Store.
    /// </summary>
    public partial class MainWindow
    {
        private Grid _pageOptimize;
        private TextBlock _lblOptTitle, _lblOptSub;
        private readonly Button[] _btnOptSub = new Button[5];
        private readonly ScrollViewer[] _optViews = new ScrollViewer[5];
        private readonly StackPanel[] _optPanels = new StackPanel[5];
        private int _optSub;
        private bool _optShownOnce;

        private List<AppEntry> _optApps;
        private List<StartupEntry> _optStartup;
        private List<CleanItem> _optClean;
        private bool _optAppsLoading, _optStartupLoading, _optCleanLoading;
        private readonly Dictionary<CheckBox, AppEntry> _optAppChecks = new Dictionary<CheckBox, AppEntry>();
        private readonly Dictionary<CheckBox, CleanItem> _optCleanChecks = new Dictionary<CheckBox, CleanItem>();
        private Button _btnOptRemoveApps, _btnOptClean;

        private static string OL(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        // ==================== BUILD ====================
        private void BuildOptimizePage()
        {
            _pageOptimize = new Grid { Visibility = Visibility.Collapsed };
            _pageOptimize.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageOptimize.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var hdr = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            _lblOptTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblOptSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            titles.Children.Add(_lblOptTitle);
            titles.Children.Add(_lblOptSub);
            hdr.Children.Add(titles);

            var seg = new WrapPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12.2);
                b.FontWeight = FontWeights.SemiBold;
                b.Margin = new Thickness(6, 0, 0, 6);
                b.Click += (s, e) => SwitchOptimizeSub(idx);
                _btnOptSub[i] = b;
                seg.Children.Add(b);
            }
            Grid.SetColumn(seg, 1);
            hdr.Children.Add(seg);
            _pageOptimize.Children.Add(hdr);

            var host = new Grid();
            Grid.SetRow(host, 1);
            for (int i = 0; i < 5; i++)
            {
                _optPanels[i] = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
                _optViews[i] = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _optPanels[i], Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
                host.Children.Add(_optViews[i]);
            }
            _pageOptimize.Children.Add(host);

            _pageOptimize.IsVisibleChanged += (s, e) =>
            {
                if (!_pageOptimize.IsVisible) return;
                if (!_optShownOnce) { _optShownOnce = true; RenderOptimizeSub(); }
            };
        }

        private void RefreshOptimizeLabels()
        {
            _lblOptTitle.Text = OL("Optimiser", "Optimize", "Optimizar", "Optimieren", "Ottimizza", "Otimizar");
            _lblOptSub.Text = OL("Retirer le superflu et régler Windows sans rien casser : chaque réglage s'annule depuis Outils › Historique.",
                                 "Remove the clutter and tune Windows without breaking anything: every setting can be undone from Tools › History.",
                                 "Quitar lo superfluo y ajustar Windows sin romper nada: cada ajuste se deshace desde Herramientas › Historial.",
                                 "Überflüssiges entfernen und Windows einstellen, ohne etwas kaputtzumachen: alles lässt sich unter Werkzeuge › Verlauf rückgängig machen.",
                                 "Rimuovi il superfluo e regola Windows senza rompere nulla: ogni impostazione si annulla da Strumenti › Cronologia.",
                                 "Remova o supérfluo e ajuste o Windows sem quebrar nada: cada ajuste pode ser desfeito em Ferramentas › Histórico.");
            string[] names =
            {
                OL("Réglages", "Settings", "Ajustes", "Einstellungen", "Impostazioni", "Ajustes"),
                OL("Applications", "Apps", "Aplicaciones", "Apps", "App", "Aplicativos"),
                OL("Démarrage", "Startup", "Inicio", "Autostart", "Avvio", "Inicialização"),
                OL("Nettoyage", "Cleanup", "Limpieza", "Bereinigung", "Pulizia", "Limpeza"),
                OL("Mythes", "Myths", "Mitos", "Mythen", "Miti", "Mitos")
            };
            for (int i = 0; i < 5; i++) { _btnOptSub[i].Content = names[i]; StyleTogglePill(_btnOptSub[i], i == _optSub); }
            if (_optShownOnce) RenderOptimizeSub();
        }

        public void SwitchOptimizeSub(int sub)
        {
            _optSub = sub;
            for (int i = 0; i < 5; i++)
            {
                _optViews[i].Visibility = i == sub ? Visibility.Visible : Visibility.Collapsed;
                StyleTogglePill(_btnOptSub[i], i == sub);
            }
            RenderOptimizeSub();
            AnimatePageIn(_optViews[sub]);
        }

        private void RenderOptimizeSub()
        {
            switch (_optSub)
            {
                case 0: RenderOptTweaks(); break;
                case 1: RenderOptApps(); break;
                case 2: RenderOptStartup(); break;
                case 3: RenderOptClean(); break;
                default: RenderOptMyths(); break;
            }
        }

        // ==================== Shared bits ====================
        private TextBlock OptSectionTitle(string text)
        {
            return ResFg(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) }, "TextPrimary");
        }

        private TextBlock OptNote(string text)
        {
            return ResFg(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), LineHeight = 17 }, "TextMuted");
        }

        private Border OptCard(UIElement child)
        {
            Border b = ResBorder(ResBg(new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) }, "BgCard"), "BorderCard");
            b.Child = child;
            return b;
        }

        private Grid OptActionBar(string note, Button action)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = OptNote(note);
            t.Margin = new Thickness(0, 0, 16, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            if (action != null) { Grid.SetColumn(action, 1); g.Children.Add(action); }
            return g;
        }

        /// <summary>Runs one Optimiser operation through the fix pipeline (UAC if needed, journal, undo).</summary>
        private async void ExecuteOptimizeOp(string op, string id)
        {
            using (BeginBusy(PendingFixMessage))
            {
                FixOutcome outcome = await RunFixAsync(op);
                SetToast(outcome.Message, outcome.Ok ? "SuccessFg" : "DangerFg");
            }
            if (OptStartup.IsOp(op)) _optStartup = null;
            RenderOptimizeSub();
        }

        // ==================== 0 · Settings ====================
        private void RenderOptTweaks()
        {
            var p = _optPanels[0];
            p.Children.Clear();
            List<RegistryFixItem> items = OptTweaks.Items();
            int rec = items.Count(i => i.Status == FixStatus.Recommended);

            var applyRec = CreateFlatButton(rec > 0
                    ? string.Format(OL("⚡ Appliquer les recommandés ({0})", "⚡ Apply recommended ({0})", "⚡ Aplicar recomendados ({0})", "⚡ Empfohlene anwenden ({0})", "⚡ Applica consigliati ({0})", "⚡ Aplicar recomendados ({0})"), rec)
                    : OL("✓ Recommandés déjà appliqués", "✓ Recommended already applied", "✓ Recomendados ya aplicados", "✓ Empfohlene bereits aktiv", "✓ Consigliati già applicati", "✓ Recomendados já aplicados"),
                rec > 0 ? "Accent" : "BgSubtle", rec > 0 ? "OnAccent" : "TextMuted", rec > 0 ? "Accent" : "BorderCard", new Thickness(14, 7, 14, 7), 12.5);
            applyRec.FontWeight = FontWeights.SemiBold;
            applyRec.IsEnabled = rec > 0;
            applyRec.Click += async (s, e) =>
            {
                var ops = OptTweaks.Items().Where(i => i.Status == FixStatus.Recommended).Select(i => i.Id + "_apply").ToList();
                if (ops.Count == 0) return;
                List<FixRunResult> results;
                using (BeginBusy(PendingFixMessage)) results = await RunFixBatchAsync(ops);
                int ok = results.Count(r => r.Ok);
                SetToast(string.Format(OL("{0} réglage(s) appliqué(s). Certains prennent effet après redémarrage de l'Explorateur ou de la session.",
                                          "{0} setting(s) applied. Some take effect after File Explorer or the session restarts.",
                                          "{0} ajuste(s) aplicado(s). Algunos se aplican tras reiniciar el Explorador o la sesión.",
                                          "{0} Einstellung(en) angewendet. Manche wirken nach Neustart des Explorers oder der Sitzung.",
                                          "{0} impostazione/i applicata/e. Alcune hanno effetto dopo il riavvio di Esplora file o della sessione.",
                                          "{0} ajuste(s) aplicado(s). Alguns valem após reiniciar o Explorador ou a sessão."), ok), ok == results.Count ? "SuccessFg" : "WarnFg");
                RenderOptTweaks();
            };
            p.Children.Add(OptActionBar(OL("Uniquement des réglages et stratégies documentés par Microsoft : aucun service désactivé, rien de supprimé. Les éléments marqués ADMIN demandent une autorisation Windows.",
                                           "Only settings and policies documented by Microsoft: no service disabled, nothing deleted. Items marked ADMIN ask for Windows permission.",
                                           "Solo ajustes y directivas documentados por Microsoft: ningún servicio desactivado. Los marcados ADMIN piden permiso.",
                                           "Nur von Microsoft dokumentierte Einstellungen und Richtlinien: kein Dienst deaktiviert. ADMIN-Einträge fragen nach Freigabe.",
                                           "Solo impostazioni e criteri documentati da Microsoft: nessun servizio disattivato. Le voci ADMIN chiedono autorizzazione.",
                                           "Só ajustes e políticas documentados pela Microsoft: nenhum serviço desativado. Itens ADMIN pedem permissão."), applyRec));

            foreach (OptGroup g in Enum.GetValues(typeof(OptGroup)))
            {
                var inGroup = items.Where(i => OptTweaks.GroupOf(i.Id) == g).ToList();
                if (inGroup.Count == 0) continue;
                p.Children.Add(OptSectionTitle(OptTweaks.GroupName(g)));
                foreach (var it in inGroup) p.Children.Add(BuildFixCard(it, ExecuteOptimizeOp));
            }
        }

        // ==================== 1 · Apps ====================
        private void RenderOptApps()
        {
            var p = _optPanels[1];
            p.Children.Clear();
            _optAppChecks.Clear();
            if (_optApps == null)
            {
                p.Children.Add(OptNote(OL("Recherche des applications installées…", "Looking for installed apps…", "Buscando aplicaciones instaladas…", "Installierte Apps werden gesucht…", "Ricerca delle app installate…", "Procurando aplicativos instalados…")));
                if (!_optAppsLoading)
                {
                    _optAppsLoading = true;
                    Task.Run(() => OptApps.Scan()).ContinueWith(t =>
                    {
                        _optAppsLoading = false;
                        _optApps = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<AppEntry>();
                        if (_optSub == 1) RenderOptApps();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            _btnOptRemoveApps = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
            _btnOptRemoveApps.FontWeight = FontWeights.SemiBold;
            _btnOptRemoveApps.Click += (s, e) => RemoveSelectedApps();
            p.Children.Add(OptActionBar(OL("Retirées pour ton compte uniquement, sans droits administrateur. Chacune se réinstalle depuis le Microsoft Store.",
                                           "Removed for your account only, without admin rights. Each one can be reinstalled from the Microsoft Store.",
                                           "Se quitan solo para tu cuenta, sin permisos de administrador. Cada una se reinstala desde Microsoft Store.",
                                           "Nur für dein Konto entfernt, ohne Adminrechte. Jede lässt sich im Microsoft Store neu installieren.",
                                           "Rimosse solo per il tuo account, senza diritti admin. Ognuna si reinstalla dal Microsoft Store.",
                                           "Removidos só da sua conta, sem direitos de administrador. Cada um pode ser reinstalado pela Microsoft Store."), _btnOptRemoveApps));

            if (_optApps.Count == 0)
                p.Children.Add(OptCard(OptNote(OL("Aucune application superflue de la liste n'est installée.", "None of the listed unnecessary apps is installed.", "Ninguna app superflua de la lista está instalada.", "Keine der gelisteten überflüssigen Apps ist installiert.", "Nessuna app superflua dell'elenco è installata.", "Nenhum app supérfluo da lista está instalado."))));

            AddAppSection(p, OL("Superflu (coché d'office)", "Clutter (checked by default)", "Superfluo (marcado)", "Überflüssig (vorausgewählt)", "Superfluo (selezionato)", "Supérfluo (marcado)"), _optApps.Where(a => a.Suggested));
            AddAppSection(p, OL("À toi de voir", "Your call", "Tú decides", "Deine Entscheidung", "Decidi tu", "Você decide"), _optApps.Where(a => !a.Suggested));

            if (OptApps.OneDriveInstalled)
            {
                p.Children.Add(OptSectionTitle("OneDrive"));
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = OptNote(OL("OneDrive n'est pas une app du Store : il se désinstalle depuis Paramètres › Applications. Vérifie d'abord que tes fichiers synchronisés sont bien aussi sur le PC.",
                                   "OneDrive is not a Store app: uninstall it from Settings › Apps. First make sure your synced files are also on the PC.",
                                   "OneDrive no es una app de la Store: se desinstala desde Configuración › Aplicaciones.",
                                   "OneDrive ist keine Store-App: unter Einstellungen › Apps deinstallieren.",
                                   "OneDrive non è un'app dello Store: si disinstalla da Impostazioni › App.",
                                   "O OneDrive não é um app da Store: desinstale em Configurações › Aplicativos."));
                t.Margin = new Thickness(0, 0, 12, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                var b = CreateFlatButton(OL("Ouvrir Applications", "Open Apps", "Abrir Aplicaciones", "Apps öffnen", "Apri App", "Abrir Aplicativos"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
                b.Click += (s, e) => { try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:appsfeatures", UseShellExecute = true }); } catch { } };
                Grid.SetColumn(b, 1);
                row.Children.Add(b);
                p.Children.Add(OptCard(row));
            }
            UpdateRemoveAppsButton();
        }

        private void AddAppSection(StackPanel p, string title, IEnumerable<AppEntry> apps)
        {
            var list = apps.ToList();
            if (list.Count == 0) return;
            p.Children.Add(OptSectionTitle(title));
            var stack = new StackPanel();
            foreach (AppEntry a in list)
            {
                var content = new StackPanel();
                content.Children.Add(ResFg(new TextBlock { Text = a.Title, FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary"));
                content.Children.Add(ResFg(new TextBlock { Text = (a.Note != null ? a.Note + "  ·  " : "") + a.Name, FontSize = 11.5 }, "TextMuted"));
                var cb = new CheckBox { IsChecked = a.Suggested, Content = content, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
                cb.Checked += (s, e) => UpdateRemoveAppsButton();
                cb.Unchecked += (s, e) => UpdateRemoveAppsButton();
                _optAppChecks[cb] = a;
                stack.Children.Add(cb);
            }
            p.Children.Add(OptCard(stack));
        }

        private void UpdateRemoveAppsButton()
        {
            if (_btnOptRemoveApps == null) return;
            int n = _optAppChecks.Count(kv => kv.Key.IsChecked == true);
            _btnOptRemoveApps.IsEnabled = n > 0;
            _btnOptRemoveApps.Content = string.Format(OL("Retirer la sélection ({0})", "Remove selected ({0})", "Quitar selección ({0})", "Auswahl entfernen ({0})", "Rimuovi selezione ({0})", "Remover seleção ({0})"), n);
        }

        private async void RemoveSelectedApps()
        {
            var sel = _optAppChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            if (sel.Count == 0) return;
            string msg = OL("Retirer ces applications pour ton compte ?\n\n", "Remove these apps for your account?\n\n", "¿Quitar estas aplicaciones de tu cuenta?\n\n", "Diese Apps für dein Konto entfernen?\n\n", "Rimuovere queste app per il tuo account?\n\n", "Remover estes aplicativos da sua conta?\n\n")
                         + string.Join("\n", sel.Select(a => "• " + a.Title))
                         + OL("\n\nElles se réinstallent depuis le Microsoft Store si besoin.", "\n\nThey can be reinstalled from the Microsoft Store if needed.", "\n\nSe pueden reinstalar desde Microsoft Store.", "\n\nSie lassen sich im Microsoft Store neu installieren.", "\n\nSi possono reinstallare dal Microsoft Store.", "\n\nPodem ser reinstalados pela Microsoft Store.");
            if (MessageBox.Show(this, msg, "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            List<string> removed = null, failed = null;
            using (BeginBusy(OL("Retrait des applications…", "Removing apps…", "Quitando aplicaciones…", "Apps werden entfernt…", "Rimozione app…", "Removendo aplicativos…")))
                await Task.Run(() => OptApps.Remove(sel, out removed, out failed));

            // Journal: listed with the other changes, marked "reinstall from the Store" instead of undoable.
            History.AddJournal(removed.Select(t => new JournalEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Time = DateTime.Now,
                Op = "optapp_remove",
                FixId = "optapp",
                Title = OL("Application retirée : ", "App removed: ", "Aplicación quitada: ", "App entfernt: ", "App rimossa: ", "Aplicativo removido: ") + t,
                Ok = true,
                Message = OL("Réinstallable depuis le Microsoft Store.", "Can be reinstalled from the Microsoft Store.", "Reinstalable desde Microsoft Store.", "Im Microsoft Store neu installierbar.", "Reinstallabile dal Microsoft Store.", "Reinstalável pela Microsoft Store."),
                Changes = new List<ChangeRecord>(),
                Reversible = false
            }).ToList());
            RenderJournal();

            if (failed.Count == 0)
                SetToast(string.Format(OL("{0} application(s) retirée(s).", "{0} app(s) removed.", "{0} aplicación(es) quitada(s).", "{0} App(s) entfernt.", "{0} app rimossa/e.", "{0} aplicativo(s) removido(s)."), removed.Count), "SuccessFg");
            else
                SetToast(string.Format(OL("{0} retirée(s), {1} impossible(s) : {2}", "{0} removed, {1} failed: {2}", "{0} quitada(s), {1} fallaron: {2}", "{0} entfernt, {1} fehlgeschlagen: {2}", "{0} rimossa/e, {1} non riuscita/e: {2}", "{0} removido(s), {1} falharam: {2}"), removed.Count, failed.Count, string.Join(", ", failed)), "WarnFg");
            _optApps = null;
            RenderOptApps();
        }

        // ==================== 2 · Startup ====================
        private void RenderOptStartup()
        {
            var p = _optPanels[2];
            p.Children.Clear();
            if (_optStartup == null)
            {
                p.Children.Add(OptNote(OL("Lecture des programmes au démarrage…", "Reading startup programs…", "Leyendo programas de inicio…", "Autostart-Programme werden gelesen…", "Lettura dei programmi all'avvio…", "Lendo programas de inicialização…")));
                if (!_optStartupLoading)
                {
                    _optStartupLoading = true;
                    Task.Run(() => OptStartup.Scan()).ContinueWith(t =>
                    {
                        _optStartupLoading = false;
                        _optStartup = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<StartupEntry>();
                        if (_optSub == 2) RenderOptStartup();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            int on = _optStartup.Count(e => e.Enabled);
            p.Children.Add(OptActionBar(string.Format(OL(
                "{0} programme(s) se lancent avec Windows. Désactiver ne désinstalle rien : le programme ne démarre simplement plus tout seul (comme dans le Gestionnaire des tâches).",
                "{0} program(s) start with Windows. Disabling uninstalls nothing: the program just no longer starts on its own (like in Task Manager).",
                "{0} programa(s) se inician con Windows. Desactivar no desinstala nada (como en el Administrador de tareas).",
                "{0} Programm(e) starten mit Windows. Deaktivieren deinstalliert nichts (wie im Task-Manager).",
                "{0} programma/i si avviano con Windows. Disattivare non disinstalla nulla (come in Gestione attività).",
                "{0} programa(s) iniciam com o Windows. Desativar não desinstala nada (como no Gerenciador de Tarefas)."), on), null));

            if (_optStartup.Count == 0)
                p.Children.Add(OptCard(OptNote(OL("Aucun programme au démarrage.", "No startup programs.", "Ningún programa de inicio.", "Keine Autostart-Programme.", "Nessun programma all'avvio.", "Nenhum programa de inicialização."))));

            foreach (StartupEntry e in _optStartup)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                var head = new WrapPanel();
                head.Children.Add(ResFg(new TextBlock { Text = e.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) }, e.Enabled ? "TextPrimary" : "TextMuted"));
                string bg, fg;
                if (e.Enabled) { bg = "SuccessSoftBg"; fg = "SuccessFg"; } else { bg = "BgSubtle"; fg = "TextMuted"; }
                head.Children.Add(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 1, 7, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = ResFg(new TextBlock { Text = e.Enabled ? OL("Au démarrage", "At startup", "Al inicio", "Beim Start", "All'avvio", "Na inicialização") : OL("Désactivé", "Disabled", "Desactivado", "Deaktiviert", "Disattivato", "Desativado"), FontSize = 11, FontWeight = FontWeights.SemiBold }, fg)
                }, bg));
                left.Children.Add(head);
                left.Children.Add(ResFg(new TextBlock { Text = (e.Publisher ?? OL("Éditeur inconnu", "Unknown publisher", "Editor desconocido", "Unbekannter Herausgeber", "Editore sconosciuto", "Editor desconhecido")) + "  ·  " + e.SourceLabel + (e.Admin ? "  ·  ADMIN" : ""), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary"));
                left.Children.Add(ResFg(new TextBlock { Text = e.Command, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), ToolTip = e.Command }, "TextMuted"));
                if (e.Publisher != null && e.Publisher.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                    left.Children.Add(ResFg(new TextBlock { Text = OL("Composant de Windows : mieux vaut le laisser actif.", "Windows component: best left on.", "Componente de Windows: mejor dejarlo activo.", "Windows-Komponente: besser aktiviert lassen.", "Componente di Windows: meglio lasciarlo attivo.", "Componente do Windows: melhor deixar ativo."), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) }, "WarnFg"));
                row.Children.Add(left);

                StartupEntry entry = e;
                var b = CreateFlatButton(e.Enabled ? OL("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar") : OL("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
                                         "BgCard", "TextPrimary", "BorderStrong", new Thickness(13, 6, 13, 6), 12);
                b.MinWidth = 110;
                b.VerticalAlignment = VerticalAlignment.Center;
                b.Click += (s, ev) => ExecuteOptimizeOp(entry.Op(entry.Enabled), null);
                Grid.SetColumn(b, 1);
                row.Children.Add(b);
                p.Children.Add(OptCard(row));
            }
        }

        // ==================== 3 · Cleanup ====================
        private static string SizeText(long bytes)
        {
            if (bytes < 0) return OL("taille lisible avec les droits admin", "size readable with admin rights", "tamaño visible con permisos de administrador", "Größe mit Adminrechten sichtbar", "dimensione visibile con diritti admin", "tamanho visível com direitos de administrador");
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("N0") + " " + OL("Mo", "MB", "MB", "MB", "MB", "MB");
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " " + OL("Go", "GB", "GB", "GB", "GB", "GB");
        }

        private void RenderOptClean()
        {
            var p = _optPanels[3];
            p.Children.Clear();
            _optCleanChecks.Clear();
            if (_optClean == null)
            {
                p.Children.Add(OptNote(OL("Calcul de la place récupérable…", "Measuring reclaimable space…", "Calculando el espacio recuperable…", "Freigebbarer Platz wird berechnet…", "Calcolo dello spazio recuperabile…", "Calculando o espaço recuperável…")));
                if (!_optCleanLoading)
                {
                    _optCleanLoading = true;
                    Task.Run(() => OptCleanup.Scan()).ContinueWith(t =>
                    {
                        _optCleanLoading = false;
                        _optClean = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<CleanItem>();
                        if (_optSub == 3) RenderOptClean();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            _btnOptClean = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
            _btnOptClean.FontWeight = FontWeights.SemiBold;
            _btnOptClean.Click += async (s, e) =>
            {
                var ops = _optCleanChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value.Op).ToList();
                if (ops.Count == 0) return;
                List<FixRunResult> results;
                using (BeginBusy(OL("Nettoyage…", "Cleaning…", "Limpiando…", "Bereinigung…", "Pulizia…", "Limpando…"))) results = await RunFixBatchAsync(ops);
                SetToast(string.Join("  ", results.Select(r => r.Message)), results.All(r => r.Ok) ? "SuccessFg" : "WarnFg");
                _optClean = null;
                RenderOptClean();
            };
            p.Children.Add(OptActionBar(OL("Seulement des caches que Windows reconstruit tout seul. Les dumps d'écran bleu et les rapports d'erreur ne sont jamais touchés : c'est ce que Dump Finder analyse.",
                                           "Only caches Windows rebuilds on its own. Blue screen dumps and error reports are never touched: they are what Dump Finder analyzes.",
                                           "Solo cachés que Windows reconstruye solo. Los volcados y los informes de error nunca se tocan.",
                                           "Nur Caches, die Windows selbst neu aufbaut. Dumps und Fehlerberichte werden nie angerührt.",
                                           "Solo cache che Windows ricostruisce da solo. Dump e rapporti di errore non vengono mai toccati.",
                                           "Só caches que o Windows recria sozinho. Dumps e relatórios de erro nunca são tocados."), _btnOptClean));

            foreach (CleanItem it in _optClean)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var content = new StackPanel();
                content.Children.Add(ResFg(new TextBlock { Text = it.Title + (it.Admin ? "  ·  ADMIN" : ""), FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary"));
                content.Children.Add(ResFg(new TextBlock { Text = it.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary"));
                var cb = new CheckBox { IsChecked = it.Bytes != 0, Content = content, VerticalContentAlignment = VerticalAlignment.Top };
                cb.Checked += (s, e) => UpdateCleanButton();
                cb.Unchecked += (s, e) => UpdateCleanButton();
                _optCleanChecks[cb] = it;
                row.Children.Add(cb);
                var size = ResFg(new TextBlock { Text = SizeText(it.Bytes), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), MaxWidth = 200, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }, it.Bytes > 0 ? "AccentText" : "TextMuted");
                Grid.SetColumn(size, 1);
                row.Children.Add(size);
                p.Children.Add(OptCard(row));
            }
            UpdateCleanButton();
        }

        private void UpdateCleanButton()
        {
            if (_btnOptClean == null) return;
            var sel = _optCleanChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            long known = sel.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
            _btnOptClean.IsEnabled = sel.Count > 0;
            _btnOptClean.Content = string.Format(OL("Nettoyer la sélection ({0})", "Clean selected ({0})", "Limpiar selección ({0})", "Auswahl bereinigen ({0})", "Pulisci selezione ({0})", "Limpar seleção ({0})"), SizeText(known));
        }

        // ==================== 4 · Myths ====================
        private void RenderOptMyths()
        {
            var p = _optPanels[4];
            p.Children.Clear();
            p.Children.Add(OptActionBar(OL("Des « optimisations » très répandues que Dump Finder ne propose volontairement pas, et pourquoi.",
                                           "Widespread 'optimizations' Dump Finder deliberately leaves out, and why.",
                                           "«Optimizaciones» muy difundidas que Dump Finder no ofrece a propósito, y por qué.",
                                           "Verbreitete „Optimierungen“, die Dump Finder bewusst weglässt, und warum.",
                                           "«Ottimizzazioni» diffuse che Dump Finder non propone di proposito, e perché.",
                                           "«Otimizações» populares que o Dump Finder deixa de fora de propósito, e por quê."), null));
            foreach (var m in Myths())
            {
                var s = new StackPanel();
                s.Children.Add(ResFg(new TextBlock { Text = m.Item1, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
                s.Children.Add(ResFg(new TextBlock { Text = m.Item2, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 17 }, "TextSecondary"));
                p.Children.Add(OptCard(s));
            }
        }

        private static IEnumerable<Tuple<string, string>> Myths()
        {
            yield return Tuple.Create(
                OL("Désactiver Microsoft Defender", "Disabling Microsoft Defender", "Desactivar Microsoft Defender", "Microsoft Defender abschalten", "Disattivare Microsoft Defender", "Desativar o Microsoft Defender"),
                OL("Gain quasi nul sur un PC récent, contre une vraie protection (ransomwares). Pour réduire son impact en jeu, ajoute plutôt une exclusion pour le dossier de tes jeux.", "Next to no gain on a recent PC, for real protection lost (ransomware). To lower its impact while gaming, add an exclusion for your games folder instead.", "Ganancia casi nula, a cambio de perder protección real. Mejor añade una exclusión para la carpeta de juegos.", "Kaum Gewinn, aber echter Schutz geht verloren. Besser eine Ausnahme für den Spieleordner anlegen.", "Guadagno quasi nullo, perdendo una protezione reale. Meglio aggiungere un'esclusione per la cartella dei giochi.", "Ganho quase nulo, perdendo proteção real. Melhor adicionar uma exclusão para a pasta de jogos."));
            yield return Tuple.Create(
                OL("Couper les protections Spectre / Meltdown", "Turning off Spectre / Meltdown mitigations", "Quitar las protecciones Spectre / Meltdown", "Spectre-/Meltdown-Schutz abschalten", "Disattivare le protezioni Spectre / Meltdown", "Desligar as proteções Spectre / Meltdown"),
                OL("Les processeurs récents (Ryzen 7000/9000, Intel Core 12e gen et plus) corrigent ces failles en matériel : il n'y a presque rien à gagner, et la faille redevient exploitable.", "Recent CPUs (Ryzen 7000/9000, Intel Core 12th gen and later) fix these flaws in hardware: there is almost nothing to gain, and the flaw becomes exploitable again.", "Los procesadores recientes las corrigen en hardware: casi nada que ganar y la falla vuelve a ser explotable.", "Aktuelle CPUs beheben das in Hardware: kaum Gewinn, und die Lücke wird wieder ausnutzbar.", "Le CPU recenti le correggono in hardware: quasi nulla da guadagnare e la falla torna sfruttabile.", "CPUs recentes corrigem isso no hardware: quase nada a ganhar e a falha volta a ser explorável."));
            yield return Tuple.Create(
                OL("Supprimer le fichier d'échange", "Removing the page file", "Eliminar el archivo de paginación", "Auslagerungsdatei entfernen", "Eliminare il file di paging", "Remover o arquivo de paginação"),
                OL("Quand la RAM est pleine, les applications plantent au lieu de ralentir, et Windows ne peut plus écrire de dump après un écran bleu : tu perds la trace de la panne.", "When RAM fills up, apps crash instead of slowing down, and Windows can no longer write a dump after a blue screen: you lose the trace of the crash.", "Con la RAM llena, las apps se cierran y Windows ya no puede guardar el volcado de una pantalla azul.", "Bei voller RAM stürzen Apps ab, und Windows kann nach einem Bluescreen keinen Dump mehr schreiben.", "Con la RAM piena le app si chiudono e Windows non può più scrivere il dump di una schermata blu.", "Com a RAM cheia, apps fecham e o Windows não grava mais o dump de uma tela azul."));
            yield return Tuple.Create(
                OL("Désactiver SysMain (Superfetch) ou l'indexation", "Disabling SysMain (Superfetch) or indexing", "Desactivar SysMain (Superfetch) o la indexación", "SysMain (Superfetch) oder Indizierung abschalten", "Disattivare SysMain (Superfetch) o l'indicizzazione", "Desativar o SysMain (Superfetch) ou a indexação"),
                OL("Utile à l'époque des disques durs lents. Sur SSD, l'effet est nul, et sans indexation la recherche Windows devient lente.", "Made sense in the slow hard drive days. On an SSD it does nothing, and without indexing Windows search becomes slow.", "Tenía sentido con discos lentos. En SSD no cambia nada y sin indexación la búsqueda se vuelve lenta.", "Zu HDD-Zeiten sinnvoll. Auf SSD wirkungslos, ohne Indizierung wird die Suche langsam.", "Aveva senso con i dischi lenti. Su SSD non cambia nulla e senza indicizzazione la ricerca rallenta.", "Fazia sentido com HDs lentos. Em SSD não muda nada e sem indexação a pesquisa fica lenta."));
            yield return Tuple.Create(
                OL("HPET, « dynamic tick » et autres commandes bcdedit", "HPET, 'dynamic tick' and other bcdedit tweaks", "HPET, «dynamic tick» y otros ajustes de bcdedit", "HPET, „Dynamic Tick“ und andere bcdedit-Tweaks", "HPET, «dynamic tick» e altre modifiche bcdedit", "HPET, «dynamic tick» e outros ajustes do bcdedit"),
                OL("Les mesures n'y trouvent pas de gain, parfois plus de latence, et une horloge système mal réglée peut provoquer des comportements étranges.", "Measurements find no gain, sometimes more latency, and a badly set system clock can cause odd behavior.", "Las mediciones no muestran ganancia, a veces más latencia.", "Messungen zeigen keinen Gewinn, teils mehr Latenz.", "Le misure non mostrano guadagni, a volte più latenza.", "As medições não mostram ganho, às vezes mais latência."));
            yield return Tuple.Create(
                OL("Les nettoyeurs de registre", "Registry cleaners", "Limpiadores de registro", "Registry-Cleaner", "Pulitori del registro", "Limpadores de registro"),
                OL("Quelques Ko gagnés pour un vrai risque de casser des applications. Microsoft ne les prend pas en charge.", "A few KB saved for a real risk of breaking apps. Microsoft does not support them.", "Unos KB ganados con riesgo real de romper apps. Microsoft no los admite.", "Ein paar KB gegen echtes Risiko, Apps zu beschädigen. Von Microsoft nicht unterstützt.", "Pochi KB guadagnati con il rischio di rompere app. Microsoft non li supporta.", "Poucos KB ganhos com risco real de quebrar apps. A Microsoft não os suporta."));
            yield return Tuple.Create(
                OL("Les réglages réseau « anti-lag » (Nagle, NetworkThrottlingIndex…)", "'Anti-lag' network tweaks (Nagle, NetworkThrottlingIndex…)", "Ajustes de red «anti-lag» (Nagle, NetworkThrottlingIndex…)", "„Anti-Lag“-Netzwerktweaks (Nagle, NetworkThrottlingIndex…)", "Modifiche di rete «anti-lag» (Nagle, NetworkThrottlingIndex…)", "Ajustes de rede «anti-lag» (Nagle, NetworkThrottlingIndex…)"),
                OL("Les jeux actuels gèrent eux-mêmes leurs connexions : ces clés n'ont pas d'effet mesurable sur le ping.", "Today's games manage their own connections: these keys have no measurable effect on ping.", "Los juegos actuales gestionan sus conexiones: sin efecto medible en el ping.", "Aktuelle Spiele verwalten ihre Verbindungen selbst: kein messbarer Effekt auf den Ping.", "I giochi attuali gestiscono le connessioni: nessun effetto misurabile sul ping.", "Os jogos atuais gerenciam suas conexões: sem efeito mensurável no ping."));
            yield return Tuple.Create(
                OL("Supprimer Edge ou WebView2", "Removing Edge or WebView2", "Eliminar Edge o WebView2", "Edge oder WebView2 entfernen", "Rimuovere Edge o WebView2", "Remover o Edge ou o WebView2"),
                OL("Widgets, Teams, Outlook et de nombreux launchers de jeux s'appuient sur WebView2 ; ils cessent de fonctionner, et Windows Update réinstalle Edge.", "Widgets, Teams, Outlook and many game launchers rely on WebView2; they stop working, and Windows Update reinstalls Edge anyway.", "Widgets, Teams, Outlook y muchos launchers dependen de WebView2; dejan de funcionar.", "Widgets, Teams, Outlook und viele Launcher brauchen WebView2; sie funktionieren nicht mehr.", "Widget, Teams, Outlook e molti launcher usano WebView2; smettono di funzionare.", "Widgets, Teams, Outlook e muitos launchers usam o WebView2; param de funcionar."));
            yield return Tuple.Create(
                OL("Le plan d'alimentation « Performances ultimes » sur un PC de bureau", "The 'Ultimate Performance' power plan on a desktop", "El plan «Máximo rendimiento» en un PC de escritorio", "Energieplan „Ultimative Leistung“ am Desktop", "Il piano «Prestazioni eccellenti» su un desktop", "O plano «Desempenho máximo» em um desktop"),
                OL("Empêche le processeur de baisser sa fréquence au repos : plus de chaleur et de bruit, pour un gain de quelques pour cent au mieux. Le mode Équilibré gère très bien les Ryzen et Intel récents.", "Stops the CPU from clocking down at idle: more heat and noise, for a few percent at best. Balanced handles recent Ryzen and Intel CPUs very well.", "Impide bajar la frecuencia en reposo: más calor y ruido por pocos %. El modo Equilibrado funciona muy bien.", "Verhindert das Heruntertakten im Leerlauf: mehr Wärme und Lärm für wenige Prozent. „Ausbalanciert“ ist bei aktuellen CPUs ideal.", "Impedisce di abbassare la frequenza a riposo: più calore e rumore per pochi %. Bilanciato va benissimo.", "Impede reduzir a frequência em repouso: mais calor e ruído por poucos %. O Equilibrado funciona muito bem."));
            yield return Tuple.Create(
                OL("Désactiver des services Windows « inutiles » en masse", "Disabling 'useless' Windows services in bulk", "Desactivar servicios «inútiles» en masa", "„Unnötige“ Dienste massenhaft abschalten", "Disattivare in massa servizi «inutili»", "Desativar serviços «inúteis» em massa"),
                OL("Gain de mémoire négligeable ; selon le service, l'impression, le Bluetooth, Windows Update ou le Store cessent de fonctionner, souvent des semaines plus tard sans qu'on fasse le lien.", "Negligible memory saved; depending on the service, printing, Bluetooth, Windows Update or the Store stop working, often weeks later with no obvious link.", "Ahorro de memoria mínimo; impresión, Bluetooth, Windows Update o la Store pueden dejar de funcionar.", "Kaum Speichergewinn; Drucken, Bluetooth, Windows Update oder der Store können ausfallen.", "Risparmio minimo; stampa, Bluetooth, Windows Update o lo Store possono smettere di funzionare.", "Economia mínima; impressão, Bluetooth, Windows Update ou a Store podem parar de funcionar."));
        }
    }
}
